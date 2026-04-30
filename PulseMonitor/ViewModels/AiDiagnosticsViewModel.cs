using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PulseMonitor.Hardware;
using PulseMonitor.Processing;
using SkiaSharp;

namespace PulseMonitor.ViewModels;

public partial class AiDiagnosticsViewModel : ObservableObject
{
  private const int MaxRrHistory = 64; // for FFT input

  private readonly List<long> _rrHistory = [];

  // ---- Rhythm display -----------------------------------------------

  [ObservableProperty]
  private string _rhythmLabel = "Awaiting data...";

  [ObservableProperty]
  private string _rhythmColor = "#8E9BB0";

  [ObservableProperty]
  private string _rhythmIcon = "⏳";

  // ---- SDNN / RMSSD cards --------------------------------------------

  [ObservableProperty]
  private string _sdnnText = "--";

  [ObservableProperty]
  private string _rmssdText = "--";

  [ObservableProperty]
  private string _sdnnTrend = "";

  // ---- Stress gauge -------------------------------------------------

  [ObservableProperty]
  private int _stressLevel = -1;   // -1 = no data yet

  [ObservableProperty]
  private string _stressLabel = "No Data";

  [ObservableProperty]
  private string _stressColor = "#8E9BB0";

  // ---- LF/HF display ------------------------------------------------

  [ObservableProperty]
  private string _lfHfRatioText = "--";

  [ObservableProperty]
  private string _lfPowerText = "--";

  [ObservableProperty]
  private string _hfPowerText = "--";

  // ---- Timestamp ----------------------------------------------------

  [ObservableProperty]
  private string _lastUpdateText = "Never";

  // ---- AF Detection --------------------------------------------------

  [ObservableProperty]
  private float _afProbability = 0;

  [ObservableProperty]
  private string _afLabel = "Normal";

  [ObservableProperty]
  private string _afColor = "#34C759";

  // ---- Frequency spectrum chart -------------------------------------

  public ISeries[] SpectrumSeries { get; }
  public Axis[]    SpectrumXAxes  { get; }
  public Axis[]    SpectrumYAxes  { get; }

  private readonly ObservableCollection<double> _lfBins = [];
  private readonly ObservableCollection<double> _hfBins = [];

  public AiDiagnosticsViewModel()
  {
    SpectrumSeries =
    [
      new ColumnSeries<double>
      {
        Name   = "LF (0.04–0.15 Hz)",
        Values = _lfBins,
        Fill   = new SolidColorPaint(new SKColor(0x00, 0x7A, 0xFF, 0xCC)),
        Stroke = null
      },
      new ColumnSeries<double>
      {
        Name   = "HF (0.15–0.40 Hz)",
        Values = _hfBins,
        Fill   = new SolidColorPaint(new SKColor(0x30, 0xD1, 0x58, 0xCC)),
        Stroke = null
      }
    ];

    SpectrumXAxes =
    [
      new Axis
      {
        Name     = "Frequency band",
        Labels   = ["LF", "HF"],
        TextSize = 12
      }
    ];

    SpectrumYAxes =
    [
      new Axis
      {
        Name     = "Power (ms²)",
        TextSize = 12
      }
    ];
  }

  // ---- Called from MainViewModel when firmware emits HRV packet -----

  public void UpdateFromFirmwareResult(AiDiagnosticResult result)
  {
    MainThread.BeginInvokeOnMainThread(() =>
    {
      ApplyHrvValues(result.Sdnn, result.Rmssd, result.Rhythm, result.StressLevel);
      LastUpdateText = $"Updated {DateTime.Now:HH:mm:ss}";
    });
  }

  public void UpdateAfResult(float probability)
  {
    MainThread.BeginInvokeOnMainThread(() =>
    {
      AfProbability = probability; 
      if (probability >= 0.5f)
      {
        AfLabel = "⚠ AF DETECTED";
        AfColor = "#FF3B30";
      }
      else
      {
        AfLabel = "Normal Rhythm";
        AfColor = "#34C759";
      }
      LastUpdateText = $"Updated {DateTime.Now:HH:mm:ss}";
    });
  }

  // ---- Called from MainViewModel when a new RR interval is detected -
  // (client-side HRV supplement — useful when running old firmware)

  public void OnRrInterval(long rrMs)
  {
    _rrHistory.Add(rrMs);
    if (_rrHistory.Count > MaxRrHistory)
    {
      _rrHistory.RemoveAt(0);
    }

    if (_rrHistory.Count < 8)
    {
      return;
    }

    // Recompute frequency spectrum asynchronously (DFT is O(n²) but n=64)
    Task.Run(() =>
    {
      FrequencySpectrum spectrum = FftProcessor.Compute(_rrHistory);
      MainThread.BeginInvokeOnMainThread(() => UpdateSpectrum(spectrum));
    });
  }

  // ---- Private helpers -----------------------------------------------

  private void ApplyHrvValues(float sdnn, float rmssd, string rhythm, int stressLevel)
  {
    SdnnText  = $"{sdnn:F1} ms";
    RmssdText = $"{rmssd:F1} ms";

    // Rhythm
    (RhythmLabel, RhythmColor, RhythmIcon) = rhythm switch
    {
      "Normal"       => ("Normal Sinus Rhythm",  "#34C759", "💚"),
      "Tachycardia"  => ("Tachycardia",          "#FF9500", "⚡"),
      "Bradycardia"  => ("Bradycardia",          "#FF9500", "🐢"),
      "Irregular"    => ("Irregular Rhythm",     "#FF3B30", "⚠️"),
      _              => ("Unknown",              "#8E9BB0", "❓")
    };

    // Stress
    StressLevel = stressLevel;
    (StressLabel, StressColor) = stressLevel switch
    {
      0 => ("Low Stress",       "#34C759"),
      1 => ("Moderate Stress",  "#FF9500"),
      2 => ("High Stress",      "#FF6B35"),
      3 => ("Very High Stress", "#FF3B30"),
      _ => ("No Data",          "#8E9BB0")
    };
  }

  private void UpdateSpectrum(FrequencySpectrum spectrum)
  {
    _lfBins.Clear();
    _hfBins.Clear();
    _lfBins.Add(Math.Round(spectrum.LfPower,  2));
    _hfBins.Add(Math.Round(spectrum.HfPower,  2));

    LfPowerText   = $"{spectrum.LfPower:F2} ms²";
    HfPowerText   = $"{spectrum.HfPower:F2} ms²";
    LfHfRatioText = spectrum.HfPower > 0
      ? $"{spectrum.LfHfRatio:F2}"
      : "--";
  }
}
