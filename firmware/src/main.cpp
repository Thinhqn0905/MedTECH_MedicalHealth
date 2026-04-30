#include <Arduino.h>
#include "ble_manager.h"
#include <Wire.h>
#include "MAX30105.h"
#include "peak_detector.h"
#include "spo2_calculator.h"
#include "hrv_analyzer.h"

MAX30105 sensor;
BleManager g_ble;

PeakDetector peakDetector;
SpO2Calculator spo2Calculator;
HrvAnalyzer hrvAnalyzer;

#define I2C_SDA 8
#define I2C_SCL 9
#define I2C_SPEED_FAST 400000
#define TICK_MS 10 // 100Hz

uint32_t lastSampleMs = 0;
bool sensorReady = false;

// Time tracking for periodic BLE updates
uint32_t lastSpo2UpdateMs = 0;
uint32_t lastPeakMs = 0;

void setup() {
    Serial.begin(115200);
    delay(2000);
    Serial.println("\n\n!!! PPG System Reset - Starting !!!");

    // 1. Init I2C
    Serial.print("Init I2C (Pins 8, 9)... ");
    Wire.begin(I2C_SDA, I2C_SCL);
    Serial.println("OK");

    // 2. Init Sensor (Defensive)
    Serial.print("Init MAX30102... ");
    if(sensor.begin(Wire, I2C_SPEED_FAST)) {
        sensor.setup(0x3F, 1, 2, 100, 411, 4096);
        Serial.println("OK");
        sensorReady = true;
    } else {
        Serial.println("FAIL (Continuing anyway)");
    }

    // 3. Init BLE
    Serial.print("Init BLE... ");
    g_ble.begin();
    Serial.println("OK");

    Serial.println("System Running. Advertising BLE...");
}

void loop() {
    uint32_t now = millis();

    // 100Hz Loop
    if (now - lastSampleMs >= TICK_MS) {
        lastSampleMs = now;
        
        uint32_t ir = 0, red = 0;
        if (sensorReady) {
            ir = sensor.getIR();
            red = sensor.getRed();
        } else {
            // Fake data if sensor is missing
            ir = 50000 + (sin(now/1000.0) * 1000);
            red = 45000 + (cos(now/1000.0) * 1000);
        }

        // 1. Send raw waveform
        g_ble.queueWaveformSample(now, ir, red);

        // 2. Process SpO2
        spo2Calculator.update(red, ir);

        // 3. Process Peak Detection (HR)
        if (peakDetector.process(ir)) {
            uint32_t currentPeakMs = peakDetector.getLastPeakMs();
            if (lastPeakMs > 0) {
                uint32_t rrMs = currentPeakMs - lastPeakMs;
                
                // Submit to HRV analyzer
                hrvAnalyzer.submitRrInterval(rrMs);
                
                // Calculate and notify HR
                if (rrMs > 0) {
                    uint16_t bpm = 60000 / rrMs;
                    g_ble.notifyHeartRate(bpm);
                    // Serial.printf("Peak! BPM: %u, RR: %u ms\n", bpm, rrMs);
                }
            }
            lastPeakMs = currentPeakMs;
        }

        // 4. Notify SpO2 every 2 seconds
        if (now - lastSpo2UpdateMs >= 2000) {
            lastSpo2UpdateMs = now;
            if (spo2Calculator.isValid()) {
                g_ble.notifySpO2(spo2Calculator.getSpO2());
            }
        }

        // 5. Notify HRV every 5 seconds
        if (hrvAnalyzer.shouldEmit(now)) {
            HrvResult result = hrvAnalyzer.compute();
            if (result.valid) {
                g_ble.notifyHrv(result);
                // Serial.printf("HRV Update - SDNN: %.1f, RMSSD: %.1f\n", result.sdnn, result.rmssd);
            }
        }
    }
}
