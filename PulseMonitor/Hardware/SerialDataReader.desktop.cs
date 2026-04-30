#if WINDOWS
using System.IO.Ports;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PulseMonitor.Hardware;

public sealed class SerialDataReader : ISampleDataReader
{
  private readonly string _portName;
  private readonly int _baudRate;
  private readonly TimeSpan _reconnectDelay;

  private CancellationTokenSource? _internalCts;
  private Task? _readerTask;
  private SerialPort? _serialPort;
  private bool _isRunning;

  public SerialDataReader(string portName, int baudRate, TimeSpan? reconnectDelay = null)
  {
    _portName = portName;
    _baudRate = baudRate;
    _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2);
  }

  public event EventHandler<IRSample>? RawSampleReceived;
  public event EventHandler<bool>? ConnectionStateChanged;
  public event EventHandler<AiDiagnosticResult>? AiDiagnosticReceived;

  public bool IsRunning => _isRunning;
  public string Name => $"Serial:{_portName}@{_baudRate}";

  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isRunning)
    {
      return Task.CompletedTask;
    }

    _isRunning = true;
    _internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _readerTask = Task.Run(() => ReadLoopAsync(_internalCts.Token), CancellationToken.None);
    return Task.CompletedTask;
  }

  public async Task StopAsync()
  {
    if (!_isRunning)
    {
      ClosePort();
      return;
    }

    if (_internalCts is null)
    {
      return;
    }

    _internalCts.Cancel();

    if (_readerTask is not null)
    {
      try
      {
        await _readerTask.ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
    }

    _internalCts.Dispose();
    _internalCts = null;
    _readerTask = null;
    _isRunning = false;
    ClosePort();
    ConnectionStateChanged?.Invoke(this, false);
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync().ConfigureAwait(false);
  }

  private async Task ReadLoopAsync(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      if (!EnsureOpen())
      {
        await DelayReconnectAsync(token).ConfigureAwait(false);
        continue;
      }

      try
      {
        string line = await Task.Run(() => _serialPort!.ReadLine(), token).ConfigureAwait(false);
        if (TryParseAiResult(line, out AiDiagnosticResult aiResult))
        {
          AiDiagnosticReceived?.Invoke(this, aiResult);
        }
        else if (TryParseSample(line, out IRSample sample))
        {
          RawSampleReceived?.Invoke(this, sample);
        }
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (TimeoutException)
      {
      }
      catch (IOException)
      {
        HandleDisconnect();
        await DelayReconnectAsync(token).ConfigureAwait(false);
      }
      catch (InvalidOperationException)
      {
        HandleDisconnect();
        await DelayReconnectAsync(token).ConfigureAwait(false);
      }
    }

    ClosePort();
    _isRunning = false;
    ConnectionStateChanged?.Invoke(this, false);
  }

  private bool EnsureOpen()
  {
    if (_serialPort is { IsOpen: true })
    {
      return true;
    }

    try
    {
      ClosePort();
      _serialPort = new SerialPort(_portName, _baudRate)
      {
        NewLine = "\n",
        ReadTimeout = 2500,
        DtrEnable = false,
        RtsEnable = false,
        Encoding = Encoding.ASCII
      };

      _serialPort.Open();
      _serialPort.DiscardInBuffer();
      ConnectionStateChanged?.Invoke(this, true);
      return true;
    }
    catch
    {
      ConnectionStateChanged?.Invoke(this, false);
      return false;
    }
  }

  private void HandleDisconnect()
  {
    ClosePort();
    ConnectionStateChanged?.Invoke(this, false);
  }

  private async Task DelayReconnectAsync(CancellationToken token)
  {
    try
    {
      await Task.Delay(_reconnectDelay, token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
    }
  }

  private void ClosePort()
  {
    try
    {
      if (_serialPort?.IsOpen == true)
      {
        _serialPort.Close();
      }
    }
    catch
    {
    }
    finally
    {
      _serialPort?.Dispose();
      _serialPort = null;
    }
  }

  private static bool TryParseAiResult(string? line, out AiDiagnosticResult result)
  {
    result = default;
    if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"hrv\"")) return false;
    
    try
    {
      // Fast path for HRV packets: they are less frequent, but still using JsonDocument for safety 
      // as they are more complex, but we filter with 'Contains' first.
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

    // PERFORMANCE OPTIMIZATION:
    // Raw samples are simple: {"ts":long,"ir":uint,"red":uint}
    // Using JsonDocument.Parse(line) 100 times per second is a bottleneck.
    // Instead, we use a very fast manual parser for this specific schema.
    try
    {
      if (!line.Contains("\"ir\"")) return false;

      // Extract values without heavy JSON parsing
      long ts = 0; uint ir = 0; uint red = 0;
      
      // Simple index-based partitioning for minimal allocation
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
#endif