using System.Text;
using System.Text.Json;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions;

namespace PulseMonitor.Hardware;

public sealed class BleReader : IBleReader
{
  // Mirroring the Board B (ECG) UUID base for maximum Android compatibility
  private static readonly Guid ServiceUuid = Guid.Parse("DE010001-0000-1000-8000-00805F9B34FB");
  private static readonly Guid WaveformCharacteristicUuid = Guid.Parse("DE010003-0000-1000-8000-00805F9B34FB");
  private static readonly Guid MetricsCharacteristicUuid = Guid.Parse("DE010004-0000-1000-8000-00805F9B34FB");

  private readonly IBluetoothLE _bluetooth;
  private readonly IAdapter _adapter;
  private readonly string _deviceName;
  private readonly StringBuilder _lineBuffer = new();
  private readonly object _bufferSync = new();

  private IDevice? _device;
  private ICharacteristic? _waveformChar;
  private ICharacteristic? _metricsChar;
  private bool _isRunning;

  // ISampleDataReader interface
  public event EventHandler<IRSample>? RawSampleReceived;
  public event EventHandler<AiDiagnosticResult>? AiDiagnosticReceived;
  public string Name => "PPG Reader (BLE)";

  public event EventHandler<byte[]>? RawDataReceived;
  public event EventHandler<string>? MetricsReceived;
  public event EventHandler<bool>? ConnectionStateChanged;
  public event EventHandler<string>? DiagnosticLog;

  public bool IsRunning => _isRunning;

  public BleReader() : this("PulseMonitor-PPG") { }

  public BleReader(string deviceName)
  {
    _deviceName = deviceName;
    _bluetooth = CrossBluetoothLE.Current;
    _adapter = _bluetooth.Adapter;
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isRunning) return;

    if (_bluetooth.State != BluetoothState.On)
    {
      throw new InvalidOperationException("Bluetooth is not enabled.");
    }

