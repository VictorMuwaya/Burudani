// SettingsPage.xaml.cs — Preferences and options
namespace Burudani.Views;

public partial class SettingsPage : ContentView
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        var page = App.Current?.Windows.FirstOrDefault()?.Page as MainPage;
        page?.SwitchPage(0); // Return to Explorer
    }
}
