# Phase 1 — PPG Firmware Specification (MAX30102 on ESP32-S3)

## 1. Hardware Wiring Spec

```
    ESP32-S3 DevKitC-1               MAX30102 Breakout
    ┌──────────────────┐             ┌──────────────┐
    │                  │             │              │
    │  GPIO 8 (SDA) ───┼─── I2C ────┼── SDA        │
    │  GPIO 9 (SCL) ───┼─── I2C ────┼── SCL        │
    │  GPIO 4 (IRQ) ───┼─── INT ────┼── INT (O.D.) │
    │  3V3 ────────────┼─── VCC ────┼── VIN        │
    │  GND ────────────┼─── GND ────┼── GND        │
    │                  │             │              │
    └──────────────────┘             └──────────────┘
```

| Signal | ESP32-S3 Pin | MAX30102 Pin | Notes |
|--------|-------------|-------------|-------|
| SDA | GPIO 8 | SDA | I2C data, 4.7kΩ pull-up to 3V3 |
| SCL | GPIO 9 | SCL | I2C clock, 4.7kΩ pull-up to 3V3 |
| INT | GPIO 4 | INT | Open-drain, active LOW, falling-edge trigger |
| VCC | 3V3 | VIN | 3.3V rail from ESP32-S3 LDO |
| GND | GND | GND | Common ground |

- **I2C Address:** `0x57` (7-bit)
- **I2C Clock:** 400 kHz (Fast Mode)
- **Power consumption:** MAX30102 draws ~1.2 mA in Red+IR mode at 6.4 mA LED current

## 2. MAX30102 Register Initialization Sequence

The SparkFun library (`MAX30105.h`) abstracts most register writes. Below is the equivalent raw register sequence for reference.

| Step | Register | Address | Value | Description |
|------|----------|---------|-------|-------------|
| 1 | Mode Config | `0x09` | `0x40` | Reset device (bit 6) |
| 2 | *wait* | — | ~100ms | Wait for reset complete |
| 3 | FIFO Config | `0x08` | `0x40` | SMP_AVE=4 (bits 7:5 = 010), FIFO_ROLLOVER_EN=0 |
| 4 | Mode Config | `0x09` | `0x03` | SpO2 mode (Red + IR LEDs) |
| 5 | SpO2 Config | `0x0A` | `0x27` | ADC range=16384nA (bits 6:5=01), SR=100Hz (bits 4:2=001), PW=411µs (bits 1:0=11) |
| 6 | LED1 Pulse Amp | `0x0C` | `0x20` | Red LED current = 6.4 mA |
| 7 | LED2 Pulse Amp | `0x0D` | `0x20` | IR LED current = 6.4 mA |
| 8 | INT Enable 1 | `0x02` | `0xC0` | A_FULL_EN + PPG_RDY_EN |
| 9 | FIFO Write Ptr | `0x04` | `0x00` | Clear FIFO pointers |
| 10 | FIFO Read Ptr | `0x06` | `0x00` | Clear FIFO pointers |
| 11 | Overflow Ctr | `0x05` | `0x00` | Clear overflow counter |

> **Note:** The firmware uses `g_sensor.setup()` from the SparkFun library which performs steps 3–7 internally. Steps 8–11 are handled by `enableDATARDY()`, `enableAFULL()`, and `clearFIFO()`.

**Current firmware call (from `main.cpp:282–292`):**
```cpp
g_sensor.setup(
  LED_CURRENT_6P4MA,  // 0x20 = 6.4 mA
  SAMPLE_AVERAGE,     // 4
  LED_MODE_RED_IR,    // 2 = SpO2 mode
  SAMPLE_RATE_HZ,     // 100
  PULSE_WIDTH_US,     // 411
  ADC_RANGE_NA);      // 16384
```

## 3. Sampling Pipeline

```
  MAX30102 FIFO                                          Output
  ┌────────────┐                                      ┌──────────┐
  │ Red sample │──┐                                   │ JSON:    │
  │ IR  sample │──┤   ISR sets flag    loop() reads   │ {ts,ir,  │
  └────────────┘  ├──────────────────→ FIFO via I2C ──→│  red}    │
       ↑          │   (GPIO4 falling)  g_sensor.       │          │
       │          │                    available()     └──────────┘
    100 Hz        │                         │
    hardware      │                         ↓
    clock         │                   ┌──────────────┐
                  │                   │ Pan-Tompkins │
                  │                   │ Peak Detect  │
                  │                   └──────┬───────┘
                  │                          │ RR interval
                  │                          ↓
                  │                   ┌──────────────┐
                  │                   │ HRV Analyzer │
                  │                   │ SDNN, RMSSD  │
                  │                   │ Rhythm,Stress│
                  │                   └──────────────┘
```

**Detailed pipeline steps:**

1. **FIFO Read:** `readFifoAndStream()` iterates `g_sensor.available()`, extracting 32-bit `IR` and `Red` values per sample.
2. **DC Removal (baseline tracking):** In `panTompkinsUpdate()`, a slow IIR filter: `baseline += 0.01 * (ir - baseline)`. High-pass output: `hp = ir - baseline`.
3. **Derivative:** `derivative = hp - prevHighPass` — approximates first derivative for slope detection.
4. **Squaring:** `squared = derivative²` — amplifies peaks, suppresses low-amplitude noise.
5. **Integration (moving window):** Circular buffer of size 12, mean computed as sliding average over squared values.
6. **Peak decision:** Local maximum detection + adaptive threshold (`mean + 1.4 * stddev`) + refractory period (300 ms).
7. **Output:** `publishSample()` emits `{"ts":ms,"ir":u32,"red":u32}` per sample at 100 Hz.

