// AlbumItem.cs — Data model for an album (matches Python core/models.py AlbumItem)
using CommunityToolkit.Mvvm.ComponentModel;

namespace Burudani.Models;

/// <summary>
/// Represents an album found during search.
/// Equivalent to Python's core/models.py AlbumItem dataclass.
/// </summary>
public partial class AlbumItem : ObservableObject
{
    [ObservableProperty] private string _albumId = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private int _trackCount = 0;
    [ObservableProperty] private string _releaseYear = "";
    [ObservableProperty] private string _artworkUrl = "";
    [ObservableProperty] private string _source = "itunes"; // itunes, deezer, musicbrainz
    [ObservableProperty] private bool _isExpanded = false;
    [ObservableProperty] private bool _isLoadingTracks = false;

    private List<TrackItem> _tracks = [];
    public List<TrackItem> Tracks
    {
        get => _tracks;
        set => SetProperty(ref _tracks, value);
    }

    public string TrackCountLabel =>
        TrackCount > 0 ? $"{TrackCount} tracks" : "– tracks";

    public string SubTitle =>
        string.IsNullOrEmpty(ReleaseYear)
            ? TrackCountLabel
            : $"{ReleaseYear}  •  {TrackCountLabel}";
}
