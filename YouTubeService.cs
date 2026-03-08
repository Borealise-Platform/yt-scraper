using System.Net.Http.Json;
using System.Text.Json;
using YoutubeExplode;
using YoutubeExplode.Search;

namespace BorealiseScrapYt;

sealed class YouTubeService : IDisposable
{
    private readonly YoutubeClient _yt = new();
    private readonly YouTubeApiClient? _apiClient;
    private readonly List<string> _apiKeys;
    private int _currentKeyIndex;

    public YouTubeService()
    {
        var config = LoadConfig();
        _apiKeys = config.YouTubeApiKeys ?? [];
        if (_apiKeys.Count > 0)
        {
            _apiClient = new YouTubeApiClient(_apiKeys, () => _currentKeyIndex, i => _currentKeyIndex = i);
        }
    }

    private static AppConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    /// <summary>
    /// Search YouTube for videos matching <paramref name="query"/>.
    /// Mirrors searchYouTube() in youtube.ts — limit clamped to 1-50.
    /// Uses batch iteration so we stop as soon as we have enough results,
    /// minimising the number of HTTP round-trips.
    /// </summary>
    private const string Tag = "YouTubeService";

    public async Task<IReadOnlyList<YouTubeResult>> SearchAsync(
        string query, int limit, CancellationToken ct = default)
    {
        if (_apiClient != null)
        {
            Log.Info(Tag, $"Search via API: q={query} limit={limit}");
            var apiResults = await _apiClient.SearchAsync(query, limit, ct);
            if (apiResults != null && apiResults.Count > 0)
            {
                Log.Info(Tag, $"Search via API: got {apiResults.Count} results");
                return apiResults;
            }
            Log.Warn(Tag, "Search via API returned nothing, falling back to scrape");
        }

        Log.Info(Tag, $"Search via scrape: q={query} limit={limit}");
        limit = Math.Clamp(limit, 1, 50);
        var results = new List<YouTubeResult>(limit);
        var seen    = new HashSet<string>();

        await foreach (var batch in _yt.Search.GetResultBatchesAsync(query, ct))
        {
            foreach (var item in batch.Items)
            {
                if (item is not VideoSearchResult video) continue;

                var id = video.Id.Value;
                if (!seen.Add(id)) continue;

                results.Add(new YouTubeResult(
                    Source:    "youtube",
                    SourceId:  id,
                    Title:     video.Title,
                    Artist:    video.Author.ChannelTitle,
                    Duration:  (int)(video.Duration?.TotalSeconds ?? 0),
                    Thumbnail: ThumbnailHelper.GetBest(video.Thumbnails)
                ));

                if (results.Count >= limit) break;
            }

            if (results.Count >= limit) break;
        }

        Log.Info(Tag, $"Search via scrape: got {results.Count} results");
        return results;
    }

    // -------------------------------------------------------------------------
    // Single video
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetch metadata for a single video by its ID.
    /// Mirrors getYouTubeVideo() in youtube.ts.
    /// Returns <c>null</c> when the video is unavailable or unplayable.
    /// </summary>
    public async Task<YouTubeResult?> GetVideoAsync(
        string videoId, CancellationToken ct = default)
    {
        if (_apiClient != null)
        {
            Log.Info(Tag, $"GetVideo via API: id={videoId}");
            var apiResult = await _apiClient.GetVideoAsync(videoId, ct);
            if (apiResult != null)
            {
                Log.Info(Tag, $"GetVideo via API: found '{apiResult.Title}'");
                return apiResult;
            }
            Log.Warn(Tag, $"GetVideo via API returned null for {videoId}, falling back to scrape");
        }

        Log.Info(Tag, $"GetVideo via scrape: id={videoId}");
        try
        {
            var video = await _yt.Videos.GetAsync(videoId, ct);
            Log.Info(Tag, $"GetVideo via scrape: found '{video.Title}' ({(int)(video.Duration?.TotalSeconds ?? 0)}s)");

            return new YouTubeResult(
                Source:    "youtube",
                SourceId:  video.Id.Value,
                Title:     video.Title,
                Artist:    video.Author.ChannelTitle,
                Duration:  (int)(video.Duration?.TotalSeconds ?? 0),
                Thumbnail: ThumbnailHelper.GetBest(video.Thumbnails)
            );
        }
        catch (Exception ex)
        {
            if (ex is YoutubeExplode.Exceptions.VideoUnplayableException or
                      YoutubeExplode.Exceptions.VideoUnavailableException)
                Log.Warn(Tag, $"GetVideo: video unavailable: {videoId}");
            else
                Log.Error(Tag, $"GetVideo: unexpected error for {videoId}", ex);
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Playlist
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetch all videos from a YouTube playlist.
    /// Mirrors fetchYouTubePlaylist() in import.ts — no API key, no quota.
    /// Private/unavailable videos are captured as error strings, not thrown.
    /// </summary>
    /// <param name="playlistId">
    /// A raw playlist ID (e.g. <c>PLxxxxxxxxxx</c>) or a full YouTube playlist URL.
    /// </param>
    public async Task<(IReadOnlyList<YouTubeResult> Tracks, IReadOnlyList<string> Errors)>
        GetPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        if (_apiClient != null)
        {
            Log.Info(Tag, $"GetPlaylist via API: id={playlistId}");
            var apiResult = await _apiClient.GetPlaylistAsync(playlistId, ct);
            if (apiResult != null)
            {
                Log.Info(Tag, $"GetPlaylist via API: {apiResult.Value.Tracks.Count} tracks, {apiResult.Value.Errors.Count} errors");
                return apiResult.Value;
            }
            Log.Warn(Tag, $"GetPlaylist via API returned null for {playlistId}, falling back to scrape");
        }

        Log.Info(Tag, $"GetPlaylist via scrape: id={playlistId}");
        var tracks = new List<YouTubeResult>();
        var errors = new List<string>();

        var playlistUrl = playlistId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? playlistId
            : $"https://www.youtube.com/playlist?list={playlistId}";

        await foreach (var video in _yt.Playlists.GetVideosAsync(playlistUrl, ct))
        {
            if (string.IsNullOrWhiteSpace(video.Title))
            {
                Log.Warn(Tag, $"GetPlaylist: skipping unavailable video '{video.Id}'");
                errors.Add($"Video '{video.Id}' is unavailable or private");
                continue;
            }

            tracks.Add(new YouTubeResult(
                Source:    "youtube",
                SourceId:  video.Id.Value,
                Title:     video.Title,
                Artist:    video.Author.ChannelTitle,
                Duration:  (int)(video.Duration?.TotalSeconds ?? 0),
                Thumbnail: ThumbnailHelper.GetBest(video.Thumbnails)
            ));
        }

        Log.Info(Tag, $"GetPlaylist via scrape: {tracks.Count} tracks, {errors.Count} errors");
        return (tracks, errors);
    }

    public void Dispose() 
    {
        _apiClient?.Dispose();
    }
}
