using PulseMonitor.ViewModels;

namespace PulseMonitor;

public partial class MainPage : ContentPage
{
  public MainPage(MainViewModel viewModel)
  {
    InitializeComponent();
    
    // Explicitly create views so we can bind the AI diagnostics one correctly
    var dashboard = new PulseMonitor.Views.DashboardContentView
    {
      BindingContext = viewModel
    };
    
    var aiPage = new PulseMonitor.Views.AiDiagnosticsPage
    {
      BindingContext = viewModel.AiDiagnostics
    };

    viewModel.InitializeTabs(dashboard, aiPage);
    BindingContext = viewModel;
  }
}
