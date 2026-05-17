using PulseMonitor.Hardware;

namespace PulseMonitor.Processing;

public readonly record struct PpgMetrics(int Bpm, int SpO2, int RrMs);

public sealed class PpgMetricsProcessor
{
  private const int WindowSize = 800;
  private const int MinSamples = 220;
  private const int RecomputeEverySamples = 25;
  private const int SampleRateHz = 100;
  private const int MinLag = 40;   // 150 BPM
  private const int MaxLag = 150;  // 40 BPM

  private readonly Queue<IRSample> _window = new();
  private readonly List<double> _irScratch = new(WindowSize);
  private int _samplesSinceCompute;
  private int _lastBpm;
  private int _lastSpO2;
  private int _lastRrMs;

  public string LastDebug { get; private set; } = "warming";

  public PpgMetrics Update(IRSample sample)
  {
    _window.Enqueue(sample);
    while (_window.Count > WindowSize)
    {
      _window.Dequeue();
    }

    _samplesSinceCompute++;
    if (_window.Count < MinSamples || _samplesSinceCompute < RecomputeEverySamples)
    {
      return new PpgMetrics(_lastBpm, _lastSpO2, _lastRrMs);
    }

    _samplesSinceCompute = 0;

    IRSample[] samples = _window.ToArray();
    int bpm = EstimateBpm(samples);
    int spo2 = EstimateSpO2(samples);

    if (bpm > 0)
    {
      _lastBpm = _lastBpm <= 0 ? bpm : (int)Math.Round((_lastBpm * 0.7) + (bpm * 0.3));
      _lastRrMs = (int)Math.Round(60000.0 / _lastBpm);
    }

    if (spo2 > 0)
    {
      _lastSpO2 = _lastSpO2 <= 0 ? spo2 : (int)Math.Round((_lastSpO2 * 0.8) + (spo2 * 0.2));
    }

    return new PpgMetrics(_lastBpm, _lastSpO2, _lastRrMs);
  }

  private int EstimateBpm(IRSample[] samples)
  {
    _irScratch.Clear();

    double mean = 0;
    foreach (IRSample sample in samples)
    {
      mean += sample.IR;
    }
    mean /= samples.Length;

    double variance = 0;
    foreach (IRSample sample in samples)
    {
      double centered = sample.IR - mean;
      _irScratch.Add(centered);
      variance += centered * centered;
    }

    double std = Math.Sqrt(variance / samples.Length);
    if (std < Math.Max(5.0, mean * 0.00005))
    {
      LastDebug = $"weak pulse mean={mean:F0} std={std:F1}";
      return 0;
    }

    int peakBpm = EstimateBpmFromPeaks(_irScratch, std);
    if (peakBpm > 0)
    {
      LastDebug = $"peak bpm={peakBpm} mean={mean:F0} std={std:F1}";
      return peakBpm;
    }

    double bestScore = double.MinValue;
    int bestLag = 0;
    int maxLag = Math.Min(MaxLag, _irScratch.Count - 2);

    for (int lag = MinLag; lag <= maxLag; lag++)
    {
      double numerator = 0;
      double leftEnergy = 0;
      double rightEnergy = 0;

      for (int i = lag; i < _irScratch.Count; i++)
      {
        double a = _irScratch[i];
        double b = _irScratch[i - lag];
        numerator += a * b;
        leftEnergy += a * a;
        rightEnergy += b * b;
      }

      double denom = Math.Sqrt(leftEnergy * rightEnergy);
      if (denom <= 0)
      {
        continue;
      }

      double score = numerator / denom;
      if (score > bestScore)
      {
        bestScore = score;
        bestLag = lag;
      }
    }

    if (bestLag == 0 || bestScore < 0.05)
    {
      LastDebug = $"no rhythm score={bestScore:F2} mean={mean:F0} std={std:F1}";
      return 0;
    }

    int bpm = (int)Math.Round(60.0 * SampleRateHz / bestLag);
    LastDebug = $"auto bpm={bpm} lag={bestLag} score={bestScore:F2} mean={mean:F0} std={std:F1}";
    return Math.Clamp(bpm, 40, 150);
  }

  private static int EstimateBpmFromPeaks(List<double> centeredIr, double std)
  {
    int bpm = EstimateBpmFromPolarity(centeredIr, std, polarity: 1.0);
    if (bpm > 0)
    {
      return bpm;
    }

    return EstimateBpmFromPolarity(centeredIr, std, polarity: -1.0);
  }

  private static int EstimateBpmFromPolarity(List<double> centeredIr, double std, double polarity)
  {
    double threshold = Math.Max(8.0, std * 0.35);
    List<int> peaks = [];
    int lastPeak = -1000;

    for (int i = 2; i < centeredIr.Count - 2; i++)
    {
      double prev = centeredIr[i - 1] * polarity;
      double curr = centeredIr[i] * polarity;
      double next = centeredIr[i + 1] * polarity;

      if (curr < threshold || curr < prev || curr < next || (i - lastPeak) < 35)
      {
        continue;
      }

      peaks.Add(i);
      lastPeak = i;
    }

    if (peaks.Count < 3)
    {
      return 0;
    }

    List<int> intervals = [];
    for (int i = 1; i < peaks.Count; i++)
    {
      int lag = peaks[i] - peaks[i - 1];
      if (lag >= MinLag && lag <= MaxLag)
      {
        intervals.Add(lag);
      }
    }

    if (intervals.Count < 2)
    {
      return 0;
    }

    intervals.Sort();
    int medianLag = intervals[intervals.Count / 2];
    int bpm = (int)Math.Round(60.0 * SampleRateHz / medianLag);
    return Math.Clamp(bpm, 40, 150);
  }

  private static int EstimateSpO2(IRSample[] samples)
  {
    double dcIr = 0;
    double dcRed = 0;

    foreach (IRSample sample in samples)
    {
      dcIr += sample.IR;
      dcRed += sample.Red;
    }

    dcIr /= samples.Length;
    dcRed /= samples.Length;
    if (dcIr <= 1000 || dcRed <= 1000)
    {
      return 0;
    }

    double irVar = 0;
    double redVar = 0;
    foreach (IRSample sample in samples)
    {
      double irCentered = sample.IR - dcIr;
      double redCentered = sample.Red - dcRed;
      irVar += irCentered * irCentered;
      redVar += redCentered * redCentered;
    }

    double acIr = Math.Sqrt(irVar / samples.Length);
    double acRed = Math.Sqrt(redVar / samples.Length);
    if (acIr < Math.Max(5.0, dcIr * 0.00005) || acRed < Math.Max(5.0, dcRed * 0.00005))
    {
      return 0;
    }

    double ratio = (acRed / dcRed) / (acIr / dcIr);
    if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
    {
      return 0;
    }

    int spo2 = (int)Math.Round(110.0 - (25.0 * ratio));
    return Math.Clamp(spo2, 70, 100);
  }
}
