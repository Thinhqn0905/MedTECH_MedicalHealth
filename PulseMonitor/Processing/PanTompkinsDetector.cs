using PulseMonitor.Hardware;

namespace PulseMonitor.Processing;

public sealed class PanTompkinsDetector
{
  private const int IntegrationWindow = 12;
  private const int RefractoryMs = 300;
  private const int MaxRrIntervals = 8;
  private const int MinAcceptedRr = 250;
  private const int MaxAcceptedRr = 1500;

  private readonly Queue<double> _integratedWindow = new();
  private readonly Queue<long> _rrIntervals = new();

  private double _baseline;
  private bool _isInitialized;
  private double _previousHighPass;
  private double _previousIntegrated;
  private double _olderIntegrated;
  private long _previousTimestamp;
  private long _lastPeakTimestamp = long.MinValue;

  private int _statsCount;
  private double _integratedMean;
  private double _integratedVariance;
  private int _lastBpm;
  private double _integratedSum;

  public int Update(IRSample sample)
  {
    if (!_isInitialized)
    {
      _baseline = sample.IR;
      _previousTimestamp = sample.Timestamp;
      _isInitialized = true;
      return _lastBpm;
    }

    double raw = sample.IR;
    _baseline += 0.01 * (raw - _baseline);

    double highPass = raw - _baseline;
    double derivative = highPass - _previousHighPass;
    _previousHighPass = highPass;

    double squared = derivative * derivative;
    _integratedSum += squared;
    _integratedWindow.Enqueue(squared);
    if (_integratedWindow.Count > IntegrationWindow)
    {
      _integratedSum -= _integratedWindow.Dequeue();
    }

    // Fast running sum instead of iterating the queue
    double integrated = _integratedWindow.Count == 0 ? 0 : _integratedSum / _integratedWindow.Count;
    UpdateAdaptiveStats(integrated);

    double stdDev = Math.Sqrt(Math.Max(0, _integratedVariance));
    double threshold = _integratedMean + (1.4 * stdDev);

    if (IsPeak(integrated, threshold))
    {
      RegisterPeak(_previousTimestamp);
    }

    _olderIntegrated = _previousIntegrated;
    _previousIntegrated = integrated;
    _previousTimestamp = sample.Timestamp;

    return _lastBpm;
  }

  private bool IsPeak(double currentIntegrated, double threshold)
  {
    bool localMaximum = _previousIntegrated > _olderIntegrated && _previousIntegrated >= currentIntegrated;
    bool passesThreshold = _previousIntegrated > threshold;
    bool refractoryElapsed = _lastPeakTimestamp == long.MinValue || (_previousTimestamp - _lastPeakTimestamp) > RefractoryMs;
    return localMaximum && passesThreshold && refractoryElapsed;
  }

  private void RegisterPeak(long peakTimestamp)
  {
    if (_lastPeakTimestamp != long.MinValue)
    {
      long rr = peakTimestamp - _lastPeakTimestamp;
      if (rr >= MinAcceptedRr && rr <= MaxAcceptedRr)
      {
        _rrIntervals.Enqueue(rr);
        if (_rrIntervals.Count > MaxRrIntervals)
        {
          _rrIntervals.Dequeue();
        }

        double meanRr = ComputeMean(_rrIntervals);
        if (meanRr > 0)
        {
          int bpm = (int)Math.Round(60000.0 / meanRr);
          _lastBpm = Math.Clamp(bpm, 40, 220);
        }
      }
    }

    _lastPeakTimestamp = peakTimestamp;
  }

  private void UpdateAdaptiveStats(double value)
  {
    _statsCount++;

    if (_statsCount == 1)
    {
      _integratedMean = value;
      _integratedVariance = 0;
      return;
    }

    double delta = value - _integratedMean;
    _integratedMean += delta / _statsCount;
    double delta2 = value - _integratedMean;
    _integratedVariance = ((_statsCount - 2) * _integratedVariance + (delta * delta2)) / (_statsCount - 1);
  }

  private static double ComputeMean(IEnumerable<long> values)
  {
    double sum = 0;
    int count = 0;

    foreach (long value in values)
    {
      sum += value;
      count++;
    }

    return count == 0 ? 0 : sum / count;
  }
}
