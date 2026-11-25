// TrackItem.cs — Data model for a single audio track (matches Python core/models.py TrackItem)
using CommunityToolkit.Mvvm.ComponentModel;

namespace Burudani.Models;

/// <summary>
/// Represents a single audio track that can be searched, streamed, queued, or downloaded.
/// Equivalent to Python's core/models.py TrackItem dataclass.
/// </summary>
public partial class TrackItem : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString();
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _durationStr = "--:--";
    [ObservableProperty] private int _durationSec = 0;
    [ObservableProperty] private string _sourceUrl = "";   // YouTube/media URL
    [ObservableProperty] private string _streamUrl = "";   // iTunes preview or resolved stream
    [ObservableProperty] private string _thumbnailUrl = "";
    [ObservableProperty] private string _filePath = "";    // Local MP3 path after download
    [ObservableProperty] private string _source = "itunes"; // itunes, deezer, youtube, url
    [ObservableProperty] private string _albumId = "";

    // Download state
    [ObservableProperty] private string _status = "Queued"; // Queued, Downloading, Completed, Failed, Cancelled

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private double _progress = 0.0;    // 0–100

    public double ProgressFraction => Math.Clamp(Progress / 100.0, 0.0, 1.0);

    [ObservableProperty] private string _speedStr = "";
    [ObservableProperty] private string _etaStr = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _destination = "";  // Custom download folder (e.g. album subfolder)
    [ObservableProperty] private bool _restartFresh = false;

    /// <summary>Formatted display title: "Artist - Title" or just title.</summary>
    public string DisplayTitle =>
        !string.IsNullOrEmpty(Artist) && !string.IsNullOrEmpty(Title)
            ? $"{Artist} - {Title}"
            : !string.IsNullOrEmpty(Title) ? Title
            : !string.IsNullOrEmpty(SourceUrl) ? SourceUrl
            : "Unknown Track";

    /// <summary>True if this track has a local file available for playback.</summary>
    public bool IsDownloaded => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    /// <summary>True if the status indicates an active or completed download.</summary>
    public bool IsDownloading => Status == "Downloading" || Status == "Converting";

    // Dynamic Artwork Contrast Adaptation
    [ObservableProperty] private Color _overlayPlayBg = Color.FromArgb("#8018181b");
    [ObservableProperty] private Color _overlayPlayIcon = Color.FromArgb("#ea580c");
    [ObservableProperty] private Color _overlayPlayBorder = Color.FromArgb("#40ffffff");
    [ObservableProperty] private Color _overlayDownloadBg = Color.FromArgb("#c8ea580c");
    [ObservableProperty] private Color _overlayDownloadIcon = Colors.White;

    public void ApplyArtworkContrast(double luminance)
    {
        if (luminance > 0.58) // Bright artwork
        {
            OverlayPlayBg = Color.FromArgb("#ccffffff");
            OverlayPlayIcon = Color.FromArgb("#18181b");
            OverlayPlayBorder = Color.FromArgb("#8018181b");
            OverlayDownloadBg = Color.FromArgb("#ea580c");
            OverlayDownloadIcon = Colors.White;
        }
        else // Dark or vibrant artwork
        {
            OverlayPlayBg = Color.FromArgb("#8018181b");
            OverlayPlayIcon = Color.FromArgb("#ea580c");
            OverlayPlayBorder = Color.FromArgb("#40ffffff");
            OverlayDownloadBg = Color.FromArgb("#c8ea580c");
            OverlayDownloadIcon = Colors.White;
        }
    }

    /// <summary>Color accent for status badge.</summary>
    public Color StatusColor => Status switch
    {
        "Completed"   => Color.FromArgb("#22c55e"),
        "Downloading" => Color.FromArgb("#f97316"),
        "Converting"  => Color.FromArgb("#f59e0b"),
        "Failed"      => Color.FromArgb("#ef4444"),
        "Cancelled"   => Color.FromArgb("#71717a"),
        _             => Color.FromArgb("#94a3b8"),
    };
}
