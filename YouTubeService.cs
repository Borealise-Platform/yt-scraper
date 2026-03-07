using YoutubeExplode;
using YoutubeExplode.Search;

namespace BorealiseScrapYt;

/// <summary>
/// Wraps YoutubeExplode to expose the three operations needed by the Borealise backend:
/// search, single-video lookup, and full playlist fetch — all without an API key or quota.
/// </summary>
sealed class YouTubeService : IDisposable
{
    private readonly YoutubeClient _yt = new();

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    /// <summary>
    /// Search YouTube for videos matching <paramref name="query"/>.
    /// Mirrors searchYouTube() in youtube.ts — limit clamped to 1-50.
    /// Uses batch iteration so we stop as soon as we have enough results,
    /// minimising the number of HTTP round-trips.
    /// </summary>
    public async Task<IReadOnlyList<YouTubeResult>> SearchAsync(
        string query, int limit, CancellationToken ct = default)
    {
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
        try
        {
            var video = await _yt.Videos.GetAsync(videoId, ct);

            return new YouTubeResult(
                Source:    "youtube",
                SourceId:  video.Id.Value,
                Title:     video.Title,
                Artist:    video.Author.ChannelTitle,
                Duration:  (int)(video.Duration?.TotalSeconds ?? 0),
                Thumbnail: ThumbnailHelper.GetBest(video.Thumbnails)
            );
        }
        catch (Exception ex) when (
            ex is YoutubeExplode.Exceptions.VideoUnplayableException or
                  YoutubeExplode.Exceptions.VideoUnavailableException)
        {
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
        var tracks = new List<YouTubeResult>();
        var errors = new List<string>();

        var playlistUrl = playlistId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? playlistId
            : $"https://www.youtube.com/playlist?list={playlistId}";

        await foreach (var video in _yt.Playlists.GetVideosAsync(playlistUrl, ct))
        {
            // YoutubeExplode surfaces unavailable playlist entries with an empty title
            if (string.IsNullOrWhiteSpace(video.Title))
            {
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

        return (tracks, errors);
    }

    public void Dispose() { }
}
