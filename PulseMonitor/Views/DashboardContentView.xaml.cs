using System.Diagnostics;
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
  
  // Fake data generator for emulator testing
  private float _fakePhase = 0;
  private float _fakePpgPhase = 0;

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
        GenerateFakeEcgDataForEmulator();
        GenerateFakePpgDataForEmulator();
        EcgCanvas.InvalidateSurface(); 
        if (PpgCanvas != null) PpgCanvas.InvalidateSurface();
      };
      _renderTimer.Start();
      _fpsStopwatch.Start();
    }
  }

  private void GenerateFakeEcgDataForEmulator()
  {
    // Feature removed to ensure only real data is displayed
  }

  private void GenerateFakePpgDataForEmulator()
  {
    if (BindingContext is not MainViewModel vm) return;
    
    // Only generate fake data if not connected (for emulator benchmarking)
    if (vm.ConnectionStatusText == "Disconnected")
    {
      // 16ms = ~1.6 samples at 100Hz
      // We will add 2 samples per tick on average to simulate 100Hz
      for (int i = 0; i < 2; i++)
      {
        float irWave = 4000f * (float)Math.Sin(_fakePpgPhase - 0.1f) + 1200f * (float)Math.Sin(2 * _fakePpgPhase + 0.4f);
        float redWave = 3000f * (float)Math.Sin(_fakePpgPhase) + 800f * (float)Math.Sin(2 * _fakePpgPhase + 0.5f);
        
        float valIr = 90000f + irWave;
        float valRed = 100000f + redWave;
        
        vm.PpgIrBuffer[vm.PpgHead] = valIr;
        vm.PpgRedBuffer[vm.PpgHead] = valRed;
        vm.PpgHead = (vm.PpgHead + 1) % vm.PpgIrBuffer.Length;
        
        _fakePpgPhase += 0.08f;
      }
    }
  }

  private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
  {
    SKImageInfo info = e.Info;
    SKSurface surface = e.Surface;
    SKCanvas canvas = surface.Canvas;

    canvas.Clear(SKColors.White);

    if (BindingContext is not MainViewModel vm) return;

    // Draw ECG path
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
    // Find dynamic min/max for auto-scaling
    float minVal = float.MaxValue;
    float maxVal = float.MinValue;
    bool hasData = false;

    for (int i = 0; i < capacity; i++)
    {
      float val = vm.EcgBuffer[i];
      if (!float.IsNaN(val))
      {
        if (val < minVal) minVal = val;
        if (val > maxVal) maxVal = val;
        hasData = true;
      }
    }

    if (!hasData) return;

    // Add padding to min/max
    float range = maxVal - minVal;
    if (range < 0.001f) range = 0.001f;
    minVal -= range * 0.1f;
    maxVal += range * 0.1f;
    range = maxVal - minVal;

    using SKPath path = new();
    bool isFirst = true;

    lock (vm.EcgLock)
    {
      for (int i = 0; i < capacity; i++)
      {
        // Read from oldest to newest
        int index = (head + i) % capacity;
        float val = vm.EcgBuffer[index];

        if (!float.IsNaN(val))
        {
          float x = (i / (float)capacity) * width;
          float y = height - ((val - minVal) / range) * height;

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

    canvas.DrawPath(path, paint);

    // Calculate FPS
    _frameCount++;
    if (_fpsStopwatch.ElapsedMilliseconds > 1000)
    {
      _fps = _frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
      _frameCount = 0;
      _fpsStopwatch.Restart();
    }

    // Draw Benchmark Text
    using SKFont font = new(SKTypeface.Default, 24);
    using SKPaint textPaint = new()
    {
      Color = SKColors.Gray,
      IsAntialias = true
    };
    canvas.DrawText($"FPS: {_fps:F1} | Pts: {capacity}", 10, 30, SKTextAlign.Left, font, textPaint);
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

    // Find dynamic min/max for auto-scaling
    float minVal = float.MaxValue;
    float maxVal = float.MinValue;

    for (int i = 0; i < capacity; i++)
    {
      float ir = vm.PpgIrBuffer[i];
      float red = vm.PpgRedBuffer[i];
      
      if (!float.IsNaN(ir) && ir != 0)
      {
        if (ir < minVal) minVal = ir;
        if (ir > maxVal) maxVal = ir;
      }
      if (!float.IsNaN(red) && red != 0)
      {
        if (red < minVal) minVal = red;
        if (red > maxVal) maxVal = red;
      }
    }

    // Add padding to min/max
    float range = maxVal - minVal;
    if (range < 1) range = 1;
    minVal -= range * 0.1f;
    maxVal += range * 0.1f;
    range = maxVal - minVal;

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
        float y = height - ((ir - minVal) / range) * height;
        if (isFirstIr) { irPath.MoveTo(x, y); isFirstIr = false; }
        else irPath.LineTo(x, y);
      }

      if (!float.IsNaN(red) && red != 0)
      {
        float y = height - ((red - minVal) / range) * height;
        if (isFirstRed) { redPath.MoveTo(x, y); isFirstRed = false; }
        else redPath.LineTo(x, y);
      }
    }

    canvas.DrawPath(irPath, irPaint);
    canvas.DrawPath(redPath, redPaint);

    // Draw Benchmark Text
    using SKFont font = new(SKTypeface.Default, 24);
    using SKPaint textPaint = new()
    {
      Color = SKColors.Gray,
      IsAntialias = true
    };
    canvas.DrawText($"FPS: {_fps:F1} | Pts: {capacity}", 10, 30, SKTextAlign.Left, font, textPaint);
  }
}
