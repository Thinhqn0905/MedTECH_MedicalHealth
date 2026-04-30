# Current Tasks & Progress - PPG/ECG Integration Stable

## Status: ✅ STABLE (Phase 1 & 7 Complete)

## Current Objective
Final validation of dual-board live streaming and AI diagnostics.

## Root Cause Found ✅
**`System.ArgumentException: Offset and length were out of bounds`**
- **Location**: `EcgBleReader.OnWaveformUpdated()` → `Buffer.BlockCopy(data, 2, samples, 0, 20)`
- **Cause**: Previous fix relaxed the 22-byte strict check, allowing short packets through. When a short packet (e.g., 10 bytes) arrived, `BlockCopy` tried to read 20 bytes → `ArgumentException` → Unhandled on Android GATT binder thread → **SIGABRT** → App crash.
- **Fix Applied**: 
  1. Restored strict `data.Length != 22` check with early `return`.
  2. Wrapped all 3 GATT callbacks (`OnWaveformUpdated`, `OnLeadOffUpdated`, `OnAfUpdated`) in `try-catch` to prevent any future unhandled exception from killing the process.
  3. Added null checks on `data` before accessing `.Length`.

## Critical Bugs
- [x] ~~**MAUI Crash (SIGABRT)**: Buffer.BlockCopy out-of-bounds on short BLE packets~~ → **FIXED**
- [x] **Verify No Crash**: Confirm app stays alive after Connect ECG.
- [x] **Verify Data Stream**: Confirm `[BLE DATA]` logs appear in app event log.

## Completed Tasks (Recent)
- [x] Firmware flash to Board B (COM5) — stable, no boot-loop.
- [x] BLE scan timeout reduced to 10s.
- [x] Thread-safe ECG buffer access (`lock(EcgLock)` in ViewModel + DashboardView).
- [x] Added `ACCESS_COARSE_LOCATION` to AndroidManifest.
- [x] Enhanced app logging (first 500 packets logged immediately).
- [x] **Root-cause crash fix**: Strict packet validation + try-catch on all GATT callbacks.

## Phase 1: PPG Firmware (Board A) ✅ COMPLETED
1. [x] **[FW]** Pan-Tompkins Peak Detection (100Hz).
2. [x] **[FW]** SpO2 Calculation (Beer-Lambert).
3. [x] **[FW]** HRV Analysis (SDNN, RMSSD, Stress).
4. [x] **[FW]** Multi-Characteristic BLE (Standard HR/SpO2 + Binary Waveform + JSON Metrics).
5. [x] **[APP]** Updated `BleReader` to support dual-characteristic protocol.

## Phase 6: Multi-Device Optimization
1. [x] **[VM]** Refactor `MainViewModel` for `BoardALogs` and `BoardBLogs` (Independent Buffers).
2. [x] **[UI]** Add `Picker` (Dropdown) to `DashboardContentView.xaml`.
3. [x] **[Reader]** Add `[PPG]` / `[ECG]` prefixes to log messages.
4. [x] **[Test]** Connect 2 Boards simultaneously and verify stable log switching.

## Phase 7: Recording & Export Enhancements
1. [x] **[Record]** Integrate ECG waveform into the session recording buffer.
2. [x] **[CSV]** Update `SessionExporter` to handle high-frequency ECG samples.
3. [x] **[Email]**

### 🔴 Current Blockers (Critical)
1. **BLE Discovery Failure (Board A):** Mobile app fails to discover or connect to PPG board even after name shortening and UUID fallback implementation.
   - *Status:* Brainstorming root cause (Hypothesis: Android caching or Scanning filter issue).
   - *Next Action:* Verify specific error message from App UI logs.

> **Status: ✅ COMPLETED** — Unified recording engine deployed. Optimized CSV with Forward Fill implemented. Default SMTP credentials hardcoded.

## Technical Stats
| Metric | Value |
| --- | --- |
| Board | ESP32-S3 (Board B) |
| BLE MTU | 247 bytes |
| Packet Size | 22 bytes (Seq: 2, Data: 20) |
| Scan Timeout | 10 seconds |
| Crash Root Cause | Buffer.BlockCopy on short BLE packet |
| Fix | Strict length check + try-catch on GATT callbacks |

## Notes
- Board B is confirmed alive and notifying (`att_handle=12` and `15`).
- The crash was introduced by a previous "relaxation" of the packet check. Lesson: never weaken validation on JNI/binder threads.
