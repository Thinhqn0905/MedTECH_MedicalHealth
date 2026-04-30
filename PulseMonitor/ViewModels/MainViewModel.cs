using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseMonitor.Collections;
using PulseMonitor.Config;
using PulseMonitor.Export;
using PulseMonitor.Hardware;
using PulseMonitor.Processing;
using PulseMonitor.Views;
using SkiaSharp;

namespace PulseMonitor.ViewModels;

/// <summary>
/// Atomic Performance Architecture:
///   1. WebSocket callback (100Hz) → enqueue to ConcurrentQueue (zero UI work)
///   2. DispatcherTimer (10Hz)     → drain queue into circular ring buffers
///   3. One buffered collection reset per tick triggers chart redraw exactly once
/// </summary>
public partial class MainViewModel : ObservableObject
{
  private const int MaxLogEntries = 20;
  private const int MaxSessionSamples = 200000;
  private const int PpgBufferSize = 800; // 8 seconds at 100Hz

  private readonly RawBuffer _rawBuffer = new(1000);
  private readonly PanTompkinsDetector _panTompkinsDetector = new();
  private readonly SpO2Calculator _spO2Calculator = new();
  private readonly List<string> _boardALogs = new();
  private readonly List<string> _boardBLogs = new();
  private DateTime _lastLogTimeA = DateTime.MinValue;
  private DateTime _lastLogTimeB = DateTime.MinValue;
  public object EcgLock { get; } = new();
  private readonly object _sessionLock = new();
  private readonly Queue<DiagnosticSample> _sessionSamples = [];

  [ObservableProperty]
  private int _selectedBoardIndex = 1; // Default to Board B (ECG)

  public List<string> BoardOptions { get; } = new() { "Board A (PPG)", "Board B (ECG)" };
  private readonly IServiceProvider _serviceProvider;
  private readonly IFileSaver _fileSaver;
  private readonly IDispatcherTimer? _sessionTimer;
  private readonly Stopwatch _sessionStopwatch = new();
  private readonly AiDiagnosticsViewModel _aiDiagnosticsViewModel;

  private long _lastPeakTimestampForHrv = long.MinValue;
  private int  _lastBpmForRrCalc;
  private bool _isRecording;
  private ConnectionManager? _connectionManager;
  private ISampleDataReader? _reader;
  private EcgBleReader? _ecgReader;

  // ── ECG State ──
  public float[] EcgBuffer { get; } = new float[1250];
  public int EcgHead { get; internal set; }
  private ushort _lastEcgSeq = ushort.MaxValue;

  // ── PPG State ──
  public float[] PpgIrBuffer { get; } = new float[PpgBufferSize];
  public float[] PpgRedBuffer { get; } = new float[PpgBufferSize];
  public int PpgHead { get; internal set; }

  private volatile int _latestBpm;
  private volatile int _latestSpO2;
  private long _rawSampleCount;
  private long _lastAiLogMs;
  private long _lastPerfLogMs;
  private int _lastDisplayedBpm = int.MinValue;
  private int _lastDisplayedSpO2 = int.MinValue;
  private int _lastStyledBpm = int.MinValue;
  private int _lastStyledSpO2 = int.MinValue;

  public MainViewModel(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
    _fileSaver = serviceProvider.GetRequiredService<IFileSaver>();
    _aiDiagnosticsViewModel = serviceProvider.GetRequiredService<AiDiagnosticsViewModel>();
    _lastPerfLogMs = Environment.TickCount64;

    // Session timer (1Hz) for recording elapsed time
    _sessionTimer = Application.Current?.Dispatcher.CreateTimer();
    if (_sessionTimer is not null)
    {
      _sessionTimer.Interval = TimeSpan.FromSeconds(1);
      _sessionTimer.Tick += (_, _) =>
      {
        if (_isRecording)
        {
          SessionTimerText = FormatElapsed(_sessionStopwatch.Elapsed);
        }
      };
    }

    AddLog("PulseMonitor initialized.");
  }

  public BufferedObservableCollection<string> EventLogEntries { get; } = [];

  /// <summary>Exposed so the UI can bind the AI tab to this VM.</summary>
  public AiDiagnosticsViewModel AiDiagnostics => _aiDiagnosticsViewModel;

  public ObservableCollection<ContentView> Tabs { get; } = [];

