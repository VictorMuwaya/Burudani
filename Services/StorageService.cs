// StorageService.cs — Persistent local JSON storage for settings, playlists, and history
using System.Text.Json;
using Burudani.Models;

namespace Burudani.Services;

public static class StorageService
{
    private static readonly string DataDir = Path.Combine(FileSystem.AppDataDirectory, "data");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    private static readonly string PlaylistsFile = Path.Combine(DataDir, "playlists.json");
    private static readonly string PlayHistoryFile = Path.Combine(DataDir, "play_history.json");
    private static readonly string DownloadHistoryFile = Path.Combine(DataDir, "download_history.json");

    public static readonly string[] PresetPlaylists = ["Favorites", "Chill Vibes", "Workout Energy", "Heavy Rotation", "Road Trip / Drive"];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    static StorageService()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
        }
        catch { }
    }

    public static string GetDefaultDownloadPath()
    {
#if WINDOWS
        try
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var folder = Path.Combine(music, "Top Music");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            return FileSystem.AppDataDirectory;
        }
#elif ANDROID
        try
        {
            // 1. Root phone storage directory "/storage/emulated/0/Burudani"
            var rootDir = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
            if (!string.IsNullOrEmpty(rootDir))
            {
                var burudaniDir = Path.Combine(rootDir, "Burudani");
                if (!Directory.Exists(burudaniDir))
                    Directory.CreateDirectory(burudaniDir);
                return burudaniDir;
            }
        }
        catch { }

        try
        {
            // 2. Public Music folder "/storage/emulated/0/Music/Burudani"
            var musicDir = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic)?.AbsolutePath;
            if (!string.IsNullOrEmpty(musicDir))
            {
                var folder = Path.Combine(musicDir, "Burudani");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                return folder;
            }
        }
        catch { }

        try
        {
            // 3. Public Download folder "/storage/emulated/0/Download/Burudani"
            var dlDir = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
            if (!string.IsNullOrEmpty(dlDir))
            {
                var folder = Path.Combine(dlDir, "Burudani");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                return folder;
            }
        }
        catch { }

        return "/storage/emulated/0/Burudani";
#else
        return FileSystem.AppDataDirectory;
#endif
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    if (string.IsNullOrEmpty(settings.DownloadFolder))
                        settings.DownloadFolder = GetDefaultDownloadPath();
#if ANDROID
                    if (settings.DownloadFolder.Contains("/Android/"))
                        settings.DownloadFolder = GetDefaultDownloadPath();
