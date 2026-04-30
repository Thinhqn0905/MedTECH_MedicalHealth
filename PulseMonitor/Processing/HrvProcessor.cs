using PulseMonitor.Hardware;

namespace PulseMonitor.Processing;

/// <summary>
/// Client-side HRV processor — mirrors the on-device algorithm.
/// Used in the MAUI app to supplement / verify firmware HRV output,
/// and to enable offline HRV computation for sessions recorded before
/// firmware upgrade.
/// </summary>
public sealed class HrvProcessor
{
  private const int WindowSize   = 20;
  private const int MinIntervals = 8;

  private readonly Queue<long> _rrMs = new();
  private long _lastPeakTimestamp    = long.MinValue;

  /// <summary>
  /// Feed a new peak timestamp (ms).  Call whenever a BPM peak is detected.
  /// Returns null until enough intervals are collected.
  /// </summary>
  public HrvMetrics? OnPeak(long timestampMs)
  {
    if (_lastPeakTimestamp != long.MinValue)
    {
      long rr = timestampMs - _lastPeakTimestamp;
      if (rr is >= 250 and <= 2000)
      {
        _rrMs.Enqueue(rr);
        if (_rrMs.Count > WindowSize)
        {
          _rrMs.Dequeue();
        }
      }
    }

    _lastPeakTimestamp = timestampMs;

    if (_rrMs.Count < MinIntervals)
    {
      return null;
    }

    return Compute(_rrMs);
  }

  public void Reset()
  {
    _rrMs.Clear();
    _lastPeakTimestamp = long.MinValue;
  }

  // ---- Static computation -----------------------------------------------

  public static HrvMetrics Compute(IEnumerable<long> rrIntervals)
  {
    long[] arr = rrIntervals.ToArray();
    int    n   = arr.Length;

    if (n == 0)
    {
      return new HrvMetrics(0, 0, "Unknown", 0);
    }

    double mean   = arr.Average(x => (double)x);
    double sdnn   = Math.Sqrt(arr.Average(x => Math.Pow(x - mean, 2)));
    double rmssd  = n >= 2
      ? Math.Sqrt(arr.Zip(arr.Skip(1), (a, b) => Math.Pow(b - a, 2)).Average())
      : 0;

    string rhythm = ClassifyRhythm(mean, sdnn);
    int    stress = ClassifyStress(sdnn);

    return new HrvMetrics((float)sdnn, (float)rmssd, rhythm, stress);
  }

  private static string ClassifyRhythm(double meanRr, double sdnn)
  {
    if (meanRr < 500) return "Tachycardia";
    if (meanRr > 1200) return "Bradycardia";
    double cov = meanRr > 0 ? sdnn / meanRr : 0;
    return cov > 0.18 ? "Irregular" : "Normal";
  }

  private static int ClassifyStress(double sdnn) =>
    sdnn switch
    {
      >= 50 => 0,
      >= 30 => 1,
      >= 20 => 2,
      _     => 3
    };
}

/// <summary>
/// HRV metrics for display in the AI Diagnostics tab.
/// </summary>
public readonly record struct HrvMetrics(
    float  Sdnn,
    float  Rmssd,
    string Rhythm,
    int    StressLevel);