  [ObservableProperty]
  private int _currentTabIndex;

  [ObservableProperty]
  private string _dashboardTabColor = "#007AFF";

  [ObservableProperty]
  private string _aiTabColor = "#C5CDD5";

  [ObservableProperty]
  private string _connectionStatusText = "Disconnected";

  [ObservableProperty]
  private string _connectionColor = "#FF3B30";

  [ObservableProperty]
  private string _connectButtonText = "Connect HR";

  [ObservableProperty]
  private string _recordingButtonText = "Start Recording";

  [ObservableProperty]
  private string _sessionTimerText = "00:00:00";

  [ObservableProperty]
  private string _bpmDisplay = "--";

  [ObservableProperty]
  private string _spO2Display = "--";

  [ObservableProperty]
  private string _bpmBorderColor = "#34C759";

  [ObservableProperty]
  private string _spO2BorderColor = "#34C759";

  [ObservableProperty]
  private string _bpmTextColor = "#34C759";

  [ObservableProperty]
  private string _spO2TextColor = "#34C759";

  [ObservableProperty]
  private bool _isBusy;

  // ── ECG Properties ──
  [ObservableProperty]
  private string _ecgConnectionStatus = "ECG: Disconnected";

  [ObservableProperty]
  private string _ecgConnectButtonText = "Connect ECG";

  [ObservableProperty]
  private string _ecgLeadOffText = "Normal";

  [ObservableProperty]
  private bool _isEcgLeadOff;

  public void InitializeTabs(DashboardContentView dashboard, AiDiagnosticsPage aiDiagnostics)
  {
    if (Tabs.Count == 0)
    {
      Tabs.Add(dashboard);
      Tabs.Add(aiDiagnostics);
    }
  }

  [RelayCommand]
  private void SwitchTab(string? tabIndexStr)
  {
    if (int.TryParse(tabIndexStr, out int index) && index >= 0 && index < Tabs.Count)
    {
      CurrentTabIndex = index;
    }
  }

  partial void OnCurrentTabIndexChanged(int value)
  {
    DashboardTabColor = value == 0 ? "#007AFF" : "#C5CDD5";
    AiTabColor        = value == 1 ? "#007AFF" : "#C5CDD5";
  }

  partial void OnSelectedBoardIndexChanged(int value)
  {
    UpdateDisplayLog();
  }

  private void UpdateDisplayLog()
  {
    MainThread.BeginInvokeOnMainThread(() =>
    {
      EventLogEntries.Clear();
      var source = SelectedBoardIndex == 0 ? _boardALogs : _boardBLogs;
      foreach (var log in source)
      {
        EventLogEntries.Add(log);
      }
    });
  }

