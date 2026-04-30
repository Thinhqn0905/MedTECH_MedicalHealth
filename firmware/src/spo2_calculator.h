#pragma once
// ============================================================
// spo2_calculator.h — On-device SpO2 via Beer-Lambert R-ratio
//
// Tracks DC baseline and AC amplitude for Red and IR channels.
// Call update() for every raw sample pair.
// Call getSpO2() to retrieve latest SpO2 percentage.
// ============================================================
#ifndef SPO2_CALCULATOR_H
#define SPO2_CALCULATOR_H

#include <stdint.h>

class SpO2Calculator
{
public:
  SpO2Calculator();

  /// Feed one raw sample pair (called at 100 Hz).
  void update(uint32_t red, uint32_t ir);

  /// Latest SpO2 percentage (0–100). Returns 0 if not yet valid.
  uint8_t getSpO2() const;

  /// Returns true once enough samples accumulated for a valid reading.
  bool isValid() const;

private:
  // DC baselines (slow IIR, tau ~ 2 s)
  double _dcRed;
  double _dcIr;

  // AC peak-to-peak tracking (reset each cardiac cycle window)
  double _acRedMax, _acRedMin;
  double _acIrMax,  _acIrMin;

  // Calculated SpO2
  uint8_t  _spo2;
  bool     _valid;

  // Sample counter for periodic recalculation
  uint16_t _sampleCount;

  static constexpr double  DC_ALPHA       = 0.005;  // ~2 s time constant @ 100 Hz
  static constexpr uint16_t CALC_WINDOW   = 200;    // recalculate every 2 s (200 samples @ 100 Hz)
};

#endif // SPO2_CALCULATOR_H
