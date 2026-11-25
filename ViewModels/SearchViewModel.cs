// SearchViewModel.cs — Dedicated ViewModel for Search Page with 3 View Modes (Grid, Albums, Tracks)
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _isOffline = false;
    [ObservableProperty] private bool _hasSearched = false;
    [ObservableProperty] private bool _hasSuggestions = false;
    [ObservableProperty] private bool _isRefreshing = false;
    [ObservableProperty] private string _statusMessage = "Search songs, albums, artists, or paste YouTube URLs...";

    [ObservableProperty] private string _selectedContentType = "Songs";
    [ObservableProperty] private string _selectedLayoutView = "Grid View";

    public bool IsSongsGridView => SelectedContentType == "Songs" && SelectedLayoutView == "Grid View";
    public bool IsSongsListView => SelectedContentType == "Songs" && SelectedLayoutView == "List View";
    public bool IsAlbumsView => SelectedContentType == "Albums";

    partial void OnSelectedContentTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSongsGridView));
        OnPropertyChanged(nameof(IsSongsListView));
        OnPropertyChanged(nameof(IsAlbumsView));
    }

    partial void OnSelectedLayoutViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsSongsGridView));
        OnPropertyChanged(nameof(IsSongsListView));
        OnPropertyChanged(nameof(IsAlbumsView));
    }

    public List<string> ContentTypes { get; } = ["Songs", "Albums"];
    public List<string> LayoutViews { get; } = ["Grid View", "List View"];

    public bool IsContentVisible => !IsLoading && !IsOffline && HasSearched && (_allFoundTracks.Count > 0 || _allFoundAlbums.Count > 0);

    private readonly List<TrackItem> _allFoundTracks = [];
    private readonly List<AlbumItem> _allFoundAlbums = [];
    private int _trackPage = 0;
    private int _albumPage = 0;
    private const int PageBatchSize = 14;
    [ObservableProperty] private bool _isLoadingMore = false;

    [ObservableProperty] private KeyValuePair<string, string> _currentSource;
    [ObservableProperty] private KeyValuePair<string, string> _currentRegion;

    public List<KeyValuePair<string, string>> SourceList { get; } =
    [
        new("all", "All Sources"),
        new("itunes", "Apple Music / iTunes"),
        new("deezer", "Deezer"),
        new("youtube", "YouTube Music")
    ];

    public List<KeyValuePair<string, string>> RegionList { get; } =
    [
        new("US", "United States"),
        new("GB", "United Kingdom"),
        new("KE", "Kenya"),
        new("NG", "Nigeria"),
        new("ZA", "South Africa"),
        new("TZ", "Tanzania"),
        new("UG", "Uganda"),
        new("IN", "India"),
        new("FR", "France"),
        new("DE", "Germany"),
        new("BR", "Brazil"),
        new("JP", "Japan")
    ];

    public ObservableCollection<TrackItem> Tracks { get; } = [];
    public ObservableCollection<AlbumItem> Albums { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    private CancellationTokenSource? _searchCts;

    public SearchViewModel()
    {
        var settings = StorageService.LoadSettings();
        _currentSource = SourceList.FirstOrDefault(s => s.Key == settings.DefaultSource);
        if (string.IsNullOrEmpty(_currentSource.Key)) _currentSource = SourceList[0];

        _currentRegion = RegionList.FirstOrDefault(r => r.Key == settings.DefaultRegion);
        if (string.IsNullOrEmpty(_currentRegion.Key)) _currentRegion = RegionList[0];
    }

    [RelayCommand]
    public void SetViewMode(string mode)
    {
        // Map legacy int-based modes to new string-based dropdown properties
        switch (mode)
        {
            case "Albums" or "1":
                SelectedContentType = "Albums";
                break;
            case "Tracks" or "2":
                SelectedContentType = "Songs";
                SelectedLayoutView = "List View";
                break;
            default: // Grid / 0
                SelectedContentType = "Songs";
                SelectedLayoutView = "Grid View";
                break;
        }
    }

    [RelayCommand]
    public async Task ChooseSourceAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Search Catalog",
            SourceList,
            s => s.Value,
            CurrentSource);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            CurrentSource = picked;
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                await SearchAsync();
            }
        }
    }

    [RelayCommand]
    public async Task ChooseRegionAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Search Region",
            RegionList,
            r => r.Value,
            CurrentRegion);
        if (!string.IsNullOrEmpty(picked.Key))
        {
            CurrentRegion = picked;
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                await SearchAsync();
            }
        }
    }

    [RelayCommand]
    public async Task UpdateAutocompleteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            Suggestions.Clear();
            HasSuggestions = false;
            return;
        }

        try
        {
            var results = await SearchService.GetAutocompleteSuggestionsAsync(query);
            Suggestions.Clear();
            foreach (var s in results.Take(6))
                Suggestions.Add(s);
            HasSuggestions = Suggestions.Count > 0;
        }
        catch
        {
            HasSuggestions = false;
        }
    }

    [RelayCommand]
    public async Task SelectSuggestionAsync(string suggestion)
    {
        SearchQuery = suggestion;
        HasSuggestions = false;
        Suggestions.Clear();
        await SearchAsync();
    }

    [RelayCommand]
    public async Task ChooseContentTypeAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Content Type",
            ContentTypes,
            c => c,
            SelectedContentType);
        if (!string.IsNullOrEmpty(picked))
        {
            SelectedContentType = picked;
        }
    }

    [RelayCommand]
    public async Task ChooseLayoutViewAsync()
    {
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Layout View",
            LayoutViews,
            l => l,
            SelectedLayoutView);
        if (!string.IsNullOrEmpty(picked))
        {
            SelectedLayoutView = picked;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                await SearchAsync();
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        if (!RegionService.IsInternetAvailable())
        {
            IsOffline = true;
            IsLoading = false;
            Tracks.Clear();
            Albums.Clear();
            OnPropertyChanged(nameof(IsContentVisible));
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsLoading = true;
        IsOffline = false;
        HasSearched = true;
        HasSuggestions = false;
        StatusMessage = $"Searching for \"{SearchQuery}\"...";

        // Old content disappears immediately while activity loader shows
        Tracks.Clear();
        Albums.Clear();
        OnPropertyChanged(nameof(IsContentVisible));

        try
        {
            var source = string.IsNullOrEmpty(CurrentSource.Key) ? "all" : CurrentSource.Key;
            var tracksTask = Task.Run(() => SearchService.SearchSongsAsync(SearchQuery, source, 50, token), token);
            var albumsTask = Task.Run(() => SearchService.SearchAlbumsAsync(SearchQuery, source, 30, token), token);
            await Task.WhenAll(tracksTask, albumsTask);

            _allFoundTracks.Clear();
            _allFoundTracks.AddRange(tracksTask.Result);

            _allFoundAlbums.Clear();
            _allFoundAlbums.AddRange(albumsTask.Result);

            Tracks.Clear();
            Albums.Clear();
            _trackPage = 0;
            _albumPage = 0;

            LoadNextTrackBatch();
            LoadNextAlbumBatch();

            if (_allFoundTracks.Count == 0 && _allFoundAlbums.Count == 0)
            {
                StatusMessage = $"No results found for \"{SearchQuery}\".";
            }
            else
            {
                StatusMessage = $"Found {_allFoundTracks.Count} tracks and {_allFoundAlbums.Count} albums.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsOffline = true;
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsContentVisible));
        }
    }

    [RelayCommand]
    public void LoadMoreTracks()
    {
        if (IsLoading || IsLoadingMore) return;
        LoadNextTrackBatch();
    }

    [RelayCommand]
    public void LoadMoreAlbums()
    {
        if (IsLoading || IsLoadingMore) return;
        LoadNextAlbumBatch();
    }

    private void LoadNextTrackBatch()
    {
        if (_allFoundTracks.Count == 0) return;
        int startIndex = _trackPage * PageBatchSize;
        if (startIndex >= _allFoundTracks.Count) return;

        IsLoadingMore = true;
        try
        {
            var nextBatch = _allFoundTracks.Skip(startIndex).Take(PageBatchSize).ToList();
            foreach (var t in nextBatch)
                Tracks.Add(t);
            _trackPage++;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void LoadNextAlbumBatch()
    {
        if (_allFoundAlbums.Count == 0) return;
        int startIndex = _albumPage * PageBatchSize;
        if (startIndex >= _allFoundAlbums.Count) return;

        IsLoadingMore = true;
        try
        {
            var nextBatch = _allFoundAlbums.Skip(startIndex).Take(PageBatchSize).ToList();
            foreach (var a in nextBatch)
                Albums.Add(a);
            _albumPage++;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = "";
        _allFoundTracks.Clear();
        _allFoundAlbums.Clear();
        _trackPage = 0;
        _albumPage = 0;
        Tracks.Clear();
        Albums.Clear();
        Suggestions.Clear();
        HasSuggestions = false;
        HasSearched = false;
        IsOffline = false;
        StatusMessage = "Search songs, albums, artists, or paste YouTube URLs...";
        OnPropertyChanged(nameof(IsContentVisible));
    }

    [RelayCommand]
    public async Task PlayTrackAsync(TrackItem track)
    {
        if (track == null) return;
        var queue = _allFoundTracks.Count > 0 ? _allFoundTracks : Tracks.ToList();
        await PlayerService.Instance.PlayTrackAsync(track, queue);
    }

    [RelayCommand]
    public async Task QueueTrackAsync(TrackItem track)
    {
        if (track == null) return;

        bool confirmed = await CustomDialogService.ShowConfirmAsync(
            "Download Song",
            $"Are you sure you want to download \"{track.DisplayTitle}\"?",
            "Yes",
            "No");

        if (confirmed)
        {
            DownloadService.Instance.Enqueue(track);
        }
    }

    [RelayCommand]
    public async Task QueueAlbumAsync(AlbumItem album)
    {
        if (album == null) return;

        bool confirmed = await CustomDialogService.ShowConfirmAsync(
            "Download Album",
            $"Are you sure you want to download album \"{album.Title}\" by {album.Artist}?",
            "Yes",
            "No");

        if (confirmed)
        {
            var tracks = await SearchService.LookupAlbumTracksAsync(album.AlbumId);
            if (tracks.Count > 0)
            {
                DownloadService.Instance.EnqueueAll(tracks);
            }
        }
    }
}
