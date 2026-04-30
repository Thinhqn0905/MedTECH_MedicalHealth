// ============================================================
// ble_manager.cpp — BLE GATT Server implementation
// ============================================================
#include "ble_manager.h"

#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
#include <string.h>

// ---- Service & Characteristic UUIDs -------------------------
// PPG Service (Nordic UART base)
#define PPG_SERVICE_UUID          "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"

// Standard Bluetooth SIG Heart Rate Measurement
#define HR_CHAR_UUID              "00002A37-0000-1000-8000-00805F9B34FB"

// Standard Bluetooth SIG Pulse Oximetry SpO2
#define SPO2_CHAR_UUID            "00002A5E-0000-1000-8000-00805F9B34FB"

// Custom: Raw waveform stream
#define WAVEFORM_CHAR_UUID        "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"

// Custom: HRV summary
#define HRV_CHAR_UUID             "6E400004-B5A3-F393-E0A9-E50E24DCCA9E"

// ---- Forward declarations -----------------------------------
static BleManager* g_instance = nullptr;

static BLEServer*         g_pServer   = nullptr;
static BLECharacteristic* g_pHrChar   = nullptr;
static BLECharacteristic* g_pSpo2Char = nullptr;
static BLECharacteristic* g_pWfChar   = nullptr;
static BLECharacteristic* g_pHrvChar  = nullptr;

// ---- Server callbacks ---------------------------------------
class PulseServerCallbacks : public BLEServerCallbacks
{
  void onConnect(BLEServer* pServer) override
  {
    if (g_instance) { g_instance->_connected = true; }
    Serial.println("BLE client connected.");
  }
  void onDisconnect(BLEServer* pServer) override
  {
    if (g_instance) { g_instance->_connected = false; }
    Serial.println("BLE client disconnected. Re-advertising...");
    // Restart advertising after disconnect
    pServer->startAdvertising();
  }
};

// ---- Constructor --------------------------------------------
BleManager::BleManager()
  : _connected(false), _wfIdx(0)
{
  memset(_wfBuf, 0, sizeof(_wfBuf));
}

// ---- begin() ------------------------------------------------
void BleManager::begin()
{
  g_instance = this;

  BLEDevice::init("PulseMonitor-PPG");
  BLEDevice::setMTU(247);

  g_pServer = BLEDevice::createServer();
  g_pServer->setCallbacks(new PulseServerCallbacks());

  // ---- Create PPG service -----------------------------------
  BLEService* pService = g_pServer->createService(
      BLEUUID(PPG_SERVICE_UUID), 20);  // 20 handles for 4 chars + descriptors

  // Heart Rate characteristic (Notify)
  g_pHrChar = pService->createCharacteristic(
      HR_CHAR_UUID,
      BLECharacteristic::PROPERTY_NOTIFY);
  g_pHrChar->addDescriptor(new BLE2902());

  // SpO2 characteristic (Notify)
  g_pSpo2Char = pService->createCharacteristic(
      SPO2_CHAR_UUID,
      BLECharacteristic::PROPERTY_NOTIFY);
  g_pSpo2Char->addDescriptor(new BLE2902());

  // Raw Waveform characteristic (Notify)
  g_pWfChar = pService->createCharacteristic(
      WAVEFORM_CHAR_UUID,
      BLECharacteristic::PROPERTY_NOTIFY);
  g_pWfChar->addDescriptor(new BLE2902());

  // HRV Summary characteristic (Notify)
  g_pHrvChar = pService->createCharacteristic(
      HRV_CHAR_UUID,
      BLECharacteristic::PROPERTY_NOTIFY);
  g_pHrvChar->addDescriptor(new BLE2902());

  pService->start();

  startAdvertising();
  Serial.println("BLE GATT server started. Advertising as PulseMonitor-PPG.");
}

// ---- isConnected() ------------------------------------------
bool BleManager::isConnected() const
{
  return _connected;
}

// ---- notifyHeartRate() --------------------------------------
void BleManager::notifyHeartRate(uint16_t bpm)
{
  if (!_connected) return;

  // BLE Heart Rate Measurement format:
  // Byte 0: flags (0x00 = uint8 BPM format)
  // Byte 1: BPM value
  uint8_t hrData[2];
  hrData[0] = 0x00;                        // Flags: HR format uint8
  hrData[1] = (bpm > 255) ? 255 : (uint8_t)bpm;
  g_pHrChar->setValue(hrData, 2);
  g_pHrChar->notify();
}

// ---- notifySpO2() -------------------------------------------
void BleManager::notifySpO2(uint8_t spo2Pct)
{
  if (!_connected) return;

  uint8_t data[1] = { spo2Pct };
  g_pSpo2Char->setValue(data, 1);
  g_pSpo2Char->notify();
}

// ---- queueWaveformSample() ----------------------------------
void BleManager::queueWaveformSample(uint32_t ts, uint32_t ir, uint32_t red)
{
  // Pack [ts:u32][ir:u32][red:u32] = 12 bytes per sample
  const uint8_t offset = _wfIdx * 12;
  memcpy(&_wfBuf[offset + 0], &ts,  4);
  memcpy(&_wfBuf[offset + 4], &ir,  4);
  memcpy(&_wfBuf[offset + 8], &red, 4);

  ++_wfIdx;

  if (_wfIdx >= 5)  // Batch of 5 samples = 60 bytes
  {
    if (_connected)
    {
      g_pWfChar->setValue(_wfBuf, 60);
      g_pWfChar->notify();
    }
    _wfIdx = 0;
  }
}

// ---- notifyHrv() --------------------------------------------
void BleManager::notifyHrv(const HrvResult& result)
{
  if (!_connected) return;

  // Pack: [sdnn:f32][rmssd:f32][rhythm:u8][stress:u8] = 10 bytes
  uint8_t data[10];
  memcpy(&data[0], &result.sdnn,  4);
  memcpy(&data[4], &result.rmssd, 4);
  data[8] = static_cast<uint8_t>(result.rhythm);
  data[9] = static_cast<uint8_t>(result.stress);

  g_pHrvChar->setValue(data, 10);
  g_pHrvChar->notify();
}

// ---- startAdvertising() -------------------------------------
void BleManager::startAdvertising()
{
  BLEAdvertising* pAdv = BLEDevice::getAdvertising();
  pAdv->addServiceUUID(PPG_SERVICE_UUID);
  pAdv->setScanResponse(true);
  pAdv->setMinPreferred(0x06);  // 7.5 ms connection interval
  pAdv->setMaxPreferred(0x12);  // 22.5 ms connection interval
  BLEDevice::startAdvertising();
}
