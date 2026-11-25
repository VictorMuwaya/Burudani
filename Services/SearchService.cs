// SearchService.cs — Multi-API song/album search aggregator
// Equivalent to Python core/search.py + core/deezer_service.py
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Burudani.Models;

namespace Burudani.Services;

/// <summary>
/// Industry-standard multi-engine music search aggregator.
/// Sources: Deezer (90M+ HD catalogue) + iTunes + YouTube (via YoutubeExplode).
/// Runs searches in parallel and merges/deduplicates results.
/// All methods are fully async — never blocks the UI thread.
/// </summary>
public static class SearchService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    static SearchService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    }

    // ── Shared utilities ──────────────────────────────────────────────────────

    public static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "--:--";
        int h = seconds / 3600;
        int m = (seconds % 3600) / 60;
        int s = seconds % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }

    private static string NormalizeStr(string text) =>
        Regex.Replace((text ?? "").ToLowerInvariant(), @"[^a-z0-9]", "");

    private static string UpgradeArtworkUrl(string url) =>
        string.IsNullOrEmpty(url) ? url :
        url.Replace("100x100bb", "300x300bb")
           .Replace("100x100", "300x300")
           .Replace("600x600", "300x300")
           .Replace("1000x1000", "300x300")
           .Replace("1400x1400", "300x300");

    // ── iTunes song search ────────────────────────────────────────────────────

    public static async Task<List<TrackItem>> SearchItunesSongsAsync(
        string query, int limit = 25, CancellationToken ct = default)
    {
        var tracks = new List<TrackItem>();
        if (string.IsNullOrWhiteSpace(query)) return tracks;
        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit={limit}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return tracks;

            foreach (var item in results.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var title   = item.TryGetProperty("trackName",       out var t)  ? t.GetString() ?? "" : "";
                var artist  = item.TryGetProperty("artistName",      out var a)  ? a.GetString() ?? "" : "";
                var album   = item.TryGetProperty("collectionName",  out var c)  ? c.GetString() ?? "" : "";
                var millis  = item.TryGetProperty("trackTimeMillis", out var ms) ? ms.GetInt64() : 0;
                var sec     = (int)(millis / 1000);
                var art     = item.TryGetProperty("artworkUrl100",   out var ar) ? ar.GetString() ?? "" : "";
                var preview = item.TryGetProperty("previewUrl",      out var pr) ? pr.GetString() ?? "" : "";
                var viewUrl = item.TryGetProperty("trackViewUrl",    out var vr) ? vr.GetString() ?? "" : "";
                var trackId = item.TryGetProperty("trackId",         out var id) ? id.GetInt64().ToString() : Guid.NewGuid().ToString();

                tracks.Add(new TrackItem
                {
                    Id = $"itunes_{trackId}",
                    Title = title,
                    Artist = artist,
                    Album = album,
                    DurationStr = FormatDuration(sec),
                    DurationSec = sec,
                    StreamUrl = "", // iTunes preview URLs are 30-sec clips; YouTube stream resolved at play time
                    ThumbnailUrl = UpgradeArtworkUrl(art),
                    SourceUrl = viewUrl ?? "",
                    Source = "itunes"
                });
            }
        }
        catch (OperationCanceledException) { /* cancelled */ }
        catch (Exception ex) { Debug.WriteLine($"[iTunes songs] {ex.Message}"); }
        return tracks;
    }

    // ── Deezer track search ───────────────────────────────────────────────────

    public static async Task<List<TrackItem>> SearchDeezerTracksAsync(
        string query, int limit = 30, CancellationToken ct = default)
    {
        var tracks = new List<TrackItem>();
        if (string.IsNullOrWhiteSpace(query)) return tracks;
        try
        {
            var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit={limit}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return tracks;

            foreach (var item in data.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var title    = item.TryGetProperty("title",          out var t)  ? t.GetString()  ?? "" : "";
                var dur      = item.TryGetProperty("duration",       out var d)  ? d.GetInt32()          : 0;
                var preview  = item.TryGetProperty("preview",        out var pr) ? pr.GetString() ?? "" : "";
                var link     = item.TryGetProperty("link",           out var lk) ? lk.GetString() ?? "" : "";
                var albumEl  = item.TryGetProperty("album",          out var al) ? al : default;
                var artistEl = item.TryGetProperty("artist",         out var ar) ? ar : default;
                var trackId  = item.TryGetProperty("id",             out var id) ? id.GetInt64().ToString() : Guid.NewGuid().ToString();

                var albumTitle  = albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("title",   out var at) ? at.GetString() ?? "" : "";
                var albumArt    = albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("cover_xl", out var cx) ? cx.GetString() ?? "" :
                                  albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("cover_big", out var cb) ? cb.GetString() ?? "" : "";
                var artistName  = artistEl.ValueKind != JsonValueKind.Undefined && artistEl.TryGetProperty("name",  out var an) ? an.GetString() ?? "" : "";

                tracks.Add(new TrackItem
                {
                    Id = $"deezer_{trackId}",
                    Title = title,
                    Artist = artistName,
                    Album = albumTitle,
                    DurationStr = FormatDuration(dur),
                    DurationSec = dur,
                    StreamUrl = "", // iTunes preview URLs are 30-sec clips; YouTube stream resolved at play time
                    ThumbnailUrl = albumArt,
                    SourceUrl = link ?? "",
                    Source = "deezer"
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Deezer tracks] {ex.Message}"); }
        return tracks;
    }

    // ── YouTube track search via YoutubeExplode ───────────────────────────────

    public static async Task<List<TrackItem>> SearchYouTubeTracksAsync(
        string query, int limit = 15, CancellationToken ct = default)
    {
        var tracks = new List<TrackItem>();
        if (string.IsNullOrWhiteSpace(query)) return tracks;

        // Direct URL handling
        if (query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            tracks.Add(new TrackItem
            {
                Title = query,
                SourceUrl = query,
                Artist = "Direct Link",
                Source = "youtube"
            });
            return tracks;
        }

        try
        {
            var youtube = new YoutubeExplode.YoutubeClient();
            int count = 0;
            await foreach (var result in youtube.Search.GetVideosAsync(query, ct))
            {
                if (count >= limit) break;
                ct.ThrowIfCancellationRequested();

                var cleanArtist = result.Author.ChannelTitle.Replace(" - Topic", "").Trim();
                var thumbUrl = result.Thumbnails.OrderByDescending(th => th.Resolution.Area)
                    .Select(th => th.Url).FirstOrDefault()
                    ?? $"https://i.ytimg.com/vi/{result.Id}/hqdefault.jpg";

                tracks.Add(new TrackItem
                {
                    Id = $"yt_{result.Id}",
                    Title = result.Title,
                    Artist = cleanArtist,
                    DurationStr = FormatDuration((int)(result.Duration?.TotalSeconds ?? 0)),
                    DurationSec = (int)(result.Duration?.TotalSeconds ?? 0),
                    SourceUrl = result.Url,
                    ThumbnailUrl = thumbUrl,
                    Source = "youtube"
                });
                count++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[YouTube search] {ex.Message}"); }
        return tracks;
    }

    // ── Unified multi-source song search (parallel, deduped, ranked) ──────────

    public static async Task<List<TrackItem>> SearchSongsAsync(
        string query,
        string sourceFilter = "all",
        int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Direct URL — return immediately
        if (query.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return [new TrackItem { Title = query, SourceUrl = query, Artist = "Direct Link", Source = "url" }];

        var tasks = new List<Task<List<TrackItem>>>();
        if (sourceFilter is "all" or "deezer")  tasks.Add(SearchDeezerTracksAsync(query, 30, ct));
        if (sourceFilter is "all" or "itunes")  tasks.Add(SearchItunesSongsAsync(query, 25, ct));
        if (sourceFilter is "all" or "youtube") tasks.Add(SearchYouTubeTracksAsync(query, 15, ct));

        var allResults = new List<TrackItem>();
        var completed = await Task.WhenAll(tasks);
        foreach (var list in completed)
            allResults.AddRange(list);

        // Deduplicate by normalized "artist+title" key
        var seen = new HashSet<string>();
        var deduped = new List<TrackItem>();
        foreach (var t in allResults)
        {
            var key = NormalizeStr(t.Artist) + "_" + NormalizeStr(t.Title);
            if (seen.Add(key))
                deduped.Add(t);
        }

        // Relevance sort
        var queryNorm = NormalizeStr(query);
        deduped.Sort((a, b) =>
        {
            int scoreA = Score(a, queryNorm);
            int scoreB = Score(b, queryNorm);
            return scoreB.CompareTo(scoreA);
        });

        return deduped.Take(limit).ToList();

        static int Score(TrackItem t, string q)
        {
            int s = 0;
            var norm = NormalizeStr(t.Title + t.Artist);
            if (norm.Contains(q)) s += 10;
            if (NormalizeStr(t.Title).Contains(q)) s += 5;
            s += t.Source switch { "deezer" => 3, "itunes" => 2, "youtube" => 1, _ => 0 };
            return s;
        }
    }

    // ── iTunes album search ───────────────────────────────────────────────────

    public static async Task<List<AlbumItem>> SearchItunesAlbumsAsync(
        string query, int limit = 20, CancellationToken ct = default)
    {
        var albums = new List<AlbumItem>();
        if (string.IsNullOrWhiteSpace(query)) return albums;
        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=album&limit={limit}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return albums;

            foreach (var item in results.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id      = item.TryGetProperty("collectionId",   out var ci) ? ci.GetInt64().ToString() : "";
                var title   = item.TryGetProperty("collectionName", out var cn) ? cn.GetString() ?? "" : "";
                var artist  = item.TryGetProperty("artistName",     out var an) ? an.GetString() ?? "" : "";
                var count   = item.TryGetProperty("trackCount",     out var tc) ? tc.GetInt32() : 0;
                var art     = item.TryGetProperty("artworkUrl100",  out var ar) ? ar.GetString() ?? "" : "";
                var date    = item.TryGetProperty("releaseDate",    out var rd) ? rd.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(id))
                    albums.Add(new AlbumItem
                    {
                        AlbumId = id,
                        Title = title,
                        Artist = artist,
                        TrackCount = count,
                        ArtworkUrl = UpgradeArtworkUrl(art),
                        ReleaseYear = date.Length >= 4 ? date[..4] : "",
                        Source = "itunes"
                    });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[iTunes albums] {ex.Message}"); }
        return albums;
    }

    // ── Deezer album search ───────────────────────────────────────────────────

    public static async Task<List<AlbumItem>> SearchDeezerAlbumsAsync(
        string query, int limit = 20, CancellationToken ct = default)
    {
        var albums = new List<AlbumItem>();
        if (string.IsNullOrWhiteSpace(query)) return albums;
        try
        {
            var url = $"https://api.deezer.com/search/album?q={Uri.EscapeDataString(query)}&limit={limit}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return albums;

            foreach (var item in data.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id         = item.TryGetProperty("id",        out var i)   ? i.GetInt64().ToString()  : "";
                var title      = item.TryGetProperty("title",     out var t)   ? t.GetString()  ?? ""     : "";
                var trackCount = item.TryGetProperty("nb_tracks", out var nbt) ? nbt.GetInt32()           : 0;
                var art        = item.TryGetProperty("cover_xl",  out var cx)  ? cx.GetString() ?? ""     :
                                 item.TryGetProperty("cover_big", out var cb)  ? cb.GetString() ?? ""     : "";
                var artistEl   = item.TryGetProperty("artist",    out var ar)  ? ar : default;
                var artist     = artistEl.ValueKind != JsonValueKind.Undefined && artistEl.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(title))
                    albums.Add(new AlbumItem
                    {
                        AlbumId = $"deezer_{id}",
                        Title = title,
                        Artist = artist,
                        TrackCount = trackCount,
                        ArtworkUrl = art,
                        Source = "deezer"
                    });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Deezer albums] {ex.Message}"); }
        return albums;
    }

    // ── Unified album search ──────────────────────────────────────────────────

    public static async Task<List<AlbumItem>> SearchAlbumsAsync(
        string query,
        string sourceFilter = "all",
        int limit = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tasks = new List<Task<List<AlbumItem>>>();
        if (sourceFilter is "all" or "deezer") tasks.Add(SearchDeezerAlbumsAsync(query, 20, ct));
        if (sourceFilter is "all" or "itunes") tasks.Add(SearchItunesAlbumsAsync(query, 20, ct));

        var allAlbums = new List<AlbumItem>();
        var completed = await Task.WhenAll(tasks);
        foreach (var list in completed)
            allAlbums.AddRange(list);

        // Deduplicate by normalized title
        var seen = new HashSet<string>();
        var result = new List<AlbumItem>();
        foreach (var a in allAlbums)
            if (seen.Add(NormalizeStr(a.Title)))
                result.Add(a);

        return result.Take(limit).ToList();
    }

    // ── Album track lookup ────────────────────────────────────────────────────

    public static async Task<List<TrackItem>> LookupAlbumTracksAsync(
        string albumId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(albumId)) return [];

        if (albumId.StartsWith("deezer_"))
            return await LookupDeezerAlbumTracksAsync(albumId, ct);

        return await LookupItunesAlbumTracksAsync(albumId, ct);
    }

    private static async Task<List<TrackItem>> LookupItunesAlbumTracksAsync(
        string albumId, CancellationToken ct)
    {
        var tracks = new List<TrackItem>();
        try
        {
            var url = $"https://itunes.apple.com/lookup?id={albumId}&entity=song";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return tracks;

            string albumArt = "", albumTitle = "", albumArtist = "";
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("wrapperType", out var wt) && wt.GetString() == "collection")
                {
                    albumTitle  = item.TryGetProperty("collectionName", out var cn) ? cn.GetString() ?? "" : "";
                    albumArtist = item.TryGetProperty("artistName",     out var an) ? an.GetString() ?? "" : "";
                    var rawArt  = item.TryGetProperty("artworkUrl100",  out var ar) ? ar.GetString() ?? "" : "";
                    albumArt = UpgradeArtworkUrl(rawArt);
                    break;
                }
            }

            foreach (var item in results.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (!item.TryGetProperty("wrapperType", out var wt) || wt.GetString() != "track") continue;

                var title   = item.TryGetProperty("trackName",      out var tn) ? tn.GetString() ?? "" : "";
                var artist  = item.TryGetProperty("artistName",     out var an) ? an.GetString() ?? albumArtist : albumArtist;
                var album   = item.TryGetProperty("collectionName", out var cn) ? cn.GetString() ?? albumTitle  : albumTitle;
                var millis  = item.TryGetProperty("trackTimeMillis",out var ms) ? ms.GetInt64() : 0;
                var sec     = (int)(millis / 1000);
                var art     = item.TryGetProperty("artworkUrl100",  out var ar) ? UpgradeArtworkUrl(ar.GetString() ?? "") : albumArt;
                var preview = item.TryGetProperty("previewUrl",     out var pr) ? pr.GetString() ?? "" : "";
                var tid     = item.TryGetProperty("trackId",        out var id) ? id.GetInt64().ToString() : Guid.NewGuid().ToString();

                tracks.Add(new TrackItem
                {
                    Id = $"itunes_{tid}",
                    Title = title,
                    Artist = artist,
                    Album = album,
                    DurationStr = FormatDuration(sec),
                    DurationSec = sec,
                    StreamUrl = "", // iTunes preview URLs are 30-sec clips; YouTube stream resolved at play time
                    ThumbnailUrl = art,
                    AlbumId = albumId,
                    Source = "itunes"
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[iTunes album tracks] {ex.Message}"); }
        return tracks;
    }

    private static async Task<List<TrackItem>> LookupDeezerAlbumTracksAsync(
        string albumId, CancellationToken ct)
    {
        var tracks = new List<TrackItem>();
        try
        {
            var numericId = albumId.Replace("deezer_", "");
            var albumJson = await _http.GetStringAsync($"https://api.deezer.com/album/{numericId}", ct);
            using var albumDoc = JsonDocument.Parse(albumJson);
            var albumEl  = albumDoc.RootElement;
            var albumArt = albumEl.TryGetProperty("cover_xl",  out var cx) ? cx.GetString() ?? "" :
                           albumEl.TryGetProperty("cover_big", out var cb) ? cb.GetString() ?? "" : "";
            var albumTitle  = albumEl.TryGetProperty("title",          out var at) ? at.GetString() ?? "" : "";
            var albumArtist = albumEl.TryGetProperty("artist",         out var ar) ? (ar.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "") : "";

            var tracksJson = await _http.GetStringAsync($"https://api.deezer.com/album/{numericId}/tracks", ct);
            using var tracksDoc = JsonDocument.Parse(tracksJson);
            if (!tracksDoc.RootElement.TryGetProperty("data", out var data)) return tracks;

            foreach (var item in data.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var title    = item.TryGetProperty("title",    out var t)  ? t.GetString()  ?? "" : "";
                var dur      = item.TryGetProperty("duration", out var d)  ? d.GetInt32()         : 0;
                var preview  = item.TryGetProperty("preview",  out var pr) ? pr.GetString() ?? "" : "";
                var link     = item.TryGetProperty("link",     out var lk) ? lk.GetString() ?? "" : "";
                var tid      = item.TryGetProperty("id",       out var id) ? id.GetInt64().ToString() : Guid.NewGuid().ToString();
                var artistEl = item.TryGetProperty("artist",   out var ae) ? ae : default;
                var artist   = artistEl.ValueKind != JsonValueKind.Undefined && artistEl.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? albumArtist : albumArtist;

                tracks.Add(new TrackItem
                {
                    Id = $"deezer_{tid}",
                    Title = title,
                    Artist = artist,
                    Album = albumTitle,
                    DurationStr = FormatDuration(dur),
                    DurationSec = dur,
                    StreamUrl = "", // iTunes preview URLs are 30-sec clips; YouTube stream resolved at play time
                    ThumbnailUrl = albumArt,
                    SourceUrl = link ?? "",
                    AlbumId = albumId,
                    Source = "deezer"
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Deezer album tracks] {ex.Message}"); }
        return tracks;
    }

    // ── Trending top charts via Deezer & Apple Music ──────────────────────────

    public static readonly Dictionary<string, int?> GenreMap = new()
    {
        { "All Genres", null },
        { "Pop", 14 },
        { "Hip-Hop / Rap", 18 },
        { "R&B / Soul", 15 },
        { "Rock", 21 },
        { "Dance / Electronic", 17 },
        { "Country", 6 },
        { "Alternative", 20 },
        { "Reggae / Afrobeats", 24 },
    };

    public static Task<List<TrackItem>> GetTrendingHitsAsync(
        string genre = "All Genres", string region = "US", CancellationToken ct = default)
    {
        return GetTrendingTracksAsync(region, genre, 50, ct);
    }

    public static async Task<List<TrackItem>> GetTrendingTracksAsync(
        string region = "US", string genre = "All Genres",
        int limit = 50, CancellationToken ct = default)
    {
        var tracks = new List<TrackItem>();
        var reg = (region ?? "US").Trim().ToLowerInvariant();
        if (reg == "ww" || reg == "worldwide" || reg == "global") reg = "us";

        GenreMap.TryGetValue(genre ?? "All Genres", out var genreId);

        // If specific genre selected, query Apple Music RSS for that genre
        if (genreId.HasValue)
        {
            try
            {
                var rssUrl = $"https://itunes.apple.com/{reg}/rss/topsongs/limit={limit}/genre={genreId.Value}/json";
                var json = await _http.GetStringAsync(rssUrl, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("feed", out var feed) && feed.TryGetProperty("entry", out var entries))
                {
                    if (entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            ct.ThrowIfCancellationRequested();
                            var title = entry.TryGetProperty("im:name", out var nm) && nm.TryGetProperty("label", out var nl) ? nl.GetString() ?? "" : "";
                            var artist = entry.TryGetProperty("im:artist", out var ar) && ar.TryGetProperty("label", out var al) ? al.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(title)) continue;

                            var cat = entry.TryGetProperty("category", out var c) && c.TryGetProperty("attributes", out var ca) && ca.TryGetProperty("label", out var cl) ? cl.GetString() ?? "" : "";
                            var relDate = entry.TryGetProperty("im:releaseDate", out var rd) && rd.TryGetProperty("attributes", out var rda) && rda.TryGetProperty("label", out var rdl) ? rdl.GetString() ?? "" : "";
                            var year = relDate.Length >= 4 ? relDate[..4] : "";

                            string thumb = "";
                            if (entry.TryGetProperty("im:image", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var img in imgs.EnumerateArray())
                                {
                                    if (img.TryGetProperty("label", out var il))
                                        thumb = il.GetString() ?? "";
                                }
                            }
                            if (!string.IsNullOrEmpty(thumb))
                                thumb = thumb.Replace("170x170", "300x300").Replace("100x100", "300x300");

                            string streamUrl = "";
                            if (entry.TryGetProperty("link", out var links) && links.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var lk in links.EnumerateArray())
                                {
                                    if (lk.TryGetProperty("attributes", out var lka) &&
                                        lka.TryGetProperty("type", out var lkt) && (lkt.GetString() ?? "").Contains("audio") &&
                                        lka.TryGetProperty("href", out var lkh))
                                    {
                                        streamUrl = lkh.GetString() ?? "";
                                        break;
                                    }
                                }
                            }

                            tracks.Add(new TrackItem
                            {
                                Id = $"itunes_{Guid.NewGuid():N}",
                                Title = title,
                                Artist = artist,
                                Album = cat,
                                DurationStr = !string.IsNullOrEmpty(year) ? year : "--:--",
                                StreamUrl = "", // iTunes RSS links are preview URLs; YouTube stream resolved at play time
                                ThumbnailUrl = thumb,
                                Source = "itunes"
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[Trending Genre] {ex.Message}"); }

            return tracks;
        }

        // All Genres: Pull Deezer global top charts
        try
        {
            var url = $"https://api.deezer.com/chart/0/tracks?limit={limit}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    var title    = item.TryGetProperty("title",    out var t)  ? t.GetString()  ?? "" : "";
                    var dur      = item.TryGetProperty("duration", out var d)  ? d.GetInt32()         : 0;
                    var preview  = item.TryGetProperty("preview",  out var pr) ? pr.GetString() ?? "" : "";
                    var link     = item.TryGetProperty("link",     out var lk) ? lk.GetString() ?? "" : "";
                    var albumEl  = item.TryGetProperty("album",    out var al) ? al : default;
                    var artistEl = item.TryGetProperty("artist",   out var ar) ? ar : default;
                    var tid      = item.TryGetProperty("id",       out var id) ? id.GetInt64().ToString() : Guid.NewGuid().ToString();

                    var albumTitle = albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("title",    out var at) ? at.GetString() ?? "" : "";
                    var albumArt   = albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("cover_xl", out var cx) ? cx.GetString() ?? "" :
                                     albumEl.ValueKind != JsonValueKind.Undefined && albumEl.TryGetProperty("cover_big",out var cb) ? cb.GetString() ?? "" : "";
                    var artistName = artistEl.ValueKind != JsonValueKind.Undefined && artistEl.TryGetProperty("name",   out var an) ? an.GetString() ?? "" : "";

                    tracks.Add(new TrackItem
                    {
                        Id = $"deezer_{tid}",
                        Title = title,
                        Artist = artistName,
                        Album = albumTitle,
                        DurationStr = FormatDuration(dur),
                        DurationSec = dur,
                        StreamUrl = "", // iTunes preview URLs are 30-sec clips; YouTube stream resolved at play time
                        ThumbnailUrl = albumArt,
                        SourceUrl = link ?? "",
                        Source = "deezer"
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Trending Deezer] {ex.Message}"); }
        return tracks;
    }

    // ── Autocomplete suggestions via iTunes ───────────────────────────────────

    public static async Task<List<string>> GetAutocompleteSuggestionsAsync(
        string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return [];
        var results = new List<string>();
        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=music&entity=musicTrack&limit=8";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var items)) return results;
            foreach (var item in items.EnumerateArray())
            {
                var artist = item.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : "";
                var track  = item.TryGetProperty("trackName",  out var t) ? t.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(track))
                    results.Add($"{artist} - {track}");
            }
        }
        catch { /* silent */ }
        return results;
    }
}
