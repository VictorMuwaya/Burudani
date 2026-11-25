using Microsoft.Extensions.DependencyInjection;

namespace Burudani;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		try
		{
			var settings = Services.StorageService.LoadSettings();
			Services.ThemeService.ApplyTheme(settings.Theme);
		}
		catch { }
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage())
		{
			Title = "Burudani (Beta)",
			Width = 1180,
			Height = 760,
			MinimumWidth = 940,
			MinimumHeight = 580
		};

		return window;
	}
}
