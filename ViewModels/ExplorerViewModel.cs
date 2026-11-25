// ExplorerViewModel.cs — ViewModel for Explorer / Search Page
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class ExplorerViewModel : ObservableObject
{
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private int _filterIndex = 0; // 0 = Songs, 1 = Albums
    [ObservableProperty] private int _viewModeIndex = 0; // 0 = Grid, 1 = List
    [ObservableProperty] private string _selectedFilterType = "Songs";
    [ObservableProperty] private string _selectedViewMode = "Grid View";
    [ObservableProperty] private string _selectedSource = "all";
    [ObservableProperty] private string _selectedRegion = "WW";
    [ObservableProperty] private KeyValuePair<string, string> _currentSource;
    [ObservableProperty] private KeyValuePair<string, string> _currentRegion;
    [ObservableProperty] private string _selectedGenre = "All Genres";
    [ObservableProperty] private bool _isGenreBarVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentVisible))]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private bool _isLoading = true; // Start true so activity loader immediately displays on startup

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentVisible))]
    private bool _isOffline = false;

    [ObservableProperty] private string _errorMessage = "Could not connect to online music catalogs. Please check your connection and tap retry.";

    [ObservableProperty] private string _statusMessage = "Discovering trending hits across all genres...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentVisible))]
    private bool _hasResults = true;

    [ObservableProperty] private bool _hasNoResults = false;
    [ObservableProperty] private bool _hasSuggestions = false;

    public bool IsContentVisible => !IsLoading && !IsOffline && HasResults;

    public ObservableCollection<TrackItem> Tracks { get; } = [];
    public ObservableCollection<AlbumItem> Albums { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public List<string> FilterTypes { get; } = ["Songs", "Albums"];
    public List<string> ViewModes { get; } = ["Grid View", "List View"];

    public ObservableCollection<GenreItem> Genres { get; } =
    [
        new() { Name = "All Genres", IsSelected = true },
        new() { Name = "Pop", IsSelected = false },
        new() { Name = "Hip-Hop / Rap", IsSelected = false },
        new() { Name = "R&B / Soul", IsSelected = false },
        new() { Name = "Rock", IsSelected = false },
        new() { Name = "Dance / Electronic", IsSelected = false },
        new() { Name = "Country", IsSelected = false },
        new() { Name = "Alternative", IsSelected = false },
        new() { Name = "Reggae / Afrobeats", IsSelected = false }
    ];

    public List<KeyValuePair<string, string>> RegionList { get; } =
        RegionService.SupportedRegions.ToList();

    public List<KeyValuePair<string, string>> SourceList { get; } =
    [
        new("all", "All Public Sources"),
        new("deezer", "Deezer HD (90M+)"),
        new("itunes", "Apple Music Charts"),
        new("youtube", "YouTube (yt-dlp)")
    ];

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _autocompleteCts;
    private bool _suppressAutocomplete = false;

    public ExplorerViewModel()
    {
        var settings = StorageService.LoadSettings();
        SelectedRegion = string.IsNullOrEmpty(settings.DefaultRegion) ? "WW" : settings.DefaultRegion;
        SelectedSource = string.IsNullOrEmpty(settings.DefaultSource) ? "all" : settings.DefaultSource;

        CurrentRegion = RegionList.FirstOrDefault(r => r.Key.Equals(SelectedRegion, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(CurrentRegion.Key)) CurrentRegion = RegionList.First();

        CurrentSource = SourceList.FirstOrDefault(s => s.Key.Equals(SelectedSource, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(CurrentSource.Key)) CurrentSource = SourceList.First();

        _ = LoadTrendingFeedAsync();
    }

    partial void OnSelectedFilterTypeChanged(string value)
    {
        int targetIndex = value == "Albums" ? 1 : 0;
        if (FilterIndex != targetIndex)
        {
            FilterIndex = targetIndex;
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                _ = SearchAsync();
            }
        }
    }

    partial void OnFilterIndexChanged(int value)
    {
        var targetType = value == 1 ? "Albums" : "Songs";
        if (SelectedFilterType != targetType)
        {
            SelectedFilterType = targetType;
        }
    }

    partial void OnSelectedViewModeChanged(string value)
    {
        int targetIndex = value == "List View" ? 1 : 0;
        if (ViewModeIndex != targetIndex)
        {
            ViewModeIndex = targetIndex;
        }
    }

    partial void OnViewModeIndexChanged(int value)
    {
        var targetMode = value == 1 ? "List View" : "Grid View";
        if (SelectedViewMode != targetMode)
        {
            SelectedViewMode = targetMode;
        }
    }

    [RelayCommand]
    public async Task ChooseFilterTypeAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Content Type",
            FilterTypes,
            t => t,
            SelectedFilterType);
        if (!string.IsNullOrEmpty(picked))
        {
            SelectedFilterType = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseViewModeAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Layout Style",
            ViewModes,
            m => m,
            SelectedViewMode);
        if (!string.IsNullOrEmpty(picked))
        {
            SelectedViewMode = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseSourceAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Music Catalog",
            SourceList,
            s => s.Value,
            CurrentSource);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            CurrentSource = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseRegionAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Discovery Region",
            RegionList,
            r => r.Value,
            CurrentRegion);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            CurrentRegion = picked;
        }
    }

    partial void OnCurrentRegionChanged(KeyValuePair<string, string> value)
    {
        if (!string.IsNullOrEmpty(value.Key) && value.Key != SelectedRegion)
        {
            SelectedRegion = value.Key;
            _ = LoadTrendingFeedAsync();
        }
    }

    partial void OnCurrentSourceChanged(KeyValuePair<string, string> value)
    {
        if (!string.IsNullOrEmpty(value.Key) && value.Key != SelectedSource)
        {
            SelectedSource = value.Key;
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                _ = SearchAsync();
            }
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        _suppressAutocomplete = true;
        _autocompleteCts?.Cancel();
        HasSuggestions = false;
        Suggestions.Clear();

        var q = SearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            _suppressAutocomplete = false;
            await LoadTrendingFeedAsync();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsLoading = true;
        IsOffline = false;
        IsGenreBarVisible = false;
        StatusMessage = $"Searching for \"{q}\"...";
        ArtworkService.Instance.ClearPending();
        Tracks.Clear();
        Albums.Clear();
        HasResults = false;
        HasNoResults = false;

        try
        {
            if (FilterIndex == 0) // Songs
            {
                var results = await Task.Run(() => SearchService.SearchSongsAsync(q, SelectedSource, 50, ct), ct);
                Tracks.Clear();
                foreach (var t in results)
                    Tracks.Add(t);

                HasResults = Tracks.Count > 0;
                HasNoResults = Tracks.Count == 0;
                StatusMessage = HasResults ? $"{Tracks.Count} tracks found" : "No tracks found.";
            }
            else // Albums
            {
                var results = await Task.Run(() => SearchService.SearchAlbumsAsync(q, SelectedSource, 30, ct), ct);
                Albums.Clear();
                foreach (var a in results)
                    Albums.Add(a);

                HasResults = Albums.Count > 0;
                HasNoResults = Albums.Count == 0;
                StatusMessage = HasResults ? $"{Albums.Count} albums found" : "No albums found.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (Tracks.Count == 0 && Albums.Count == 0)
            {
                IsOffline = true;
                bool isNet = !RegionService.IsInternetAvailable();
                ErrorMessage = isNet 
                    ? "No internet connection detected. Please check your Wi-Fi or mobile network and retry."
                    : $"Unable to fetch music catalog from servers: {ex.Message}";
                StatusMessage = "Connection error.";
            }
            else
            {
                StatusMessage = $"Connection issue: {ex.Message}";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RetryAsync()
    {
        IsOffline = false;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
            await SearchAsync();
        else
            await LoadTrendingFeedAsync();
    }

    [RelayCommand]
    public async Task LoadTrendingFeedAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsLoading = true;
        IsOffline = false;
        IsGenreBarVisible = true;
        StatusMessage = SelectedGenre == "All Genres" ? "Discovering trending hits across all genres..." : $"Discovering {SelectedGenre} hits...";
        ArtworkService.Instance.ClearPending();
        Tracks.Clear();
        Albums.Clear();
        HasResults = false;
        HasNoResults = false;

        try
        {
            var results = await Task.Run(() => SearchService.GetTrendingTracksAsync(SelectedRegion, SelectedGenre, limit: 50, ct: ct), ct);
            Tracks.Clear();
            foreach (var t in results)
                Tracks.Add(t);

            HasResults = Tracks.Count > 0;
            HasNoResults = Tracks.Count == 0;
            StatusMessage = HasResults ? (SelectedGenre == "All Genres" ? "Top Trending Hits" : $"{SelectedGenre} Trending Hits") : "No trending songs found.";
        }
        catch (Exception ex)
        {
            if (Tracks.Count == 0)
            {
                IsOffline = true;
                bool isNet = !RegionService.IsInternetAvailable();
                ErrorMessage = isNet 
                    ? "No internet connection detected. Please check your network and tap retry."
                    : $"Failed to load trending hits: {ex.Message}";
                StatusMessage = "No Internet Connection.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SelectGenreAsync(object? param)
    {
        string? genre = param switch
        {
            GenreItem gi => gi.Name,
            string s => s,
            _ => null
        };
        if (string.IsNullOrEmpty(genre) || SelectedGenre == genre) return;
        SelectedGenre = genre;
        foreach (var g in Genres)
            g.IsSelected = (g.Name == genre);

        await LoadTrendingFeedAsync();
    }

    [RelayCommand]
    public async Task UpdateAutocompleteAsync(string text)
    {
        if (_suppressAutocomplete || string.IsNullOrWhiteSpace(text) || text.Length < 2)
        {
            HasSuggestions = false;
            Suggestions.Clear();
            return;
        }

        _autocompleteCts?.Cancel();
        _autocompleteCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(250, _autocompleteCts.Token); // debounce
            if (_suppressAutocomplete)
            {
                HasSuggestions = false;
                Suggestions.Clear();
                return;
            }
            var results = await SearchService.GetAutocompleteSuggestionsAsync(text, _autocompleteCts.Token);
            if (!_suppressAutocomplete)
            {
                Suggestions.Clear();
                foreach (var s in results)
                    Suggestions.Add(s);
                HasSuggestions = Suggestions.Count > 0 && !IsLoading;
            }
        }
        catch { }
    }

    [RelayCommand]
    public void SelectSuggestion(string suggestion)
    {
        _suppressAutocomplete = true;
        _autocompleteCts?.Cancel();
        HasSuggestions = false;
        Suggestions.Clear();
        SearchQuery = suggestion;
        _ = SearchAsync();
    }

    [RelayCommand]
    public void ClearSearch()
    {
        _suppressAutocomplete = true;
        _autocompleteCts?.Cancel();
        HasSuggestions = false;
        Suggestions.Clear();
        SearchQuery = string.Empty;
        _ = LoadTrendingFeedAsync();
    }

    [RelayCommand]
    public async Task PlayTrackAsync(TrackItem track)
    {
        if (track == null) return;
        await PlayerService.Instance.PlayTrackAsync(track, Tracks.ToList());
    }

    [RelayCommand]
    public void EnqueueTrack(TrackItem track)
    {
        if (track == null) return;
        DownloadService.Instance.Enqueue(track);
    }

    [RelayCommand]
    public void DownloadTrackNowAsync(TrackItem track)
    {
        if (track == null) return;
        DownloadService.Instance.Enqueue(track);
    }

    [RelayCommand]
    public void DownloadAllDisplayedAsync()
    {
        if (Tracks.Count == 0) return;
        DownloadService.Instance.EnqueueAll(Tracks);
    }

    [RelayCommand]
    public async Task ViewAlbumAsync(AlbumItem album)
    {
        if (album == null) return;
        IsLoading = true;
        StatusMessage = $"Loading album \"{album.Title}\"...";
        try
        {
            var tracks = await Task.Run(() => SearchService.LookupAlbumTracksAsync(album.AlbumId));
            album.Tracks = tracks;
            album.TrackCount = tracks.Count;
            album.IsExpanded = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load album: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DownloadAlbumNowAsync(AlbumItem album)
    {
        if (album == null) return;
        if (album.Tracks == null || album.Tracks.Count == 0)
        {
            var tracks = await Task.Run(() => SearchService.LookupAlbumTracksAsync(album.AlbumId));
            album.Tracks = tracks;
        }
        if (album.Tracks != null && album.Tracks.Count > 0)
        {
            DownloadService.Instance.EnqueueAll(album.Tracks);
        }
    }
}

public partial class GenreItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;

    public GenreItem() { }

    public GenreItem(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }
}
