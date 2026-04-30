# Phase 2 — ECG Firmware Specification (AD8232 on ESP32-S3)

## 1. AD8232 Hardware Connection

```
    ESP32-S3 DevKitC-1 (Board B)        AD8232 Breakout
    ┌──────────────────────┐            ┌──────────────────┐
    │                      │            │                  │
    │  GPIO 34 (ADC1_CH6)──┼── OUTPUT ──┼── OUTPUT         │
    │  GPIO 35 (input) ────┼── LO+ ────┼── LO+            │
    │  GPIO 36 (input) ────┼── LO- ────┼── LO-            │
    │  GPIO 32 (output) ───┼── SDN ────┼── SDN            │
    │  3V3 ────────────────┼── VCC ────┼── 3.3V           │
    │  GND ────────────────┼── GND ────┼── GND            │
    │                      │            │                  │
    └──────────────────────┘            └──────────────────┘
                                         │  │  │
                                         │  │  └── RL (Right Leg)
                                         │  └───── LL (Left Leg)  
                                         └──────── RA (Right Arm)
```

| Signal | ESP32-S3 Pin | AD8232 Pin | Notes |
|--------|-------------|-----------|-------|
| OUTPUT | GPIO 34 [VERIFY] | OUTPUT | Analog ECG signal (0–3.3V, DC-biased at ~1.5V) |
| LO+ | GPIO 35 [VERIFY] | LO+ | Lead-off detection +, digital HIGH = lead off |
| LO- | GPIO 36 [VERIFY] | LO- | Lead-off detection -, digital HIGH = lead off |
| SDN | GPIO 32 [VERIFY] | SDN | Shutdown control: HIGH = active, LOW = shutdown |
| VCC | 3V3 | 3.3V | Powered from ESP32-S3 LDO |
| GND | GND | GND | Common ground |

> **Pin assignments marked [VERIFY]** — actual GPIO numbers depend on the specific ESP32-S3 board variant used. Choose ADC1 pins (GPIO 1–10 on ESP32-S3) for reliable analog reads. ADC2 pins are unavailable when Wi-Fi is active.

### Electrode Placement (3-lead)
- **RA (Right Arm):** Below right clavicle
- **LA (Left Arm):** Below left clavicle
- **RL (Right Leg):** Lower left rib cage (reference/ground)

## 2. ESP32-S3 ADC Configuration

### Target Specifications
| Parameter | Value |
|-----------|-------|
| Resolution | 12-bit (0–4095) |
| Sample Rate | 250 SPS minimum |
| Attenuation | 11 dB (input range: 0–3.3V) [VERIFY] |
| ADC Unit | ADC1 (required — ADC2 conflicts with Wi-Fi/BLE) |
| DMA | Yes — continuous mode via `adc_continuous` driver |

### Implementation Approach

**Option A: Timer-driven `analogRead()` (Simple)**
```cpp
// Arduino framework — simple but may jitter at high rates
hw_timer_t* adcTimer = timerBegin(0, 80, true);  // 1 MHz base
timerAttachInterrupt(adcTimer, &onAdcTimerISR, true);
timerAlarmWrite(adcTimer, 4000, true);  // 250 Hz = 4000 µs
timerAlarmEnable(adcTimer);
```

**Option B: ESP-IDF `adc_continuous` (Recommended for deterministic timing)**
```cpp
adc_continuous_handle_cfg_t adc_config = {
    .max_store_buf_size = 4096,
    .conv_frame_size = 256,
};
adc_digi_pattern_config_t adc_pattern = {
    .atten = ADC_ATTEN_DB_11,
    .channel = ADC_CHANNEL_6,  // GPIO 34 [VERIFY]
    .unit = ADC_UNIT_1,
    .bit_width = ADC_BITWIDTH_12,
};
```

> **Recommendation:** Use Option A for initial bring-up (Arduino framework compatibility), migrate to Option B if timing jitter exceeds ±1 ms at 250 SPS.

### Sample Buffering
- Ring buffer: 500 samples (2 seconds at 250 Hz)
- DMA transfers fill buffer in background
- Main loop drains buffer → DSP filter → BLE notify

## 2.5 Signal Processing Pipeline (ESP-DSP)

> **Requirement:** The on-device ECG filter chain MUST match the AFDB model preprocessing pipeline
> (see `Model/final_2_afdb_preprocessing.pdf`) so that inference input is consistent with training data.

### Library: ESP-DSP
- PlatformIO dependency: `espressif/esp-dsp` (included in ESP32 Arduino framework)
- Uses hardware-accelerated IIR Biquad (SOS form) and vector math on ESP32-S3 Xtensa PIE extensions.

