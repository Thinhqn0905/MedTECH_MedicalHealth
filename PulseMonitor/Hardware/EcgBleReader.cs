using System.Text;
using System.Diagnostics;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace PulseMonitor.Hardware;

public sealed class EcgBleReader : IAsyncDisposable
{
  private static readonly Guid ServiceUuid = Guid.Parse("A0000001-0000-1000-8000-00805F9B34FB");
  private static readonly Guid WaveformUuid = Guid.Parse("A0000002-0000-1000-8000-00805F9B34FB");
  private static readonly Guid LeadOffUuid = Guid.Parse("A0000003-0000-1000-8000-00805F9B34FB");
  private static readonly Guid AfResultUuid = Guid.Parse("A0000005-0000-1000-8000-00805F9B34FB");

  private readonly string _deviceName = "PulseMonitor-ECG";
  private readonly IBluetoothLE _bluetooth;
  private readonly IAdapter _adapter;

  private IDevice? _device;
  private ICharacteristic? _waveformChar;
  private ICharacteristic? _leadoffChar;
  private ICharacteristic? _afChar;
  private bool _isRunning;

  public event EventHandler<(ushort seq, short[] samples)>? WaveformReceived;
  public event EventHandler<byte>? LeadOffChanged;
  public event EventHandler<float>? AfProbabilityReceived;
  public event EventHandler<bool>? ConnectionStateChanged;
  public event EventHandler<string>? DiagnosticLog;

  private DateTime _lastDiagLog = DateTime.MinValue;

  public bool IsRunning => _isRunning;

  public EcgBleReader()
  {
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

      try
      {
        // Request higher MTU so we can receive the full 22-byte packet (2 seq + 20 data).
        // Default MTU is 23 (20 bytes payload). We need at least 25 (22 bytes payload).
        await _device.RequestMtuAsync(247).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[BLE] Failed to negotiate MTU: {ex.Message}");
      }

      IService service = await _device.GetServiceAsync(ServiceUuid).ConfigureAwait(false)
        ?? throw new InvalidOperationException("ECG BLE service not found.");

      _waveformChar = await service.GetCharacteristicAsync(WaveformUuid).ConfigureAwait(false);
      _leadoffChar = await service.GetCharacteristicAsync(LeadOffUuid).ConfigureAwait(false);
      _afChar = await service.GetCharacteristicAsync(AfResultUuid).ConfigureAwait(false);

      if (_waveformChar != null)
      {
        _waveformChar.ValueUpdated += OnWaveformUpdated;
        await _waveformChar.StartUpdatesAsync().ConfigureAwait(false);
      }

      if (_leadoffChar != null)
      {
        _leadoffChar.ValueUpdated += OnLeadOffUpdated;
        await _leadoffChar.StartUpdatesAsync().ConfigureAwait(false);
      }
      
      if (_afChar != null)
      {
        _afChar.ValueUpdated += OnAfUpdated;
        await _afChar.StartUpdatesAsync().ConfigureAwait(false);
      }

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
    if (!_isRunning) return;
    _isRunning = false;

    if (_waveformChar is not null)
    {
      try { await _waveformChar.StopUpdatesAsync().ConfigureAwait(false); } catch { }
      _waveformChar.ValueUpdated -= OnWaveformUpdated;
      _waveformChar = null;
    }

    if (_leadoffChar is not null)
    {
      try { await _leadoffChar.StopUpdatesAsync().ConfigureAwait(false); } catch { }
      _leadoffChar.ValueUpdated -= OnLeadOffUpdated;
      _leadoffChar = null;
    }

    if (_afChar is not null)
    {
      try { await _afChar.StopUpdatesAsync().ConfigureAwait(false); } catch { }
      _afChar.ValueUpdated -= OnAfUpdated;
      _afChar = null;
    }

    if (_device is not null)
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
      if (args.Device == null) return;
      
      string name = args.Device.Name ?? "";
      bool matches = false;

      // 1. Match by name contains (robust against prefix/suffix issues)
      if (name.Contains("PulseMonitor", StringComparison.OrdinalIgnoreCase))
      {
        matches = true;
      }
      
      // 2. Match by Service UUID if present in scan record
      if (!matches && args.Device.AdvertisementRecords != null)
      {
        foreach (var record in args.Device.AdvertisementRecords)
        {
          // Check for Service UUID A0000001
          // Note: In advertisement, it might be in different formats
          if (record.Data != null && record.Data.Length >= 16)
          {
             // We won't parse byte by byte here to avoid errors, 
             // name check usually is enough if advertising correctly
          }
        }
      }
      
      if (matches)
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
      _adapter.ScanTimeout = 10000;
      
      ConnectionStateChanged?.Invoke(this, false); 
      
      // Scan for ALL devices then filter manually (more robust on some Androids)
      await _adapter.StartScanningForDevicesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

      if (!discoveredDevice.Task.IsCompleted)
      {
        throw new TimeoutException($"ECG Board not found after 10s. Please ensure Board B is powered and in range.");
      }

      IDevice device = await discoveredDevice.Task.ConfigureAwait(false);
      if (_adapter.IsScanning) await _adapter.StopScanningForDevicesAsync().ConfigureAwait(false);

      await _adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
      return device;
    }
    finally
    {
      _adapter.DeviceDiscovered -= OnDeviceDiscovered;
      if (_adapter.IsScanning)
      {
        try { await _adapter.StopScanningForDevicesAsync().ConfigureAwait(false); } catch { }
      }
    }
  }

  private void OnWaveformUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    try
    {
      byte[] data = args.Characteristic.Value;

      // Throttle diagnostic log: print once every 2 seconds to avoid UI lag
      if ((DateTime.UtcNow - _lastDiagLog).TotalSeconds >= 2)
      {
        _lastDiagLog = DateTime.UtcNow;
        DiagnosticLog?.Invoke(this, $"[BLE] Receiving pkts, len={data?.Length ?? -1}");
      }

      // MTU default = 23 bytes → max payload = 20 bytes.
      // Firmware sends [seq:2][samples:20]=22.
      // Now that we requested MTU=247, we should receive all 22 bytes.
      if (data == null || data.Length != 22) return;

      ushort seq = BitConverter.ToUInt16(data, 0);
      short[] samples = new short[10];
      Buffer.BlockCopy(data, 2, samples, 0, 20);

      WaveformReceived?.Invoke(this, (seq, samples));
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[BLE] OnWaveformUpdated error: {ex.Message}");
      DiagnosticLog?.Invoke(this, $"[BLE ERR] {ex.Message}");
    }
  }

  private void OnLeadOffUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    try
    {
      byte[] data = args.Characteristic.Value;
      if (data != null && data.Length >= 1)
      {
        LeadOffChanged?.Invoke(this, data[0]);
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[BLE] OnLeadOffUpdated error: {ex.Message}");
    }
  }

  private void OnAfUpdated(object? sender, CharacteristicUpdatedEventArgs args)
  {
    try
    {
      byte[] data = args.Characteristic.Value;
      if (data != null && data.Length >= 4)
      {
        float prob = BitConverter.ToSingle(data, 0);
        AfProbabilityReceived?.Invoke(this, prob);
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[BLE] OnAfUpdated error: {ex.Message}");
    }
  }
}
