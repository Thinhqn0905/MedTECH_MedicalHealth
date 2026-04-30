// ============================================================
// main.cpp — PulseMonitor firmware (PlatformIO / ESP32-S3)
// Migrated from esp32s3_max30102_pulsemonitor.ino
//
// Changes vs original .ino:
//  • PlatformIO-compatible C++ structure (setup/loop in extern "C")
//  • HrvAnalyzer integrated — runs in loop(), NOT in ISR
//  • publishSample() emits raw sample JSON every sample
//  • publishAiResult() emits HRV JSON every HRV_EMIT_PERIOD_MS
//  • Extended JSON: {"ts":...,"ir":...,"red":...,"hrv":{...}}
//  • SpO2Calculator: on-device SpO2 via Beer-Lambert R-ratio
//  • BleManager: BLE GATT server (HR, SpO2, Waveform, HRV)
// ============================================================
#include <Arduino.h>
#include <Wire.h>
#include "MAX30105.h"
#include "hrv_analyzer.h"
#include "spo2_calculator.h"
#include "ble_manager.h"
#include <ArduinoJson.h>

#ifdef USE_WIFI
#include <WiFi.h>
#include <WebSocketsServer.h>
#endif

// ============================================================
// Constants
// ============================================================
namespace
{
  constexpr uint8_t  PIN_I2C_SDA        = 8;
  constexpr uint8_t  PIN_I2C_SCL        = 9;
  constexpr uint8_t  PIN_MAX30102_IRQ   = 4;

  constexpr uint32_t SERIAL_BAUD        = 115200;
  constexpr uint8_t  LED_CURRENT_6P4MA  = 0x20;
  constexpr uint8_t  SAMPLE_AVERAGE     = 4;
  constexpr uint8_t  LED_MODE_RED_IR    = 2;
  constexpr uint16_t SAMPLE_RATE_HZ     = 100;
  constexpr uint16_t PULSE_WIDTH_US     = 411;
  constexpr uint16_t ADC_RANGE_NA       = 16384;

  // Pan-Tompkins peak detection (simplified, same as original)
  constexpr uint32_t REFRACTORY_MS      = 300;
  constexpr uint32_t MIN_RR_MS          = 250;
  constexpr uint32_t MAX_RR_MS          = 2000;
  constexpr int      INTEGRATION_WIN    = 12;

#ifdef USE_WIFI
  constexpr uint16_t WEBSOCKET_PORT     = 8080;
  const char*        WIFI_SSID          = "YOUR_WIFI_SSID";
  const char*        WIFI_PASSWORD      = "YOUR_WIFI_PASSWORD";
#endif
} // namespace

// ============================================================
// Globals
// ============================================================
static volatile bool g_fifoInterrupt = false;
static portMUX_TYPE  g_isrMux        = portMUX_INITIALIZER_UNLOCKED;
static MAX30105      g_sensor;
static HrvAnalyzer   g_hrv;
static SpO2Calculator g_spo2;
static BleManager    g_ble;

// BLE HR/SpO2 notify cadence (1 Hz)
static uint32_t g_lastBleNotifyMs  = 0;
static uint32_t g_lastDetectedBpm  = 0;
static constexpr uint32_t BLE_NOTIFY_PERIOD_MS = 1000;

// Pan-Tompkins state (non-ISR)
static double   g_baseline        = 0.0;
static double   g_prevHighPass    = 0.0;
static double   g_prevIntegrated  = 0.0;
static double   g_olderIntegrated = 0.0;
static long     g_prevTimestamp   = 0;
static long     g_lastPeakMs      = LONG_MIN;
static bool     g_ptInitialized   = false;
static double   g_intBuf[INTEGRATION_WIN];
static int      g_intBufIdx       = 0;
static int      g_intBufCount     = 0;
static double   g_statMean        = 0.0;
static double   g_statVar         = 0.0;
static int      g_statN           = 0;

#ifdef USE_WIFI
static WebSocketsServer g_wsServer(WEBSOCKET_PORT);
static bool             g_wifiConnected = false;
#endif

// ============================================================
// ISR
// ============================================================
void IRAM_ATTR onMax30102Irq()
{
  portENTER_CRITICAL_ISR(&g_isrMux);
  g_fifoInterrupt = true;
  portEXIT_CRITICAL_ISR(&g_isrMux);
}

