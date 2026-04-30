#include "peak_detector.h"
#include <Arduino.h>

PeakDetector::PeakDetector() 
    : _baseline(0), _prevHp(0), _movingAvg(0), _lastPeakMs(0), _threshold(500) {
}

bool PeakDetector::process(uint32_t irSample) {
    float sample = (float)irSample;

    // 1. DC Removal (High-pass filter)
    if (_baseline == 0) _baseline = sample;
    _baseline += ALPHA * (sample - _baseline);
    float hp = sample - _baseline;

    // 2. Derivative
    float derivative = hp - _prevHp;
    _prevHp = hp;

    // 3. Squaring
    float squared = derivative * derivative;

    // 4. Moving Average (simplified sliding window)
    _movingAvg += (squared - _movingAvg) / WINDOW_SIZE;

    // 5. Adaptive Thresholding & Peak Detection
    bool peakDetected = false;
    uint32_t now = millis();

    if (_movingAvg > _threshold && (now - _lastPeakMs) > REFRACTORY) {
        _lastPeakMs = now;
        peakDetected = true;
        // Adjust threshold downwards slowly
        _threshold = _movingAvg * 0.6f; 
    } else {
        // Slowly decay threshold to handle amplitude changes
        _threshold *= 0.9995f;
        if (_threshold < 100) _threshold = 100; // Floor
    }

    return peakDetected;
}
