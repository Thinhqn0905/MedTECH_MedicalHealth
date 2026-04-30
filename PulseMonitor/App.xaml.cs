using Microsoft.Extensions.DependencyInjection;

namespace PulseMonitor;

public partial class App : Application
{
	public App(IServiceProvider services)
	{
		InitializeComponent();

		MainPage mainPage = services.GetRequiredService<MainPage>();
		MainPage = new NavigationPage(mainPage)
		{
			BarBackgroundColor = Color.FromArgb("#007AFF"),
			BarTextColor = Colors.White
		};
	}
}