static bool consumeInterruptFlag()
{
  bool f = false;
  portENTER_CRITICAL(&g_isrMux);
  f = g_fifoInterrupt;
  g_fifoInterrupt = false;
  portEXIT_CRITICAL(&g_isrMux);
  return f;
}

// ============================================================
// Pan-Tompkins helpers (all non-ISR)
// ============================================================
static double intBufMean()
{
  double s = 0.0;
  int    n = min(g_intBufCount, INTEGRATION_WIN);
  for (int i = 0; i < n; ++i) { s += g_intBuf[i]; }
  return (n > 0) ? s / n : 0.0;
}

static void updateStats(double v)
{
  ++g_statN;
  if (g_statN == 1) { g_statMean = v; g_statVar = 0.0; return; }
  double d1 = v - g_statMean;
  g_statMean += d1 / g_statN;
  double d2 = v - g_statMean;
  g_statVar = ((g_statN - 2) * g_statVar + d1 * d2) / (g_statN - 1);
}

// Returns detected RR interval in ms, or 0 if no peak this sample.
static uint32_t panTompkinsUpdate(uint32_t ir, long timestampMs)
{
  uint32_t detectedRr = 0;

  if (!g_ptInitialized)
  {
    g_baseline      = ir;
    g_prevTimestamp = timestampMs;
    g_ptInitialized = true;
    return 0;
  }

  g_baseline += 0.01 * (ir - g_baseline);
  double hp          = ir - g_baseline;
  double derivative  = hp - g_prevHighPass;
  g_prevHighPass     = hp;
  double squared     = derivative * derivative;

  g_intBuf[g_intBufIdx] = squared;
  g_intBufIdx           = (g_intBufIdx + 1) % INTEGRATION_WIN;
  if (g_intBufCount < INTEGRATION_WIN) { ++g_intBufCount; }

  double integrated = intBufMean();
  updateStats(integrated);

  double stdDev    = sqrt(max(0.0, g_statVar));
  double threshold = g_statMean + 1.4 * stdDev;

  bool localMax    = (g_prevIntegrated > g_olderIntegrated) &&
                     (g_prevIntegrated >= integrated);
  bool overThresh  = (g_prevIntegrated > threshold);
  bool refractory  = (g_lastPeakMs == LONG_MIN) ||
                     ((g_prevTimestamp - g_lastPeakMs) > (long)REFRACTORY_MS);

  if (localMax && overThresh && refractory && g_lastPeakMs != LONG_MIN)
  {
    long rr = g_prevTimestamp - g_lastPeakMs;
    if (rr >= (long)MIN_RR_MS && rr <= (long)MAX_RR_MS)
    {
      g_hrv.submitRrInterval((uint32_t)rr);
      detectedRr = (uint32_t)rr;
    }
    g_lastPeakMs = g_prevTimestamp;
  }
  else if (localMax && overThresh && refractory && g_lastPeakMs == LONG_MIN)
  {
    g_lastPeakMs = g_prevTimestamp;
  }

  g_olderIntegrated = g_prevIntegrated;
  g_prevIntegrated  = integrated;
  g_prevTimestamp   = timestampMs;
  return detectedRr;
}

// ============================================================
// JSON helpers
// ============================================================
static void transmit(const char* payload)
{
  Serial.println(payload);
#ifdef USE_WIFI
  if (g_wifiConnected) { g_wsServer.broadcastTXT(payload); }
#endif
}

static void publishSample(uint32_t ts, uint32_t ir, uint32_t red)
{
  // Fast path: minimal stack usage, no heap allocation
  char buf[112];
  snprintf(buf, sizeof(buf),
    "{\"ts\":%lu,\"ir\":%lu,\"red\":%lu}",
    (unsigned long)ts,
    (unsigned long)ir,
    (unsigned long)red);
  transmit(buf);
}

static void publishAiResult(uint32_t ts, const HrvResult& r)
{
  // Emitted every HRV_EMIT_PERIOD_MS (~5 s) when valid
  char buf[200];
  snprintf(buf, sizeof(buf),
    "{\"ts\":%lu,\"hrv\":{\"sdnn\":%.1f,\"rmssd\":%.1f,\"rhythm\":\"%s\",\"stress\":%d}}",
    (unsigned long)ts,
    r.sdnn,
    r.rmssd,
    HrvAnalyzer::rhythmString(r.rhythm),
    HrvAnalyzer::stressInt(r.stress));
  transmit(buf);
}

