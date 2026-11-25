// AppSettings.cs — Application settings model (persisted as JSON)
namespace Burudani.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";          // Dark, Light, System
    public string Bitrate { get; set; } = "192";          // 128, 192, 256, 320
    public string DownloadFolder { get; set; } = "";
    public bool EmbedId3Tags { get; set; } = true;
    public string DefaultRegion { get; set; } = "US";
    public string DefaultSource { get; set; } = "all";   // all, deezer, itunes, youtube
    public string PlaybackMode { get; set; } = "InApp";  // InApp, System
    public double PlayerVolume { get; set; } = 1.0;
    public string LastSearchQuery { get; set; } = "";
}

public class DownloadRecord
{
    public string Id => TrackId;
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Quality { get; set; } = "192";
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public long FileSizeBytes { get; set; } = 0;
}