### Filter Chain (matching AFDB training pipeline)

```
ADC raw (12-bit, 250 Hz)
   │
   ▼
┌──────────────────────────────────────────┐
│ 1. Bandpass Filter (IIR Biquad / SOS)    │
│    Butterworth Order 4                   │
│    Low Cut:  0.5 Hz                      │
│    High Cut: 30.0 Hz                     │
│    → Removes DC drift + high-freq noise  │
└──────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────┐
│ 2. Notch Filter (IIR Biquad)             │
│    f0 = 50 Hz, Q = 30                   │
│    → Removes powerline interference      │
└──────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────┐
│ 3. Transient Removal                     │
│    Discard first N samples after boot    │
│    N = ceil(ORDER/4 * 4/f_low) * Fs     │
│    ≈ ceil(4/4 * 4/0.5) * 250 = 2000     │
│    → Skip first 8 seconds of data       │
└──────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────┐
│ 4. Quality Checks (per 10s window)       │
│    • Flatline: std < 0.00488 → reject   │
│    • Saturation: |max| > 9.5 → reject   │
│    • Partial flat: >40% near zero       │
└──────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────┐
│ 5. Z-Score Normalization (per window)    │
│    z = (x - mean) / (std + 1e-7)        │
│    clip to [-3.0, 3.0]                   │
│    → Matches AFDB INT8 quantization      │
└──────────────────────────────────────────┘
   │
   ▼
  BLE Notify + AI Inference Buffer
```

### ESP-DSP Biquad Coefficients (pre-computed)

The Butterworth order-4 bandpass [0.5, 30] Hz at Fs=250 decomposes into 4 SOS (Second-Order Sections)
biquad stages. Coefficients are pre-computed offline using `scipy.signal.butter(..., output='sos')`
and embedded as `const float` arrays in firmware.

```cpp
// Pseudo-code for ESP-DSP IIR filtering
#include "dsps_biquad.h"

// 4 SOS stages for bandpass: each stage has 6 coefficients [b0,b1,b2,a0,a1,a2]
static float bp_coeffs[4][6];  // Pre-computed from scipy
static float bp_delay[4][2];   // Delay line per stage

// 1 SOS stage for notch 50Hz
static float notch_coeffs[1][6];
static float notch_delay[1][2];

void dsp_filter_sample(float* sample) {
    // Cascade bandpass stages
    for (int i = 0; i < 4; i++) {
        dsps_biquad_f32(sample, sample, 1, bp_coeffs[i], bp_delay[i]);
    }
    // Notch stage
    dsps_biquad_f32(sample, sample, 1, notch_coeffs[0], notch_delay[0]);
}
```

### Coefficient Generation Script (Python, run once)
```python
from scipy.signal import butter, iirnotch, tf2sos
import numpy as np

TARGET_FS = 250
nyquist = 0.5 * TARGET_FS

# Bandpass 0.5–30 Hz, order 4
sos_bp = butter(4, [0.5/nyquist, 30.0/nyquist], btype='band', output='sos')
print("// Bandpass SOS coefficients (4 stages × 6 coefficients)")
for i, section in enumerate(sos_bp):
    print(f"// Stage {i}: b0={section[0]:.10f}, b1={section[1]:.10f}, b2={section[2]:.10f}, a0={section[3]:.10f}, a1={section[4]:.10f}, a2={section[5]:.10f}")

# Notch 50 Hz, Q=30
b, a = iirnotch(50.0 / nyquist, 30.0)
sos_notch = tf2sos(b, a)
print("// Notch 50Hz SOS coefficients")
for i, section in enumerate(sos_notch):
    print(f"// Stage {i}: b0={section[0]:.10f}, b1={section[1]:.10f}, b2={section[2]:.10f}, a0={section[3]:.10f}, a1={section[4]:.10f}, a2={section[5]:.10f}")
```

## 3. Lead-Off Detection

The AD8232 provides two digital outputs (`LO+` and `LO-`) that go HIGH when a lead is disconnected.

### Detection Strategy
```cpp
void checkLeadOff() {
    bool leadOff = digitalRead(PIN_LO_PLUS) || digitalRead(PIN_LO_MINUS);
    if (leadOff) {
        // Pause ADC sampling, notify "lead off" status via BLE
        // Set status characteristic to LEAD_OFF (0x01)
    } else {
        // Resume sampling, set status to CONNECTED (0x00)
    }
}
```

