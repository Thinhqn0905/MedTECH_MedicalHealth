using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseMonitor.Config;

namespace PulseMonitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
  [ObservableProperty]
  private string _bleDeviceName = "PulseMonitor";

  [ObservableProperty]
  private string _wifiUri = "ws://192.168.4.1:8080";

  [ObservableProperty]
  private string _connectionMode = "Ble";

  [ObservableProperty]
  private string _serialPort = "COM5";

  [ObservableProperty]
  private int _serialBaud = 115200;

  [ObservableProperty]
  private string _smtpHost = string.Empty;

  [ObservableProperty]
  private int _smtpPort = 587;

  [ObservableProperty]
  private bool _useSsl = true;

  [ObservableProperty]
  private string _smtpUser = string.Empty;

  [ObservableProperty]
  private string _smtpPassword = string.Empty;

  [ObservableProperty]
  private string _recipientEmail = string.Empty;

  [ObservableProperty]
  private string _statusMessage = string.Empty;

  public SettingsViewModel()
  {
    Load();
  }

  [RelayCommand]
  private async Task SaveAsync()
  {
    PulseMonitorSettings settings = new()
    {
      Hardware = new HardwareSettings
      {
        ConnectionMode = ConnectionMode,
        SerialPort = SerialPort,
        BaudRate = SerialBaud,
        WebSocketUri = WifiUri,
        BleDeviceName = BleDeviceName
      },
      Smtp = new SmtpSettings
      {
        Host = SmtpHost,
        Port = SmtpPort,
        UseSsl = UseSsl,
        User = SmtpUser,
        Password = SmtpPassword,
        RecipientEmail = RecipientEmail
      }
    };

    PreferencesSettingsStore.Save(settings);
    StatusMessage = "Settings saved.";

    if (Application.Current?.MainPage is not null)
    {
      await Application.Current.MainPage.Navigation.PopAsync();
    }
  }

  [RelayCommand]
  private async Task BackAsync()
  {
    if (Application.Current?.MainPage is not null)
    {
      await Application.Current.MainPage.Navigation.PopAsync();
    }
  }

  private void Load()
  {
    PulseMonitorSettings settings = PreferencesSettingsStore.Load();

    BleDeviceName = settings.Hardware.BleDeviceName;
    WifiUri = settings.Hardware.WebSocketUri;
    ConnectionMode = settings.Hardware.ConnectionMode;
    SerialPort = settings.Hardware.SerialPort;
    SerialBaud = settings.Hardware.BaudRate;

    SmtpHost = settings.Smtp.Host;
    SmtpPort = settings.Smtp.Port;
    UseSsl = settings.Smtp.UseSsl;
    SmtpUser = settings.Smtp.User;
    SmtpPassword = settings.Smtp.Password;
    RecipientEmail = settings.Smtp.RecipientEmail;
  }
}