## 4. Heart Rate Algorithm — Pan-Tompkins Adapted for PPG

The firmware implements a **modified Pan-Tompkins** pipeline. The original algorithm was designed for ECG R-peak detection; this adaptation works on the PPG IR waveform.

### Key Differences from Classical (ECG) Pan-Tompkins

| Aspect | ECG (Original) | PPG (This Implementation) |
|--------|----------------|--------------------------|
| Input signal | ECG lead (mV) | IR photoplethysmogram (raw ADC counts) |
| Target feature | QRS complex R-peak | Systolic peak (PPG pulse) |
| Bandpass filter | 5–15 Hz passband | DC removal via IIR (τ ≈ 1s) + derivative |
| Threshold | Dual adaptive (signal + noise level) | Single adaptive: `mean + 1.4 * σ` |
| Refractory | 200 ms | 300 ms |
| Integration window | 150 ms (≈30 samples @ 200Hz) | 12 samples (120 ms @ 100Hz) |

### Algorithm Validity for PPG
The simplified approach is **adequate for resting HR** (50–120 BPM) where PPG morphology produces clean systolic peaks. For ambulatory or motion-artifact conditions, a dedicated PPG peak detector (e.g., adaptive threshold with template matching) would be needed. The current thresholding works because:
- PPG pulses have a single dominant peak per cardiac cycle
- The derivative-square-integrate chain isolates pulse energy from baseline wander
- The 300 ms refractory prevents double-counting dicrotic notch

### RR Interval Extraction
- Valid RR range: 250 ms (240 BPM) to 2000 ms (30 BPM)
- Each valid RR interval is submitted to `HrvAnalyzer::submitRrInterval()`

## 5. SpO2 Calculation

**Method:** Beer-Lambert R-ratio from Red and IR AC/DC components.

```
R = (AC_red / DC_red) / (AC_ir / DC_ir)

SpO2 = a - b * R
```

Where `a` and `b` are empirically calibrated constants. A typical linear model:
- `SpO2 ≈ 110 - 25 * R` [VERIFY — depends on LED wavelength and sensor placement]

Alternatively, a lookup table from the MAX30102 datasheet or empirical calibration curve.

**Current state:** The MAUI app (`Processing/SpO2Calculator.cs`) implements the R-ratio calculation on the client side. The firmware does NOT currently compute SpO2 on-device. For Phase 1 completion, SpO2 calculation should be added to firmware and exposed via BLE.

## 6. BLE GATT Service Definition

### Service: PulseMonitor PPG Service
**Service UUID:** `6E400001-B5A3-F393-E0A9-E50E24DCCA9E` (Nordic UART compatible for initial testing) [VERIFY — consider custom medical UUID]

| Characteristic | UUID | Properties | Format | Update Rate |
|---------------|------|-----------|--------|-------------|
| Heart Rate | `00002A37-0000-1000-8000-00805F9B34FB` | Notify | uint8 flags + uint16 BPM | 1 Hz |
| SpO2 | `00002A5E-0000-1000-8000-00805F9B34FB` [VERIFY] | Notify | uint8 SpO2 (%) | 1 Hz |
| Raw Waveform | `6E400003-B5A3-F393-E0A9-E50E24DCCA9E` | Notify | Packed binary: [ts:u32][ir:u32][red:u32] per sample, batched | 10 Hz (10 samples/packet) |
| HRV Summary | Custom UUID [VERIFY] | Notify | JSON or packed struct: SDNN(f32)+RMSSD(f32)+rhythm(u8)+stress(u8) | 0.2 Hz (every 5s) |

### Advertising
- Device Name: `"PulseMonitor-PPG"`
- Advertising Interval: 100 ms (fast connect) → 1000 ms (power save after connection)
- Include TX Power Level in advertisement

### Connection Parameters
- Connection Interval: 15–30 ms (for 100 Hz waveform throughput)
- Slave Latency: 0
- Supervision Timeout: 4000 ms

## 7. Test Case Checklist

### Hardware Verification
- [ ] I2C communication verified — device responds at `0x57` on `Wire.begin(8, 9, 400000)`
- [ ] FIFO reads stable at target ODR — confirm 100 samples/second via timestamp delta analysis
- [ ] Interrupt pin (GPIO 4) triggers on FIFO threshold — verify with oscilloscope or `digitalRead` counter

### Signal Quality
- [ ] IR waveform shows clean PPG morphology when finger placed on sensor
- [ ] Red waveform tracks IR with expected amplitude difference
- [ ] DC removal produces zero-mean AC signal
- [ ] Pan-Tompkins detects peaks reliably at rest (sitting, 60–100 BPM)

### Accuracy
- [ ] HR accuracy ±5 BPM vs reference pulse oximeter (resting, 1 min average, N=10 measurements)
- [ ] SpO2 accuracy ±2% vs reference pulse oximeter (resting, N=10 measurements)
- [ ] HRV metrics (SDNN) within ±10 ms of reference (5 min recording)

### BLE
- [ ] BLE advertising confirmed on smartphone scanner (nRF Connect or equivalent)
- [ ] BLE connection established from smartphone
- [ ] HR characteristic notifies every 1 second with valid BPM value
- [ ] SpO2 characteristic notifies with valid percentage
- [ ] Raw waveform characteristic delivers 100 samples/sec without data loss
- [ ] Connection stable for ≥10 minutes continuous streaming

### Power
- [ ] Total board current draw < 50 mA at 3.3V during active streaming
- [ ] Current draw < 5 mA in advertising-only mode (no connection)
