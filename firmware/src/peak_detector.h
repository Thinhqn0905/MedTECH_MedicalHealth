#ifndef PEAK_DETECTOR_H
#define PEAK_DETECTOR_H

#include <stdint.h>

class PeakDetector {
public:
    PeakDetector();
    
    // Process one sample and return true if a peak (heartbeat) was detected
    bool process(uint32_t irSample);
    
    // Returns the timestamp of the last detected peak
    uint32_t getLastPeakMs() const { return _lastPeakMs; }

private:
    float _baseline;
    float _prevHp;
    float _movingAvg;
    uint32_t _lastPeakMs;
    float _threshold;
    
    static constexpr float ALPHA = 0.01f;      // DC removal constant
    static constexpr int WINDOW_SIZE = 12;     // Moving average window
    static constexpr uint32_t REFRACTORY = 300; // 300ms refractory period
};

#endif
