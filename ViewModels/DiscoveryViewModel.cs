// DiscoveryViewModel.cs — Comprehensive Trending Music & Genre Explorer with Incremental Lazy Loading
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class DiscoveryViewModel : ObservableObject
{
    private const int PageBatchSize = 16;
    private readonly List<TrackItem> _allLoadedTracks = [];

    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _isLoadingMore = false;
    [ObservableProperty] private bool _isOffline = false;
    [ObservableProperty] private bool _isRefreshing = false;
    [ObservableProperty] private string _statusMessage = "Discovering trending hits...";
    [ObservableProperty] private string _selectedGenre = "All Genres";
    [ObservableProperty] private KeyValuePair<string, string> _currentRegion;

    public bool IsContentVisible => !IsLoading && !IsOffline && DisplayedTracks.Count > 0;

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

    public ObservableCollection<GenreItem> Genres { get; } =
    [
        new("All Genres", true),
        new("Pop"),
        new("Hip-Hop"),
        new("R&B"),
        new("Afrobeats"),
        new("Rock"),
        new("Electronic"),
        new("Reggae"),
        new("Country"),
        new("Jazz"),
        new("Latin"),
        new("Acoustic"),
        new("Metal")
    ];

    /// <summary>
    /// Virtualized and paginated subset of tracks loaded incrementally for smooth 60fps scrolling and zero memory spikes.
    /// </summary>
    public ObservableCollection<TrackItem> DisplayedTracks { get; } = [];

    private CancellationTokenSource? _fetchCts;
    private int _currentPage = 0;

    public DiscoveryViewModel()
    {
        var settings = StorageService.LoadSettings();
        _currentRegion = RegionList.FirstOrDefault(r => r.Key == settings.DefaultRegion);
        if (string.IsNullOrEmpty(_currentRegion.Key)) _currentRegion = RegionList[0];

        _ = LoadTrendingAsync();
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
            await LoadTrendingAsync();
        }
    }

    [RelayCommand]
    public async Task SelectGenreAsync(GenreItem genre)
    {
        if (genre == null || genre.Name == SelectedGenre) return;

        foreach (var g in Genres)
            g.IsSelected = g.Name == genre.Name;

        SelectedGenre = genre.Name;
        await LoadTrendingAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadTrendingAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task LoadTrendingAsync()
    {
        if (!RegionService.IsInternetAvailable())
        {
            IsOffline = true;
            IsLoading = false;
            _allLoadedTracks.Clear();
            DisplayedTracks.Clear();
            StatusMessage = "No Internet Connection";
            OnPropertyChanged(nameof(IsContentVisible));
            return;
        }

        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var token = _fetchCts.Token;

        IsLoading = true;
        IsOffline = false;
        StatusMessage = SelectedGenre == "All Genres"
            ? $"Discovering trending hits in {CurrentRegion.Value}..."
            : $"Discovering {SelectedGenre} hits in {CurrentRegion.Value}...";

        // Content disappears while activity loader shows
        _allLoadedTracks.Clear();
        DisplayedTracks.Clear();
        OnPropertyChanged(nameof(IsContentVisible));

        try
        {
            _currentPage = 0;

            var results = await SearchService.GetTrendingHitsAsync(SelectedGenre, CurrentRegion.Key, token);

            foreach (var track in results)
            {
                _allLoadedTracks.Add(track);
            }

            if (_allLoadedTracks.Count == 0)
            {
                StatusMessage = $"No trending {SelectedGenre} tracks found in this region.";
            }
            else
            {
                StatusMessage = SelectedGenre == "All Genres"
                    ? $"Top Trending Hits ({_allLoadedTracks.Count} songs)"
                    : $"{SelectedGenre} Trending Hits ({_allLoadedTracks.Count} songs)";

                LoadNextBatch();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsOffline = true;
            StatusMessage = $"Failed to load trending hits: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsContentVisible));
        }
    }

    [RelayCommand]
    public void LoadMoreItems()
    {
        if (IsLoading || IsLoadingMore) return;
        LoadNextBatch();
    }

    private void LoadNextBatch()
    {
        if (_allLoadedTracks.Count == 0) return;

        int startIndex = _currentPage * PageBatchSize;
        if (startIndex >= _allLoadedTracks.Count) return;

        IsLoadingMore = true;
        try
        {
            var nextBatch = _allLoadedTracks.Skip(startIndex).Take(PageBatchSize).ToList();
            foreach (var track in nextBatch)
            {
                ArtworkService.Instance.AnalyzeTrackContrast(track);
                DisplayedTracks.Add(track);
            }
            _currentPage++;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    public async Task PlayTrackAsync(TrackItem track)
    {
        if (track == null) return;
        await PlayerService.Instance.PlayTrackAsync(track, DisplayedTracks);
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
}
