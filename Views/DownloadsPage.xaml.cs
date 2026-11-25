// DownloadsPage.xaml.cs — Unified Downloads Hub
namespace Burudani.Views;

public partial class DownloadsPage : ContentView
{
    public DownloadsPage()
    {
        InitializeComponent();
    }

    private void OnMobileQueueTabClicked(object? sender, EventArgs e)
    {
        ViewModel.MobileTabSelected = 0;
    }

    private void OnMobileHistoryTabClicked(object? sender, EventArgs e)
    {
        ViewModel.MobileTabSelected = 1;
    }
}
