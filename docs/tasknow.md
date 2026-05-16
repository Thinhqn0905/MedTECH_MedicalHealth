# Current Tasks & Progress - PulseMonitor

## Current App Notes - 2026-05-16

### GUI Stream Scaling Plan - Board A PPG + Board B ECG

#### Current problem
- Both waveform canvases currently use near-pure min/max auto-scale over the visible ring buffer.
- This makes the chart unstable:
  - If the signal is very small, noise is stretched to full height and looks like a real waveform.
  - If one spike/artifact enters the buffer, the real waveform becomes tiny until that spike leaves the window.
  - The vertical scale can jump frame-to-frame, so the user cannot judge whether the signal is improving or getting worse.
- For ECG specifically, no-electrode/floating AD8232 output can be auto-stretched into an ECG-like trace. Lead-off should visually dominate this state, not the waveform.
- For PPG, raw IR/Red values include a large DC baseline plus a small AC pulse. Pure min/max on raw values makes the pulse amplitude depend heavily on finger pressure, LED level, and motion artifacts.

#### Current stream principle
- **Board A - PPG stream**
  - Firmware sends waveform packets through BLE characteristic `DE010003`.
  - Packet format: `[ts:u32][ir:u32][red:u32]`, about 100 samples/sec.
  - App stores data in `PpgIrBuffer` and `PpgRedBuffer` with 800 points, about 8 seconds at 100Hz.
  - Metrics such as BPM and SpO2 are sent separately through `DE010004` as JSON.
  - Display goal: show pulse morphology clearly, not preserve absolute ADC DC level.
- **Board B - ECG stream**
  - Firmware sends waveform packets through BLE characteristic `A0000002`.
  - Packet format: `[seq:u16][sample:i16 x 10]`, 25 packets/sec = 250 samples/sec.
  - App stores data in `EcgBuffer` with 1250 points, about 5 seconds at 250Hz.
  - Lead-off status is sent separately through `A0000003`.
  - Display goal: show ECG morphology consistently, and avoid presenting floating/no-electrode noise as a valid signal.

#### Proposed scaling method
- Use **hybrid stable scaling**, not pure min/max:
  1. Preprocess the visible buffer by removing invalid values and ignoring zeros/NaN.
  2. Compute a robust center:
     - ECG: center around `0` because firmware/app data is already normalized around baseline.
     - PPG: center each channel by a rolling mean or median so the chart shows AC pulse, not raw DC level.
  3. Compute robust amplitude from percentiles, not raw min/max:
     - Use P5/P95 or P2/P98.
     - Ignore the most extreme spikes.
  4. Smooth the display scale with attack/release:
     - Grow scale quickly when signal gets larger.
     - Shrink scale slowly so the waveform does not jump.
  5. Clamp scale to a reasonable min/max:
     - Prevent tiny noise from becoming full-height.
     - Prevent real signal from becoming invisible after one artifact.

#### Recommended default scale behavior
- **ECG default: fixed clinical scale**
  - Use fixed Y range around zero first, e.g. `-1.5 .. +1.5` normalized units.
  - Clip samples outside the range instead of rescaling the entire graph.
  - Add optional gain levels: `0.5x`, `1x`, `2x`, `4x`.
  - If `IsEcgLeadOff == true`, draw a flat baseline and overlay lead-off status; do not keep drawing incoming waveform as if valid.
- **ECG debug mode: robust auto**
  - For debugging only, use percentile scale with smoothing.
  - Minimum vertical range should be enforced so floating noise stays visibly small.
- **PPG default: AC-coupled robust auto**
  - Subtract rolling mean/median from IR and Red before display.
  - Scale by robust amplitude from recent window, e.g. percentile or RMS-based gain.
  - Draw IR and Red in the same centered band, or split into two half-height lanes if overlap becomes hard to read.
  - Keep a minimum amplitude floor so weak noise does not fill the chart.
- **PPG fallback when no finger / weak signal**
  - If AC amplitude is below threshold or DC level is too low, show baseline and a subtle "signal weak" state instead of zooming noise.

#### Concrete implementation tasks
- [ ] Create reusable chart scale helper, e.g. `WaveformScaleState`, with:
  - `Center`
  - `HalfRange`
  - percentile/RMS calculation
  - min/max clamp
  - attack/release smoothing
- [ ] Update ECG canvas:
  - Default to fixed scale around zero.
  - Add lead-off visual gate: baseline + overlay when lead-off is active.
  - Add debug robust-auto option later.
- [ ] Update PPG canvas:
  - Display AC-coupled IR/Red, not raw DC values.
  - Use robust auto-scale with amplitude floor and smoothing.
  - Consider split lanes if IR/Red overlap makes interpretation difficult.
- [ ] Add small UI control or setting:
  - ECG scale: `Fixed 1x`, `Fixed 2x`, `Auto debug`.
  - PPG scale: `Auto stable`, `Split lanes`.

