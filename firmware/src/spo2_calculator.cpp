// ============================================================
// spo2_calculator.cpp — SpO2 via Beer-Lambert R-ratio
// ============================================================
#include "spo2_calculator.h"

#include <math.h>

SpO2Calculator::SpO2Calculator()
  : _dcRed(0.0), _dcIr(0.0),
    _acRedMax(0.0), _acRedMin(1e9),
    _acIrMax(0.0),  _acIrMin(1e9),
    _spo2(0), _valid(false), _sampleCount(0)
{
}

void SpO2Calculator::update(uint32_t red, uint32_t ir)
{
  const double dRed = static_cast<double>(red);
  const double dIr  = static_cast<double>(ir);

  // ---- DC baseline tracking (slow IIR) ----
  if (_dcRed == 0.0)
  {
    // First sample: seed the baseline
    _dcRed = dRed;
    _dcIr  = dIr;
  }
  else
  {
    _dcRed += DC_ALPHA * (dRed - _dcRed);
    _dcIr  += DC_ALPHA * (dIr  - _dcIr);
  }

  // ---- AC envelope tracking (min/max within window) ----
  const double acRed = dRed - _dcRed;
  const double acIr  = dIr  - _dcIr;

  if (acRed > _acRedMax) _acRedMax = acRed;
  if (acRed < _acRedMin) _acRedMin = acRed;
  if (acIr  > _acIrMax)  _acIrMax  = acIr;
  if (acIr  < _acIrMin)  _acIrMin  = acIr;

  ++_sampleCount;

  // ---- Periodic recalculation ----
  if (_sampleCount >= CALC_WINDOW)
  {
    const double acRedPP = _acRedMax - _acRedMin;
    const double acIrPP  = _acIrMax  - _acIrMin;

    if (_dcRed > 1.0 && _dcIr > 1.0 && acIrPP > 1.0)
    {
      const double R = (acRedPP / _dcRed) / (acIrPP / _dcIr);

      // Empirical linear model (typical for MAX30102)
      // SpO2 ≈ 110 - 25 * R
      double spo2 = 110.0 - 25.0 * R;

      // Clamp to physiological range
      if (spo2 > 100.0) spo2 = 100.0;
      if (spo2 < 0.0)   spo2 = 0.0;

      _spo2  = static_cast<uint8_t>(spo2 + 0.5);
      _valid = true;
    }

    // Reset AC envelope for next window
    _acRedMax = -1e9;
    _acRedMin =  1e9;
    _acIrMax  = -1e9;
    _acIrMin  =  1e9;
    _sampleCount = 0;
  }
}

uint8_t SpO2Calculator::getSpO2() const
{
  return _spo2;
}

bool SpO2Calculator::isValid() const
{
  return _valid;
}
