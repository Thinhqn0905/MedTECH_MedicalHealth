using System.Text;
using CommunityToolkit.Maui.Storage;
using PulseMonitor.Processing;

namespace PulseMonitor.Export;

public static class SessionExporter
{
  public static string GenerateCsv(IEnumerable<ProcessedSample> samples)
  {
    StringBuilder sb = new();
    sb.AppendLine("Timestamp,IR,Red,BPM,SpO2");

    foreach (ProcessedSample sample in samples)
    {
      sb.AppendLine($"{sample.Timestamp},{sample.IR},{sample.Red},{sample.BPM},{sample.SpO2}");
    }

    return sb.ToString();
  }

  public static async Task<FileSaverResult> SaveToLocalAsync(IFileSaver fileSaver, IEnumerable<ProcessedSample> samples, CancellationToken cancellationToken = default)
  {
    string csvData = GenerateCsv(samples);
    using MemoryStream stream = new(Encoding.UTF8.GetBytes(csvData));
    
    string fileName = $"pulsemonitor_session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    
    return await fileSaver.SaveAsync(fileName, stream, cancellationToken).ConfigureAwait(false);
  }
}
