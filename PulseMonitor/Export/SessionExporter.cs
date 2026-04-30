using System.Text;
using CommunityToolkit.Maui.Storage;
using PulseMonitor.Processing;

namespace PulseMonitor.Export;

public static class SessionExporter
{
  public static string GenerateCsv(IEnumerable<DiagnosticSample> samples)
  {
    StringBuilder sb = new();
    sb.AppendLine("Timestamp,IR,Red,BPM,SpO2,ECG");

    uint? lastIr = null;
    uint? lastRed = null;
    int? lastBpm = null;
    int? lastSpO2 = null;
    float? lastEcg = null;

    foreach (DiagnosticSample sample in samples)
    {
      // Update last known values
      if (sample.IR.HasValue) lastIr = sample.IR;
      if (sample.Red.HasValue) lastRed = sample.Red;
      if (sample.BPM.HasValue) lastBpm = sample.BPM;
      if (sample.SpO2.HasValue) lastSpO2 = sample.SpO2;
      if (sample.Ecg.HasValue) lastEcg = sample.Ecg;

      // Write row with forward-filled values
      sb.AppendLine($"{sample.Timestamp},{lastIr},{lastRed},{lastBpm},{lastSpO2},{lastEcg}");
    }

    return sb.ToString();
  }

  public static async Task<FileSaverResult> SaveToLocalAsync(IFileSaver fileSaver, IEnumerable<DiagnosticSample> samples, CancellationToken cancellationToken = default)
  {
    string csvData = GenerateCsv(samples);
    using MemoryStream stream = new(Encoding.UTF8.GetBytes(csvData));
    
    string fileName = $"pulsemonitor_session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    
    return await fileSaver.SaveAsync(fileName, stream, cancellationToken).ConfigureAwait(false);
  }
}