  [RelayCommand]
  private async Task ConnectAsync()
  {
    if (IsBusy) return;
    IsBusy = true;

    try
    {
      // 1. Request Runtime Permissions (Mandatory for BLE scanning)
      PermissionStatus status = await Permissions.RequestAsync<Permissions.Bluetooth>().ConfigureAwait(true);
      if (status != PermissionStatus.Granted)
      {
        AddLog("Bluetooth permissions denied.");
        return;
      }

      PermissionStatus locStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().ConfigureAwait(true);
      if (locStatus != PermissionStatus.Granted)
      {
        AddLog("Location permission denied (required for BLE scan).");
        return;
      }

      if (_reader?.IsRunning == true)
      {
        await DisconnectReaderAsync().ConfigureAwait(false);
        AddLog("Reader stopped.");
        return;
      }

      ConnectionStatusText = "Connecting";
      ConnectionColor = "#007AFF";

      PulseMonitorSettings settings = PreferencesSettingsStore.Load();
      
      // ── CONNECTION GATEWAY ──
      // Default name for Board A if using BLE
      if (settings.Hardware.ConnectionMode == "Ble")
      {
          settings.Hardware.BleDeviceName = "PulseMonitor_PPG";
      }

#if ANDROID
      string wsUri = settings.Hardware.WebSocketUri;
      if (wsUri.Contains("192.168.") || wsUri.Contains("10.122."))
      {
        // On emulator, the host machine is at 10.0.2.2
        bool isEmulator = Android.OS.Build.Hardware?.Contains("ranchu") == true
                       || Android.OS.Build.Model?.Contains("Emulator") == true
                       || Android.OS.Build.Fingerprint?.Contains("generic") == true;
        if (isEmulator)
        {
          Uri parsed = new(wsUri);
          wsUri = $"ws://10.0.2.2:{parsed.Port}";
          AddLog($"Emulator detected → {wsUri}");
          // Force WebSocket mode when on emulator since BLE isn't supported there
          settings.Hardware.ConnectionMode = "WebSocket"; 
          settings.Hardware.WebSocketUri = wsUri;
        }
      }
#endif

      _connectionManager = new ConnectionManager(settings.Hardware);
      _reader = new BleReader(settings.Hardware.BleDeviceName);

      _reader.RawSampleReceived     += OnRawSampleReceived;
      _reader.ConnectionStateChanged += OnConnectionStateChanged;
      _reader.AiDiagnosticReceived  += OnAiDiagnosticReceived;
      _reader.DiagnosticLog         += (s, msg) => AddBoardLog(0, msg);

      using CancellationTokenSource cts = new();
      cts.CancelAfter(TimeSpan.FromSeconds(15));
      
      _reader.DiagnosticLog         += (s, msg) => AddBoardLog(0, msg);
      await _reader.StartAsync(cts.Token).ConfigureAwait(false);

      MainThread.BeginInvokeOnMainThread(() =>
      {
        ConnectButtonText = "Disconnect";
        SetConnectionState(true);
      });
      AddLog($"Reader started: {_reader.Name}");
    }
    catch (Exception ex)
    {
      AddLog($"Connect failed: {ex.Message}");
      await DisconnectReaderAsync().ConfigureAwait(false);
    }
    finally
    {
      MainThread.BeginInvokeOnMainThread(() => IsBusy = false);
    }
  }

