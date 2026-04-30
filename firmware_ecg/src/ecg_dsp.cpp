// ============================================================
// ecg_dsp.cpp — ESP-DSP based ECG signal processing
//
// Filter coefficients generated from Python (scipy.signal):
//   Bandpass: butter(4, [0.5/125, 30/125], btype='band', output='sos')
//   Notch:    iirnotch(50/125, 30) → tf2sos
//
// Each SOS stage has 6 coefficients: [b0, b1, b2, a0, a1, a2]
// ESP-DSP dsps_biquad_f32 expects: [b0, b1, b2, a1, a2]
// (a0 is always 1.0, ESP-DSP omits it — we use 5-coeff format)
// ============================================================
#include "ecg_dsp.h"

#include <math.h>
#include <string.h>

// Manual IIR Biquad (Direct Form II Transposed)
// Equivalent to ESP-DSP dsps_biquad_f32 but without library dependency.
// coeffs = {b0, b1, b2, a1, a2}  (5 floats, a0 assumed = 1.0)
// delay  = {w1, w2}              (2 floats state)
static inline void biquad_f32(const float* input, float* output, int len,
                               const float* coeffs, float* delay)
{
  for (int i = 0; i < len; i++)
  {
    float x  = input[i];
    float b0 = coeffs[0];
    float b1 = coeffs[1];
    float b2 = coeffs[2];
    float a1 = coeffs[3];
    float a2 = coeffs[4];

    float y = b0 * x + delay[0];
    delay[0] = b1 * x - a1 * y + delay[1];
    delay[1] = b2 * x - a2 * y;
    output[i] = y;
  }
}

// ============================================================
// Pre-computed SOS coefficients (from scipy)
// Format for ESP-DSP: {b0, b1, b2, a1, a2} (5 elements, no a0)
// ============================================================

// Bandpass Butterworth order 4, [0.5, 30.0] Hz @ Fs=250
static const float BP_COEFFS[BP_NUM_STAGES][5] = {
  // Stage 0
  { 8.4286279452e-03f,  1.6857255890e-02f,  8.4286279452e-03f,
   -9.1750842918e-01f,  2.3714874906e-01f },
  // Stage 1
  { 1.0000000000e+00f,  2.0000000000e+00f,  1.0000000000e+00f,
   -1.1658778519e+00f,  5.9483537713e-01f },
  // Stage 2
  { 1.0000000000e+00f, -2.0000000000e+00f,  1.0000000000e+00f,
   -1.9763660388e+00f,  9.7653094452e-01f },
  // Stage 3
  { 1.0000000000e+00f, -2.0000000000e+00f,  1.0000000000e+00f,
   -1.9904868225e+00f,  9.9064539107e-01f },
};

// Notch 50 Hz, Q=30 @ Fs=250
static const float NOTCH_COEFFS[NOTCH_NUM_STAGES][5] = {
  // Stage 0
  { 9.7948276098e-01f, -6.0535363768e-01f,  9.7948276098e-01f,
   -6.0535363768e-01f,  9.5896552196e-01f },
};

// ============================================================
// Constructor
// ============================================================
EcgDsp::EcgDsp()
  : _sampleCount(0), _windowIdx(0), _windowReady(false)
{
  memset(_bpDelay,    0, sizeof(_bpDelay));
  memset(_notchDelay, 0, sizeof(_notchDelay));
  memset(_windowBuf,  0, sizeof(_windowBuf));
  memset(_normBuf,    0, sizeof(_normBuf));
}

// ============================================================
// begin() — init ESP-DSP (no special init needed for biquad)
// ============================================================
void EcgDsp::begin()
{
  // Clear filter delay lines
  memset(_bpDelay,    0, sizeof(_bpDelay));
  memset(_notchDelay, 0, sizeof(_notchDelay));
  _sampleCount = 0;
  _windowIdx   = 0;
  _windowReady = false;
}

