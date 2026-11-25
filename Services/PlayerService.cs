// PlayerService.cs — Central in-app audio playback manager
using CommunityToolkit.Mvvm.ComponentModel;
using Burudani.Models;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Burudani.Services;

public enum RepeatMode
{
    Off,
    All,
    One
}

public partial class PlayerService : ObservableObject
{
    private static PlayerService? _instance;
    public static PlayerService Instance => _instance ??= new PlayerService();

    private readonly YoutubeClient _youtube = new();
    private readonly Random _random = new();

    [ObservableProperty] private TrackItem? _currentTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private bool _isMuted = false;
    private double _previousVolume = 1.0;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private RepeatMode _repeat = RepeatMode.Off;
    [ObservableProperty] private string _playbackSourceUrl = string.Empty;

    private readonly List<TrackItem> _playlist = [];
    private int _currentIndex = -1;

    public IReadOnlyList<TrackItem> Playlist => _playlist;
    public int CurrentIndex => _currentIndex;

    // Events for platform audio or MediaElement bridge
    public event Action<string>? MediaSourceRequested; // url or local path
    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action<TimeSpan>? SeekRequested;
    public event Action<double>? VolumeChanged;

    partial void OnVolumeChanged(double value)
    {
        if (value <= 0.001)
        {
            _isMuted = true;
            OnPropertyChanged(nameof(IsMuted));
        }
        else if (_isMuted)
        {
            _isMuted = false;
            OnPropertyChanged(nameof(IsMuted));
        }
        VolumeChanged?.Invoke(_isMuted ? 0 : value);
    }

    public void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _previousVolume > 0.05 ? _previousVolume : 0.8;
        }
        else
        {
            _previousVolume = Volume > 0.05 ? Volume : 0.8;
            IsMuted = true;
            Volume = 0;
        }
    }

    private CancellationTokenSource? _playCts;
    private readonly SemaphoreSlim _playGate = new(1, 1);

    private PlayerService() { }

    public async Task PlayTrackAsync(TrackItem track, IEnumerable<TrackItem>? queue = null)
    {
        if (track == null) return;

        // Immediately cancel any previous playback preparation and stop current audio
        _playCts?.Cancel();
        var cts = new CancellationTokenSource();
        _playCts = cts;

        StopRequested?.Invoke();
        IsPlaying = false;

        await _playGate.WaitAsync();
        try
        {
            if (cts.IsCancellationRequested) return;

            if (queue != null)
            {
                _playlist.Clear();
                _playlist.AddRange(queue);
                _currentIndex = _playlist.FindIndex(t => t.Id == track.Id);
                if (_currentIndex == -1)
                {
                    _playlist.Insert(0, track);
                    _currentIndex = 0;
                }
            }
            else
            {
                if (!_playlist.Any(t => t.Id == track.Id))
                {
                    _playlist.Add(track);
                }
                _currentIndex = _playlist.FindIndex(t => t.Id == track.Id);
            }

            if (!string.IsNullOrEmpty(track.FilePath) && string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                track.ThumbnailUrl = track.FilePath;
            }

            CurrentTrack = track;
            IsLoading = true;

            string playUrl = string.Empty;

            // 1. If local file exists, play local
            if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            {
                playUrl = track.FilePath;
            }
            else
            {
                var settings = StorageService.LoadSettings();
                var (exists, localPath) = StorageService.IsTrackAlreadyDownloaded(track, settings.DownloadFolder);
                if (exists && File.Exists(localPath))
                {
                    track.FilePath = localPath;
                    if (string.IsNullOrEmpty(track.ThumbnailUrl))
                    {
                        track.ThumbnailUrl = localPath;
                    }
                    playUrl = localPath;
                }
                else
                {
                    try
                    {
                        playUrl = await ResolveStreamUrlAsync(track, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PlayerService] Stream resolve failed: {ex.Message}");
                    }

                    // Never fall back to 30-second preview audio snippets (e.g. from iTunes/Deezer RSS feeds)
                    if (string.IsNullOrEmpty(playUrl) && !string.IsNullOrEmpty(track.StreamUrl) &&
                        !track.StreamUrl.Contains("apple.com") && !track.StreamUrl.Contains("itunes.apple.com") &&
                        !track.StreamUrl.Contains("e-cdn-preview") && !track.StreamUrl.Contains("dzcdn.net"))
                    {
                        playUrl = track.StreamUrl;
                    }
                }
            }

            if (cts.IsCancellationRequested) return;

            if (string.IsNullOrEmpty(playUrl))
            {
                throw new Exception("Unable to resolve full audio stream for track.");
            }

            PlaybackSourceUrl = playUrl;
            MediaSourceRequested?.Invoke(playUrl);
            IsPlaying = true;
            StorageService.LogPlay(track);
            StartPlatformMediaService();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerService] Error playing track: {ex.Message}");
            IsPlaying = false;
        }
        finally
        {
            IsLoading = false;
            _playGate.Release();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _streamCache = new();

    private async Task<string> ResolveStreamUrlAsync(TrackItem track, CancellationToken ct = default)
    {
        string cacheKey = !string.IsNullOrEmpty(track.Id) ? track.Id : $"{track.Artist}_{track.Title}".Trim().ToLowerInvariant();
        if (_streamCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        string videoUrl = track.SourceUrl;
        if (string.IsNullOrEmpty(videoUrl) || (!videoUrl.Contains("youtube.com") && !videoUrl.Contains("youtu.be")))
        {
            var cleanTitle = CleanSearchString(track.Title);
            var cleanArtist = CleanSearchString(track.Artist);
            var queries = new List<string>();

            if (!string.IsNullOrEmpty(cleanArtist) && !string.IsNullOrEmpty(cleanTitle))
            {
                queries.Add($"{cleanArtist} {cleanTitle} official audio");
                queries.Add($"{cleanArtist} {cleanTitle}");
                queries.Add($"{cleanTitle} {cleanArtist}");
            }
            else
            {
                var cleanDisplay = CleanSearchString(track.DisplayTitle);
                queries.Add($"{cleanDisplay} official audio");
                queries.Add(cleanDisplay);
            }

            foreach (var q in queries)
            {
                try
                {
                    await foreach (var item in _youtube.Search.GetVideosAsync(q, ct))
                    {
                        videoUrl = item.Url;
                        track.SourceUrl = videoUrl;
                        break;
                    }

                    if (!string.IsNullOrEmpty(videoUrl))
                        break;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(videoUrl)) return string.Empty;
        }

        try
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoUrl, ct);
            var audioStreams = manifest.GetAudioOnlyStreams().ToList();

            // Prefer highest-bitrate MP4 / AAC audio stream for universal mobile / ExoPlayer streaming compatibility
            var audioStream = audioStreams
                .Where(s => s.Container == Container.Mp4 ||
                            s.Container.Name.Equals("m4a", StringComparison.OrdinalIgnoreCase) ||
                            s.Container.Name.Equals("mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault()
                ?? audioStreams.OrderByDescending(s => s.Bitrate).FirstOrDefault();

            if (audioStream != null && !string.IsNullOrEmpty(audioStream.Url))
            {
                string url = audioStream.Url;
                _streamCache[cacheKey] = url;
                return url;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResolveStreamUrlAsync Manifest Error] {ex.Message}");
        }

        return string.Empty;
    }

    private static string CleanSearchString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var clean = System.Text.RegularExpressions.Regex.Replace(input, @"[^\w\s]", " ");
        return System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Play()
    {
        if (CurrentTrack != null)
        {
            IsPlaying = true;
            PlayRequested?.Invoke();
            StartPlatformMediaService();
        }
    }

    private static void StartPlatformMediaService()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(Platforms.Android.Services.BurudaniMediaService));
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartMediaService Error] {ex.Message}");
        }
