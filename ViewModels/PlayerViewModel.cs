// PlayerViewModel.cs — Comprehensive ViewModel for Music Player & Playlist Manager
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    public PlayerService Player => PlayerService.Instance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private double _durationSeconds;

    public double ProgressFraction => DurationSeconds > 0 ? Math.Clamp(PositionSeconds / DurationSeconds, 0.0, 1.0) : 0.0;
    [ObservableProperty] private string _positionStr = "00:00";
    [ObservableProperty] private string _durationStr = "00:00";
    [ObservableProperty] private bool _isUserSeeking = false;
    [ObservableProperty] private int _selectedTabIndex = 0; // 0 = Up Next Queue, 1 = Playlists, 2 = History
    [ObservableProperty] private string _selectedPlaylist = "Favorites";
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private bool _canDeleteSelectedPlaylist = false;

    public ObservableCollection<TrackItem> Playlist { get; } = [];
    public ObservableCollection<string> Playlists { get; } = [];
    public ObservableCollection<TrackItem> SelectedPlaylistTracks { get; } = [];
    public ObservableCollection<TrackItem> PlayHistory { get; } = [];

    public PlayerViewModel()
    {
        Player.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Player.Position) && !IsUserSeeking)
            {
                PositionSeconds = Player.Position.TotalSeconds;
                PositionStr = Player.Position.ToString(@"mm\:ss");
            }
            else if (e.PropertyName == nameof(Player.Duration))
            {
                DurationSeconds = Player.Duration.TotalSeconds;
                DurationStr = Player.Duration.ToString(@"mm\:ss");
            }
            else if (e.PropertyName == nameof(Player.CurrentTrack))
            {
                RefreshPlaylist();
                RefreshHistory();
            }
            else if (e.PropertyName == nameof(Player.Playlist))
            {
                RefreshPlaylist();
            }
        };

        RefreshPlaylist();
        RefreshPlaylistsList();
        RefreshHistory();
    }

    public void RefreshPlaylist()
    {
        Playlist.Clear();
        foreach (var t in Player.Playlist)
            Playlist.Add(t);
    }

    public void RefreshPlaylistsList()
    {
        Playlists.Clear();
        Playlists.Add("Recent Downloads");
        var all = StorageService.GetPlaylists();
        foreach (var key in all.Keys)
            Playlists.Add(key);

        if (!Playlists.Contains(SelectedPlaylist))
            SelectedPlaylist = Playlists.FirstOrDefault() ?? "Favorites";

        RefreshSelectedPlaylistTracks();
    }

    [RelayCommand]
    public async Task ChoosePlaylistAsync()
    {
        if (Playlists.Count == 0) return;
        var picked = await CustomDialogService.ShowOptionsAsync(
            "Select Playlist",
            Playlists,
            p => p,
            SelectedPlaylist);
        if (!string.IsNullOrEmpty(picked))
        {
            SelectedPlaylist = picked;
        }
    }

    public void RefreshSelectedPlaylistTracks()
    {
        SelectedPlaylistTracks.Clear();

        if (SelectedPlaylist == "Recent Downloads")
        {
            var history = StorageService.GetDownloadHistory();
            foreach (var rec in history)
            {
                SelectedPlaylistTracks.Add(new TrackItem
                {
                    Id = rec.TrackId,
                    Title = rec.Title,
                    Artist = rec.Artist,
                    FilePath = rec.FilePath,
                    ThumbnailUrl = string.Empty,
                    DurationStr = "--:--",
                    Status = "Downloaded"
                });
            }
            CanDeleteSelectedPlaylist = false;
            return;
        }

        var all = StorageService.GetPlaylists();
        if (all.TryGetValue(SelectedPlaylist, out var list))
        {
            foreach (var t in list)
                SelectedPlaylistTracks.Add(t);
        }

        CanDeleteSelectedPlaylist = !StorageService.PresetPlaylists.Contains(SelectedPlaylist, StringComparer.OrdinalIgnoreCase) && SelectedPlaylist != "Recent Downloads";
    }

    public void RefreshHistory()
    {
        PlayHistory.Clear();
        foreach (var r in StorageService.GetPlayHistory())
        {
            PlayHistory.Add(r);
        }
    }

    partial void OnSelectedPlaylistChanged(string value)
    {
        RefreshSelectedPlaylistTracks();
    }

    partial void OnVolumeChanged(double value)
    {
        Player.Volume = Math.Clamp(value, 0.0, 1.0);
    }

    [RelayCommand]
    public void SelectTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
            SelectedTabIndex = idx;
    }

    [RelayCommand]
    public void TogglePlayPause() => Player.TogglePlayPause();

    [RelayCommand]
    public async Task NextAsync() => await Player.NextAsync();

    [RelayCommand]
    public async Task PreviousAsync() => await Player.PreviousAsync();

    [RelayCommand]
    public void ToggleShuffle() => Player.ToggleShuffle();

    [RelayCommand]
    public void CycleRepeat() => Player.CycleRepeat();

    [RelayCommand]
    public void OnSeekStarted() => IsUserSeeking = true;

    [RelayCommand]
    public void SeekFraction(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        double targetSec = fraction * DurationSeconds;
        PositionSeconds = targetSec;
        PositionStr = TimeSpan.FromSeconds(targetSec).ToString(@"mm\:ss");
        Player.Seek(TimeSpan.FromSeconds(targetSec));
    }

    [RelayCommand]
    public void OnSeekCompleted(double value)
    {
        IsUserSeeking = false;
        Player.Seek(TimeSpan.FromSeconds(value));
    }

    [RelayCommand]
    public async Task PlayFromPlaylistAsync(TrackItem track)
    {
        if (track == null) return;
        await Player.PlayTrackAsync(track, Playlist);
    }

    [RelayCommand]
    public void RemoveFromQueue(TrackItem track)
    {
        if (track == null) return;
        Player.RemoveFromQueue(track.Id);
        RefreshPlaylist();
    }

    [RelayCommand]
    public void ClearQueue()
    {
        Player.ClearQueue();
        RefreshPlaylist();
    }

    // ── Playlist Management ──

    [RelayCommand]
    public async Task CreatePlaylistAsync()
    {
        string? name = await CustomDialogService.ShowPromptAsync("New Playlist", "Enter a name for your new playlist:", "My Playlist", "Create", "Cancel");
        if (!string.IsNullOrWhiteSpace(name))
        {
            name = name.Trim();
            if (StorageService.CreatePlaylist(name))
            {
                SelectedPlaylist = name;
                RefreshPlaylistsList();
            }
            else
            {
                await CustomDialogService.ShowAlertAsync("Playlist Exists", $"A playlist named \"{name}\" already exists.");
            }
        }
    }

    [RelayCommand]
    public async Task DeletePlaylistAsync()
    {
        if (StorageService.PresetPlaylists.Contains(SelectedPlaylist, StringComparer.OrdinalIgnoreCase))
        {
            await CustomDialogService.ShowAlertAsync("Cannot Delete", "Preset playlists cannot be deleted.");
            return;
        }

        bool confirm = await CustomDialogService.ShowConfirmAsync("Delete Playlist", $"Are you sure you want to delete playlist \"{SelectedPlaylist}\"?", "Delete", "Cancel");
        if (confirm)
        {
            StorageService.DeletePlaylist(SelectedPlaylist);
            SelectedPlaylist = StorageService.PresetPlaylists[0];
            RefreshPlaylistsList();
        }
    }

    [RelayCommand]
    public async Task PlayAllFromPlaylistAsync()
    {
        if (SelectedPlaylistTracks.Count == 0) return;
        var first = SelectedPlaylistTracks.First();
        await Player.PlayTrackAsync(first, SelectedPlaylistTracks);
    }

    [RelayCommand]
    public async Task PlayPlaylistTrackAsync(TrackItem track)
    {
        if (track == null) return;
        await Player.PlayTrackAsync(track, SelectedPlaylistTracks);
    }

    [RelayCommand]
    public void RemoveTrackFromPlaylist(TrackItem track)
    {
        if (track == null) return;
        StorageService.RemoveTrackFromPlaylist(SelectedPlaylist, track.Id);
        RefreshSelectedPlaylistTracks();
    }

    [RelayCommand]
    public async Task AddCurrentTrackToPlaylistAsync()
    {
        if (Player.CurrentTrack == null) return;
        var track = Player.CurrentTrack;

        var availablePlaylists = StorageService.GetPlaylists().Keys.ToList();
        var choice = await CustomDialogService.ShowOptionsAsync(
            "Save Track to Playlist",
            availablePlaylists,
            p => p,
            availablePlaylists.FirstOrDefault() ?? "Favorites");

        if (!string.IsNullOrEmpty(choice))
        {
            // If track is not yet downloaded, enqueue download first for offline access
            if (string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
            {
                DownloadService.Instance.Enqueue(track);
            }

            StorageService.AddTrackToPlaylist(choice, track);
            if (SelectedPlaylist == choice)
                RefreshSelectedPlaylistTracks();

            await CustomDialogService.ShowAlertAsync("Saved to Playlist", $"Added \"{track.DisplayTitle}\" to playlist \"{choice}\".");
        }
    }
}
