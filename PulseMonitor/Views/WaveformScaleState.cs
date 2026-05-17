namespace PulseMonitor.Views;

internal sealed class WaveformScaleState
{
  private readonly float _minHalfRange;
  private readonly float _maxHalfRange;
  private readonly float _attack;
  private readonly float _release;
  private float _halfRange;

  public WaveformScaleState(float minHalfRange, float maxHalfRange, float attack = 0.35f, float release = 0.04f)
  {
    _minHalfRange = minHalfRange;
    _maxHalfRange = maxHalfRange;
    _attack = attack;
    _release = release;
    _halfRange = minHalfRange;
  }

  public float HalfRange => _halfRange;

  public void UpdateFromCenteredValues(List<float> centeredValues, float padding = 1.25f)
  {
    if (centeredValues.Count < 8)
    {
      return;
    }

    centeredValues.Sort(static (a, b) => Math.Abs(a).CompareTo(Math.Abs(b)));
    int index = Math.Clamp((int)(centeredValues.Count * 0.95f), 0, centeredValues.Count - 1);
    float target = Math.Abs(centeredValues[index]) * padding;
    target = Math.Clamp(target, _minHalfRange, _maxHalfRange);

    float factor = target > _halfRange ? _attack : _release;
    _halfRange += (target - _halfRange) * factor;
    _halfRange = Math.Clamp(_halfRange, _minHalfRange, _maxHalfRange);
  }
}
