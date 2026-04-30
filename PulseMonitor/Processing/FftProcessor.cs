namespace PulseMonitor.Processing;

/// <summary>
/// Simple 64-point DFT over RR-interval time series.
/// Produces LF (0.04–0.15 Hz) and HF (0.15–0.40 Hz) power bands
/// used in frequency-domain HRV analysis.
///
/// For 100 bpm max, RR ≈ 600 ms → effective sample rate ~1.67 Hz when interpolated.
/// We interpolate the RR series to a uniform 4 Hz grid before DFT.
/// </summary>
public static class FftProcessor
{
  private const int    DftSize      = 64;
  private const double SampleRateHz = 4.0; // uniform resampling rate

  // Frequency band definitions (Hz)
  private const double LfLow  = 0.04;
  private const double LfHigh = 0.15;
  private const double HfLow  = 0.15;
  private const double HfHigh = 0.40;

  /// <summary>
  /// Computes the power spectrum of the given RR intervals (ms).
  /// Returns a <see cref="FrequencySpectrum"/> with per-bin magnitude and LF/HF power.
  /// </summary>
  public static FrequencySpectrum Compute(IReadOnlyList<long> rrMs)
  {
    if (rrMs.Count < 4)
    {
      return FrequencySpectrum.Empty;
    }

    // --- Step 1: Interpolate RR series to uniform 4 Hz grid ---
    double[] uniformSamples = Interpolate(rrMs, SampleRateHz, DftSize);

    // --- Step 2: Apply Hanning window to reduce spectral leakage ---
    ApplyHanningWindow(uniformSamples);

    // --- Step 3: Compute DFT magnitude spectrum ---
    double[] magnitude = ComputeDftMagnitude(uniformSamples);

    // --- Step 4: Extract LF and HF power ---
    double freqResolution = SampleRateHz / DftSize;
    double lfPower = 0;
    double hfPower = 0;
    int    halfLen = magnitude.Length;

    var bins = new FrequencyBin[halfLen];
    for (int k = 0; k < halfLen; ++k)
    {
      double freq = k * freqResolution;
      bins[k] = new FrequencyBin(freq, magnitude[k]);

      if (freq >= LfLow && freq < LfHigh) lfPower += magnitude[k] * magnitude[k];
      if (freq >= HfLow && freq < HfHigh) hfPower += magnitude[k] * magnitude[k];
    }

    double lfHfRatio = hfPower > 0 ? lfPower / hfPower : 0;
    return new FrequencySpectrum(bins, lfPower, hfPower, lfHfRatio);
  }

  // ---- Helpers -----------------------------------------------------------

  private static double[] Interpolate(IReadOnlyList<long> rrMs, double fs, int targetCount)
  {
    // Cumulative time axis of original RR series (seconds)
    double[] t = new double[rrMs.Count];
    t[0] = rrMs[0] / 1000.0;
    for (int i = 1; i < rrMs.Count; ++i)
    {
      t[i] = t[i - 1] + rrMs[i] / 1000.0;
    }

    double totalDuration = t[^1];
    double dt = 1.0 / fs;
    int maxSamples = Math.Min(targetCount, (int)(totalDuration * fs) + 1);
    if (maxSamples < 4) maxSamples = 4;

    double[] result = new double[targetCount];
    int ti = 0;

    for (int n = 0; n < targetCount; ++n)
    {
      double tq = n * dt;
      if (tq > totalDuration) { result[n] = rrMs[^1]; continue; }

      while (ti < t.Length - 2 && t[ti + 1] < tq) ++ti;

      double span = t[ti + 1] - t[ti];
      double alpha = span > 0 ? (tq - t[ti]) / span : 0;
      result[n] = rrMs[ti] + alpha * (rrMs[ti + 1] - rrMs[ti]);
    }

    return result;
  }

  private static void ApplyHanningWindow(double[] samples)
  {
    int n = samples.Length;
    for (int i = 0; i < n; ++i)
    {
      samples[i] *= 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
    }
  }

  private static double[] ComputeDftMagnitude(double[] x)
  {
    int n    = x.Length;
    int half = n / 2;
    double[] mag = new double[half];

    for (int k = 0; k < half; ++k)
    {
      double re = 0, im = 0;
      for (int t = 0; t < n; ++t)
      {
        double angle = 2 * Math.PI * k * t / n;
        re += x[t] * Math.Cos(angle);
        im -= x[t] * Math.Sin(angle);
      }
      mag[k] = Math.Sqrt(re * re + im * im) / n;
    }

    return mag;
  }
}

/// <summary>Single frequency bin (Hz, magnitude).</summary>
public readonly record struct FrequencyBin(double FrequencyHz, double Magnitude);

/// <summary>Full power spectrum result from <see cref="FftProcessor"/>.</summary>
public sealed class FrequencySpectrum
{
  public static readonly FrequencySpectrum Empty = new([], 0, 0, 0);

  public FrequencySpectrum(FrequencyBin[] bins, double lfPower, double hfPower, double lfHfRatio)
  {
    Bins      = bins;
    LfPower   = lfPower;
    HfPower   = hfPower;
    LfHfRatio = lfHfRatio;
  }

  public FrequencyBin[] Bins      { get; }
  public double         LfPower   { get; }
  public double         HfPower   { get; }
  /// <summary>LF/HF ratio — sympathovagal balance indicator.</summary>
  public double         LfHfRatio { get; }
}
