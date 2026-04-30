// ============================================================
// main.cpp — PulseMonitor ECG firmware (Board B — BLE Peripheral)
//
// Hardware: ESP32-S3 + AD8232
// Role:     BLE Peripheral, streams filtered ECG at 250 Hz
//
// Signal pipeline:
//   ADC 250 Hz → ESP-DSP Bandpass (0.5–30 Hz, order 4)
//                     → Notch (50 Hz, Q=30)
//                     → BLE Notify (batched 10 samples / 22 bytes)
//                     → AI Inference Buffer (2500 samples / 10 sec)
// ============================================================
#include <Arduino.h>
#include <NimBLEDevice.h>
#include <esp_task_wdt.h>
#include <sdkconfig.h>
#include <string.h>

#include "af_inference.h"
#include "ecg_dsp.h"

// ============================================================
// Pin definitions (Board B — ECG)
// ============================================================
namespace {
// AD8232 connections [VERIFY against actual wiring]
constexpr uint8_t PIN_ECG_OUTPUT = 4; // ADC1_CH3 on ESP32-S3
constexpr uint8_t PIN_LO_PLUS = 5;    // Lead-off + detection
constexpr uint8_t PIN_LO_MINUS = 6;   // Lead-off - detection
constexpr uint8_t PIN_SDN = 7;        // Shutdown control

constexpr uint32_t SERIAL_BAUD = 115200;
constexpr uint16_t SAMPLE_RATE_HZ = 250;
constexpr uint32_t SAMPLE_PERIOD_US = 1000000 / SAMPLE_RATE_HZ; // 4000 µs

// Lead-off check interval
constexpr uint32_t LEADOFF_CHECK_MS = 100;

// BLE UUIDs
#ifndef STREAM_TEST_BUILD
#define ECG_SERVICE_UUID "A0000001-0000-1000-8000-00805F9B34FB"
#define ECG_WAVEFORM_UUID "A0000002-0000-1000-8000-00805F9B34FB"
#define ECG_LEADOFF_UUID "A0000003-0000-1000-8000-00805F9B34FB"
#define ECG_SAMPLERATE_UUID "A0000004-0000-1000-8000-00805F9B34FB"
#define ECG_AF_RESULT_UUID "A0000005-0000-1000-8000-00805F9B34FB"
#endif
} // namespace

// ============================================================
// Globals
// ============================================================
static EcgDsp g_dsp;

// Timer-driven sampling
static hw_timer_t *g_adcTimer = nullptr;
static volatile bool g_sampleReady = false;
static portMUX_TYPE g_timerMux = portMUX_INITIALIZER_UNLOCKED;

// BLE
#ifndef STREAM_TEST_BUILD
static BLEServer *g_pServer = nullptr;
static BLECharacteristic *g_pEcgChar = nullptr;
static BLECharacteristic *g_pLeadOffChar = nullptr;
static BLECharacteristic *g_pSrChar = nullptr;
static BLECharacteristic *g_pAfChar = nullptr;
static bool g_bleConnected = false;
#endif

// Lead-off state
static bool g_leadOff = false;
static uint32_t g_lastLeadOffMs = 0;

// BLE packet batching (10 samples per packet)
static uint16_t g_packetSeq = 0;
static int16_t g_sampleBatch[10];
static uint8_t g_batchIdx = 0;

// AI inference result (shared for BLE notify)
static bool g_lastAfResult = false;
static float g_lastAfProb = 0.0f;
static uint32_t g_lastInfTimeMs = 0;
static uint32_t g_inferenceCount = 0;

// Serial stream test mode
static bool g_streamTestMode = false;

// Background Inference Task
static TaskHandle_t g_infTaskHandle = nullptr;
static SemaphoreHandle_t g_windowSem = nullptr;
static float g_infWindowCopy[2500];
static bool g_infBusy = false;
static bool g_aiReady = false;

void inferenceTask(void *pvParameters);

// ============================================================
// ISR — Timer fires at 250 Hz
// ============================================================
void IRAM_ATTR onAdcTimerISR() {
  portENTER_CRITICAL_ISR(&g_timerMux);
  g_sampleReady = true;
  portEXIT_CRITICAL_ISR(&g_timerMux);
}

// ============================================================
// BLE Server Callbacks
// ============================================================
#ifndef STREAM_TEST_BUILD
class EcgServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer *pServer) override {
    g_bleConnected = true;
    Serial.println("BLE client connected.");
  }
  void onDisconnect(BLEServer *pServer) override {
    g_bleConnected = false;
    Serial.println("BLE client disconnected. Re-advertising...");
    pServer->startAdvertising();
  }
};

