using System.Diagnostics;
using PulseMonitor.Config;
using PulseMonitor.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PulseMonitor.Views;

public partial class DashboardContentView : ContentView
{
  private IDispatcherTimer? _renderTimer;
  private Stopwatch _fpsStopwatch = new();
  private int _frameCount = 0;
  private double _fps = 0;
  private long _lastDisplaySettingsReadMs;
  private float _ecgDisplayGain = 3f;
  private readonly WaveformScaleState _ppgScale = new(minHalfRange: 180f, maxHalfRange: 12000f);
  private readonly List<float> _scaleScratch = new(1800);
  private const float EcgFixedHalfRange = 1.5f;

  public DashboardContentView()
  {
    InitializeComponent();
    
    // Start a 60 FPS render loop
    _renderTimer = Application.Current?.Dispatcher.CreateTimer();
    if (_renderTimer != null)
    {
      _renderTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
      _renderTimer.Tick += (s, e) => 
      {
        EcgCanvas.InvalidateSurface(); 
        if (PpgCanvas != null) PpgCanvas.InvalidateSurface();
      };
      _renderTimer.Start();
      _fpsStopwatch.Start();
    }
  }

  private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
  {
    SKImageInfo info = e.Info;
    SKSurface surface = e.Surface;
    SKCanvas canvas = surface.Canvas;

    canvas.Clear(SKColors.White);

    if (BindingContext is not MainViewModel vm) return;
    RefreshDisplaySettings();

    using SKPaint paint = new()
    {
      Style = SKPaintStyle.Stroke,
      Color = SKColor.Parse("#007AFF"),
      StrokeWidth = 2,
      IsAntialias = true
    };

    int capacity = vm.EcgBuffer.Length;
    int head = vm.EcgHead;
    float width = info.Width;
    float height = info.Height;

    if (vm.IsEcgLeadOff)
    {
      DrawBaseline(canvas, width, height / 2f, paint);
      DrawDebugText(canvas, capacity);
      return;
    }

    using SKPath path = new();
    bool isFirst = true;
    bool hasData = false;
    float baselineY = height / 2f;
    float amplitudePx = height * 0.42f;

    lock (vm.EcgLock)
    {
      for (int i = 0; i < capacity; i++)
      {
        // Read from oldest to newest
        int index = (head + i) % capacity;
        float val = vm.EcgBuffer[index];

        if (!float.IsNaN(val))
        {
          hasData = true;
          float x = (i / (float)capacity) * width;
          float clipped = Math.Clamp(val * _ecgDisplayGain, -EcgFixedHalfRange, EcgFixedHalfRange);
          float y = baselineY - (clipped / EcgFixedHalfRange) * amplitudePx;

          if (isFirst)
          {
            path.MoveTo(x, y);
            isFirst = false;
          }
          else
          {
            path.LineTo(x, y);
          }
        }
        else
        {
          isFirst = true; // break the line if packet lost
        }
      }
    }

    if (!hasData)
    {
      DrawBaseline(canvas, width, baselineY, paint);
      DrawDebugText(canvas, capacity);
      return;
    }

    DrawBaseline(canvas, width, baselineY, paint, alpha: 45);
    canvas.DrawPath(path, paint);
    DrawDebugText(canvas, capacity);
  }

