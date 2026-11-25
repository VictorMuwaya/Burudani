// ArtworkService.cs — Two-tier LRU artwork cache (RAM + disk) + async fetch
// Equivalent to Python core/artwork_loader.py ArtworkManager
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Burudani.Models;

namespace Burudani.Services;

/// <summary>
/// Centralized artwork/thumbnail manager.
/// Two-tier cache: in-memory LRU (up to 300 items) + persistent disk cache.
/// Per-key subscription model: only the widget waiting for a specific key is notified (O(1)).
/// All network I/O runs on background threads — never blocks the UI.
/// </summary>
public sealed class ArtworkService
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static ArtworkService? _instance;
    public static ArtworkService Instance => _instance ??= new ArtworkService();

    // ── Cache ─────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, byte[]> _ramCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _lruQueue = new();
    private const int MaxRamCacheSize = 600;
    private readonly object _ramLock = new();

    private static readonly string DiskCacheDir =
        Path.Combine(FileSystem.CacheDirectory, "burudani_artwork");

    private static readonly SemaphoreSlim _downloadSemaphore = new(4, 4);

    // ── Pending tracking ──────────────────────────────────────────────────────
    private readonly HashSet<string> _pendingKeys = new(StringComparer.Ordinal);
    private readonly object _pendingLock = new();
    private int _epoch = 0;

    // ── Subscription dict: key → list of callbacks ────────────────────────────
    private readonly Dictionary<string, List<Action<string, byte[]>>> _subscribers =
        new(StringComparer.Ordinal);
    private readonly object _subLock = new();

    // ── HTTP client ───────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

    static ArtworkService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        Directory.CreateDirectory(DiskCacheDir);
    }

    private ArtworkService() { }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Subscribe a callback to be notified when artwork for `key` arrives.</summary>
    public void Subscribe(string key, Action<string, byte[]> callback)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_subLock)
        {
            if (!_subscribers.TryGetValue(key, out var list))
                _subscribers[key] = list = [];
            list.Add(callback);
        }
    }

    public static double CalculateLuminanceFromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 100) return 0.2;
        try
        {
            long sum = 0;
            int count = 0;
            int step = Math.Max(1, bytes.Length / 150);
            for (int i = 40; i < bytes.Length - 10; i += step)
            {
                sum += bytes[i];
                count++;
            }
            return count > 0 ? (double)sum / (count * 255.0) : 0.3;
        }
        catch
        {
            return 0.3;
        }
    }

    public void AnalyzeTrackContrast(TrackItem track)
    {
        if (track == null) return;
        var cached = GetRamCached(track.Id);
        if (cached != null)
        {
            Task.Run(() =>
            {
                double lum = CalculateLuminanceFromBytes(cached);
                MainThread.BeginInvokeOnMainThread(() => track.ApplyArtworkContrast(lum));
            });
            return;
        }

        Subscribe(track.Id, (key, data) =>
        {
            if (data != null && data.Length > 0)
            {
                Task.Run(() =>
                {
                    double lum = CalculateLuminanceFromBytes(data);
                    MainThread.BeginInvokeOnMainThread(() => track.ApplyArtworkContrast(lum));
                });
            }
        });
    }

    /// <summary>Unsubscribe a callback (call when widget is recycled/destroyed).</summary>
    public void Unsubscribe(string key, Action<string, byte[]> callback)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_subLock)
        {
            if (_subscribers.TryGetValue(key, out var list))
            {
                list.Remove(callback);
                if (list.Count == 0) _subscribers.Remove(key);
            }
        }
    }

    /// <summary>
    /// Request artwork for a key. Delivers instantly if in RAM cache;
    /// otherwise fires a background task. Never blocks the caller.
    /// </summary>
    public void RequestArtwork(string key, string thumbnailUrl = "", string sourceUrl = "")
    {
        if (string.IsNullOrEmpty(key)) return;

        // RAM cache hit — deliver on main thread immediately
        var cached = GetRamCached(key);
        if (cached is not null)
        {
            Deliver(key, cached);
            return;
        }

        lock (_pendingLock)
        {
            if (_pendingKeys.Contains(key)) return;
            _pendingKeys.Add(key);
        }

        int epoch = _epoch;
        Task.Run(async () =>
        {
            try
            {
                byte[]? data = null;

                // 1. Disk cache
                data = ReadDiskCache(key);

                // 2. HTTP fetch
                if (data is null && !string.IsNullOrEmpty(thumbnailUrl))
                    data = await FetchBytesAsync(thumbnailUrl);

                // 3. YouTube thumbnail fallback
                if (data is null && !string.IsNullOrEmpty(sourceUrl))
                {
                    var ytThumb = GetYtThumbnailUrl(sourceUrl);
                    if (ytThumb is not null)
                        data = await FetchBytesAsync(ytThumb);
                }

                if (_epoch != epoch) return; // Cancelled by epoch bump

                if (data is not null)
                {
                    PutCache(key, data);
                    MainThread.BeginInvokeOnMainThread(() => Deliver(key, data));
                }
            }
            finally
            {
                lock (_pendingLock) _pendingKeys.Remove(key);
            }
        });
    }

    public byte[]? ExtractArtworkFromAudioFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
            {
                var pic = file.Tag.Pictures[0];
                if (pic.Data?.Data != null && pic.Data.Data.Length > 0)
                {
                    var data = pic.Data.Data;
                    PutRamCache(filePath, data);
                    return data;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>RAM-only instant cache check. 0ms disk I/O. Safe to call on UI thread.</summary>
    public byte[]? GetRamCached(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_ramLock)
        {
            return _ramCache.TryGetValue(key, out var data) ? data : null;
        }
    }

    /// <summary>Store bytes in RAM and disk caches.</summary>
    public void PutCache(string key, byte[] data)
    {
        if (string.IsNullOrEmpty(key) || data is null || data.Length < 100) return;
        lock (_ramLock)
        {
            if (!_ramCache.ContainsKey(key))
            {
                if (_ramCache.Count >= MaxRamCacheSize && _lruQueue.TryDequeue(out var evict))
                    _ramCache.Remove(evict);
                _lruQueue.Enqueue(key);
            }
            _ramCache[key] = data;
        }
        WriteDiskCache(key, data);
    }

    public void PutRamCache(string key, byte[] data) => PutCache(key, data);

    /// <summary>Store bytes in RAM cache only without writing to disk.</summary>
    public void PutRamCacheOnly(string key, byte[] data)
    {
        if (string.IsNullOrEmpty(key) || data is null || data.Length < 100) return;
        lock (_ramLock)
        {
            if (!_ramCache.ContainsKey(key))
            {
                if (_ramCache.Count >= MaxRamCacheSize && _lruQueue.TryDequeue(out var evict))
                    _ramCache.Remove(evict);
                _lruQueue.Enqueue(key);
            }
            _ramCache[key] = data;
        }
    }

    public static string DiskCacheThumbPath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(DiskCacheDir, Convert.ToHexString(hash).ToLowerInvariant() + "_thumb.jpg");
    }

    public string? GetDiskCachedThumbPath(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            var thumbPath = DiskCacheThumbPath(key);
            if (File.Exists(thumbPath)) return thumbPath;

            var origPath = DiskCachePath(key);
            if (File.Exists(origPath))
            {
#if ANDROID
                var origBytes = File.ReadAllBytes(origPath);
                var thumbBytes = DownsampleForThumbnail(origBytes);
                if (thumbBytes != null && thumbBytes.Length > 0)
                {
                    File.WriteAllBytes(thumbPath, thumbBytes);
                    PutRamCacheOnly(key, thumbBytes);
                    return thumbPath;
                }
#endif
                return origPath;
            }
        }
        catch { }
        return null;
    }

    public byte[]? ReadDiskCacheDirect(string key) => ReadDiskCache(key);