### Polling vs Interrupt
- **Polling (recommended initially):** Check `LO+`/`LO-` every 100 ms in `loop()`. Simple, reliable.
- **Interrupt (optional optimization):** Attach rising-edge interrupt on `LO+`/`LO-` pins. Respond within 1 sample period.

### Behavior on Lead-Off
1. Stop forwarding ECG samples via BLE (send zero-filled packets or pause notify)
2. Update lead-off status characteristic
3. Resume automatically when leads reconnected (LO+/LO- return LOW)

## 4. BLE Peripheral Role

### GATT Server Definition

**Service: ECG Data Service**
**Service UUID:** `A0000001-0000-1000-8000-00805F9B34FB` [VERIFY — custom UUID]

| Characteristic | UUID | Properties | Format | Update Rate |
|---------------|------|-----------|--------|-------------|
| ECG Waveform | `A0000002-...-34FB` [VERIFY] | Notify | Packed: `[seq:u16][sample:i16 × 10]` = 22 bytes/packet | 25 Hz (10 samples × 25 = 250 SPS) |
| Lead-Off Status | `A0000003-...-34FB` [VERIFY] | Read, Notify | uint8: 0x00=OK, 0x01=LO+, 0x02=LO-, 0x03=both | On change |
| Sample Rate | `A0000004-...-34FB` [VERIFY] | Read | uint16: sample rate in Hz (250) | Static |

### Advertising
- Device Name: `"PulseMonitor-ECG"`
- Advertising Interval: 100 ms → 500 ms after connection
- Include service UUID in scan response

### BLE MTU & Throughput
- Request MTU of 247 bytes after connection (ESP32 BLE supports up to 512)
- At 250 SPS with 16-bit samples: 500 bytes/sec raw data
- Batched in 22-byte packets at 25 Hz: well within BLE 4.2+ throughput limits

## 5. Data Framing Format

### ECG Packet Structure (BLE Notify payload)

```
Byte Offset   Field        Type      Description
───────────────────────────────────────────────────
0–1           seq_num      uint16    Packet sequence number (wraps at 65535)
2–3           sample[0]    int16     ECG sample 0 (signed, 12-bit in 16-bit container)
4–5           sample[1]    int16     ECG sample 1
...
20–21         sample[9]    int16     ECG sample 9
───────────────────────────────────────────────────
Total: 22 bytes per packet, 25 packets/sec = 550 bytes/sec
```

### Sample Value Encoding
- Raw ADC (0–4095) centered: `int16_t ecg_sample = (int16_t)(raw_adc - 2048);`
- Signed representation allows direct waveform rendering without offset subtraction
- Sequence number enables packet loss detection on receiver

### JSON Fallback (Serial debug, same format as PPG board)
```json
{"ts":1234567890,"ecg":[2048,2051,2045,...]}
```

## 6. Test Case Checklist

### Hardware
- [ ] AD8232 powered, SDN held HIGH — verify OUTPUT pin produces ~1.5V DC with no electrodes
- [ ] Electrode leads connected — OUTPUT pin shows ECG-like waveform on oscilloscope
- [ ] LO+ goes HIGH when RA electrode disconnected
- [ ] LO- goes HIGH when LL electrode disconnected
- [ ] Both LO+/LO- go HIGH when all electrodes removed

### ADC
- [ ] ADC samples at 250 SPS confirmed — measure timestamp delta over 1000 samples, expect 4.0 ±0.1 ms
- [ ] ADC values span expected range (~800–3200 for normal ECG with DC bias) [VERIFY]
- [ ] No ADC aliasing artifacts — verify with known frequency signal generator [VERIFY]

### Signal Quality
- [ ] ECG waveform on Serial plotter shows recognizable QRS complexes at rest
- [ ] P-wave and T-wave visible in clean recording
- [ ] Baseline wander acceptable (< 0.5 mV equivalent drift over 10 seconds)

### BLE
- [ ] BLE advertising confirmed on smartphone scanner (nRF Connect)
- [ ] Connection established, MTU negotiated ≥ 23 bytes
- [ ] ECG characteristic notifies at 25 Hz with 22-byte packets
- [ ] Sequence numbers are continuous (no packet loss in 1 min test)
- [ ] Lead-off status characteristic updates within 200 ms of electrode removal

### Integration
- [ ] ECG waveform visible on smartphone BLE scanner (raw hex → verify changing values)
- [ ] Board B operates standalone for ≥30 minutes without crash or memory leak
- [ ] Power consumption < 60 mA at 3.3V during active BLE streaming [VERIFY]
