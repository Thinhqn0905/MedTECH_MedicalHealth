using PulseMonitor.Config;

namespace PulseMonitor.Hardware;

public sealed class ConnectionManager : IAsyncDisposable
{
  private readonly HardwareSettings _settings;

  public ConnectionManager(HardwareSettings settings)
  {
    _settings = settings;
  }

  public ISampleDataReader? ActiveReader { get; private set; }

  public async Task<ISampleDataReader> ConnectAsync(CancellationToken cancellationToken = default)
  {
    await DisconnectAsync().ConfigureAwait(false);

#if WINDOWS
    if (string.Equals(_settings.ConnectionMode, "Serial", StringComparison.OrdinalIgnoreCase))
    {
      SerialDataReader serialReader = new(_settings.SerialPort, _settings.BaudRate);
      await serialReader.StartAsync(cancellationToken).ConfigureAwait(false);
      ActiveReader = serialReader;
      return serialReader;
    }
#endif

    if (string.Equals(_settings.ConnectionMode, "Ble", StringComparison.OrdinalIgnoreCase))
    {
      IBleReader bleReader = new BleReader(_settings.BleDeviceName);
      using (CancellationTokenSource bleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
      {
        bleTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
          await bleReader.StartAsync(bleTimeout.Token).ConfigureAwait(false);
          ActiveReader = bleReader;
          return bleReader;
        }
        catch
        {
          await bleReader.DisposeAsync().ConfigureAwait(false);
        }
      }
    }

    WebSocketDataReader webSocketReader = new(_settings.WebSocketUri);
    await webSocketReader.StartAsync(cancellationToken).ConfigureAwait(false);
    ActiveReader = webSocketReader;
    return webSocketReader;
  }

  public async Task DisconnectAsync()
  {
    if (ActiveReader is null)
    {
      return;
    }

    ISampleDataReader reader = ActiveReader;
    ActiveReader = null;

    try
    {
      await reader.StopAsync().ConfigureAwait(false);
    }
    catch
    {
    }

    await reader.DisposeAsync().ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync().ConfigureAwait(false);
  }
}