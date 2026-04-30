using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace PulseMonitor.Hardware;

public sealed class WebSocketDataReader : ISampleDataReader
{
  private readonly Uri _serverUri;
  private ClientWebSocket? _client;
  private bool _isRunning;
  private Task? _receiveTask;
  private CancellationTokenSource? _runCts;

  public WebSocketDataReader(string serverUri)
  {
    _serverUri = new Uri(serverUri);
  }

  public event EventHandler<IRSample>? RawSampleReceived;
  public event EventHandler<bool>? ConnectionStateChanged;
  public event EventHandler<AiDiagnosticResult>? AiDiagnosticReceived;

  public bool IsRunning => _isRunning;
  public string Name => $"WebSocket:{_serverUri}";

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isRunning) return;

    _client = new ClientWebSocket();
    _runCts = new CancellationTokenSource();

    try
    {
      using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      connectCts.CancelAfter(TimeSpan.FromSeconds(5));
      await _client.ConnectAsync(_serverUri, connectCts.Token).ConfigureAwait(false);
    }
    catch
    {
      _client.Dispose();
      _client = null;
      throw;
    }

    _isRunning = true;
    ConnectionStateChanged?.Invoke(this, true);

    _receiveTask = Task.Run(() => ReceiveLoopAsync(_runCts.Token));
  }

  private async Task ReceiveLoopAsync(CancellationToken token)
  {
    byte[] buffer = new byte[8192];
    var decoder = Encoding.UTF8.GetDecoder();
    var stringBuilder = new StringBuilder();
    char[] charBuffer = new char[8192];

    try
    {
      while (_client?.State == WebSocketState.Open && !token.IsCancellationRequested)
      {
        WebSocketReceiveResult result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

        if (result.MessageType == WebSocketMessageType.Close)
        {
          break;
        }

        int charsUsed = decoder.GetChars(buffer, 0, result.Count, charBuffer, 0, result.EndOfMessage);
        stringBuilder.Append(charBuffer, 0, charsUsed);

        if (result.EndOfMessage)
        {
          string message = stringBuilder.ToString();
          stringBuilder.Clear();

          if (!string.IsNullOrWhiteSpace(message))
          {
            if (TryParseSample(message, out IRSample sample))
            {
              RawSampleReceived?.Invoke(this, sample);
            }
            else if (TryParseAiResult(message, out AiDiagnosticResult aiResult))
            {
              AiDiagnosticReceived?.Invoke(this, aiResult);
            }
          }
        }
      }
    }
    catch (OperationCanceledException) { }
    catch (Exception) { }
    finally
    {
      await StopAsync().ConfigureAwait(false);
    }
  }

  public async Task StopAsync()
  {
    if (!_isRunning) return;
    _isRunning = false;

    _runCts?.Cancel();

    if (_client is not null)
    {
      if (_client.State == WebSocketState.Open)
      {
        try
        {
          await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
      }
      _client.Dispose();
      _client = null;
    }

    ConnectionStateChanged?.Invoke(this, false);
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync().ConfigureAwait(false);
    _runCts?.Dispose();
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
      if (!line.Contains("\"ir\"")) return false;

      long ts = 0; uint ir = 0; uint red = 0;
      
      string[] parts = line.Replace("{", "").Replace("}", "").Replace("\"", "").Split(',');
      foreach (var part in parts)
      {
        var kv = part.Split(':');
        if (kv.Length != 2) continue;
        var key = kv[0].Trim();
        var val = kv[1].Trim();
        
        if (key == "ts") long.TryParse(val, out ts);
        else if (key == "ir") uint.TryParse(val, out ir);
        else if (key == "red") uint.TryParse(val, out red);
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
}