// ============================================================
// BLE Setup
// ============================================================
static void setupBLE() {
  BLEDevice::init("PulseMonitor-ECG");
  BLEDevice::setMTU(247);

  g_pServer = BLEDevice::createServer();
  g_pServer->setCallbacks(new EcgServerCallbacks());

  BLEService *pService = g_pServer->createService(BLEUUID(ECG_SERVICE_UUID));

  // ECG Waveform characteristic (Notify)
  g_pEcgChar = pService->createCharacteristic(ECG_WAVEFORM_UUID,
                                              NIMBLE_PROPERTY::NOTIFY);

  // Lead-off status characteristic (Read + Notify)
  g_pLeadOffChar = pService->createCharacteristic(
      ECG_LEADOFF_UUID, NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::NOTIFY);
  uint8_t initLeadOff = 0x00;
  g_pLeadOffChar->setValue(&initLeadOff, 1);

  // Sample Rate characteristic (Read)
  g_pSrChar = pService->createCharacteristic(ECG_SAMPLERATE_UUID,
                                             NIMBLE_PROPERTY::READ);
  uint16_t sr = SAMPLE_RATE_HZ;
  g_pSrChar->setValue((uint8_t *)&sr, 2);

  // AF Result characteristic (Read + Notify)
  g_pAfChar = pService->createCharacteristic(
      ECG_AF_RESULT_UUID, NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::NOTIFY);
  float initProb = 0.0f;
  g_pAfChar->setValue((uint8_t *)&initProb, 4);

  pService->start();

  BLEAdvertising *pAdv = BLEDevice::getAdvertising();
  pAdv->addServiceUUID(ECG_SERVICE_UUID);
  pAdv->setScanResponse(true);
  pAdv->setMinPreferred(0x06);
  pAdv->setMaxPreferred(0x12);

  // Set advertising power to max
  pAdv->setAppearance(0x0341); // Heart Rate Sensor appearance

  BLEDevice::startAdvertising();
  Serial.println("BLE GATT server started. Advertising as PulseMonitor-ECG.");
}
#endif

// ============================================================
// Send batched ECG packet over BLE
// ============================================================
#ifndef STREAM_TEST_BUILD
// BLE status LED (GPIO 48 for WS2812 on DevKitC, but we'll use a simple indicator if possible)
// Since we don't have a WS2812 library linked, we'll use Serial for now 
// but add placeholders for LED pins.
#define PIN_STATUS_LED 48 

static void updateBleStatus() {
  if (g_bleConnected) {
    // Solid Green-ish (if we had RGB)
    Serial.println("[BLE] Status: CONNECTED");
  } else {
    // Blinking Blue-ish
    Serial.println("[BLE] Status: ADVERTISING...");
  }
}

static void sendBlePacket() {
  if (!g_bleConnected)
    return;

  // Packet: [seq:u16][sample0:i16]...[sample9:i16] = 22 bytes
  uint8_t pkt[22];
  memcpy(&pkt[0], &g_packetSeq, 2);
  memcpy(&pkt[2], g_sampleBatch, 20);

  g_pEcgChar->setValue(pkt, 22);
  g_pEcgChar->notify();

  g_packetSeq++;
}
#endif

// ============================================================
// Check lead-off status
// ============================================================
#ifndef STREAM_TEST_BUILD
static void checkLeadOff() {
  bool loPlus = digitalRead(PIN_LO_PLUS);
  bool loMinus = digitalRead(PIN_LO_MINUS);
  bool newLeadOff = loPlus || loMinus;

  if (newLeadOff != g_leadOff) {
    g_leadOff = newLeadOff;

    uint8_t status = 0x00;
    if (loPlus && loMinus)
      status = 0x03;
    else if (loPlus)
      status = 0x01;
    else if (loMinus)
      status = 0x02;

    Serial.printf("LO+:%d, LO-:%d -> Status:0x%02X\n", (int)loPlus, (int)loMinus, status);
    g_pLeadOffChar->setValue(&status, 1);
    if (g_bleConnected) {
      g_pLeadOffChar->notify();
    }

    Serial.printf("Lead-off status: 0x%02X (%s)\n", status,
                  newLeadOff ? "DISCONNECTED" : "OK");
  }
}
#endif