// ============================================================
// FIFO read + stream
// ============================================================
static void readFifoAndStream()
{
  while (g_sensor.available())
  {
    const uint32_t ir  = g_sensor.getIR();
    const uint32_t red = g_sensor.getRed();
    const uint32_t ts  = millis();

    publishSample(ts, ir, red);

    // Feed SpO2 calculator
    g_spo2.update(red, ir);

    // Peak detection → RR interval
    uint32_t rr = panTompkinsUpdate(ir, (long)ts);
    if (rr > 0)
    {
      // Convert RR to BPM for BLE notification
      g_lastDetectedBpm = 60000 / rr;
    }

    // Queue waveform sample for BLE batching
    g_ble.queueWaveformSample(ts, ir, red);

    g_sensor.nextSample();
  }
  g_sensor.getINT1();
  g_sensor.getINT2();
}

// ============================================================
// WiFi / WebSocket setup (USE_WIFI only)
// ============================================================
#ifdef USE_WIFI
static void setupWifiWebSocket()
{
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  const uint32_t timeoutMs = 12000;
  const uint32_t startMs   = millis();
  while (WiFi.status() != WL_CONNECTED && (millis() - startMs) < timeoutMs)
  {
    delay(200);
  }

  if (WiFi.status() == WL_CONNECTED)
  {
    g_wifiConnected = true;
    g_wsServer.begin();
    g_wsServer.enableHeartbeat(15000, 3000, 2);
    Serial.printf("WiFi connected. WebSocket server on ws://%s:8080\n",
                  WiFi.localIP().toString().c_str());
  }
  else
  {
    g_wifiConnected = false;
    Serial.println("WiFi unavailable. Streaming via USB serial only.");
  }
}
#endif

// ============================================================
// Arduino entry points
// ============================================================
void setup()
{
  Serial.begin(SERIAL_BAUD);
  delay(200);

  // ---- BLE initialization (before sensor, so advertising starts early) ----
  g_ble.begin();

  Wire.begin(PIN_I2C_SDA, PIN_I2C_SCL, 400000);

  if (!g_sensor.begin(Wire, I2C_SPEED_FAST))
  {
    Serial.println("MAX30102 not detected at 0x57. Check wiring and power.");
    // Don't halt — allow BLE to stay alive for diagnostics
    Serial.println("Continuing without sensor (BLE still active).");
  }
  else
  {
    g_sensor.setup(
      LED_CURRENT_6P4MA,
      SAMPLE_AVERAGE,
      LED_MODE_RED_IR,
      SAMPLE_RATE_HZ,
      PULSE_WIDTH_US,
      ADC_RANGE_NA);

    g_sensor.enableDATARDY();
    g_sensor.enableAFULL();
    g_sensor.clearFIFO();

    pinMode(PIN_MAX30102_IRQ, INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(PIN_MAX30102_IRQ), onMax30102Irq, FALLING);
  }

#ifdef USE_WIFI
  setupWifiWebSocket();
#endif

  Serial.println("PulseMonitor firmware ready. (PlatformIO build)");
}

void loop()
{
#ifdef USE_WIFI
  g_wsServer.loop();
#endif

  // ---- 100 Hz sampling path (ISR-triggered) -----
  if (consumeInterruptFlag())
  {
    readFifoAndStream();
  }

  const uint32_t now = millis();

  // ---- BLE HR + SpO2 notify (1 Hz) ---------------
  if ((now - g_lastBleNotifyMs) >= BLE_NOTIFY_PERIOD_MS)
  {
    g_lastBleNotifyMs = now;

    if (g_lastDetectedBpm > 0)
    {
      g_ble.notifyHeartRate(static_cast<uint16_t>(g_lastDetectedBpm));
    }

    if (g_spo2.isValid())
    {
      g_ble.notifySpO2(g_spo2.getSpO2());
    }
  }

  // ---- HRV AI path (~5 s cadence) ---------------
  // Runs in loop(), never in ISR, so it is safe to call
  // floating-point / sqrt without disabling sampling.
  if (g_hrv.shouldEmit(now))
  {
    HrvResult result = g_hrv.compute();
    if (result.valid)
    {
      publishAiResult(now, result);
      g_ble.notifyHrv(result);
    }
  }
}
