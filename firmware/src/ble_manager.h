#pragma once
// ============================================================
// ble_manager.h — BLE GATT Server for PulseMonitor PPG node
//
// Provides Heart Rate, SpO2, Raw Waveform, and HRV Summary
// characteristics over BLE 5.0 on ESP32-S3.
// ============================================================
#ifndef BLE_MANAGER_H
#define BLE_MANAGER_H

#include <stdint.h>
#include "hrv_analyzer.h"

class BleManager
{
public:
  BleManager();

  /// Initialize BLE stack, create GATT services, start advertising.
  void begin();

  /// Returns true if a smartphone client is connected.
  bool isConnected() const;

  /// Notify Heart Rate (call ~1 Hz or on each detected beat).
  void notifyHeartRate(uint16_t bpm);

  /// Notify SpO2 (call ~1 Hz).
  void notifySpO2(uint8_t spo2Pct);

  /// Queue a raw waveform sample. Internally batches and sends
  /// when the batch is full (5 samples → 60 bytes).
  void queueWaveformSample(uint32_t ts, uint32_t ir, uint32_t red);

  /// Notify HRV summary (call every ~5 s).
  void notifyHrv(const HrvResult& result);

  // Allow BLE server callbacks to set connection state
  friend class PulseServerCallbacks;

private:
  bool     _connected;
  uint8_t  _wfBuf[60];   // 5 samples × 12 bytes each
  uint8_t  _wfIdx;       // current slot in batch (0–4)

  void startAdvertising();
};

#endif // BLE_MANAGER_H
