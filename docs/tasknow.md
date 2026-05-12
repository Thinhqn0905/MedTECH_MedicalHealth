# Current Tasks & Progress - PulseMonitor

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
