// LottieLoaderView.xaml.cs — Cross-platform transparent Lottie activity loader
using Burudani.Services;

namespace Burudani.Controls;

public partial class LottieLoaderView : ContentView
{
    private static string? _lottieJs;
    private static string? _animData;

    public static readonly BindableProperty StatusMessageProperty =
        BindableProperty.Create(
            nameof(StatusMessage),
            typeof(string),
            typeof(LottieLoaderView),
            "Loading...",
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is LottieLoaderView view && newValue is string text)
                {
                    view.StatusLabel.Text = text;
                }
            });

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public LottieLoaderView()
    {
        InitializeComponent();

        Loaded += async (s, e) => await InitializeLottieAsync();
        PropertyChanged += async (s, e) =>
        {
            if (e.PropertyName == nameof(IsVisible) && IsVisible)
            {
                await InitializeLottieAsync();
            }
        };

        ThemeService.ThemeChanged += async _ => await InitializeLottieAsync();
    }

    private async Task InitializeLottieAsync()
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
            System.Diagnostics.Debug.WriteLine($"[LottieLoader] Initialize error: {ex.Message}");
        }
    }
}