    _isRunning = true;
    try
    {
      _device = await ScanAndConnectAsync(cancellationToken).ConfigureAwait(false);

      // MIRROR BOARD B: Request MTU IMMEDIATELY after connection
      try
      {
        DiagnosticLog?.Invoke(this, "[PPG] Negotiating MTU 247...");
        await _device.RequestMtuAsync(247).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        DiagnosticLog?.Invoke(this, $"[PPG] MTU Note: {ex.Message}");
      }

      // Discover Services
      DiagnosticLog?.Invoke(this, "[PPG] Discovering Services...");
      var service = await _device.GetServiceAsync(ServiceUuid).ConfigureAwait(false)
        ?? throw new InvalidOperationException("PPG Service not found on board.");

      _waveformChar = await service.GetCharacteristicAsync(WaveformCharacteristicUuid).ConfigureAwait(false);
      _metricsChar = await service.GetCharacteristicAsync(MetricsCharacteristicUuid).ConfigureAwait(false);

      if (_waveformChar != null)
      {
        _waveformChar.ValueUpdated += OnWaveformUpdated;
        await _waveformChar.StartUpdatesAsync().ConfigureAwait(false);
        DiagnosticLog?.Invoke(this, "[PPG] Waveform stream active.");
      }

      if (_metricsChar != null)
      {
        _metricsChar.ValueUpdated += OnMetricsUpdated;
        await _metricsChar.StartUpdatesAsync().ConfigureAwait(false);
        DiagnosticLog?.Invoke(this, "[PPG] Metrics stream active.");
      }

      ConnectionStateChanged?.Invoke(this, true);
    }
    catch (Exception ex)
    {
      _isRunning = false;
      ConnectionStateChanged?.Invoke(this, false);
      DiagnosticLog?.Invoke(this, $"[PPG ERR] Connection failed: {ex.Message}");
      throw;
    }
  }

  public async Task StopAsync()
  {
    if (!_isRunning) return;
    _isRunning = false;

    if (_waveformChar != null)
    {
      try { await _waveformChar.StopUpdatesAsync().ConfigureAwait(false); } catch { }
      _waveformChar.ValueUpdated -= OnWaveformUpdated;
      _waveformChar = null;
    }

    if (_metricsChar != null)
    {
      try { await _metricsChar.StopUpdatesAsync().ConfigureAwait(false); } catch { }
      _metricsChar.ValueUpdated -= OnMetricsUpdated;
      _metricsChar = null;
    }

    if (_device != null)
    {
      try { await _adapter.DisconnectDeviceAsync(_device).ConfigureAwait(false); } catch { }
      _device = null;
    }

    ConnectionStateChanged?.Invoke(this, false);
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync().ConfigureAwait(false);
  }

  private async Task<IDevice> ScanAndConnectAsync(CancellationToken cancellationToken)
  {
    TaskCompletionSource<IDevice> discoveredDevice = new(TaskCreationOptions.RunContinuationsAsynchronously);

    void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
      if (args.Device is null) return;
      
      string name = args.Device.Name ?? "Unknown";
      
      // Match by the new Board-B-style name prefix
      if (name.Contains("PulseMonitor", StringComparison.OrdinalIgnoreCase) || 
          name.Contains("PPG", StringComparison.OrdinalIgnoreCase))
      {
        discoveredDevice.TrySetResult(args.Device);
      }
    }

    _adapter.DeviceDiscovered += OnDeviceDiscovered;

    try
    {
      DiagnosticLog?.Invoke(this, $"[PPG] Scanning for Board A (Mirroring Board B logic)...");
      _adapter.ScanTimeout = 10000;
      await _adapter.StartScanningForDevicesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

      if (!discoveredDevice.Task.IsCompleted)
      {
        throw new TimeoutException("PPG Board (PulseMonitor-PPG) not found.");
      }

      IDevice device = await discoveredDevice.Task.ConfigureAwait(false);
      if (_adapter.IsScanning) await _adapter.StopScanningForDevicesAsync().ConfigureAwait(false);

      DiagnosticLog?.Invoke(this, $"[PPG] Connecting to {device.Name}...");
      await _adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
      return device;
    }
    finally
    {
      _adapter.DeviceDiscovered -= OnDeviceDiscovered;
    }
  }

  private DateTime _lastWfLog = DateTime.MinValue;
  private int _wfPacketCount = 0;

  private void OnWaveformUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    byte[] data = args.Characteristic.Value;
    _wfPacketCount++;
    RawDataReceived?.Invoke(this, data);

    // Log every 2 seconds so we can see what's happening
    if ((DateTime.UtcNow - _lastWfLog).TotalSeconds >= 2)
    {
      _lastWfLog = DateTime.UtcNow;
      DiagnosticLog?.Invoke(this, $"[PPG] Pkts:{_wfPacketCount} len={data?.Length ?? -1}");
    }

    // Parse 12-byte binary packet: [TS:u32][IR:u32][RED:u32]
    if (data != null && data.Length == 12)
    {
      uint ts  = BitConverter.ToUInt32(data, 0);
      uint ir  = BitConverter.ToUInt32(data, 4);
      uint red = BitConverter.ToUInt32(data, 8);

      RawSampleReceived?.Invoke(this, new IRSample((long)ts, ir, red));
    }
  }

  private void OnMetricsUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    string json = Encoding.UTF8.GetString(args.Characteristic.Value);
    MetricsReceived?.Invoke(this, json);

    // Parse the JSON string to fire the correct events to the UI
    try
    {
      using var doc = System.Text.Json.JsonDocument.Parse(json);
      var root = doc.RootElement;
      long ts = root.GetProperty("ts").GetInt64();

      if (root.TryGetProperty("hrv", out var hrvObj))
      {
        float sdnn = hrvObj.GetProperty("sdnn").GetSingle();
        float rmssd = hrvObj.GetProperty("rmssd").GetSingle();

        if (sdnn < 0 || sdnn > 220 || rmssd < 0 || rmssd > 260)
        {
          DiagnosticLog?.Invoke(this, $"[PPG] Ignored noisy HRV: SDNN={sdnn:F1}, RMSSD={rmssd:F1}");
          return;
        }

        var result = new AiDiagnosticResult(ts, sdnn, rmssd, "Normal", 0);
        AiDiagnosticReceived?.Invoke(this, result);
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"Failed to parse metrics: {ex.Message}");
    }
  }
}
