using PulseMonitor.ViewModels;

namespace PulseMonitor.Views;

public partial class SettingsPage : ContentPage
{
  public SettingsPage(SettingsViewModel viewModel)
  {
    InitializeComponent();
    BindingContext = viewModel;
  }
}
