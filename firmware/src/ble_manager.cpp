#include "ble_manager.h"
#include <Arduino.h>

// Mirroring Board B's UUID Pattern
#define PPG_SERVICE_UUID   "DE010001-0000-1000-8000-00805F9B34FB"
#define WAVEFORM_CHAR_UUID "DE010003-0000-1000-8000-00805F9B34FB"
#define METRICS_CHAR_UUID  "DE010004-0000-1000-8000-00805F9B34FB"

static BleManager* g_instance = nullptr;
static BLEServer* g_pServer = nullptr;
static BLECharacteristic* g_pWfChar = nullptr;
static BLECharacteristic* g_pMetChar = nullptr;

class PulseServerCallbacks : public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) override {
        if (g_instance) g_instance->_connected = true;
        Serial.println(">>> [BLE] Android Client Connected (Mirror Mode)");
    }
    void onDisconnect(BLEServer* pServer) override {
        if (g_instance) g_instance->_connected = false;
        Serial.println(">>> [BLE] Android Client Disconnected. Re-advertising...");
        pServer->startAdvertising();
    }
};

BleManager::BleManager() : _connected(false) {}

void BleManager::begin() {
    g_instance = this;
    
    // 1. Same Init as Board B
    BLEDevice::init("PulseMonitor-PPG");
    BLEDevice::setMTU(247);

    g_pServer = BLEDevice::createServer();
    g_pServer->setCallbacks(new PulseServerCallbacks());

    // 2. Same Service Creation
    BLEService* pService = g_pServer->createService(BLEUUID(PPG_SERVICE_UUID));

    g_pWfChar = pService->createCharacteristic(WAVEFORM_CHAR_UUID, NIMBLE_PROPERTY::NOTIFY);
    g_pMetChar = pService->createCharacteristic(METRICS_CHAR_UUID, NIMBLE_PROPERTY::NOTIFY);

    pService->start();
    
    // 3. Mirror Board B Advertising EXACTLY
    BLEAdvertising* pAdv = BLEDevice::getAdvertising();
    pAdv->addServiceUUID(PPG_SERVICE_UUID);
    pAdv->setScanResponse(true);
    
    // Board B's preferred timings
    pAdv->setMinPreferred(0x06); 
    pAdv->setMaxPreferred(0x12);
    
    // CRITICAL: Set appearance to Heart Rate Sensor (0x0341) like Board B
    pAdv->setAppearance(0x0341); 
    
    BLEDevice::startAdvertising();
    Serial.println("[BLE] Board A Mirror Build Active. Advertising as PulseMonitor-PPG.");
}

void BleManager::queueWaveformSample(uint32_t ts, uint32_t ir, uint32_t red) {
    if (!_connected || !g_pWfChar) return;

    uint8_t pkt[12];
    memcpy(&pkt[0], &ts,  4);
    memcpy(&pkt[4], &ir,  4);
    memcpy(&pkt[8], &red, 4);

    g_pWfChar->setValue(pkt, 12);
    g_pWfChar->notify();
}

void BleManager::notifyHeartRate(uint16_t bpm) {
    if (!_connected || !g_pMetChar) return;
    char buf[64];
    snprintf(buf, sizeof(buf), "{\"ts\":%lu,\"bpm\":%u}", millis(), bpm);
    g_pMetChar->setValue((uint8_t*)buf, strlen(buf));
    g_pMetChar->notify();
}

void BleManager::notifySpO2(uint8_t spo2Pct) {
    if (!_connected || !g_pMetChar) return;
    char buf[64];
    snprintf(buf, sizeof(buf), "{\"ts\":%lu,\"spo2\":%u}", millis(), spo2Pct);
    g_pMetChar->setValue((uint8_t*)buf, strlen(buf));
    g_pMetChar->notify();
}

void BleManager::notifyHrv(const HrvResult& result) {
    if (!_connected || !g_pMetChar) return;
    char buf[128];
    snprintf(buf, sizeof(buf), 
        "{\"ts\":%lu,\"hrv\":{\"sdnn\":%.1f,\"rmssd\":%.1f}}",
        millis(), result.sdnn, result.rmssd);
    g_pMetChar->setValue((uint8_t*)buf, strlen(buf));
    g_pMetChar->notify();
}

bool BleManager::isConnected() const { return _connected; }
