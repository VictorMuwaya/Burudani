using Microsoft.UI.Xaml;

namespace Burudani.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.UnhandledException += (sender, e) =>
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "crash_winui.txt"), e.Exception?.ToString() ?? e.Message);
        };
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