  private void OnPaintPpgSurface(object sender, SKPaintSurfaceEventArgs e)
  {
    SKImageInfo info = e.Info;
    SKSurface surface = e.Surface;
    SKCanvas canvas = surface.Canvas;

    canvas.Clear(SKColors.White);

    if (BindingContext is not MainViewModel vm) return;

    using SKPaint irPaint = new()
    {
      Style = SKPaintStyle.Stroke,
      Color = SKColor.Parse("#007AFF"), // Blue
      StrokeWidth = 2,
      IsAntialias = true
    };

    using SKPaint redPaint = new()
    {
      Style = SKPaintStyle.Stroke,
      Color = SKColor.Parse("#C5CDD5"), // Grayish Red
      StrokeWidth = 2,
      IsAntialias = true
    };

    int capacity = vm.PpgIrBuffer.Length;
    int head = vm.PpgHead;

    float width = info.Width;
    float height = info.Height;

    double irSum = 0;
    double redSum = 0;
    int irCount = 0;
    int redCount = 0;

    for (int i = 0; i < capacity; i++)
    {
      float ir = vm.PpgIrBuffer[i];
      float red = vm.PpgRedBuffer[i];
      
      if (!float.IsNaN(ir) && ir != 0)
      {
        irSum += ir;
        irCount++;
      }

      if (!float.IsNaN(red) && red != 0)
      {
        redSum += red;
        redCount++;
      }
    }

    if (irCount == 0 && redCount == 0)
    {
      float baselineY = height / 2f;
      DrawBaseline(canvas, width, baselineY, irPaint);
      DrawDebugText(canvas, capacity);
      return;
    }

    float irCenter = irCount > 0 ? (float)(irSum / irCount) : 0f;
    float redCenter = redCount > 0 ? (float)(redSum / redCount) : 0f;

    _scaleScratch.Clear();
    for (int i = 0; i < capacity; i++)
    {
      float ir = vm.PpgIrBuffer[i];
      float red = vm.PpgRedBuffer[i];

      if (!float.IsNaN(ir) && ir != 0) _scaleScratch.Add(ir - irCenter);
      if (!float.IsNaN(red) && red != 0) _scaleScratch.Add(red - redCenter);
    }
    _ppgScale.UpdateFromCenteredValues(_scaleScratch);

    float halfRange = _ppgScale.HalfRange;
    float irBaselineY = height * 0.32f;
    float redBaselineY = height * 0.72f;
    float laneAmplitudePx = height * 0.20f;

    using SKPath irPath = new();
    using SKPath redPath = new();

    bool isFirstIr = true;
    bool isFirstRed = true;

    for (int i = 0; i < capacity; i++)
    {
      int index = (head + i) % capacity;
      float ir = vm.PpgIrBuffer[index];
      float red = vm.PpgRedBuffer[index];

      float x = (i / (float)capacity) * width;

      if (!float.IsNaN(ir) && ir != 0)
      {
        float centered = Math.Clamp(ir - irCenter, -halfRange, halfRange);
        float y = irBaselineY - (centered / halfRange) * laneAmplitudePx;
        if (isFirstIr) { irPath.MoveTo(x, y); isFirstIr = false; }
        else irPath.LineTo(x, y);
      }

      if (!float.IsNaN(red) && red != 0)
      {
        float centered = Math.Clamp(red - redCenter, -halfRange, halfRange);
        float y = redBaselineY - (centered / halfRange) * laneAmplitudePx;
        if (isFirstRed) { redPath.MoveTo(x, y); isFirstRed = false; }
        else redPath.LineTo(x, y);
      }
    }

    DrawBaseline(canvas, width, irBaselineY, irPaint, alpha: 45);
    DrawBaseline(canvas, width, redBaselineY, redPaint, alpha: 65);
    canvas.DrawPath(irPath, irPaint);
    canvas.DrawPath(redPath, redPaint);
    DrawLaneLabel(canvas, "IR", 10, irBaselineY - laneAmplitudePx - 4, irPaint.Color);
    DrawLaneLabel(canvas, "RED", 10, redBaselineY - laneAmplitudePx - 4, redPaint.Color);
    DrawDebugText(canvas, capacity);
  }

  private void RefreshDisplaySettings()
  {
    long nowMs = Environment.TickCount64;
    if (nowMs - _lastDisplaySettingsReadMs < 1000)
    {
      return;
    }

    _lastDisplaySettingsReadMs = nowMs;
    double gain = PreferencesSettingsStore.Load().Hardware.EcgDisplayGain;
    _ecgDisplayGain = (float)Math.Clamp(gain, 0.5, 10.0);
  }

  private void DrawDebugText(SKCanvas canvas, int capacity)
  {
    _frameCount++;
    if (_fpsStopwatch.ElapsedMilliseconds > 1000)
    {
      _fps = _frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
      _frameCount = 0;
      _fpsStopwatch.Restart();
    }

    using SKFont font = new(SKTypeface.Default, 24);
    using SKPaint textPaint = new()
    {
      Color = SKColors.Gray,
      IsAntialias = true
    };
    canvas.DrawText($"FPS: {_fps:F1} | Pts: {capacity}", 10, 30, SKTextAlign.Left, font, textPaint);
  }

  private static void DrawBaseline(SKCanvas canvas, float width, float y, SKPaint sourcePaint, byte alpha = 120)
  {
    using SKPaint baselinePaint = new()
    {
      Style = SKPaintStyle.Stroke,
      Color = sourcePaint.Color.WithAlpha(alpha),
      StrokeWidth = 1,
      IsAntialias = true
    };
    canvas.DrawLine(0, y, width, y, baselinePaint);
  }

  private static void DrawLaneLabel(SKCanvas canvas, string label, float x, float y, SKColor color)
  {
    using SKFont font = new(SKTypeface.Default, 18);
    using SKPaint paint = new()
    {
      Color = color.WithAlpha(180),
      IsAntialias = true
    };
    canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
  }
}
