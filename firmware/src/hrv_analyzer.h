#pragma once
// ============================================================
// hrv_analyzer.h — On-device HRV computation for ESP32-S3
//
// Computes SDNN, RMSSD, Rhythm class and Stress level from a
// sliding window of RR-intervals detected by a peak detector.
//
// Call HrvAnalyzer::submitRrInterval() whenever a new R-peak
// is detected.  Call HrvAnalyzer::update() from loop() every
// ~5 seconds to get the latest result.
//
// NOTE: All methods are safe to call from non-ISR context.
// ============================================================
#ifndef HRV_ANALYZER_H
#define HRV_ANALYZER_H

#include <stdint.h>

// ---- Constants ---------------------------------------------
static constexpr uint8_t  HRV_WINDOW_SIZE    = 20;   // RR intervals kept
static constexpr uint8_t  HRV_MIN_INTERVALS  = 8;    // minimum before computing
static constexpr uint32_t HRV_EMIT_PERIOD_MS = 5000; // emit every 5 s

// ---- Rhythm classification ---------------------------------
enum class Rhythm : uint8_t
{
  Normal       = 0,
  Tachycardia  = 1,  // mean RR < 500 ms → BPM > 120
  Bradycardia  = 2,  // mean RR > 1200 ms → BPM < 50
  Irregular    = 3   // CoV(RR) > 0.18 outside Brady/Tachy
};

// ---- Stress level (sympathovagal balance) ------------------
// Based on SDNN thresholds (ms):
//   ≥ 50   : Low stress      (0)
//   30–49  : Moderate        (1)
//   20–29  : High            (2)
//   < 20   : Very High       (3)
enum class StressLevel : uint8_t
{
  Low      = 0,
  Moderate = 1,
  High     = 2,
  VeryHigh = 3
};

// ---- Result struct -----------------------------------------
struct HrvResult
{
  float      sdnn;       // ms — standard deviation of NN intervals
  float      rmssd;      // ms — root mean square of successive differences
  Rhythm     rhythm;
  StressLevel stress;
  bool       valid;      // false if not enough RR intervals yet
};

// ---- Analyzer class ----------------------------------------
class HrvAnalyzer
{
public:
  HrvAnalyzer();

  // Call this whenever a new R-peak is confirmed (non-ISR).
  // rrMs: time between the previous and current R-peak in ms.
  void submitRrInterval(uint32_t rrMs);

  // Returns latest HrvResult.  Call from loop().
  // result.valid == false if fewer than HRV_MIN_INTERVALS available.
  HrvResult compute() const;

  // Returns true if the emit period has elapsed since last call.
  bool shouldEmit(uint32_t nowMs);

  // Expose rhythm/stress as C-string for JSON serialisation.
  static const char* rhythmString(Rhythm r);
  static int         stressInt(StressLevel s);

private:
  uint32_t _rr[HRV_WINDOW_SIZE]; // circular buffer of RR intervals (ms)
  uint8_t  _head;
  uint8_t  _count;
  uint32_t _lastEmitMs;

  float computeSdnn(uint8_t n) const;
  float computeRmssd(uint8_t n) const;
  float computeMean(uint8_t n) const;
};

#endif // HRV_ANALYZER_H
