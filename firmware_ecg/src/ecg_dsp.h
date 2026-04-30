#pragma once
// ============================================================
// ecg_dsp.h — ESP-DSP based ECG signal processing
//
// Implements the AFDB preprocessing pipeline on-device:
// 1. Bandpass IIR Biquad (Butterworth order 4, 0.5–30 Hz)
// 2. Notch IIR Biquad (50 Hz, Q=30)
// 3. Transient removal (first 2000 samples)
// 4. Z-Score normalization + clip [-3, 3]
//
// Filter coefficients were generated from scipy.signal.butter()
// and scipy.signal.iirnotch() to match the AFDB training pipeline.
// ============================================================
#ifndef ECG_DSP_H
#define ECG_DSP_H

#include <stdint.h>

/// Number of SOS stages for bandpass filter
#define BP_NUM_STAGES 4

/// Number of SOS stages for notch filter
#define NOTCH_NUM_STAGES 1

/// Total filter transient samples to discard after boot (8 seconds at 250 Hz)
#define FILTER_TRANSIENT_SAMPLES 2000

/// AI inference window size: 10 seconds at 250 Hz = 2500 samples
#define AI_WINDOW_SIZE 2500

class EcgDsp
{
public:
  EcgDsp();

  /// Initialize ESP-DSP biquad filters with pre-computed coefficients.
  void begin();

  /// Process one raw ADC sample (12-bit unsigned).
  /// Returns the filtered float value.
  /// Internally handles transient discarding.
  float processSample(uint16_t rawAdc);

  /// Returns true once transient period has passed.
  bool isStable() const;

  /// Check if the AI inference window buffer is full (2500 samples).
  bool isWindowReady() const;

  /// Get pointer to the normalized window buffer for AI inference.
  /// After calling, the window is reset.
  const float* getWindowBuffer();

  /// Get the number of valid samples in the current window.
  uint16_t getWindowCount() const;

  /// Reset the window buffer (call after inference).
  void resetWindow();

  // Quality metrics for current window
  bool isWindowClean() const;

private:
  // IIR Biquad delay lines
  float _bpDelay[BP_NUM_STAGES][2];
  float _notchDelay[NOTCH_NUM_STAGES][2];

  // Transient tracking
  uint32_t _sampleCount;

  // AI inference window buffer
  float    _windowBuf[AI_WINDOW_SIZE];
  float    _normBuf[AI_WINDOW_SIZE];   // Normalized copy for inference
  uint16_t _windowIdx;
  bool     _windowReady;

  // Internal: apply biquad cascade
  float applyFilters(float sample);

  // Internal: normalize and check quality
  void normalizeWindow();
};

#endif // ECG_DSP_H
