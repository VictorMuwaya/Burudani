// DownloadService.cs — Background sequential audio download manager with FFmpeg transcoding & ID3 tagging
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Burudani.Models;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Burudani.Services;

public class DownloadProgressEventArgs : EventArgs
{
    public string TrackId { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string SpeedStr { get; set; } = string.Empty;
    public string EtaStr { get; set; } = string.Empty;
}

public class DownloadCompletedEventArgs : EventArgs
{
    public string TrackId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class DownloadService
{
    private static DownloadService? _instance;
    public static DownloadService Instance => _instance ??= new DownloadService();

    static DownloadService()
    {
        // Enforce ID3v2.3 for 100% compatibility with Windows Explorer, Media Player, and Android
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    private readonly YoutubeClient _youtube = new();
    private readonly ConcurrentQueue<TrackItem> _queue = new();
    private readonly List<TrackItem> _activeQueueList = [];
    private readonly ConcurrentDictionary<string, byte[]> _albumCoverCache = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private bool _isProcessing = false;

    public event EventHandler<DownloadProgressEventArgs>? ItemProgress;
    public event EventHandler<DownloadCompletedEventArgs>? ItemCompleted;
    public event EventHandler<string>? ItemStarted;
    public event EventHandler<string>? QueueStatusChanged;

    public IReadOnlyList<TrackItem> Queue
    {
        get
        {
            lock (_lock) return _activeQueueList.ToList();
        }
    }

    public bool IsBusy => _isProcessing;

    public static string? GetFFmpegPath()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localExe = Path.Combine(baseDir, "ffmpeg.exe");
            if (File.Exists(localExe)) return localExe;

            var localBin = Path.Combine(baseDir, "ffmpeg");
            if (File.Exists(localBin)) return localBin;

            // Search system PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var cand = Path.Combine(p.Trim(), "ffmpeg.exe");
                if (File.Exists(cand)) return cand;
                var candNoExt = Path.Combine(p.Trim(), "ffmpeg");
                if (File.Exists(candNoExt)) return candNoExt;
            }
        }
        catch { }
        return null;
    }

    public void Enqueue(TrackItem track, string? destinationFolder = null)
    {
        if (track == null) return;

        var settings = StorageService.LoadSettings();
        var dest = destinationFolder ?? (string.IsNullOrEmpty(track.Destination) ? settings.DownloadFolder : track.Destination);
        track.Destination = dest;
        track.Status = "Queued";
        track.Progress = 0.0;
        track.ErrorMessage = string.Empty;

        lock (_lock)
        {
            if (_activeQueueList.Any(t => t.Id == track.Id))
                return; // already in queue

            _activeQueueList.Add(track);
            _queue.Enqueue(track);
        }

        QueueStatusChanged?.Invoke(this, $"Queued: {track.DisplayTitle}");
        StartProcessing();
    }

    public void EnqueueAll(IEnumerable<TrackItem> tracks, string? destinationFolder = null)
    {
        foreach (var t in tracks)
            Enqueue(t, destinationFolder);
    }

    public void RemoveFromQueue(string trackId)
    {
        lock (_lock)
        {
            var item = _activeQueueList.FirstOrDefault(t => t.Id == trackId);
            if (item != null && item.Status != "Downloading")
            {
                _activeQueueList.Remove(item);
            }
        }
        QueueStatusChanged?.Invoke(this, "Item removed");
    }

    public void CancelCurrent()
    {
        _cts?.Cancel();
    }

    public void ClearQueue()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            _activeQueueList.RemoveAll(t => t.Status != "Downloading");
            while (_queue.TryDequeue(out _)) { }
        }
        QueueStatusChanged?.Invoke(this, "Queue cleared");
    }

    public void RetryFailed()
    {
        List<TrackItem> failed;
        lock (_lock)
        {
            failed = _activeQueueList.Where(t => t.Status == "Failed").ToList();
        }
        foreach (var f in failed)
        {
            f.Status = "Queued";
            f.Progress = 0.0;
            f.ErrorMessage = string.Empty;
            _queue.Enqueue(f);
        }
        if (failed.Count > 0)
        {
            QueueStatusChanged?.Invoke(this, $"Retrying {failed.Count} failed tracks");
            StartProcessing();
        }
    }

    private void StartProcessing()
    {
        lock (_lock)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var intent = new Android.Content.Intent(ctx, typeof(Platforms.Android.Services.BurudaniDownloadService));
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                ctx.StartForegroundService(intent);
            else
                ctx.StartService(intent);
        }
        catch { }