### Task 2 - BPM / SpO2 display not updating
- **Observed:** Dashboard cards stay at `--` even when Board A is connected/streaming.
- **Root cause found in app:** `UpdateVitalsDisplay()` existed but was not called by any timer, so `BpmDisplay` and `SpO2Display` bindings never refreshed.
- **Second issue found:** Board A firmware sends metrics as JSON on `DE010004`:
  - `{"ts":...,"bpm":...}`
  - `{"ts":...,"spo2":...}`
  App previously parsed only `hrv`, so direct firmware BPM/SpO2 notifications were ignored.
- **Fix applied:** `MainViewModel` now starts a 10Hz UI vitals timer and subscribes to `BleReader.MetricsReceived` to update `_latestBpm` / `_latestSpO2` from firmware metrics.
- [ ] Build Android app and verify BPM card updates after heartbeat peaks.
- [ ] Verify SpO2 card updates after firmware `spo2Calculator.isValid()` becomes true.
- [ ] If SpO2 still remains `--`, inspect Board A serial logs and verify finger contact/raw Red+IR amplitude.

## Status: 🟡 ACTIVE (Export & Chart Fixes Applied)

---

## 🔴 Current Bugs Fixed (This Session)

### Fix 1 – PPG flat baseline line when disconnected ✅
- **File:** `DashboardContentView.xaml.cs` → `OnPaintPpgSurface()`
- Draw a horizontal center line when no data (matching ECG disconnected style)

### Fix 2 – Export Email / Save to Device crashes app ✅
- **Root Cause:** `IsBusy = true/false` and `Shell.DisplayAlert` were being called from a **background thread** (after `ConfigureAwait(false)` continuation), causing a cross-thread MAUI UI dispatch crash on Android.
- **Fix:** All UI interactions (IsBusy, DisplayAlert) now explicitly dispatched via `MainThread.BeginInvokeOnMainThread()` or `MainThread.InvokeOnMainThreadAsync()`. Heavy email IO wrapped in `Task.Run`.

### Fix 3 – Frequency Domain chart not rendering ✅
- **Root Cause:** `SpectrumSeries` was declared as `ISeries[]` (plain array). LiveChartsCore requires `ObservableCollection<ISeries>` to receive change notifications and trigger chart redraws.
- **Fix:** Changed to `ObservableCollection<ISeries>` with explicit `.Add()` in constructor.

---

## 🔴 Pending – Requires Configuration Before Execution

### SMTP Email Export – Needs Verified Credentials
> **⚠ IMPORTANT:** The default Gmail credentials (`giabao05vng@gmail.com`) in `PreferencesSettingsStore.cs` may be expired or the App Password may have been revoked. Gmail App Passwords expire if 2FA is changed or the password is regenerated.

**Before testing Export Email, verify:**
1. Go to **Google Account → Security → App Passwords**
2. Create a new App Password for "Mail" on "Windows Computer"  
3. Update `PreferencesSettingsStore.cs` line 71: `settings.Smtp.Password = "NEW_16_CHAR_APP_PASSWORD";`
4. Or configure via **Settings page** in the app (preferred — no code change needed)

**SMTP Config checklist:**
- [ ] Host: `smtp.gmail.com`
- [ ] Port: `587`
- [ ] UseSsl: `true`
- [ ] User: valid Gmail address
- [ ] Password: 16-character App Password (NOT your Gmail login password)
- [ ] RecipientEmail: destination address

---

## 🟡 Active Tasks

### Frequency Domain Chart – Needs Real ECG Data to Validate
- Chart now uses `ObservableCollection<ISeries>` (fixed)
- Chart only populates when **≥20 RR intervals** are detected from Pan-Tompkins detector
- **Requires:** PPG board (Board A) connected and streaming — HRV computation is client-side from RR intervals
- [ ] Connect Board A (PPG) → verify `_rrHistory.Count` reaches 20+ → confirm chart renders

### Board A (PPG) BLE Discovery
- App still fails to discover Board A (`PulseMonitor_PPG`)
- **Next action:** Check Android BLE cache. Try on real device with Bluetooth off/on cycle.
- [ ] Verify board is advertising with name `PulseMonitor_PPG`
- [ ] Test with nRF Connect app to confirm advertisement is visible

---

## Technical Reference

### Build Command
```powershell
dotnet build PulseMonitor -t:Run -f net8.0-android -p:RuntimeIdentifier=android-arm64 -p:AndroidSdkDirectory="C:\Users\ADMIN\AppData\Local\Android\Sdk"
```

### ADB
```powershell
& "C:\Users\ADMIN\AppData\Local\Android\Sdk\platform-tools\adb.exe" devices
```

### Key Architecture
| Component | Detail |
|-----------|--------|
| Board B (ECG) | ESP32-S3, BLE, COM5, confirmed alive |
| Board A (PPG) | MAX30102, BLE, discovery failing |
| ECG Buffer | 1250 samples, circular ring |
| PPG Buffer | 800 samples (8s @ 100Hz) |
| HRV FFT | Client-side, 4Hz resampled, 256-pt DFT |
| Export Email | Gmail SMTP, requires valid App Password |
