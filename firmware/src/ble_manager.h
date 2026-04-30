#ifndef BLE_MANAGER_H
#define BLE_MANAGER_H

#include <NimBLEDevice.h>
#include <stdint.h>
#include "hrv_analyzer.h" // For HrvResult

class BleManager
{
public:
  BleManager();
  void begin();
  bool isConnected() const;

  void notifyHeartRate(uint16_t bpm);
  void notifySpO2(uint8_t spo2Pct);
  void queueWaveformSample(uint32_t ts, uint32_t ir, uint32_t red);
  void notifyHrv(const HrvResult& result);

  void startAdvertising();

  // Internal callback flag
  bool _connected;

private:
  uint8_t _wfBuf[60]; // 12 bytes * 5 samples
  uint8_t _wfIdx;
};

#endif
