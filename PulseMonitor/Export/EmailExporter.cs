using System.Text;
using System.IO;
using MailKit.Net.Smtp;
using MimeKit;
using PulseMonitor.Config;
using PulseMonitor.Processing;

namespace PulseMonitor.Export;

public sealed class EmailExporter
{
  private readonly SmtpSettings _settings;

  public EmailExporter(SmtpSettings settings)
  {
    _settings = settings;
  }

  public async Task<string> ExportSessionAsync(IReadOnlyCollection<DiagnosticSample> samples, CancellationToken cancellationToken = default)
  {
    if (samples.Count == 0)
    {
      throw new InvalidOperationException("No samples available for export.");
    }

    ValidateConfiguration();

    string csvPath = Path.Combine(Path.GetTempPath(), $"pulsemonitor_session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    string csvData = SessionExporter.GenerateCsv(samples);
    await File.WriteAllTextAsync(csvPath, csvData, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

    MimeMessage message = BuildMessage(csvPath);

    using SmtpClient smtpClient = new();
    await smtpClient.ConnectAsync(_settings.Host, _settings.Port, _settings.UseSsl, cancellationToken).ConfigureAwait(false);
    await smtpClient.AuthenticateAsync(_settings.User, _settings.Password, cancellationToken).ConfigureAwait(false);
    await smtpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
    await smtpClient.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

    return csvPath;
  }

  private MimeMessage BuildMessage(string csvPath)
  {
    MimeMessage message = new();
    message.From.Add(MailboxAddress.Parse(_settings.User));
    message.To.Add(MailboxAddress.Parse(_settings.RecipientEmail));
    message.Subject = $"[PulseMonitor] Session Export {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    BodyBuilder bodyBuilder = new()
    {
      TextBody = "PulseMonitor session export attached."
    };

    bodyBuilder.Attachments.Add(csvPath);
    message.Body = bodyBuilder.ToMessageBody();
    return message;
  }

  private void ValidateConfiguration()
  {
    if (string.IsNullOrWhiteSpace(_settings.Host))
    {
      throw new InvalidOperationException("SMTP host is not configured.");
    }

    if (_settings.Port <= 0)
    {
      throw new InvalidOperationException("SMTP port is invalid.");
    }

    if (string.IsNullOrWhiteSpace(_settings.User) || string.IsNullOrWhiteSpace(_settings.Password))
    {
      throw new InvalidOperationException("SMTP credentials are missing.");
    }

    if (string.IsNullOrWhiteSpace(_settings.RecipientEmail))
    {
      throw new InvalidOperationException("Recipient email is not configured.");
    }
  }

}