// ============================================================
// applyFilters() — cascade bandpass + notch
// ============================================================
float EcgDsp::applyFilters(float sample)
{
  float x = sample;

  // Bandpass cascade (4 stages)
  for (int i = 0; i < BP_NUM_STAGES; i++)
  {
    float y;
    // dsps_biquad_f32 processes an array; we process 1 sample at a time
    biquad_f32(&x, &y, 1, (float*)BP_COEFFS[i], _bpDelay[i]);
    x = y;
  }

  // Notch cascade (1 stage)
  for (int i = 0; i < NOTCH_NUM_STAGES; i++)
  {
    float y;
    biquad_f32(&x, &y, 1, (float*)NOTCH_COEFFS[i], _notchDelay[i]);
    x = y;
  }

  return x;
}

// ============================================================
// processSample() — main entry point per ADC sample
// ============================================================
float EcgDsp::processSample(uint16_t rawAdc)
{
  // Convert 12-bit unsigned ADC to float voltage centered around 0
  // ADC range 0–4095, midpoint 2048
  float sample = (float)((int)rawAdc - 2048) / 2048.0f;

  // Apply IIR filter chain
  float filtered = applyFilters(sample);

  ++_sampleCount;

  // Discard transient samples
  if (_sampleCount <= FILTER_TRANSIENT_SAMPLES)
  {
    return filtered;  // Return filtered value but don't buffer it
  }

  // Buffer for AI inference window
  if (_windowIdx < AI_WINDOW_SIZE)
  {
    _windowBuf[_windowIdx] = filtered;
    ++_windowIdx;

    if (_windowIdx >= AI_WINDOW_SIZE)
    {
      normalizeWindow();
      _windowReady = true;
    }
  }

  return filtered;
}

// ============================================================
// normalizeWindow() — Z-score + clip [-3, 3]
// ============================================================
void EcgDsp::normalizeWindow()
{
  // Compute mean
  double sum = 0.0;
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    sum += _windowBuf[i];
  }
  float mean = (float)(sum / AI_WINDOW_SIZE);

  // Compute std
  double varSum = 0.0;
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    float d = _windowBuf[i] - mean;
    varSum += d * d;
  }
  float sigma = sqrtf((float)(varSum / AI_WINDOW_SIZE));

  // Z-score normalize and clip to [-3, 3]
  float invSigma = 1.0f / (sigma + 1e-7f);
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    float z = (_windowBuf[i] - mean) * invSigma;
    // Hard clip for INT8 quantization compatibility
    if (z > 3.0f)  z = 3.0f;
    if (z < -3.0f) z = -3.0f;
    _normBuf[i] = z;
  }
}

// ============================================================
// Accessors
// ============================================================
bool EcgDsp::isStable() const
{
  return _sampleCount > FILTER_TRANSIENT_SAMPLES;
}

bool EcgDsp::isWindowReady() const
{
  return _windowReady;
}

const float* EcgDsp::getWindowBuffer()
{
  return _normBuf;
}

uint16_t EcgDsp::getWindowCount() const
{
  return _windowIdx;
}

void EcgDsp::resetWindow()
{
  _windowIdx   = 0;
  _windowReady = false;
}

bool EcgDsp::isWindowClean() const
{
  if (!_windowReady) return false;

  // Quality check 1: Flatline detection (std < 0.00488)
  double sum = 0.0;
  for (int i = 0; i < AI_WINDOW_SIZE; i++) { sum += _windowBuf[i]; }
  float mean = (float)(sum / AI_WINDOW_SIZE);

  double varSum = 0.0;
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    float d = _windowBuf[i] - mean;
    varSum += d * d;
  }
  float sigma = sqrtf((float)(varSum / AI_WINDOW_SIZE));
  if (sigma < 0.00488f) return false;

  // Quality check 2: Saturation (|max| > 9.5)
  float maxAbs = 0.0f;
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    float a = fabsf(_windowBuf[i]);
    if (a > maxAbs) maxAbs = a;
  }
  if (maxAbs > 9.5f) return false;

  // Quality check 3: Partial flatline (>40% near zero)
  int flatCount = 0;
  for (int i = 0; i < AI_WINDOW_SIZE; i++)
  {
    if (fabsf(_windowBuf[i]) < 0.005f) flatCount++;
  }
  if ((float)flatCount / AI_WINDOW_SIZE > 0.4f) return false;

  return true;
}
