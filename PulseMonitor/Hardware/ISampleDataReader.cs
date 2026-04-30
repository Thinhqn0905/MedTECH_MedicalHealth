namespace PulseMonitor.Hardware;

public interface ISampleDataReader : IAsyncDisposable
{
  event EventHandler<IRSample>? RawSampleReceived;
  event EventHandler<bool>? ConnectionStateChanged;

  /// <summary>Raised ~every 5 s when the firmware sends an HRV diagnostic packet.</summary>
  event EventHandler<AiDiagnosticResult>? AiDiagnosticReceived;
  event EventHandler<string>? DiagnosticLog;

  bool IsRunning { get; }
  string Name { get; }

  Task StartAsync(CancellationToken cancellationToken = default);
  Task StopAsync();
}
 