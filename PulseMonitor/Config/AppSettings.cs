namespace PulseMonitor.Config;

public sealed class PulseMonitorSettings
{
  public HardwareSettings Hardware { get; set; } = new();
  public SmtpSettings Smtp { get; set; } = new();
}

public sealed class HardwareSettings
{
  public string ConnectionMode { get; set; } = "Ble";
  public string SerialPort { get; set; } = "COM5";
  public int BaudRate { get; set; } = 115200;
  public string WebSocketUri { get; set; } = "ws://192.168.4.1:8080";
  public string BleDeviceName { get; set; } = "PulseMonitor";
}

public sealed class SmtpSettings
{
  public string Host { get; set; } = string.Empty;
  public int Port { get; set; } = 587;
  public bool UseSsl { get; set; } = true;
  public string User { get; set; } = string.Empty;
  public string Password { get; set; } = string.Empty;
  public string RecipientEmail { get; set; } = string.Empty;
}