#endif
    }

    public void Pause()
    {
        IsPlaying = false;
        PauseRequested?.Invoke();
    }

    public void Stop()
    {
        IsPlaying = false;
        Position = TimeSpan.Zero;
        StopRequested?.Invoke();
    }

    public void RemoveFromQueue(string trackId)
    {
        int idx = _playlist.FindIndex(t => t.Id == trackId);
        if (idx != -1)
        {
            _playlist.RemoveAt(idx);
            if (_currentIndex >= _playlist.Count)
                _currentIndex = _playlist.Count - 1;
            OnPropertyChanged(nameof(Playlist));
        }
    }

    public void ClearQueue()
    {
        _playlist.Clear();
        _currentIndex = -1;
        OnPropertyChanged(nameof(Playlist));
    }

    public void Seek(TimeSpan target)
    {
        Position = target;
        SeekRequested?.Invoke(target);
    }

    public async Task NextAsync()
    {
        if (_playlist.Count == 0) return;

        if (Repeat == RepeatMode.One && CurrentTrack != null)
        {
            await PlayTrackAsync(CurrentTrack);
            return;
        }

        if (IsShuffle)
        {
            _currentIndex = _random.Next(0, _playlist.Count);
        }
        else
        {
            _currentIndex++;
            if (_currentIndex >= _playlist.Count)
            {
                if (Repeat == RepeatMode.All)
                    _currentIndex = 0;
                else
                {
                    Stop();
                    return;
                }
            }
        }

        var nextTrack = _playlist[_currentIndex];
        await PlayTrackAsync(nextTrack);
    }

    public async Task PreviousAsync()
    {
        if (_playlist.Count == 0) return;

        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        _currentIndex--;
        if (_currentIndex < 0)
        {
            _currentIndex = _playlist.Count - 1;
        }

        var prevTrack = _playlist[_currentIndex];
        await PlayTrackAsync(prevTrack);
    }

    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
    }

    public double PositionSeconds => Position.TotalSeconds;
    public double DurationSeconds => Duration.TotalSeconds > 0 ? Duration.TotalSeconds : 1;
    public string TimeString => $"{(int)Position.TotalMinutes}:{Position.Seconds:D2} / {(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";

    // Called by the UI or MediaElement when media ends
    public async Task OnMediaEndedAsync()
    {
        await NextAsync();
    }

    // Called by MediaElement position changed
    public void UpdatePosition(TimeSpan pos, TimeSpan dur)
    {
        Position = pos;
        if (dur > TimeSpan.Zero)
        {
            Duration = dur;
        }
        else if (CurrentTrack != null && CurrentTrack.DurationSec > 0 && Duration <= TimeSpan.Zero)
        {
            Duration = TimeSpan.FromSeconds(CurrentTrack.DurationSec);
        }
        else if (CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.DurationStr) && Duration <= TimeSpan.Zero)
        {
            if (TimeSpan.TryParseExact(CurrentTrack.DurationStr, @"m\:ss", null, out var parsed) ||
                TimeSpan.TryParseExact(CurrentTrack.DurationStr, @"mm\:ss", null, out parsed))
            {
                Duration = parsed;
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(PositionSeconds));
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(TimeString));
        });
    }
}
