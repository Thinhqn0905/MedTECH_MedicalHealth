using System;

namespace PulseMonitor.WinUI;

public partial class App : MauiWinUIApplication
{
  public App()
  {
    Microsoft.UI.Xaml.Application.LoadComponent(this, new Uri("ms-appx:///Platforms/Windows/App.xaml"));
  }

  protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
