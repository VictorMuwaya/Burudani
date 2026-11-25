// ExplorerPage.xaml.cs — Code behind for Explorer / Search Page
using Burudani.Helpers;
using Burudani.Services;
using Burudani.ViewModels;

namespace Burudani.Views;

public partial class ExplorerPage : ContentView
{
    public ExplorerPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        ThemeService.ThemeChanged += OnAppThemeChanged;
    }

    private void OnAppThemeChanged(AppTheme theme)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await RefreshLottieThemeAsync();
        });
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await RefreshLottieThemeAsync();
    }

    private static string? _lottieJs;
    private static string? _animData;

    private bool IsCurrentThemeDark()
    {
        var requested = Application.Current?.UserAppTheme;
        if (requested == AppTheme.Dark) return true;
        if (requested == AppTheme.Light) return false;
        return Application.Current?.RequestedTheme != AppTheme.Light;
    }

    private async Task RefreshLottieThemeAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_lottieJs))
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("animations/lottie.min.js");
                using var reader = new StreamReader(stream);
                _lottieJs = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrEmpty(_animData))
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("animations/sound-viz.json");
                using var reader = new StreamReader(stream);
                _animData = await reader.ReadToEndAsync();
            }

            if (!string.IsNullOrEmpty(_lottieJs) && !string.IsNullOrEmpty(_animData))
            {
                string html = $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
* { margin: 0; padding: 0; box-sizing: border-box; }
html, body {
    width: 180px;
    height: 180px;
    overflow: hidden;
    background-color: transparent !important;
    background: transparent !important;
}
#lottie {
    width: 180px;
    height: 180px;
    background-color: transparent !important;
    background: transparent !important;
}
svg {
    width: 100% !important;
    height: 100% !important;
    background: transparent !important;
    background-color: transparent !important;
}
</style>
<script>{{_lottieJs}}</script>
</head>
<body style='background-color: transparent !important; background: transparent !important;'>
<div id='lottie' style='background-color: transparent !important; background: transparent !important;'></div>
<script>
try {
    lottie.loadAnimation({
        container: document.getElementById('lottie'),
        renderer: 'svg',
        loop: true,
        autoplay: true,
        animationData: {{_animData}}
    });
} catch(e) {}
</script>
</body>
</html>
""";

                LottieWebView.Source = new HtmlWebViewSource { Html = html };

#if WINDOWS
                try
                {
                    if (LottieWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWv2)
                    {
                        nativeWv2.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    }
                }
                catch { }
#elif ANDROID
                try
                {
                    if (LottieWebView.Handler?.PlatformView is Android.Webkit.WebView nativeAndroidWv)
                    {
                        nativeAndroidWv.SetBackgroundColor(Android.Graphics.Color.Transparent);
                        nativeAndroidWv.Settings.JavaScriptEnabled = true;
                        nativeAndroidWv.Settings.AllowFileAccess = true;
                        nativeAndroidWv.Settings.DomStorageEnabled = true;
                    }
                }
                catch { }
#endif
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lottie Animation Load Error] {ex.Message}");
        }
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        SearchEntry.Unfocus();
        KeyboardHelper.HideKeyboard();
        ViewModel.HasSuggestions = false;
        ViewModel.Suggestions.Clear();
        ViewModel.SearchCommand.Execute(null);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ViewModel.UpdateAutocompleteCommand.Execute(e.NewTextValue);
    }

    private void OnSuggestionTapped(object? sender, TappedEventArgs e)
    {
        SearchEntry.Unfocus();
        KeyboardHelper.HideKeyboard();
        if (sender is Border border && border.BindingContext is string text)
        {
            ViewModel.HasSuggestions = false;
            ViewModel.Suggestions.Clear();
            ViewModel.SelectSuggestionCommand.Execute(text);
        }
    }

    private void OnSongsFilterClicked(object? sender, EventArgs e)
    {
        ViewModel.FilterIndex = 0;
    }

    private void OnAlbumsFilterClicked(object? sender, EventArgs e)
    {
        ViewModel.FilterIndex = 1;
    }

    private void OnGridViewClicked(object? sender, EventArgs e)
    {
        ViewModel.ViewModeIndex = 0;
    }

    private void OnListViewClicked(object? sender, EventArgs e)
    {
        ViewModel.ViewModeIndex = 1;
    }

    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
    {
#if WINDOWS
        if (sender is Border border)
        {
            border.Stroke = Color.FromArgb("#ea580c");
            _ = border.ScaleToAsync(1.025, 120, Easing.CubicOut);
        }
#endif
    }

    private void OnCardPointerExited(object? sender, PointerEventArgs e)
    {
#if WINDOWS
        if (sender is Border border)
        {
            var isLight = Application.Current?.RequestedTheme == AppTheme.Light;
            border.Stroke = isLight ? Color.FromArgb("#e2e8f0") : Color.FromArgb("#27272a");
            _ = border.ScaleToAsync(1.0, 120, Easing.CubicIn);
        }
#endif
    }
}
