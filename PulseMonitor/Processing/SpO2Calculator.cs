using PulseMonitor.Hardware;

namespace PulseMonitor.Processing;

public sealed class SpO2Calculator
{
  private readonly Queue<double> _irWindow = new();
  private readonly Queue<double> _redWindow = new();
  private readonly int _windowSize;
  private int _lastSpO2;
  private long _sampleCount;

  private static readonly int[] SpO2Lookup = BuildLookupTable();

  public SpO2Calculator(int windowSize = 400)
  {
    if (windowSize < 50)
    {
      throw new ArgumentOutOfRangeException(nameof(windowSize), "Window must be at least 50 samples.");
    }

    _windowSize = windowSize;
  }

  public int Update(IRSample sample)
  {
    _irWindow.Enqueue(sample.IR);
    _redWindow.Enqueue(sample.Red);

    if (_irWindow.Count > _windowSize)
    {
      _irWindow.Dequeue();
      _redWindow.Dequeue();
    }

    _sampleCount++;

    // PERFORMANCE OPTIMIZATION:
    // Calculating SpO2 means iterating over two 400-item queues. If run at 100Hz, 
    // it triggers 160,000 loop operations per second.
    // Physiologically, SpO2 takes 3-5 seconds to change. We throttle it to 1Hz 
    // (once every 100 samples) to reduce CPU load of this DSP operation by 99%.
    if (_irWindow.Count < (_windowSize / 4) || _sampleCount % 100 != 0)
    {
      return _lastSpO2;
    }

    double dcIr = ComputeMean(_irWindow);
    double dcRed = ComputeMean(_redWindow);

    if (dcIr <= 0 || dcRed <= 0)
    {
      return _lastSpO2;
    }

    double acIr = ComputeStandardDeviation(_irWindow, dcIr);
    double acRed = ComputeStandardDeviation(_redWindow, dcRed);

    if (acIr <= 0 || acRed <= 0)
    {
      return _lastSpO2;
    }

    double ratio = (acRed / dcRed) / (acIr / dcIr);
    int index = Math.Clamp((int)Math.Round(ratio * 100), 0, SpO2Lookup.Length - 1);

    _lastSpO2 = Math.Clamp(SpO2Lookup[index], 0, 100);
    return _lastSpO2;
  }

  private static int[] BuildLookupTable()
  {
    int[] table = new int[201];

    for (int i = 0; i < table.Length; i++)
    {
      double r = i / 100.0;
      int estimatedSpO2 = (int)Math.Round(110.0 - (25.0 * r));
      table[i] = Math.Clamp(estimatedSpO2, 70, 100);
    }

    return table;
  }

  private static double ComputeMean(IEnumerable<double> values)
  {
    double sum = 0;
    int count = 0;

    foreach (double value in values)
    {
      sum += value;
      count++;
    }

    return count == 0 ? 0 : sum / count;
  }

  private static double ComputeStandardDeviation(IEnumerable<double> values, double mean)
  {
    double sumSquares = 0;
    int count = 0;

    foreach (double value in values)
    {
      double centered = value - mean;
      sumSquares += centered * centered;
      count++;
    }

    return count == 0 ? 0 : Math.Sqrt(sumSquares / count);
  }
}
