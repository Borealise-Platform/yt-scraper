using System.Net.Http.Json;
using System.Net;

namespace BorealiseScrapYt;

sealed class YouTubeApiClient : IDisposable
{
    private const string Tag     = "YouTubeApiClient";
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3";

    private readonly List<string> _apiKeys;
    private readonly Func<int>    _getKeyIndex;
    private readonly Action<int>  _setKeyIndex;
    private readonly HttpClient   _http;

    public YouTubeApiClient(List<string> apiKeys, Func<int> getKeyIndex, Action<int> setKeyIndex)
    {
        _apiKeys     = apiKeys;
        _getKeyIndex = getKeyIndex;
        _setKeyIndex = setKeyIndex;
        _http        = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Log.Info(Tag, $"Initialized with {apiKeys.Count} API key(s)");
    }

    private string GetNextKey()
    {
        if (_apiKeys.Count == 1)
            return _apiKeys[0];

        var newIndex = (_getKeyIndex() + 1) % _apiKeys.Count;
        _setKeyIndex(newIndex);
        Log.Info(Tag, $"Rotated to API key index {newIndex} ({_apiKeys[newIndex]})");
        return _apiKeys[newIndex];
    }

    public async Task<IReadOnlyList<YouTubeResult>?> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var results = new List<YouTubeResult>();
        string? nextPageToken = null;
        int page = 0;

        while (results.Count < limit)
        {
            page++;
            var key = GetNextKey();
            var url = $"{BaseUrl}/search?part=snippet&type=video&q={Uri.EscapeDataString(query)}&maxResults={Math.Min(50, limit - results.Count)}&key={key}";
            if (!string.IsNullOrEmpty(nextPageToken))
                url += $"&pageToken={nextPageToken}";

            Log.Info(Tag, $"Search page {page}: q={query}");

            try
            {
                var response = await _http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == HttpStatusCode.Forbidden ||
                        errorBody.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                        errorBody.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warn(Tag, $"Search: quota exceeded or forbidden ({(int)response.StatusCode}), trying next key");
                        continue;
                    }
                    Log.Error(Tag, $"Search: API error {(int)response.StatusCode}: {errorBody}");
                    return null;
                }

                var data = await response.Content.ReadFromJsonAsync<YouTubeSearchResponse>();
                if (data?.Items == null || data.Items.Count == 0)
                {
                    Log.Info(Tag, "Search: no more items from API");
                    break;
                }

                foreach (var item in data.Items)
                {
                    if (item.Id?.VideoId == null || item.Snippet == null)
                        continue;

                    results.Add(new YouTubeResult(
                        Source:    "youtube",
                        SourceId:  item.Id.VideoId,
                        Title:     item.Snippet.Title ?? "",
                        Artist:    item.Snippet.ChannelTitle ?? "",
                        Duration:  0,
                        Thumbnail: item.Snippet.Thumbnails?.Medium?.Url ?? item.Snippet.Thumbnails?.High?.Url
                    ));

                    if (results.Count >= limit)
                        break;
                }