  [RelayCommand]
  private async Task ConnectEcgAsync()
  {
    if (IsBusy) return;
    IsBusy = true;

    try
    {
      // 1. Request Runtime Permissions (Mandatory for Android 12+)
      PermissionStatus status = await Permissions.RequestAsync<Permissions.Bluetooth>().ConfigureAwait(true);
      if (status != PermissionStatus.Granted)
      {
        AddLog("Bluetooth permissions denied.");
        return;
      }

      // Location is also required for BLE scanning on many Android versions
      PermissionStatus locStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().ConfigureAwait(true);
      
      if (locStatus != PermissionStatus.Granted)
      {
        AddLog("Location permission denied (required for BLE scan).");
        return;
      }

      await Task.Delay(500).ConfigureAwait(true); 

      if (_ecgReader?.IsRunning == true)
      {
        await _ecgReader.StopAsync().ConfigureAwait(false);
        AddLog("ECG Reader stopped.");
        return;
      }

      EcgConnectionStatus = "ECG: Connecting...";
      
      if (_ecgReader == null)
      {
        _ecgReader = new EcgBleReader();
        _ecgReader.ConnectionStateChanged += (s, connected) =>
        {
          MainThread.BeginInvokeOnMainThread(() =>
          {
            EcgConnectionStatus = connected ? "ECG: Connected" : "ECG: Disconnected";
            EcgConnectButtonText = connected ? "Disconnect" : "Connect ECG";
            if (!connected) IsEcgLeadOff = false;
            
            // Force property change notification just in case
            OnPropertyChanged(nameof(EcgConnectionStatus));
            OnPropertyChanged(nameof(EcgConnectButtonText));
          });
          AddLog(connected ? "ECG LIVE STREAM STARTING..." : "ECG stream stopped.");
        };

        _ecgReader.DiagnosticLog += (s, msg) => AddBoardLog(1, msg);
        
        _ecgReader.WaveformReceived += (s, args) =>
        {
          int seq = args.seq;
          short[] samples = args.samples;
          
          if (_lastEcgSeq != ushort.MaxValue)
          {
            int expected = (_lastEcgSeq + 1) % 65536;
            if (seq != expected)
            {
              // Handle packet loss by filling with NaN
              int missingPackets = seq - expected;
              if (missingPackets < 0) missingPackets += 65536;
              
              // Cap missing packets to not overwhelm buffer
              if (missingPackets > 125) missingPackets = 125; 
              
              for (int i = 0; i < missingPackets * 10; i++)
              {
                EcgBuffer[EcgHead] = float.NaN;
                EcgHead = (EcgHead + 1) % 1250;
              }
            }
          }
          
          _lastEcgSeq = args.seq;
          
          lock (EcgLock)
          {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var sample in samples)
            {
              // ECG scale adjustment
              float scaledEcg = sample / 2048.0f;
              EcgBuffer[EcgHead] = scaledEcg;
              EcgHead = (EcgHead + 1) % 1250;

              // Recording
              if (_isRecording)
              {
                lock (_sessionLock)
                {
                  _sessionSamples.Enqueue(new DiagnosticSample(Timestamp: now, Ecg: scaledEcg));
                  if (_sessionSamples.Count > MaxSessionSamples) _sessionSamples.Dequeue();
                }
              }
            }
          }

          // Log once every 2 seconds to confirm connection without lagging the UI
          if ((DateTime.UtcNow - _lastLogTimeB).TotalSeconds >= 2)
          {
            _lastLogTimeB = DateTime.UtcNow;
            AddBoardLog(1, $"Seq: {args.seq}, Raw[0]: {args.samples[0]}");
            
            // Safety sync: If data is coming but UI says Disconnected, force it to Connected
            if (EcgConnectionStatus != "Connected")
            {
                MainThread.BeginInvokeOnMainThread(() => {
                    EcgConnectionStatus = "Connected";
                });
            }
          }
        };

        _ecgReader.AfProbabilityReceived += (s, prob) =>
        {
          _aiDiagnosticsViewModel.UpdateAfResult(prob);
          
          long nowMs = Environment.TickCount64;
          if (nowMs - Interlocked.Read(ref _lastAiLogMs) >= 1000)
          {
            Interlocked.Exchange(ref _lastAiLogMs, nowMs);
            AddLog($"AI ECG: AF Prob={prob:P1}");
          }
        };

        _ecgReader.LeadOffChanged += (s, status) =>
        {
          MainThread.BeginInvokeOnMainThread(() =>
          {
            if (status == 0)
            {
              IsEcgLeadOff = false;
              EcgLeadOffText = "Normal";
            }
            else
            {
              IsEcgLeadOff = true;
              EcgLeadOffText = status switch
              {
                1 => "⚠ LEAD OFF: RA/LA",
                2 => "⚠ LEAD OFF: LL",
                3 => "⚠ LEAD OFF: ALL",
                _ => "⚠ LEAD OFF"
              };
            }
          });
        };
      }

      await _ecgReader.StartAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      AddLog($"ECG Connect failed: {ex.Message}");
      if (_ecgReader != null) await _ecgReader.StopAsync().ConfigureAwait(false);
    }
    finally
    {
      MainThread.BeginInvokeOnMainThread(() => IsBusy = false);
    }
  }


  [RelayCommand]
  private void ToggleRecording()
  {
    _isRecording = !_isRecording;

    if (_isRecording)
    {
      lock (_sessionLock) { _sessionSamples.Clear(); }
      _sessionStopwatch.Restart();
      SessionTimerText = "00:00:00";
      _sessionTimer?.Start();
      RecordingButtonText = "Stop Recording";
      AddLog("Recording started.");
      return;
    }

    _sessionStopwatch.Stop();
    _sessionTimer?.Stop();
    RecordingButtonText = "Start Recording";
    AddLog("Recording stopped.");
  }

  [RelayCommand]
  private async Task ExportEmailAsync()
  {
    if (IsBusy) return;
    IsBusy = true;

    try
    {
      List<DiagnosticSample> exportSamples;
      lock (_sessionLock) { exportSamples = _sessionSamples.ToList(); }

      if (exportSamples.Count == 0)
      {
        await Shell.Current.DisplayAlert("Export", "No recorded samples available. Please record some data first.", "OK");
        AddLog("No recorded samples available. Start recording first.");
        return;
      }

      PulseMonitorSettings settings = PreferencesSettingsStore.Load();
      
      // Simple validation check before jumping into the exporter
      if (string.IsNullOrWhiteSpace(settings.Smtp.Host) || string.IsNullOrWhiteSpace(settings.Smtp.User))
      {
        await Shell.Current.DisplayAlert("Settings Required", "Please configure your SMTP settings in the Settings page first.", "OK");
        return;
      }

      AddLog("Preparing email export...");
      EmailExporter emailExporter = new(settings.Smtp);
      string csvPath = await emailExporter.ExportSessionAsync(exportSamples).ConfigureAwait(false);
      
      MainThread.BeginInvokeOnMainThread(async () => {
          await Shell.Current.DisplayAlert("Success", $"Session exported and emailed successfully.\nFile: {Path.GetFileName(csvPath)}", "OK");
      });
      
      AddLog($"Session exported and emailed. CSV: {csvPath}");
    }
    catch (Exception ex)
    {
      string error = $"Email export failed: {ex.Message}";
      AddLog(error);
      MainThread.BeginInvokeOnMainThread(async () => {
          await Shell.Current.DisplayAlert("Export Error", ex.Message, "OK");
      });
    }
    finally
    {
      IsBusy = false;
    }
  }
  
  [RelayCommand]
  private async Task ExportLocalAsync()
  {
    if (IsBusy) return;
    IsBusy = true;

    try
    {
      List<DiagnosticSample> exportSamples;
      lock (_sessionLock) { exportSamples = _sessionSamples.ToList(); }

      if (exportSamples.Count == 0)
      {
        await Shell.Current.DisplayAlert("Export", "No recorded samples available. Please record some data first.", "OK");
        AddLog("No recorded samples available. Start recording first.");
        return;
      }

      var result = await SessionExporter.SaveToLocalAsync(_fileSaver, exportSamples).ConfigureAwait(false);
      
      if (result.IsSuccessful)
      {
        AddLog($"Session saved: {result.FilePath}");
        MainThread.BeginInvokeOnMainThread(async () => {
            await Shell.Current.DisplayAlert("Success", $"Session saved successfully.\nPath: {result.FilePath}", "OK");
        });
      }
      else
      {
        AddLog($"Save cancelled or failed: {result.Exception?.Message ?? "User cancelled"}");
      }
    }
    catch (Exception ex)
    {
      string error = $"Local export failed: {ex.Message}";
      AddLog(error);
      MainThread.BeginInvokeOnMainThread(async () => {
          await Shell.Current.DisplayAlert("Export Error", ex.Message, "OK");
      });
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task OpenSettingsAsync()
  {
    if (Application.Current?.MainPage is null) return;
    SettingsPage settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();
    await MainThread.InvokeOnMainThreadAsync(async () =>
    {
      await Application.Current.MainPage.Navigation.PushAsync(settingsPage);
    });
  }

  // ════════════════════════════════════════════════════════════
  //  NETWORK CALLBACKS — run on background thread, ZERO UI work
  // ════════════════════════════════════════════════════════════

  private void OnRawSampleReceived(object? sender, IRSample sample)
  {
    Interlocked.Increment(ref _rawSampleCount);

    // Write straight to circular buffer
    PpgIrBuffer[PpgHead] = (float)sample.IR;
    PpgRedBuffer[PpgHead] = (float)sample.Red;
    PpgHead = (PpgHead + 1) % PpgBufferSize;

    // Signal processing runs on the WebSocket thread (fast, no UI)
    _rawBuffer.Add(sample);
    int bpm  = _panTompkinsDetector.Update(sample);
    int spO2 = _spO2Calculator.Update(sample);
    _latestBpm = bpm;
    _latestSpO2 = spO2;

    // RR intervals for AI diagnostics (background computation)
    if (_panTompkinsDetector.HasNewPeak)
    {
      _aiDiagnosticsViewModel.OnRrInterval(_panTompkinsDetector.LastRrMs);
    }

    // Recording buffer (background, lock-protected)
    if (_isRecording)
    {
      lock (_sessionLock)
      {
        _sessionSamples.Enqueue(new DiagnosticSample(sample.Timestamp, sample.IR, sample.Red, bpm, spO2));
        if (_sessionSamples.Count > MaxSessionSamples)
        {
          _sessionSamples.Dequeue();
        }
      }
    }
  }

  private void OnAiDiagnosticReceived(object? sender, AiDiagnosticResult result)
  {
    _aiDiagnosticsViewModel.UpdateFromFirmwareResult(result);

    // Throttle UI log noise from high-frequency AI packets to keep rendering smooth.
    long nowMs = Environment.TickCount64;
    if (nowMs - Interlocked.Read(ref _lastAiLogMs) >= 1000)
    {
      Interlocked.Exchange(ref _lastAiLogMs, nowMs);
      AddLog($"AI HRV: SDNN={result.Sdnn:F1}ms RMSSD={result.Rmssd:F1}ms Rhythm={result.Rhythm} Stress={result.StressLevel}");
    }
  }

  private void OnConnectionStateChanged(object? sender, bool connected)
  {
    MainThread.BeginInvokeOnMainThread(() =>
    {
      SetConnectionState(connected);
      AddLog(connected
        ? "Sensor connection established."
        : "Sensor disconnected. Reader is retrying in background.");
    });
  }

  // ════════════════════════════════════════════════════════════
  //  UI DATA UDPATES
  // ════════════════════════════════════════════════════════════

  public void UpdateVitalsDisplay()
  {
    int bpm = _latestBpm;
    int spO2 = _latestSpO2;
    if (bpm != _lastDisplayedBpm)
    {
      _lastDisplayedBpm = bpm;
      BpmDisplay = bpm > 0 ? bpm.ToString() : "--";
    }

    if (spO2 != _lastDisplayedSpO2)
    {
      _lastDisplayedSpO2 = spO2;
      SpO2Display = spO2 > 0 ? spO2.ToString() : "--";
    }

    if (bpm != _lastStyledBpm || spO2 != _lastStyledSpO2)
    {
      _lastStyledBpm = bpm;
      _lastStyledSpO2 = spO2;
      ApplyVitalStyles(bpm, spO2);
    }
  }


  private void ApplyVitalStyles(int bpm, int spO2)
  {
    if (bpm <= 0)
    {
      BpmBorderColor = "#007AFF";
      BpmTextColor = "#007AFF";
    }
    else
    {
      bool bpmAlert = bpm < 50 || bpm > 120;
      BpmBorderColor = bpmAlert ? "#FF3B30" : "#34C759";
      BpmTextColor = bpmAlert ? "#FF3B30" : "#34C759";
    }

    if (spO2 <= 0)
    {
      SpO2BorderColor = "#007AFF";
      SpO2TextColor = "#007AFF";
    }
    else
    {
      bool spO2Alert = spO2 < 95;
      SpO2BorderColor = spO2Alert ? "#FF3B30" : "#34C759";
      SpO2TextColor = spO2Alert ? "#FF3B30" : "#34C759";
    }
  }

  private void SetConnectionState(bool connected)
  {
    ConnectionStatusText = connected ? "Connected" : "Disconnected";
    ConnectionColor = connected ? "#34C759" : "#FF3B30";
  }

  private static string FormatElapsed(TimeSpan elapsed)
  {
    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
    int totalHours = (int)elapsed.TotalHours;
    return $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
  }

  private void AddLog(string message)
  {
    AddBoardLog(SelectedBoardIndex, message);
  }

  private void AddBoardLog(int boardIndex, string message)
  {
    string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
#if DEBUG
    Console.WriteLine($"PULSE_DEBUG_B{boardIndex}: " + message);
#endif

    var targetList = boardIndex == 0 ? _boardALogs : _boardBLogs;
    lock (targetList)
    {
      targetList.Add(entry);
      if (targetList.Count > 50) targetList.RemoveAt(0);
    }

    if (SelectedBoardIndex == boardIndex)
    {
      MainThread.BeginInvokeOnMainThread(() =>
      {
        EventLogEntries.Add(entry);
        if (EventLogEntries.Count > 50)
        {
           EventLogEntries.RemoveAt(0);
        }
      });
    }
  }

  private async Task DisconnectReaderAsync()
  {
    if (_reader is not null)
    {
      _reader.RawSampleReceived     -= OnRawSampleReceived;
      _reader.ConnectionStateChanged -= OnConnectionStateChanged;
      _reader.AiDiagnosticReceived  -= OnAiDiagnosticReceived;
    }

    if (_connectionManager is not null)
    {
      await _connectionManager.DisconnectAsync().ConfigureAwait(false);
      await _connectionManager.DisposeAsync().ConfigureAwait(false);
      _connectionManager = null;
    }

    _reader = null;

    MainThread.BeginInvokeOnMainThread(() =>
    {
      ConnectButtonText = "Connect HR";
      SetConnectionState(false);
    });
  }
}