// ============================================================
// Arduino entry points
// ============================================================
void setup() {
  Serial.begin(115200);
  Serial.setRxBufferSize(16384); // Large buffer for 10KB windows
  delay(1000);                   // Wait for USB CDC to enumerate
  Serial.println("\n\n=== BOARD IS ALIVE ===");

#ifdef STREAM_TEST_BUILD
  // ---- Stream Test Mode: NO BLE, only AI + Serial ----
  Serial.println("=== STREAM TEST BUILD (No BLE) ===");
  Serial.printf("Free heap: %u bytes\n", (unsigned)ESP.getFreeHeap());
  setCpuFrequencyMhz(240);
  Serial.printf("CPU freq set to %u MHz\n", (unsigned)getCpuFrequencyMhz());
  disableCore0WDT();
  disableCore1WDT();
  esp_task_wdt_deinit();
  Serial.println("Disabled Core/Task WDT for stream test.");

  // Defer AI initialization until first WINDOW request to avoid long blocking
  // work inside setup(), which can trigger task watchdog resets.
  Serial.println("AF inference init is deferred (lazy init on first WINDOW).");
  Serial.printf("Free heap before AI init: %u bytes\n",
                (unsigned)ESP.getFreeHeap());
  Serial.println("Waiting for STREAM_TEST command...");

#else
  // ---- Normal Mode: BLE + ECG + AI ----
  // AD8232 pin setup
  pinMode(PIN_SDN, OUTPUT);
  digitalWrite(PIN_SDN, HIGH); // Active mode

  pinMode(PIN_LO_PLUS, INPUT_PULLDOWN);
  pinMode(PIN_LO_MINUS, INPUT_PULLDOWN);

  // ADC setup
  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);

  // Initialize DSP
  g_dsp.begin();

  // Initialize BLE
  setupBLE();

  // Start 250 Hz timer
  g_adcTimer = timerBegin(0, 80, true); // 80 MHz / 80 = 1 MHz
  timerAttachInterrupt(g_adcTimer, &onAdcTimerISR, true);
  timerAlarmWrite(g_adcTimer, SAMPLE_PERIOD_US, true); // 4000 µs = 250 Hz
  timerAlarmEnable(g_adcTimer);

  // Initialize AI inference engine
  if (!afInferenceInit()) {
    Serial.println("WARNING: AF inference init failed. AI features disabled.");
  } else {
    // Create background inference task
    g_windowSem = xSemaphoreCreateBinary();
    if (g_windowSem == nullptr) {
      Serial.println("WARNING: Failed to create AI window semaphore.");
    } else if (xTaskCreatePinnedToCore(inferenceTask, "inf_task", 32768, NULL,
                                       1, &g_infTaskHandle, 1) != pdPASS) {
      Serial.println("WARNING: Failed to create AI background task.");
      vSemaphoreDelete(g_windowSem);
      g_windowSem = nullptr;
    } else {
      g_aiReady = true;
      Serial.println("AI background task created.");
    }
  }

  Serial.println("PulseMonitor ECG firmware ready. (Board B)");
  Serial.printf("Sampling at %d Hz, filter: BP[0.5-30Hz] + Notch[50Hz]\n",
                SAMPLE_RATE_HZ);
  Serial.printf("Filter transient: discarding first %d samples (%d sec)\n",
                FILTER_TRANSIENT_SAMPLES,
                FILTER_TRANSIENT_SAMPLES / SAMPLE_RATE_HZ);
#endif
}

