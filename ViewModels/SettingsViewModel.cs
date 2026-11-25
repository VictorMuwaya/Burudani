// SettingsViewModel.cs — ViewModel for App Settings Page
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _theme = "Dark Mode";
    [ObservableProperty] private string _bitrate = "320";
    [ObservableProperty] private string _downloadFolder = string.Empty;
    [ObservableProperty] private bool _embedId3Tags = true;
    [ObservableProperty] private string _defaultRegion = "WW";
    [ObservableProperty] private string _defaultSource = "all";
    [ObservableProperty] private string _playbackMode = "In-App Player";
    [ObservableProperty] private string _version = "2.4.0";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _ffmpegStatus = string.Empty;
    [ObservableProperty] private KeyValuePair<string, string> _selectedRegion;
    [ObservableProperty] private KeyValuePair<string, string> _selectedSource;

    public List<string> Themes { get; } = ["Dark Mode", "Light Mode", "System Default"];
    public List<string> Bitrates { get; } = ["128 kbps", "192 kbps", "256 kbps", "320 kbps (Maximum Fidelity)"];
    public List<string> PlaybackModes { get; } = ["In-App Player", "System Default Player"];
    public List<KeyValuePair<string, string>> Regions { get; } = RegionService.SupportedRegions.ToList();
    public List<KeyValuePair<string, string>> Sources { get; } =
    [
        new("all", "All Public Sources"),
        new("deezer", "Deezer HD (90M+)"),
        new("itunes", "Apple Music Charts"),
        new("youtube", "YouTube (yt-dlp)")
    ];

    public SettingsViewModel()
    {
        var settings = StorageService.LoadSettings();
        Theme = settings.Theme switch
        {
            "Light" => "Light Mode",
            "System" => "System Default",
            _ => "Dark Mode"
        };
        Bitrate = settings.Bitrate switch
        {
            "128" => "128 kbps",
            "192" => "192 kbps",
            "256" => "256 kbps",
            _ => "320 kbps (Maximum Fidelity)"
        };
        DownloadFolder = settings.DownloadFolder;
        EmbedId3Tags = settings.EmbedId3Tags;
        DefaultRegion = string.IsNullOrEmpty(settings.DefaultRegion) ? "WW" : settings.DefaultRegion;
        DefaultSource = string.IsNullOrEmpty(settings.DefaultSource) ? "all" : settings.DefaultSource;
        PlaybackMode = settings.PlaybackMode;

        SelectedRegion = Regions.FirstOrDefault(r => r.Key.Equals(DefaultRegion, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(SelectedRegion.Key))
            SelectedRegion = Regions.First();

        SelectedSource = Sources.FirstOrDefault(s => s.Key.Equals(DefaultSource, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(SelectedSource.Key))
            SelectedSource = Sources.First();

        var ffmpeg = DownloadService.GetFFmpegPath();
        FfmpegStatus = !string.IsNullOrEmpty(ffmpeg)
            ? $"FFmpeg Transcoding Engine: Installed & Operational\nPath: {ffmpeg}"
            : "FFmpeg Binary: Native Audio Engine Mode Active";
    }

    partial void OnThemeChanged(string value)
    {
        ThemeService.ApplyTheme(value);
        SaveSettings();
    }

    partial void OnSelectedRegionChanged(KeyValuePair<string, string> value)
    {
        if (!string.IsNullOrEmpty(value.Key))
        {
            DefaultRegion = value.Key;
            SaveSettings();
        }
    }

    partial void OnSelectedSourceChanged(KeyValuePair<string, string> value)
    {
        if (!string.IsNullOrEmpty(value.Key))
        {
            DefaultSource = value.Key;
            SaveSettings();
        }
    }

    partial void OnBitrateChanged(string value) => SaveSettings();
    partial void OnEmbedId3TagsChanged(bool value) => SaveSettings();

    [RelayCommand]
    public async Task ChooseRegionAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Discovery Region",
            Regions,
            r => r.Value,
            SelectedRegion);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            SelectedRegion = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseSourceAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Default Music Catalog",
            Sources,
            s => s.Value,
            SelectedSource);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            SelectedSource = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseBitrateAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Extraction Bitrate",
            Bitrates,
            b => b,
            Bitrate);
        if (!string.IsNullOrEmpty(picked))
        {
            Bitrate = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseThemeAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Application Theme",
            Themes,
            t => t,
            Theme);
        if (!string.IsNullOrEmpty(picked))
        {
            Theme = picked;
        }
    }

    [RelayCommand]
    public async Task ChoosePlaybackModeAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Playback Mode",
            PlaybackModes,
            p => p,
            PlaybackMode);
        if (!string.IsNullOrEmpty(picked))
        {
            PlaybackMode = picked;
            SaveSettings();
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var cleanBitrate = Bitrate.StartsWith("128") ? "128" :
                           Bitrate.StartsWith("192") ? "192" :
                           Bitrate.StartsWith("256") ? "256" : "320";

        var cleanTheme = Theme.StartsWith("Light") ? "Light" :
                         Theme.StartsWith("System") ? "System" : "Dark";

        var settings = new AppSettings
        {
            Theme = cleanTheme,
            Bitrate = cleanBitrate,
            DownloadFolder = DownloadFolder,
            EmbedId3Tags = EmbedId3Tags,
            DefaultRegion = DefaultRegion,
            DefaultSource = DefaultSource,
            PlaybackMode = PlaybackMode
        };

        StorageService.SaveSettings(settings);
        StatusMessage = "Preferences saved.";
    }

    [RelayCommand]
    public void ResetDownloadFolder()
    {
        DownloadFolder = StorageService.GetDefaultDownloadPath();
        try
        {
            if (!Directory.Exists(DownloadFolder))
                Directory.CreateDirectory(DownloadFolder);
        }
        catch { }
        SaveSettings();
        StatusMessage = $"Download path reset to: {DownloadFolder}";
    }

    [RelayCommand]
    public void SaveDownloadFolder()
    {
        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            ResetDownloadFolder();
            return;
        }

        try
        {
            DownloadFolder = DownloadFolder.Trim();
            if (!Directory.Exists(DownloadFolder))
            {
                Directory.CreateDirectory(DownloadFolder);
            }
            SaveSettings();
            StatusMessage = $"Download path saved: {DownloadFolder}";
        }
        catch (Exception ex)
        {
            SaveSettings();
            StatusMessage = $"Path saved (note: {ex.Message})";
        }
    }

    [RelayCommand]
    public void OpenDownloadFolder()
    {
        try
        {
#if WINDOWS
            if (Directory.Exists(DownloadFolder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DownloadFolder,
                    UseShellExecute = true
                });
            }
#elif ANDROID
            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                intent.SetDataAndType(Android.Net.Uri.Parse(DownloadFolder), "resource/folder");
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                intent.SetDataAndType(Android.Net.Uri.FromFile(new Java.IO.File(DownloadFolder)), "*/*");
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open directory: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task PickDownloadFolderAsync()
    {
        try
        {
#if WINDOWS
            var dialog = new Windows.Storage.Pickers.FolderPicker();
            dialog.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            dialog.FileTypeFilter.Add("*");

            var window = App.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(dialog, hwnd);

                var folder = await dialog.PickSingleFolderAsync();
                if (folder != null)
                {
                    DownloadFolder = folder.Path;
                    SaveSettings();
                }
            }
#else
            StatusMessage = "You can customize the folder path directly by editing the text field below.";
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not pick folder: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearPlayHistory()
    {
        StorageService.ClearPlayHistory();
        StatusMessage = "Play history cleared.";
    }

    [RelayCommand]
    public void ClearDownloadHistory()
    {
        StorageService.ClearDownloadHistory();
        StatusMessage = "Download history cleared.";
    }
}
