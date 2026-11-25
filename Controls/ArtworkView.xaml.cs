// ArtworkView.xaml.cs — High performance rounded artwork thumbnail control
using Burudani.Services;

namespace Burudani.Controls;

public partial class ArtworkView : ContentView
{
    public static readonly BindableProperty ArtworkUrlProperty =
        BindableProperty.Create(nameof(ArtworkUrl), typeof(string), typeof(ArtworkView), string.Empty,
            propertyChanged: (b, o, n) => ((ArtworkView)b).TriggerLoadArtwork());

    public static readonly BindableProperty CacheKeyProperty =
        BindableProperty.Create(nameof(CacheKey), typeof(string), typeof(ArtworkView), string.Empty,
            propertyChanged: (b, o, n) => ((ArtworkView)b).TriggerLoadArtwork());

    public static readonly BindableProperty CornerRadiusValueProperty =
        BindableProperty.Create(nameof(CornerRadiusValue), typeof(double), typeof(ArtworkView), 8.0,
            propertyChanged: (b, o, n) => ((ArtworkView)b).UpdateCornerRadius((double)n));

    public string ArtworkUrl
    {
        get => (string)GetValue(ArtworkUrlProperty);
        set => SetValue(ArtworkUrlProperty, value);
    }

    public string CacheKey
    {
        get => (string)GetValue(CacheKeyProperty);
        set => SetValue(CacheKeyProperty, value);
    }

    public double CornerRadiusValue
    {
        get => (double)GetValue(CornerRadiusValueProperty);
        set => SetValue(CornerRadiusValueProperty, value);
    }

    private string _currentSubscribedKey = string.Empty;
    private string _lastLoadedUrl = string.Empty;
    private string _lastLoadedKey = string.Empty;

    public ArtworkView()
    {
        InitializeComponent();
        Unloaded += (s, e) => Cleanup();
    }

    private void UpdateCornerRadius(double radius)
    {
        ContainerBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = new CornerRadius(radius)
        };
    }

    private void TriggerLoadArtwork()
    {
        var key = CacheKey;
        var url = ArtworkUrl;
        if (key == _lastLoadedKey && url == _lastLoadedUrl)
            return;

        _lastLoadedKey = key ?? string.Empty;
        _lastLoadedUrl = url ?? string.Empty;
        LoadArtwork();
    }

    private void LoadArtwork()
    {
        Cleanup();

        var key = CacheKey;
        var url = ArtworkUrl;

        if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(url))
            key = url;

        if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(url))
        {
            ShowPlaceholder();
            return;
        }

        bool isHttp = !string.IsNullOrEmpty(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        // 1. Local file path check (audio file with embedded cover or image file)
        if (!isHttp && !string.IsNullOrEmpty(url) && File.Exists(url))
        {
            var ext = Path.GetExtension(url).ToLowerInvariant();
            if (ext is ".mp3" or ".m4a" or ".aac" or ".flac" or ".wav" or ".webm")
            {
                var embedded = ArtworkService.Instance.GetRamCached(url)
                            ?? ArtworkService.Instance.ExtractArtworkFromAudioFile(url);
                if (embedded != null)
                {
                    SetImageFromBytes(embedded);
                    return;
                }
            }
            else
            {
                ArtImage.Source = ImageSource.FromFile(url);
                ArtImage.IsVisible = true;
                PlaceholderLabel.IsVisible = false;
                return;
            }
        }

        // 2. RAM Cache hit for instant 0ms retrieval
        var cached = ArtworkService.Instance.GetRamCached(key);
        if (cached != null)
        {
            SetImageFromBytes(cached);
            return;
        }

        // 3. Platform-specific image loading:
        // On Android: Use robust two-tier caching (RAM + Application Cache Directory with downsampling)
        // On Windows: Retain native WinUI 3 hardware-accelerated UriImageSource (works flawlessly)
        if (DeviceInfo.Platform == DevicePlatform.Android && isHttp)
        {
            // Check persistent Disk Cache in the application cache directory
            var diskThumbPath = ArtworkService.Instance.GetDiskCachedThumbPath(key);
            if (!string.IsNullOrEmpty(diskThumbPath) && File.Exists(diskThumbPath))
            {
                try
                {
                    ArtImage.Source = ImageSource.FromFile(diskThumbPath);
                    ArtImage.IsVisible = true;
                    PlaceholderLabel.IsVisible = false;

                    // Asynchronously warm RAM cache for future 0ms instant scroll hits
                    Task.Run(() =>
                    {
                        var data = ArtworkService.Instance.ReadDiskCacheDirect(key);
                        if (data != null)
                            ArtworkService.Instance.PutRamCacheOnly(key, data);
                    });
                    return;
                }
                catch { }
            }

            // Not yet in cache: show placeholder, subscribe, and request background download
            ShowPlaceholder();
            _currentSubscribedKey = key;
            ArtworkService.Instance.Subscribe(key, OnArtworkReady);
            ArtworkService.Instance.RequestArtwork(key, url);
            return;
        }

        // Default / Windows WinUI 3 native hardware-accelerated cached image pipeline
        if (isHttp)
        {
            ArtImage.Source = new UriImageSource
            {
                Uri = new Uri(url),
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromDays(7)
            };
            ArtImage.IsVisible = true;
            PlaceholderLabel.IsVisible = false;
            return;
        }

        ShowPlaceholder();
    }

    private void OnArtworkReady(string key, byte[] data)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var currentKey = CacheKey;
            var currentUrl = ArtworkUrl;
            if (string.IsNullOrEmpty(currentKey) && !string.IsNullOrEmpty(currentUrl))
                currentKey = currentUrl;

            if ((key == _currentSubscribedKey || key == currentKey || key == currentUrl) && data != null && data.Length > 0)
            {
                SetImageFromBytes(data);
                Cleanup();
            }
        });
    }

    private void SetImageFromBytes(byte[] bytes)
    {
        try
        {
            ArtImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            ArtImage.IsVisible = true;
            PlaceholderLabel.IsVisible = false;
        }
        catch
        {
            ShowPlaceholder();
        }
    }

    private void ShowPlaceholder()
    {
        ArtImage.IsVisible = false;
        PlaceholderLabel.IsVisible = true;
    }

    private void Cleanup()
    {
        if (!string.IsNullOrEmpty(_currentSubscribedKey))
        {
            ArtworkService.Instance.Unsubscribe(_currentSubscribedKey, OnArtworkReady);
            _currentSubscribedKey = string.Empty;
        }
    }
}