                nextPageToken = data.NextPageToken;
                if (string.IsNullOrEmpty(nextPageToken))
                    break;
            }
            catch (HttpRequestException ex)
            {
                Log.Error(Tag, "Search: HTTP error", ex);
                return results.Count > 0 ? results : null;
            }
            catch (TaskCanceledException ex)
            {
                Log.Warn(Tag, $"Search: request cancelled: {ex.Message}");
                return results.Count > 0 ? results : null;
            }
        }

        Log.Info(Tag, $"Search: returning {results.Count} results");
        return results;
    }

    public async Task<YouTubeResult?> GetVideoAsync(string videoId, CancellationToken ct = default)
    {
        var key = GetNextKey();
        var url = $"{BaseUrl}/videos?part=snippet,contentDetails&id={videoId}&key={key}";

        Log.Info(Tag, $"GetVideo: id={videoId}");

        try
        {
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    errorBody.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn(Tag, $"GetVideo: quota exceeded or forbidden ({(int)response.StatusCode})");
                    return null;
                }
                Log.Error(Tag, $"GetVideo: API error {(int)response.StatusCode}: {errorBody}");
                return null;
            }

            var data = await response.Content.ReadFromJsonAsync<YouTubeVideoResponse>();
            var item = data?.Items?.FirstOrDefault();
            if (item?.Snippet == null || string.IsNullOrEmpty(item.Id))
            {
                Log.Warn(Tag, $"GetVideo: no item returned for id={videoId}");
                return null;
            }

            var duration = ParseDuration(item.ContentDetails?.Duration);
            Log.Info(Tag, $"GetVideo: found '{item.Snippet.Title}' ({duration}s)");

            return new YouTubeResult(
                Source:    "youtube",
                SourceId:  item.Id,
                Title:     item.Snippet.Title ?? "",
                Artist:    item.Snippet.ChannelTitle ?? "",
                Duration:  duration,
                Thumbnail: item.Snippet.Thumbnails?.Medium?.Url ?? item.Snippet.Thumbnails?.High?.Url
            );
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"GetVideo: unexpected error for id={videoId}", ex);
            return null;
        }
    }

    public async Task<(IReadOnlyList<YouTubeResult> Tracks, IReadOnlyList<string> Errors)?> 
        GetPlaylistAsync(string playlistId, int limit = int.MaxValue, CancellationToken ct = default)
    {
        var tracks       = new List<YouTubeResult>();
        var errors       = new List<string>();
        var videoIds     = new List<string>();
        string? nextPageToken = null;
        int page = 0;

        var cleanPlaylistId = playlistId;
        if (playlistId.Contains("list="))
        {
            var uri   = new Uri(playlistId);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            cleanPlaylistId = query["list"] ?? playlistId;
        }

        Log.Info(Tag, $"GetPlaylist: id={cleanPlaylistId}, limit={limit}");

        // Step 1: page through playlistItems
        while (tracks.Count < limit)
        {
            page++;
            var key = GetNextKey();
            var maxResults = Math.Min(50, limit - tracks.Count);
            var url = $"{BaseUrl}/playlistItems?part=snippet,contentDetails&maxResults={maxResults}&playlistId={cleanPlaylistId}&key={key}";
            if (!string.IsNullOrEmpty(nextPageToken))
                url += $"&pageToken={nextPageToken}";

            Log.Info(Tag, $"GetPlaylist: fetching page {page}");

            try
            {
                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == HttpStatusCode.Forbidden ||
                        errorBody.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warn(Tag, $"GetPlaylist: quota exceeded or forbidden ({(int)response.StatusCode}), signalling fallback");
                        return null;
                    }
                    Log.Error(Tag, $"GetPlaylist: API error {(int)response.StatusCode}: {errorBody}");
                    break;
                }

                var data = await response.Content.ReadFromJsonAsync<YouTubePlaylistResponse>();
                if (data?.Items == null || data.Items.Count == 0)
                {
                    Log.Info(Tag, "GetPlaylist: no more items");
                    break;
                }

                foreach (var item in data.Items)
                {
                    var videoId = item.Snippet?.ResourceId?.VideoId ?? item.ContentDetails?.VideoId;
                    if (videoId == null)
                    {
                        var title = item.Snippet?.Title;
                        if (title == "Private video" || title == "Deleted video")
                        {
                            Log.Warn(Tag, $"GetPlaylist: skipping unavailable video ({title})");
                            errors.Add($"Video is unavailable: {title}");
                        }
                        continue;
                    }

                    videoIds.Add(videoId);
                    tracks.Add(new YouTubeResult(
                        Source:    "youtube",
                        SourceId:  videoId,
                        Title:     item.Snippet?.Title ?? "",
                        Artist:    item.Snippet?.ChannelTitle ?? "",
                        Duration:  0,
                        Thumbnail: item.Snippet?.Thumbnails?.Medium?.Url ?? item.Snippet?.Thumbnails?.High?.Url
                    ));

                    if (tracks.Count >= limit)
                        break;
                }

                Log.Info(Tag, $"GetPlaylist: page {page} yielded {data.Items.Count} items (total so far: {tracks.Count})");
                nextPageToken = data.NextPageToken;
                if (string.IsNullOrEmpty(nextPageToken) || tracks.Count >= limit)
                    break;
            }
            catch (Exception ex)
            {
                Log.Error(Tag, "GetPlaylist: error fetching playlist page", ex);
                break;
            }
        }

        // Step 2: batch-fetch durations
        Log.Info(Tag, $"GetPlaylist: fetching durations for {videoIds.Count} videos");
        var durations = new Dictionary<string, int>();
        for (int i = 0; i < videoIds.Count; i += 50)
        {
            var batch = videoIds.Skip(i).Take(50).ToList();
            var key   = GetNextKey();
            var url   = $"{BaseUrl}/videos?part=contentDetails&id={string.Join(",", batch)}&key={key}";

            Log.Info(Tag, $"GetPlaylist: duration batch {i / 50 + 1} ({batch.Count} ids)");

            try
            {
                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn(Tag, $"GetPlaylist: duration batch failed ({(int)response.StatusCode})");
                    break;
                }

                var data = await response.Content.ReadFromJsonAsync<YouTubeVideoResponse>();
                if (data?.Items == null)
                    continue;

                foreach (var item in data.Items)
                    if (!string.IsNullOrEmpty(item.Id) && item.ContentDetails != null)
                        durations[item.Id] = ParseDuration(item.ContentDetails.Duration);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, "GetPlaylist: error fetching duration batch", ex);
                break;
            }
        }

        Log.Info(Tag, $"GetPlaylist: resolved {durations.Count}/{videoIds.Count} durations");

        // Step 3: merge
        var result = tracks.Select(t =>
            durations.TryGetValue(t.SourceId, out var dur)
                ? t with { Duration = dur }
                : t
        ).ToList();

        Log.Info(Tag, $"GetPlaylist: done — {result.Count} tracks, {errors.Count} errors");
        return (result, errors);
    }

    private static int ParseDuration(string? iso8601)
    {
        if (string.IsNullOrEmpty(iso8601))
            return 0;

        try
        {
            return (int)System.Xml.XmlConvert.ToTimeSpan(iso8601).TotalSeconds;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
