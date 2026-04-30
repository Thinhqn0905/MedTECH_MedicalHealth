using System.Text;
using System.Text.Json;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace PulseMonitor.Hardware;

public sealed class BleReader : IBleReader
{
  private static readonly Guid ServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
  private static readonly Guid NotifyCharacteristicUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

  private readonly string _deviceName;
  private readonly IBluetoothLE _bluetooth;
  private readonly IAdapter _adapter;
  private readonly StringBuilder _lineBuffer = new();
  private readonly object _bufferSync = new();

  private IDevice? _device;
  private ICharacteristic? _notifyCharacteristic;
  private bool _isRunning;

  public BleReader(string deviceName)
  {
    _deviceName = deviceName;
    _bluetooth = CrossBluetoothLE.Current;
    _adapter = _bluetooth.Adapter;
  }

  public event EventHandler<IRSample>? RawSampleReceived;
  public event EventHandler<bool>? ConnectionStateChanged;
  public event EventHandler<AiDiagnosticResult>? AiDiagnosticReceived;

  public bool IsRunning => _isRunning;
  public string Name => $"BLE:{_deviceName}";

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isRunning)
    {
      return;
    }

    if (_bluetooth.State != BluetoothState.On)
    {
      throw new InvalidOperationException("Bluetooth is not enabled.");
    }

    _isRunning = true;

    try
    {
      _device = await ScanAndConnectAsync(cancellationToken).ConfigureAwait(false);
      IService service = await _device.GetServiceAsync(ServiceUuid).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Required BLE service not found.");

      _notifyCharacteristic = await service.GetCharacteristicAsync(NotifyCharacteristicUuid).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Notify characteristic not found.");

      _notifyCharacteristic.ValueUpdated += OnValueUpdated;
      await _notifyCharacteristic.StartUpdatesAsync().ConfigureAwait(false);
      ConnectionStateChanged?.Invoke(this, true);
    }
    catch
    {
      _isRunning = false;
      ConnectionStateChanged?.Invoke(this, false);
      throw;
    }
  }

  public async Task StopAsync()
  {
    if (!_isRunning)
    {
      return;
    }

    _isRunning = false;

    if (_notifyCharacteristic is not null)
    {
      try
      {
        await _notifyCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
      }
      catch
      {
      }

      _notifyCharacteristic.ValueUpdated -= OnValueUpdated;
      _notifyCharacteristic = null;
    }

    if (_device is not null)
    {
      try
      {
        await _adapter.DisconnectDeviceAsync(_device).ConfigureAwait(false);
      }
      catch
      {
      }

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
      if (args.Device is null)
      {
        return;
      }

      if (string.Equals(args.Device.Name, _deviceName, StringComparison.OrdinalIgnoreCase))
      {
        discoveredDevice.TrySetResult(args.Device);
      }
    }

    _adapter.DeviceDiscovered += OnDeviceDiscovered;

    using CancellationTokenRegistration registration = cancellationToken.Register(() =>
    {
      discoveredDevice.TrySetCanceled(cancellationToken);
    });

    try
    {
      _adapter.ScanTimeout = 5000;
      Task scanTask = _adapter.StartScanningForDevicesAsync(cancellationToken: cancellationToken);

      Task completed = await Task.WhenAny(discoveredDevice.Task, scanTask).ConfigureAwait(false);
      if (completed != discoveredDevice.Task)
      {
        throw new TimeoutException($"BLE device '{_deviceName}' was not found within timeout.");
      }

      IDevice device = await discoveredDevice.Task.ConfigureAwait(false);

      if (_adapter.IsScanning)
      {
        await _adapter.StopScanningForDevicesAsync().ConfigureAwait(false);
      }

      await _adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
      return device;
    }
    finally
    {
      _adapter.DeviceDiscovered -= OnDeviceDiscovered;

      if (_adapter.IsScanning)
      {
        try
        {
          await _adapter.StopScanningForDevicesAsync().ConfigureAwait(false);
        }
        catch
        {
        }
      }
    }
  }

  private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    byte[] payload = args.Characteristic.Value;
    if (payload.Length == 0)
    {
      return;
    }

    string chunk = Encoding.UTF8.GetString(payload);
    string trimmedChunk = chunk.Trim();

    if (TryParseAiResult(trimmedChunk, out AiDiagnosticResult aiResultFromChunk))
    {
      AiDiagnosticReceived?.Invoke(this, aiResultFromChunk);
      return;
    }

    if (TryParseSample(trimmedChunk, out IRSample parsedFromChunk))
    {
      RawSampleReceived?.Invoke(this, parsedFromChunk);
      return;
    }

    lock (_bufferSync)
    {
      _lineBuffer.Append(chunk);

      while (true)
      {
        string current = _lineBuffer.ToString();
        int lineBreak = current.IndexOf('\n');
        if (lineBreak < 0)
        {
          break;
        }

        string line = current[..lineBreak].Trim();
        _lineBuffer.Remove(0, lineBreak + 1);

        if (TryParseAiResult(line, out AiDiagnosticResult aiResult))
        {
          AiDiagnosticReceived?.Invoke(this, aiResult);
        }
        else if (TryParseSample(line, out IRSample sample))
        {
          RawSampleReceived?.Invoke(this, sample);
        }
      }
    }
  }

  private static bool TryParseAiResult(string? line, out AiDiagnosticResult result)
  {
    result = default;
    if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"hrv\"")) return false;
    
    try
    {
      using JsonDocument doc = JsonDocument.Parse(line);
      JsonElement root = doc.RootElement;
      if (!root.TryGetProperty("hrv", out JsonElement hrv)) return false;
      
      long ts    = root.TryGetProperty("ts",    out JsonElement tsEl) ? tsEl.GetInt64()   : 0;
      float sdnn  = hrv.TryGetProperty("sdnn",  out JsonElement s)    ? s.GetSingle()     : 0f;
      float rmssd = hrv.TryGetProperty("rmssd", out JsonElement r)    ? r.GetSingle()     : 0f;
      string rhythm = hrv.TryGetProperty("rhythm", out JsonElement rh) ? rh.GetString() ?? "Unknown" : "Unknown";
      int stress  = hrv.TryGetProperty("stress", out JsonElement st)  ? st.GetInt32()    : 0;
      
      result = new AiDiagnosticResult(ts, sdnn, rmssd, rhythm, stress);
      return true;
    }
    catch { return false; }
  }

  private static bool TryParseSample(string? line, out IRSample sample)
  {
    sample = default;
    if (string.IsNullOrWhiteSpace(line) || line.Length < 10) return false;

    try
    {
      // Fast manual parser for 100Hz waveforms
      if (!line.Contains("\"ir\"")) return false;

      long ts = 0; uint ir = 0; uint red = 0;
      
      // Simple string-based extraction to avoid repeated JsonDocument allocations
      string[] parts = line.Replace("{", "").Replace("}", "").Replace("\"", "").Split(',');
      foreach (var part in parts)
      {
        var kv = part.Split(':');
        if (kv.Length != 2) continue;
        var key = kv[0].Trim();
        var val = kv[1].Trim();
        
        if (key == "ts") ts = long.Parse(val);
        else if (key == "ir") ir = uint.Parse(val);
        else if (key == "red") red = uint.Parse(val);
      }

      if (ts > 0)
      {
        sample = new IRSample(ts, ir, red);
        return true;
      }
      return false;
    }
    catch
    {
      return false;
    }
  }

  private static uint ParseUInt(JsonElement element)
  {
    if (element.ValueKind == JsonValueKind.Number)
    {
      if (element.TryGetUInt32(out uint value))
      {
        return value;
      }

      if (element.TryGetInt64(out long signedValue) && signedValue >= 0)
      {
        return (uint)Math.Min(signedValue, uint.MaxValue);
      }
    }

    if (element.ValueKind == JsonValueKind.String && uint.TryParse(element.GetString(), out uint parsed))
    {
      return parsed;
    }

    return 0;
  }
}