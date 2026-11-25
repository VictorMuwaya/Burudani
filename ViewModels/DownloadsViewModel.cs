// DownloadsViewModel.cs — ViewModel for Downloads Hub Page
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallProgressFraction))]
    private double _overallProgress = 0.0;

    public double OverallProgressFraction => Math.Clamp(OverallProgress / 100.0, 0.0, 1.0);
    [ObservableProperty] private string _overallSpeed = "0 KB/s";
    [ObservableProperty] private string _downloadFolder = string.Empty;
    [ObservableProperty] private string _quality = "320";
    [ObservableProperty] private bool _hasQueueItems = false;
    [ObservableProperty] private bool _hasHistoryItems = false;
    [ObservableProperty] private int _activeDownloadCount = 0;
    [ObservableProperty] private string _historySearchQuery = string.Empty;
    [ObservableProperty] private int _mobileTabSelected = 0; // 0 = Queue, 1 = History for Android

    public ObservableCollection<TrackItem> QueueItems { get; } = [];
    public ObservableCollection<DownloadRecord> HistoryItems { get; } = [];
    public ObservableCollection<DownloadRecord> FilteredHistoryItems { get; } = [];

    public DownloadsViewModel()
    {
        var settings = StorageService.LoadSettings();
        DownloadFolder = settings.DownloadFolder;
        Quality = settings.Bitrate;

        // Wire up DownloadService events
        DownloadService.Instance.ItemStarted += OnItemStarted;
        DownloadService.Instance.ItemProgress += OnItemProgress;
        DownloadService.Instance.ItemCompleted += OnItemCompleted;
        DownloadService.Instance.QueueStatusChanged += OnQueueStatusChanged;

        RefreshQueue();
        RefreshHistory();
    }

    [RelayCommand]
    public void RefreshQueue()
    {
        QueueItems.Clear();
        foreach (var t in DownloadService.Instance.Queue)
            QueueItems.Add(t);

        HasQueueItems = QueueItems.Count > 0;
        ActiveDownloadCount = QueueItems.Count(t => t.Status == "Downloading" || t.Status == "Queued");
    }

    [RelayCommand]
    public void RefreshHistory()
    {
        HistoryItems.Clear();
        foreach (var r in StorageService.GetDownloadHistory())
            HistoryItems.Add(r);

        FilterHistory();
        HasHistoryItems = HistoryItems.Count > 0;
    }

    partial void OnHistorySearchQueryChanged(string value)
    {
        FilterHistory();
    }

    private void FilterHistory()
    {
        FilteredHistoryItems.Clear();
        var q = HistorySearchQuery.Trim();
        foreach (var r in HistoryItems)
        {
            if (string.IsNullOrEmpty(q) ||
                r.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Album.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredHistoryItems.Add(r);
            }
        }
    }

    [RelayCommand]
    public void CancelCurrent()
    {
        DownloadService.Instance.CancelCurrent();
    }

    [RelayCommand]
    public void RetryFailed()
    {
        DownloadService.Instance.RetryFailed();
        RefreshQueue();
    }

    [RelayCommand]
    public void RemoveQueueItem(TrackItem track)
    {
        if (track == null) return;
        DownloadService.Instance.RemoveFromQueue(track.Id);
        RefreshQueue();
    }

    [RelayCommand]
    public void ClearQueue()
    {
        DownloadService.Instance.ClearQueue();
        RefreshQueue();
    }

    [RelayCommand]
    public void ClearHistory()
    {
        StorageService.ClearDownloadHistory();
        RefreshHistory();
    }

    [RelayCommand]
    public void DeleteHistoryItem(DownloadRecord record)
    {
        if (record == null) return;
        StorageService.DeleteDownloadRecord(record.Id);
        RefreshHistory();
    }

    [RelayCommand]
    public async Task OpenDownloadFolderAsync()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
            {
#if WINDOWS
                Process.Start(new ProcessStartInfo
                {
                    FileName = DownloadFolder,
                    UseShellExecute = true
                });
#else
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(DownloadFolder)
                });
#endif
            }
        }
        catch { }
    }

    [RelayCommand]
    public void RevealFileInFolder(DownloadRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.FilePath) || !File.Exists(record.FilePath))
            return;

#if WINDOWS
        try
        {
            Process.Start("explorer.exe", $"/select,\"{record.FilePath}\"");
        }
        catch { }
#endif
    }

    [RelayCommand]
    public void RetryDownload(TrackItem track)
    {
        if (track == null) return;
        track.Status = "Queued";
        track.Progress = 0;
        track.ErrorMessage = string.Empty;
        DownloadService.Instance.Enqueue(track);
        RefreshQueue();
    }

    [RelayCommand]
    public async Task PlayDownloadedTrackAsync(DownloadRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.FilePath) || !File.Exists(record.FilePath))
            return;

        var track = new TrackItem
        {
            Id = record.TrackId,
            Title = record.Title,
            Artist = record.Artist,
            Album = record.Album,
            FilePath = record.FilePath,
            ThumbnailUrl = record.FilePath
        };

        await PlayerService.Instance.PlayTrackAsync(track);
    }

    private void OnItemStarted(object? sender, string trackId)
    {
        MainThread.BeginInvokeOnMainThread(RefreshQueue);
    }

    private void OnItemProgress(object? sender, DownloadProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OverallSpeed = e.SpeedStr;
            var item = QueueItems.FirstOrDefault(t => t.Id == e.TrackId);
            if (item != null)
            {
                item.Progress = e.Progress;
                item.SpeedStr = e.SpeedStr;
                item.EtaStr = e.EtaStr;
            }

            if (QueueItems.Count > 0)
            {
                OverallProgress = QueueItems.Average(t => t.Progress);
            }
        });
    }

    private void OnItemCompleted(object? sender, DownloadCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshQueue();
            RefreshHistory();
        });
    }

    private void OnQueueStatusChanged(object? sender, string msg)
    {
        MainThread.BeginInvokeOnMainThread(RefreshQueue);
    }
}