void loop() {
#ifndef STREAM_TEST_BUILD
  // ---- 250 Hz sampling path (Timer-driven) ----
  bool doSample = false;
  portENTER_CRITICAL(&g_timerMux);
  doSample = g_sampleReady;
  g_sampleReady = false;
  portEXIT_CRITICAL(&g_timerMux);

  if (doSample) {
    uint16_t rawAdc = analogRead(PIN_ECG_OUTPUT);

    // DSP filter chain (bandpass + notch)
    float filtered = g_dsp.processSample(rawAdc);

    // Always send BLE data regardless of isStable() for immediate feedback
    // Convert filtered float to int16 for BLE transmission
    int16_t ecgSample = (int16_t)(filtered * 2048.0f);

    // Batch for BLE
    g_sampleBatch[g_batchIdx] = ecgSample;
    g_batchIdx++;

    if (g_batchIdx >= 10) {
      sendBlePacket();
      g_batchIdx = 0;
    }

    // Serial debug (10 Hz serial output)
    static uint32_t lastSerialMs = 0;
    uint32_t now = millis();
    if (!g_streamTestMode && (now - lastSerialMs >= 100)) {
      lastSerialMs = now;
      Serial.printf("{\"ts\":%lu,\"ecg\":%d,\"raw\":%u,\"lo\":%d}\n",
                    (unsigned long)now, ecgSample, rawAdc, g_leadOff);
    }

    // AI inference window logic (Still requires stability and lead-on)
    if (g_dsp.isStable() && !g_leadOff && g_dsp.isWindowReady()) {
      if (g_dsp.isWindowClean()) {
        if (g_aiReady && !g_infBusy) {
          // Copy window to shared buffer and trigger task
          memcpy(g_infWindowCopy, g_dsp.getWindowBuffer(),
                 sizeof(g_infWindowCopy));
          g_infBusy = true;
          xSemaphoreGive(g_windowSem);
          Serial.println(
              "[AI] Window ready - Triggering background inference...");
        } else if (!g_aiReady) {
          Serial.println("[AI] Window ready - AI not initialized, skipping.");
        } else {
          Serial.println(
              "[AI] Window ready - Inference still busy, dropping window.");
        }
      } else {
        Serial.println("[AI] Window ready — noisy signal, skipping.");
      }
      g_dsp.resetWindow();
    }
  }

  // ---- Lead-off check (100 ms cadence) ----
  uint32_t now = millis();
  if (now - g_lastLeadOffMs >= LEADOFF_CHECK_MS) {
    g_lastLeadOffMs = now;
    checkLeadOff();
    updateBleStatus(); // Periodic status report
  }
#endif // !STREAM_TEST_BUILD

  // ---- Serial Stream Test Mode ----
  if (Serial.available() > 0) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();

    if (cmd == "STREAM_TEST") {
      g_streamTestMode = true;
      Serial.println("STREAM_TEST_ACK");
    } else if (cmd == "STREAM_END") {
      g_streamTestMode = false;
      Serial.println("STREAM_END_ACK");
    } else if (g_streamTestMode && cmd == "WINDOW") {
      static float windowBuf[AI_WINDOW_SIZE];
      char *ptr = reinterpret_cast<char *>(windowBuf);
      size_t totalNeeded = AI_WINDOW_SIZE * sizeof(float);
      size_t totalRead = 0;
      uint32_t startMs = millis();

      while (totalRead < totalNeeded && (millis() - startMs < 20000)) {
        size_t toRead = min((size_t)128, totalNeeded - totalRead);
        size_t n = Serial.readBytes(ptr + totalRead, toRead);
        if (n > 0) {
          totalRead += n;
          esp_task_wdt_reset();
        } else {
          vTaskDelay(1);
        }
      }

      if (totalRead == totalNeeded) {
        float afProb = 0.0f;
        bool ok = afInferenceRun(windowBuf, &afProb);
        if (ok) {
          Serial.printf("RESULT:%.6f:%lu\n", afProb,
                        (unsigned long)afInferenceTimeMs());
        } else {
          Serial.println("RESULT:ERROR");
        }
      } else {
        Serial.printf("RESULT:TIMEOUT:%zu\n", totalRead);
      }
    }
  }
}

// ============================================================
// Background Inference Task
// ============================================================
void inferenceTask(void *pvParameters) {
  while (true) {
    if (!g_aiReady || g_windowSem == nullptr) {
      vTaskDelay(1000);
      continue;
    }

    if (xSemaphoreTake(g_windowSem, portMAX_DELAY) == pdTRUE) {
      float prob = 0.0f;
      bool ok = afInferenceRun(g_infWindowCopy, &prob);

      if (ok) {
        g_lastAfProb = prob;
        g_lastAfResult = (prob > AF_THRESHOLD);
        g_lastInfTimeMs = afInferenceTimeMs();
        g_inferenceCount++;

        Serial.printf("[AF] #%lu result=%s prob=%.3f time=%lums\n",
                      (unsigned long)g_inferenceCount,
                      g_lastAfResult ? "AF_DETECTED" : "Normal", prob,
                      (unsigned long)g_lastInfTimeMs);

#ifndef STREAM_TEST_BUILD
        // Update BLE characteristic
        if (g_bleConnected) {
          g_pAfChar->setValue((uint8_t *)&prob, 4);
          g_pAfChar->notify();
        }
#endif
      }

      g_infBusy = false;
    }
    vTaskDelay(10); // Cool down
  }
}

extern "C" void app_main() {
  initArduino();
  setup();
  while (true) {
    loop();
    vTaskDelay(1);
  }
}
