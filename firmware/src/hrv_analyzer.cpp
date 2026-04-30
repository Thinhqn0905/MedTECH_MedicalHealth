// ============================================================
// hrv_analyzer.cpp — HRV analysis implementation (ESP32-S3)
// ============================================================
#include "hrv_analyzer.h"

#include <math.h>
#include <string.h>

// ---- Constructor -------------------------------------------
HrvAnalyzer::HrvAnalyzer()
  : _head(0), _count(0), _lastEmitMs(0)
{
  memset(_rr, 0, sizeof(_rr));
}

// ---- Public: submit a new RR interval ----------------------
void HrvAnalyzer::submitRrInterval(uint32_t rrMs)
{
  // Physiological sanity: 250 ms (240 bpm) – 2000 ms (30 bpm)
  if (rrMs < 250 || rrMs > 2000)
  {
    return;
  }

  _rr[_head] = rrMs;
  _head = (_head + 1) % HRV_WINDOW_SIZE;
  if (_count < HRV_WINDOW_SIZE)
  {
    ++_count;
  }
}

// ---- Public: compute HRV result ----------------------------
HrvResult HrvAnalyzer::compute() const
{
  HrvResult result{};
  result.valid = false;

  if (_count < HRV_MIN_INTERVALS)
  {
    return result;
  }

  const uint8_t n    = _count;
  const float   mean = computeMean(n);
  const float   sdnn = computeSdnn(n);
  const float   rmssd = computeRmssd(n);

  // ---- Rhythm classification --------------------------------
  Rhythm rhythm = Rhythm::Normal;

  if (mean > 0.0f)
  {
    if (mean < 500.0f)      // BPM > 120
    {
      rhythm = Rhythm::Tachycardia;
    }
    else if (mean > 1200.0f) // BPM < 50
    {
      rhythm = Rhythm::Bradycardia;
    }
    else
    {
      // Coefficient of variation: irregular if CoV > 0.18
      const float cov = (mean > 0.0f) ? (sdnn / mean) : 0.0f;
      if (cov > 0.18f)
      {
        rhythm = Rhythm::Irregular;
      }
    }
  }

  // ---- Stress level (SDNN-based) ---------------------------
  StressLevel stress;
  if (sdnn >= 50.0f)
  {
    stress = StressLevel::Low;
  }
  else if (sdnn >= 30.0f)
  {
    stress = StressLevel::Moderate;
  }
  else if (sdnn >= 20.0f)
  {
    stress = StressLevel::High;
  }
  else
  {
    stress = StressLevel::VeryHigh;
  }

  result.sdnn   = sdnn;
  result.rmssd  = rmssd;
  result.rhythm = rhythm;
  result.stress = stress;
  result.valid  = true;
  return result;
}

// ---- Public: time-gate for periodic emit -------------------
bool HrvAnalyzer::shouldEmit(uint32_t nowMs)
{
  if ((nowMs - _lastEmitMs) >= HRV_EMIT_PERIOD_MS)
  {
    _lastEmitMs = nowMs;
    return true;
  }
  return false;
}

// ---- Public: string helpers --------------------------------
const char* HrvAnalyzer::rhythmString(Rhythm r)
{
  switch (r)
  {
    case Rhythm::Normal:      return "Normal";
    case Rhythm::Tachycardia: return "Tachycardia";
    case Rhythm::Bradycardia: return "Bradycardia";
    case Rhythm::Irregular:   return "Irregular";
    default:                  return "Unknown";
  }
}

int HrvAnalyzer::stressInt(StressLevel s)
{
  return static_cast<int>(s);
}

// ---- Private helpers ----------------------------------------
float HrvAnalyzer::computeMean(uint8_t n) const
{
  double sum = 0.0;
  for (uint8_t i = 0; i < n; ++i)
  {
    const uint8_t idx = (_head + HRV_WINDOW_SIZE - n + i) % HRV_WINDOW_SIZE;
    sum += _rr[idx];
  }
  return static_cast<float>(sum / n);
}

float HrvAnalyzer::computeSdnn(uint8_t n) const
{
  const float mean = computeMean(n);
  double sumSq = 0.0;
  for (uint8_t i = 0; i < n; ++i)
  {
    const uint8_t idx = (_head + HRV_WINDOW_SIZE - n + i) % HRV_WINDOW_SIZE;
    const float diff = static_cast<float>(_rr[idx]) - mean;
    sumSq += diff * diff;
  }
  return sqrtf(static_cast<float>(sumSq / n));
}

float HrvAnalyzer::computeRmssd(uint8_t n) const
{
  if (n < 2)
  {
    return 0.0f;
  }

  double sumSq = 0.0;
  for (uint8_t i = 1; i < n; ++i)
  {
    const uint8_t idx1 = (_head + HRV_WINDOW_SIZE - n + i - 1) % HRV_WINDOW_SIZE;
    const uint8_t idx2 = (_head + HRV_WINDOW_SIZE - n + i)     % HRV_WINDOW_SIZE;
    const float diff = static_cast<float>(_rr[idx2]) - static_cast<float>(_rr[idx1]);
    sumSq += diff * diff;
  }
  return sqrtf(static_cast<float>(sumSq / (n - 1)));
}
