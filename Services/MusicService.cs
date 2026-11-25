// MusicService.cs — Compatibility wrapper delegating to SearchService & PlayerService
using Burudani.Models;

namespace Burudani.Services;

public static class MusicService
{
    public static Task<List<TrackItem>> SearchItunesAsync(string query, int limit = 15) =>
        SearchService.SearchItunesSongsAsync(query, limit);

    public static Task<List<AlbumItem>> SearchDeezerAlbumsAsync(string query, int limit = 15) =>
        SearchService.SearchDeezerAlbumsAsync(query, limit);

    public static Task<List<TrackItem>> SearchSongsAsync(string query, string source = "all", int limit = 50) =>
        SearchService.SearchSongsAsync(query, source, limit);

    public static Task<List<AlbumItem>> SearchAlbumsAsync(string query, string source = "all", int limit = 25) =>
        SearchService.SearchAlbumsAsync(query, source, limit);

    public static Task<List<TrackItem>> GetTrendingAsync(string region = "US") =>
        SearchService.GetTrendingTracksAsync(region);
}