#if ANDROID
    public static byte[] DownsampleForThumbnail(byte[] rawBytes, int reqWidth = 256, int reqHeight = 256)
    {
        if (rawBytes == null || rawBytes.Length < 100) return rawBytes ?? [];
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options
            {
                InJustDecodeBounds = true
            };
            Android.Graphics.BitmapFactory.DecodeByteArray(rawBytes, 0, rawBytes.Length, options);

            int inSampleSize = 1;
            if (options.OutHeight > reqHeight || options.OutWidth > reqWidth)
            {
                int halfHeight = options.OutHeight / 2;
                int halfWidth = options.OutWidth / 2;
                while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
                {
                    inSampleSize *= 2;
                }
            }

            options.InSampleSize = inSampleSize;
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;

            using var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(rawBytes, 0, rawBytes.Length, options);
            if (bitmap == null) return rawBytes;

            using var ms = new MemoryStream();
            bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 82, ms);
            return ms.ToArray();
        }
        catch
        {
            return rawBytes;
        }
    }
#endif

    /// <summary>Bump epoch to cancel all in-flight fetches (called on new search).</summary>
    public void ClearPending() => Interlocked.Increment(ref _epoch);

    /// <summary>Clear in-memory cache to reclaim RAM.</summary>
    public void ClearRamCache()
    {
        lock (_ramLock) { _ramCache.Clear(); _lruQueue.Clear(); }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void Deliver(string key, byte[] data)
    {
        List<Action<string, byte[]>>? callbacks;
        lock (_subLock)
        {
            _subscribers.TryGetValue(key, out callbacks);
            callbacks = callbacks?.ToList(); // snapshot to avoid lock during invocation
        }
        if (callbacks is null) return;
        foreach (var cb in callbacks)
            try { cb(key, data); } catch { /* ignore subscriber errors */ }
    }

    private static string DiskCachePath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(DiskCacheDir, Convert.ToHexString(hash).ToLowerInvariant() + ".img");
    }

    private static byte[]? ReadDiskCache(string key)
    {
        try
        {
            var thumbPath = DiskCacheThumbPath(key);
            if (File.Exists(thumbPath))
            {
                var data = File.ReadAllBytes(thumbPath);
                if (data.Length > 100) return data;
            }

            var path = DiskCachePath(key);
            if (File.Exists(path))
            {
                var data = File.ReadAllBytes(path);
                if (data.Length > 100)
                {
#if ANDROID
                    var thumb = DownsampleForThumbnail(data);
                    try { File.WriteAllBytes(thumbPath, thumb); } catch { }
                    return thumb;
#else
                    return data;
#endif
                }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static void WriteDiskCache(string key, byte[] data)
    {
        try
        {
            var path = DiskCachePath(key);
            if (!File.Exists(path))
                File.WriteAllBytes(path, data);

#if ANDROID
            var thumbPath = DiskCacheThumbPath(key);
            if (!File.Exists(thumbPath))
            {
                var thumb = DownsampleForThumbnail(data);
                if (thumb != null && thumb.Length > 0)
                    File.WriteAllBytes(thumbPath, thumb);
            }
#endif
        }
        catch { /* ignore */ }
    }

    private static async Task<byte[]?> FetchBytesAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

        bool acquired = false;
        try
        {
            acquired = await _downloadSemaphore.WaitAsync(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 100) return bytes;
            }
        }
        catch { /* ignore */ }
        finally
        {
            if (acquired) _downloadSemaphore.Release();
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex _ytIdRe =
        new(@"(?:v=|youtu\.be/)([A-Za-z0-9_-]{11})");

    private static string? GetYtThumbnailUrl(string sourceUrl)
    {
        var m = _ytIdRe.Match(sourceUrl);
        return m.Success ? $"https://i.ytimg.com/vi/{m.Groups[1].Value}/mqdefault.jpg" : null;
    }
}