#endif

        Task.Run(async () =>
        {
            while (true)
            {
                TrackItem? track = null;
                lock (_lock)
                {
                    if (!_queue.TryDequeue(out track))
                    {
                        _isProcessing = false;
                        QueueStatusChanged?.Invoke(this, "All downloads finished");
                        break;
                    }
                }

                if (track == null) continue;

                _cts = new CancellationTokenSource();
                await ProcessTrackDownloadAsync(track, _cts.Token);
            }
        });
    }

    private async Task ProcessTrackDownloadAsync(TrackItem track, CancellationToken ct)
    {
        track.Status = "Downloading";
        ItemStarted?.Invoke(this, track.Id);

        var settings = StorageService.LoadSettings();
        var destDir = string.IsNullOrEmpty(track.Destination) ? settings.DownloadFolder : track.Destination;

        try
        {
            Directory.CreateDirectory(destDir);

            // 1. Check duplicate
            var (exists, existingPath) = StorageService.IsTrackAlreadyDownloaded(track, destDir);
            if (exists && File.Exists(existingPath))
            {
                track.Status = "Completed";
                track.FilePath = existingPath;
                track.Progress = 100.0;
                StorageService.AddDownloadRecord(track, settings.Bitrate, existingPath);
                ItemCompleted?.Invoke(this, new DownloadCompletedEventArgs
                {
                    TrackId = track.Id,
                    Success = true,
                    Message = "Already downloaded",
                    FilePath = existingPath
                });
                return;
            }

            // 2. Resolve YouTube target video URL with smart candidate scoring (Duration & Studio Match)
            string videoUrl = await ResolveBestYouTubeVideoAsync(track, ct);
            track.SourceUrl = videoUrl;

            // 3. File naming
            var cleanTitle = string.Concat(track.Title.Split(Path.GetInvalidFileNameChars())).Trim();
            var cleanArtist = string.Concat(track.Artist.Split(Path.GetInvalidFileNameChars())).Trim();
            var baseName = !string.IsNullOrEmpty(cleanArtist)
                ? $"{cleanArtist} - {cleanTitle}"
                : cleanTitle;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Track";

            var ffmpegPath = GetFFmpegPath();
            bool hasFFmpeg = !string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath);

            string extension = ".mp3";
            YoutubeExplode.Videos.Streams.IStreamInfo? selectedStream = null;

            if (!hasFFmpeg)
            {
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoUrl, ct);
                var allAudio = streamManifest.GetAudioOnlyStreams().ToList();
                // Strongly prefer MP4/AAC for maximum Android/iOS hardware decode compatibility; avoid WebM/Opus
                var mp4Audio = allAudio
                    .Where(s => s.Container == Container.Mp4)
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault();
                selectedStream = mp4Audio
                    ?? allAudio.Where(s => s.Container != Container.WebM)
                              .OrderByDescending(s => s.Bitrate)
                              .FirstOrDefault()
                    ?? allAudio.OrderByDescending(s => s.Bitrate).FirstOrDefault();
                // Always use .m4a extension for consistency (regardless of actual container)
                extension = ".m4a";

                if (selectedStream == null)
                    throw new Exception("No suitable audio stream found");
            }

            var targetPath = Path.Combine(destDir, $"{baseName}{extension}");
            var tempPath = Path.Combine(destDir, $"{Guid.NewGuid()}.tmp");

            // 4. Download with speed/ETA progress
            var lastEmit = DateTime.UtcNow;
            long lastDownloadedBytes = 0;

            var progressHandler = new Progress<double>(p =>
            {
                var now = DateTime.UtcNow;
                var dt = (now - lastEmit).TotalSeconds;
                if (dt >= 0.4)
                {
                    long approxTotal = 6 * 1024 * 1024;
                    long currentDownloaded = (long)(p * approxTotal);
                    double speed = dt > 0 ? (currentDownloaded - lastDownloadedBytes) / dt : 0;
                    lastDownloadedBytes = currentDownloaded;
                    lastEmit = now;

                    string speedStr = speed > 1024 * 1024
                        ? $"{(speed / (1024 * 1024)):F2} MB/s"
                        : $"{(speed / 1024):F1} KB/s";

                    double remBytes = Math.Max(0, approxTotal - currentDownloaded);
                    double etaSec = speed > 0 ? remBytes / speed : 0;
                    string etaStr = TimeSpan.FromSeconds(etaSec).ToString(@"mm\:ss");

                    track.Progress = Math.Clamp(p * 100.0, 0, 99.0);
                    track.SpeedStr = speedStr;
                    track.EtaStr = etaStr;

                    ItemProgress?.Invoke(this, new DownloadProgressEventArgs
                    {
                        TrackId = track.Id,
                        Progress = track.Progress,
                        SpeedStr = speedStr,
                        EtaStr = etaStr
                    });
                }
            });

            // Convert to MP3 if FFmpeg is available; otherwise download clean native container (.m4a)
            if (hasFFmpeg)
            {
                var builder = new ConversionRequestBuilder(tempPath)
                    .SetFFmpegPath(ffmpegPath!)
                    .SetContainer(Container.Mp3)
                    .SetPreset(ConversionPreset.UltraFast);

                await _youtube.Videos.DownloadAsync(videoUrl, builder.Build(), progressHandler, ct);
            }
            else if (selectedStream != null)
            {
                await _youtube.Videos.Streams.DownloadAsync(selectedStream, tempPath, progressHandler, ct);
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(tempPath, targetPath);
            track.FilePath = targetPath;
            track.Status = "Completed";
            track.Progress = 100.0;
            track.SpeedStr = "";
            track.EtaStr = "";

            // 5. Embed ID3v2 metadata & full HD cover art
            if (settings.EmbedId3Tags)
            {
                await EmbedId3MetadataAsync(targetPath, track);
            }

            // 6. Save in history
            StorageService.AddDownloadRecord(track, settings.Bitrate, targetPath);

            ItemCompleted?.Invoke(this, new DownloadCompletedEventArgs
            {
                TrackId = track.Id,
                Success = true,
                Message = "Completed",
                FilePath = targetPath
            });
            QueueStatusChanged?.Invoke(this, $"Finished: {track.DisplayTitle}");
        }
        catch (OperationCanceledException)
        {
            track.Status = "Cancelled";
            track.Progress = 0.0;
            ItemCompleted?.Invoke(this, new DownloadCompletedEventArgs
            {
                TrackId = track.Id,
                Success = false,
                Message = "Cancelled"
            });
        }
        catch (Exception ex)
        {
            track.Status = "Failed";
            track.ErrorMessage = ex.Message;
            ItemCompleted?.Invoke(this, new DownloadCompletedEventArgs
            {
                TrackId = track.Id,
                Success = false,
                Message = ex.Message
            });
        }
    }

    private async Task EmbedId3MetadataAsync(string filePath, TrackItem track)
    {
        try
        {
            await Task.Delay(150); // Ensure transcode file handle is fully closed
            bool isMp3 = filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
            if (isMp3)
            {
                TagLib.Id3v2.Tag.DefaultVersion = 3;
                TagLib.Id3v2.Tag.ForceDefaultVersion = true;
            }
            using var file = TagLib.File.Create(filePath);
            if (!string.IsNullOrEmpty(track.Title))
                file.Tag.Title = track.Title;
            if (!string.IsNullOrEmpty(track.Artist))
            {
                file.Tag.Performers = [track.Artist];
                file.Tag.AlbumArtists = [track.Artist];
            }
            if (!string.IsNullOrEmpty(track.Album))
                file.Tag.Album = track.Album;

            // ── Priority 1: Shared Album Cover Cache (ensures identical HD cover for all album tracks) ──
            byte[]? artBytes = null;
            string albKey = "";
            if (!string.IsNullOrEmpty(track.Album) && !string.IsNullOrEmpty(track.Artist))
            {
                var cleanAlb = Regex.Replace(track.Album.ToLowerInvariant(), @"[^a-z0-9]", "");
                var cleanArt = Regex.Replace(track.Artist.ToLowerInvariant(), @"[^a-z0-9]", "");
                albKey = $"{cleanArt}_{cleanAlb}";

                if (_albumCoverCache.TryGetValue(albKey, out var cachedBytes))
                {
                    artBytes = cachedBytes;
                }
            }

            // ── Priority 2: Official Deezer/iTunes Full HD URL or Original Thumbnail URL ──
            if (artBytes == null && !string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                try
                {
                    var hdUrl = track.ThumbnailUrl
                        .Replace("100x100", "1000x1000")
                        .Replace("170x170", "1000x1000")
                        .Replace("600x600", "1000x1000");

                    using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                    artBytes = await hc.GetByteArrayAsync(hdUrl);
                }
                catch { }

                // Fallback directly to the exact ThumbnailUrl the user saw
                if (artBytes == null || artBytes.Length < 200)
                {
                    try
                    {
                        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                        artBytes = await hc.GetByteArrayAsync(track.ThumbnailUrl);
                    }
                    catch { }
                }

                if (artBytes != null && artBytes.Length > 200 && !string.IsNullOrEmpty(albKey))
                {
                    _albumCoverCache[albKey] = artBytes;
                }
            }

            // ── Priority 3: ArtworkService RAM cache ──
            if (artBytes == null && !string.IsNullOrEmpty(track.Id))
            {
                artBytes = ArtworkService.Instance.GetRamCached(track.Id);
            }

            // ── Priority 4: YouTube Thumbnail from Source URL (Fallback) ──
            if (artBytes == null && !string.IsNullOrEmpty(track.SourceUrl))
            {
                try
                {
                    var match = Regex.Match(track.SourceUrl, @"(?:v=|/v/|youtu\.be/|/embed/)([a-zA-Z0-9_-]{11})");
                    if (match.Success)
                    {
                        var ytThumb = $"https://img.youtube.com/vi/{match.Groups[1].Value}/hqdefault.jpg";
                        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        artBytes = await hc.GetByteArrayAsync(ytThumb);
                    }
                }
                catch { }
            }

            // Embed FrontCover APIC Frame
            if (artBytes != null && artBytes.Length > 200)
            {
                var isPng = artBytes.Length > 4 && artBytes[0] == 0x89 && artBytes[1] == 0x50 && artBytes[2] == 0x4E && artBytes[3] == 0x47;
                var pic = new TagLib.Picture(new TagLib.ByteVector(artBytes))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = isPng ? "image/png" : "image/jpeg",
                    Description = "Cover"
                };
                file.Tag.Pictures = [pic];

                // Cache in ArtworkService under the local file path for immediate player & history display
                ArtworkService.Instance.PutRamCache(filePath, artBytes);
            }

            file.Save();

#if ANDROID
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var mime = ext == ".m4a" ? "audio/mp4" : ext == ".mp3" ? "audio/mpeg" : null;
                Android.Media.MediaScannerConnection.ScanFile(
                    Android.App.Application.Context,
                    [filePath],
                    mime != null ? [mime] : null,
                    null);
            }
            catch { }