#endif
                    return settings;
                }
            }
        }
        catch { }

        var def = new AppSettings
        {
            DownloadFolder = GetDefaultDownloadPath(),
            DefaultRegion = RegionService.DetectSystemRegion()
        };
        SaveSettings(def);
        return def;
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    // ── Playlists ────────────────────────────────────────────────────────────

    public static Dictionary<string, List<TrackItem>> GetPlaylists()
    {
        var result = new Dictionary<string, List<TrackItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in PresetPlaylists)
            result[p] = [];

        try
        {
            if (File.Exists(PlaylistsFile))
            {
                var json = File.ReadAllText(PlaylistsFile);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<TrackItem>>>(json, _jsonOptions);
                if (loaded != null)
                {
                    foreach (var (k, v) in loaded)
                        result[k] = v;
                }
            }
        }
        catch { }

        return result;
    }

    public static void SavePlaylists(Dictionary<string, List<TrackItem>> playlists)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(playlists, _jsonOptions);
            File.WriteAllText(PlaylistsFile, json);
        }
        catch { }
    }

    public static bool CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var playlists = GetPlaylists();
        if (playlists.ContainsKey(name)) return false;
        playlists[name] = [];
        SavePlaylists(playlists);
        return true;
    }

    public static bool DeletePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || PresetPlaylists.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false; // Cannot delete preset playlists

        var playlists = GetPlaylists();
        if (playlists.Remove(name))
        {
            SavePlaylists(playlists);
            return true;
        }
        return false;
    }

    public static bool AddTrackToPlaylist(string playlistName, TrackItem track)
    {
        var all = GetPlaylists();
        if (!all.TryGetValue(playlistName, out var list))
            all[playlistName] = list = [];

        // Check duplicate
        if (list.Any(t => t.Id == track.Id || (!string.IsNullOrEmpty(track.Title) && t.Title.Equals(track.Title, StringComparison.OrdinalIgnoreCase) && t.Artist.Equals(track.Artist, StringComparison.OrdinalIgnoreCase))))
            return false;

        list.Add(track);
        SavePlaylists(all);
        return true;
    }

    public static bool RemoveTrackFromPlaylist(string playlistName, string trackId)
    {
        var all = GetPlaylists();
        if (all.TryGetValue(playlistName, out var list))
        {
            int removed = list.RemoveAll(t => t.Id == trackId);
            if (removed > 0)
            {
                SavePlaylists(all);
                return true;
            }
        }
        return false;
    }

    // ── Play History ─────────────────────────────────────────────────────────

    public static List<TrackItem> GetPlayHistory()
    {
        try
        {
            if (File.Exists(PlayHistoryFile))
            {
                var json = File.ReadAllText(PlayHistoryFile);
                return JsonSerializer.Deserialize<List<TrackItem>>(json, _jsonOptions) ?? [];
            }
        }
        catch { }
        return [];
    }

    public static void LogPlay(TrackItem track)
    {
        try
        {
            var history = GetPlayHistory();
            history.RemoveAll(t => t.Id == track.Id || (t.Title.Equals(track.Title, StringComparison.OrdinalIgnoreCase) && t.Artist.Equals(track.Artist, StringComparison.OrdinalIgnoreCase)));
            history.Insert(0, track);
            if (history.Count > 200) history = history.Take(200).ToList();

            Directory.CreateDirectory(DataDir);
            File.WriteAllText(PlayHistoryFile, JsonSerializer.Serialize(history, _jsonOptions));
        }
        catch { }
    }

    public static void ClearPlayHistory()
    {
        try
        {
            if (File.Exists(PlayHistoryFile))
                File.Delete(PlayHistoryFile);
        }
        catch { }
    }

    // ── Download History ─────────────────────────────────────────────────────

    public static List<DownloadRecord> GetDownloadHistory()
    {
        try
        {
            if (File.Exists(DownloadHistoryFile))
            {
                var json = File.ReadAllText(DownloadHistoryFile);
                return JsonSerializer.Deserialize<List<DownloadRecord>>(json, _jsonOptions) ?? [];
            }
        }
        catch { }
        return [];
    }

    public static void AddDownloadRecord(TrackItem track, string quality, string filePath)
    {
        try
        {
            var history = GetDownloadHistory();
            history.RemoveAll(r => r.TrackId == track.Id || (!string.IsNullOrEmpty(filePath) && r.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)));

            long size = 0;
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try { size = new FileInfo(filePath).Length; } catch { }
            }

            history.Insert(0, new DownloadRecord
            {
                TrackId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                FilePath = filePath,
                Quality = quality,
                DownloadedAt = DateTime.UtcNow,
                FileSizeBytes = size
            });

            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DownloadHistoryFile, JsonSerializer.Serialize(history, _jsonOptions));
        }
        catch { }
    }

    public static void DeleteDownloadRecord(string recordId)
    {
        try
        {
            var history = GetDownloadHistory();
            history.RemoveAll(r => r.Id == recordId || r.TrackId == recordId);
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DownloadHistoryFile, JsonSerializer.Serialize(history, _jsonOptions));
        }
        catch { }
    }

    public static void ClearDownloadHistory()
    {
        try
        {
            if (File.Exists(DownloadHistoryFile))
                File.Delete(DownloadHistoryFile);
        }
        catch { }
    }

    // ── Duplicate Detection ──────────────────────────────────────────────────

    public static (bool Exists, string FilePath) IsTrackAlreadyDownloaded(TrackItem track, string destination)
    {
        // 1. Direct path check
        if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            return (true, track.FilePath);

        // 2. History check
        var history = GetDownloadHistory();
        foreach (var r in history)
        {
            if (!string.IsNullOrEmpty(r.FilePath) && File.Exists(r.FilePath))
            {
                if (r.Title.Equals(track.Title, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(track.Artist) || r.Artist.Equals(track.Artist, StringComparison.OrdinalIgnoreCase)))
                {
                    return (true, r.FilePath);
                }
            }
        }

        // 3. Scan destination folder
        if (!string.IsNullOrEmpty(destination) && Directory.Exists(destination))
        {
            var cleanTitle = string.Concat(track.Title.Split(Path.GetInvalidFileNameChars()));
            foreach (var ext in new[] { ".mp3", ".m4a" })
            {
                var singlePath = Path.Combine(destination, $"{cleanTitle}{ext}");
                if (File.Exists(singlePath)) return (true, singlePath);

                var artistTitlePath = Path.Combine(destination, $"{track.Artist} - {cleanTitle}{ext}");
                if (File.Exists(artistTitlePath)) return (true, artistTitlePath);
            }
        }

        return (false, string.Empty);
    }
}
