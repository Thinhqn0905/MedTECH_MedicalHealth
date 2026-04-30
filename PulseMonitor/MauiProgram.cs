using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using LiveChartsCore.SkiaSharpView.Maui;
using PulseMonitor.ViewModels;
using PulseMonitor.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace PulseMonitor;

public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    MauiAppBuilder builder = MauiApp.CreateBuilder();
    builder
      .UseMauiApp<App>()
      .UseMauiCommunityToolkit()
      .UseSkiaSharp()
      .UseLiveCharts();

    builder.Services.AddSingleton<IFileSaver>(FileSaver.Default);
    builder.Services.AddSingleton<MainViewModel>();
    builder.Services.AddSingleton<AiDiagnosticsViewModel>();
    builder.Services.AddSingleton<MainPage>();
    builder.Services.AddTransient<SettingsViewModel>();
    builder.Services.AddTransient<SettingsPage>();

    return builder.Build();
  }
}