#endif
            Debug.WriteLine($"[ID3v2.3 Tagged + APIC Cover Embedded] {filePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ID3 Tagging Error] {ex.Message}");
        }
    }

    private async Task<string> ResolveBestYouTubeVideoAsync(TrackItem track, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(track.SourceUrl) && (track.SourceUrl.Contains("youtube.com") || track.SourceUrl.Contains("youtu.be")))
        {
            return track.SourceUrl;
        }

        var queries = new List<string>();
        if (!string.IsNullOrEmpty(track.Artist) && !string.IsNullOrEmpty(track.Title))
        {
            queries.Add($"{track.Artist} - {track.Title} official audio");
            queries.Add($"{track.Artist} - {track.Title}");
        }
        else
        {
            queries.Add($"{track.DisplayTitle} official audio");
            queries.Add(track.DisplayTitle);
        }

        var cleanRefTitle = CleanSearchString(track.Title);
        var cleanRefArtist = CleanSearchString(track.Artist);
        var expectedDuration = track.DurationSec > 0 ? TimeSpan.FromSeconds(track.DurationSec) : TimeSpan.Zero;

        var candidates = new List<(YoutubeExplode.Search.VideoSearchResult video, double score)>();

        foreach (var query in queries)
        {
            try
            {
                var searchResults = await _youtube.Search.GetVideosAsync(query, ct).Take(8).ToListAsync(ct);
                foreach (var item in searchResults)
                {
                    double score = CalculateVideoMatchScore(item, cleanRefTitle, cleanRefArtist, expectedDuration);
                    candidates.Add((item, score));
                }

                if (candidates.Count >= 4 && candidates.Any(c => c.score >= 50.0))
                    break;
            }
            catch { }
        }

        if (candidates.Count == 0)
            throw new Exception("No audio match found on YouTube");

        var best = candidates.OrderByDescending(c => c.score).First().video;
        return best.Url;
    }

    private static double CalculateVideoMatchScore(YoutubeExplode.Search.VideoSearchResult item, string refTitle, string refArtist, TimeSpan refDuration)
    {
        double score = 0.0;
        var title = CleanSearchString(item.Title);
        var author = CleanSearchString(item.Author.ChannelTitle);

        // 1. Title matching
        if (!string.IsNullOrEmpty(refTitle))
        {
            var titleWords = refTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int matchedWords = titleWords.Count(w => w.Length > 2 && title.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (titleWords.Length > 0)
                score += (matchedWords / (double)titleWords.Length) * 40.0;
        }

        // 2. Artist matching in title or channel
        if (!string.IsNullOrEmpty(refArtist))
        {
            var artistWords = refArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int matchedArtist = artistWords.Count(w => w.Length > 2 && (title.Contains(w, StringComparison.OrdinalIgnoreCase) || author.Contains(w, StringComparison.OrdinalIgnoreCase)));
            if (artistWords.Length > 0)
                score += (matchedArtist / (double)artistWords.Length) * 30.0;
        }

        // 3. Official releases & Topic channels
        if (author.Contains("topic", StringComparison.OrdinalIgnoreCase)) score += 25.0;
        if (author.Contains("vevo", StringComparison.OrdinalIgnoreCase)) score += 20.0;
        if (title.Contains("official audio", StringComparison.OrdinalIgnoreCase)) score += 25.0;
        else if (title.Contains("official music video", StringComparison.OrdinalIgnoreCase)) score += 15.0;

        // 4. Duration tolerance check (critical for studio vs live/interview/loop)
        if (item.Duration.HasValue && refDuration.TotalSeconds > 10)
        {
            var diff = Math.Abs((item.Duration.Value - refDuration).TotalSeconds);
            if (diff <= 8) score += 35.0;
            else if (diff <= 20) score += 20.0;
            else if (diff <= 45) score += 5.0;
            else score -= 40.0;

            if (item.Duration.Value.TotalSeconds < 60 || item.Duration.Value.TotalSeconds > 660)
                score -= 50.0;
        }

        // 5. Penalties for undesirable content
        string[] badKeywords = ["reaction", "cover", "karaoke", "instrumental", "parody", "slowed", "sped up", "1 hour", "10 hour", "loop", "tutorial", "review", "guitar lesson", "piano tutorial"];
        foreach (var bad in badKeywords)
        {
            if (title.Contains(bad, StringComparison.OrdinalIgnoreCase) && !refTitle.Contains(bad, StringComparison.OrdinalIgnoreCase))
                score -= 45.0;
        }

        if (title.Contains("live", StringComparison.OrdinalIgnoreCase) && !refTitle.Contains("live", StringComparison.OrdinalIgnoreCase))
            score -= 30.0;

        return score;
    }

    private static string CleanSearchString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var clean = Regex.Replace(input, @"[^\w\s]", " ");
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }
}
