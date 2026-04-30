using System.Text.Json;
using Microsoft.Maui.Storage;

namespace PulseMonitor.Config;

public static class PreferencesSettingsStore
{
  private const string SmtpHostKey = "smtp_host";
  private const string SmtpPortKey = "smtp_port";
  private const string SmtpUserKey = "smtp_user";
  private const string SmtpPassKey = "smtp_pass";
  private const string EmailToKey = "email_to";
  private const string WifiUriKey = "wifi_uri";
  private const string BleDeviceNameKey = "ble_device_name";

  private const string SmtpUseSslKey = "smtp_ssl";
  private const string SerialPortKey = "serial_port";
  private const string SerialBaudKey = "serial_baud";
  private const string ConnectionModeKey = "connection_mode";

  public static PulseMonitorSettings Load()
  {
    PulseMonitorSettings defaults = LoadDefaults();

    return new PulseMonitorSettings
    {
      Hardware = new HardwareSettings
      {
        ConnectionMode = Preferences.Get(ConnectionModeKey, defaults.Hardware.ConnectionMode),
        SerialPort = Preferences.Get(SerialPortKey, defaults.Hardware.SerialPort),
        BaudRate = Preferences.Get(SerialBaudKey, defaults.Hardware.BaudRate),
        WebSocketUri = Preferences.Get(WifiUriKey, defaults.Hardware.WebSocketUri),
        BleDeviceName = Preferences.Get(BleDeviceNameKey, defaults.Hardware.BleDeviceName)
      },
      Smtp = new SmtpSettings
      {
        Host = Preferences.Get(SmtpHostKey, defaults.Smtp.Host),
        Port = Preferences.Get(SmtpPortKey, defaults.Smtp.Port),
        User = Preferences.Get(SmtpUserKey, defaults.Smtp.User),
        Password = Preferences.Get(SmtpPassKey, defaults.Smtp.Password),
        RecipientEmail = Preferences.Get(EmailToKey, defaults.Smtp.RecipientEmail),
        UseSsl = Preferences.Get(SmtpUseSslKey, defaults.Smtp.UseSsl)
      }
    };
  }

  public static void Save(PulseMonitorSettings settings)
  {
    Preferences.Set(ConnectionModeKey, settings.Hardware.ConnectionMode);
    Preferences.Set(SerialPortKey, settings.Hardware.SerialPort);
    Preferences.Set(SerialBaudKey, settings.Hardware.BaudRate);
    Preferences.Set(WifiUriKey, settings.Hardware.WebSocketUri);
    Preferences.Set(BleDeviceNameKey, settings.Hardware.BleDeviceName);

    Preferences.Set(SmtpHostKey, settings.Smtp.Host);
    Preferences.Set(SmtpPortKey, settings.Smtp.Port);
    Preferences.Set(SmtpUserKey, settings.Smtp.User);
    Preferences.Set(SmtpPassKey, settings.Smtp.Password);
    Preferences.Set(EmailToKey, settings.Smtp.RecipientEmail);
    Preferences.Set(SmtpUseSslKey, settings.Smtp.UseSsl);
  }

  private static PulseMonitorSettings LoadDefaults()
  {
    PulseMonitorSettings settings = new();

    // Default SMTP settings provided by the user for convenience
    settings.Smtp.Host = "smtp.gmail.com";
    settings.Smtp.Port = 587;
    settings.Smtp.User = "giabao05vng@gmail.com";
    settings.Smtp.Password = "dovhdwmgsfcmkvyz";
    settings.Smtp.UseSsl = true;
    settings.Smtp.RecipientEmail = "giabao05vng@gmail.com"; // Default to self

    settings.Hardware.BleDeviceName = "PulseMonitor";

#if WINDOWS
    string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
    if (!File.Exists(appSettingsPath))
    {
      return settings;
    }

    try
    {
      using FileStream stream = File.OpenRead(appSettingsPath);
      using JsonDocument doc = JsonDocument.Parse(stream);

      if (doc.RootElement.TryGetProperty("Hardware", out JsonElement hardware))
      {
        settings.Hardware.ConnectionMode = hardware.TryGetProperty("ConnectionMode", out JsonElement connectionMode)
          ? connectionMode.GetString() ?? settings.Hardware.ConnectionMode
          : settings.Hardware.ConnectionMode;

        settings.Hardware.SerialPort = hardware.TryGetProperty("SerialPort", out JsonElement serialPort)
          ? serialPort.GetString() ?? settings.Hardware.SerialPort
          : settings.Hardware.SerialPort;

        settings.Hardware.BaudRate = hardware.TryGetProperty("BaudRate", out JsonElement baudRate)
          ? baudRate.GetInt32()
          : settings.Hardware.BaudRate;

        settings.Hardware.WebSocketUri = hardware.TryGetProperty("WebSocketUri", out JsonElement wsUri)
          ? wsUri.GetString() ?? settings.Hardware.WebSocketUri
          : settings.Hardware.WebSocketUri;

        settings.Hardware.BleDeviceName = hardware.TryGetProperty("BleDeviceName", out JsonElement bleName)
          ? bleName.GetString() ?? settings.Hardware.BleDeviceName
          : settings.Hardware.BleDeviceName;
      }

      if (doc.RootElement.TryGetProperty("Smtp", out JsonElement smtp))
      {
        settings.Smtp.Host = smtp.TryGetProperty("Host", out JsonElement host)
          ? host.GetString() ?? settings.Smtp.Host
          : settings.Smtp.Host;

        settings.Smtp.Port = smtp.TryGetProperty("Port", out JsonElement port)
          ? port.GetInt32()
          : settings.Smtp.Port;

        settings.Smtp.User = smtp.TryGetProperty("User", out JsonElement user)
          ? user.GetString() ?? settings.Smtp.User
          : settings.Smtp.User;

        settings.Smtp.Password = smtp.TryGetProperty("Password", out JsonElement password)
          ? password.GetString() ?? settings.Smtp.Password
          : settings.Smtp.Password;

        settings.Smtp.RecipientEmail = smtp.TryGetProperty("RecipientEmail", out JsonElement recipientEmail)
          ? recipientEmail.GetString() ?? settings.Smtp.RecipientEmail
          : settings.Smtp.RecipientEmail;

        settings.Smtp.UseSsl = smtp.TryGetProperty("UseSsl", out JsonElement useSsl)
          ? useSsl.GetBoolean()
          : settings.Smtp.UseSsl;
      }
    }
    catch
    {
      return settings;
    }
#endif

    return settings;
  }
}