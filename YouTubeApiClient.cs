using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;

namespace BorealiseScrapYt;

sealed class YouTubeApiClient : IDisposable
{
    private readonly List<string> _apiKeys;
    private readonly Func<int> _getKeyIndex;
    private readonly Action<int> _setKeyIndex;
    private readonly HttpClient _http;
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3";

    public YouTubeApiClient(List<string> apiKeys, Func<int> getKeyIndex, Action<int> setKeyIndex)
    {
        _apiKeys = apiKeys;
        _getKeyIndex = getKeyIndex;
        _setKeyIndex = setKeyIndex;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private string GetNextKey()
    {
        if (_apiKeys.Count == 1)
            return _apiKeys[0];

        var newIndex = (_getKeyIndex() + 1) % _apiKeys.Count;
        _setKeyIndex(newIndex);
        return _apiKeys[newIndex];
    }

    public async Task<IReadOnlyList<YouTubeResult>?> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var results = new List<YouTubeResult>();
        string? nextPageToken = null;

        while (results.Count < limit)
        {
            var key = GetNextKey();
            var url = $"{BaseUrl}/search?part=snippet&type=video&q={Uri.EscapeDataString(query)}&maxResults={Math.Min(50, limit - results.Count)}&key={key}";
            
            if (!string.IsNullOrEmpty(nextPageToken))
                url += $"&pageToken={nextPageToken}";

            try
            {
                var response = await _http.GetAsync(url, ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == HttpStatusCode.Forbidden || 
                        error.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    return null;
                }

                var data = await response.Content.ReadFromJsonAsync<YouTubeSearchResponse>();
                if (data?.Items == null || data.Items.Count == 0)
                    break;

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
            catch (HttpRequestException)
            {
                return results.Count > 0 ? results : null;
            }
            catch (TaskCanceledException)
            {
                return results.Count > 0 ? results : null;
            }
        }

        return results;
    }

    public async Task<YouTubeResult?> GetVideoAsync(string videoId, CancellationToken ct = default)
    {
        var key = GetNextKey();
        var url = $"{BaseUrl}/videos?part=snippet,contentDetails&id={videoId}&key={key}";

        try
        {
            var response = await _http.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    error.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return null;
            }

            var data = await response.Content.ReadFromJsonAsync<YouTubeVideoResponse>();
            var item = data?.Items?.FirstOrDefault();
            if (item?.Snippet == null || string.IsNullOrEmpty(item.Id))
                return null;

            var duration = ParseDuration(item.ContentDetails?.Duration);

            return new YouTubeResult(
                Source:    "youtube",
                SourceId:  item.Id,
                Title:     item.Snippet.Title ?? "",
                Artist:    item.Snippet.ChannelTitle ?? "",
                Duration:  duration,
                Thumbnail: item.Snippet.Thumbnails?.Medium?.Url ?? item.Snippet.Thumbnails?.High?.Url
            );
        }
        catch
        {
            return null;
        }
    }

    public async Task<(IReadOnlyList<YouTubeResult> Tracks, IReadOnlyList<string> Errors)?> 
        GetPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        var tracks    = new List<YouTubeResult>();
        var errors    = new List<string>();
        var videoIds  = new List<string>();
        string? nextPageToken = null;

        var cleanPlaylistId = playlistId;
        if (playlistId.Contains("list="))
        {
            var uri   = new Uri(playlistId);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            cleanPlaylistId = query["list"] ?? playlistId;
        }

        // Step 1: page through playlistItems to collect video IDs + metadata
        while (true)
        {
            var key = GetNextKey();
            var url = $"{BaseUrl}/playlistItems?part=snippet,contentDetails&maxResults=50&playlistId={cleanPlaylistId}&key={key}";
            if (!string.IsNullOrEmpty(nextPageToken))
                url += $"&pageToken={nextPageToken}";

            try
            {
                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == HttpStatusCode.Forbidden ||
                        error.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase))
                        return null;
                    break;
                }

                var data = await response.Content.ReadFromJsonAsync<YouTubePlaylistResponse>();
                if (data?.Items == null || data.Items.Count == 0)
                    break;

                foreach (var item in data.Items)
                {
                    var videoId = item.Snippet?.ResourceId?.VideoId ?? item.ContentDetails?.VideoId;
                    if (videoId == null)
                    {
                        var title = item.Snippet?.Title;
                        if (title == "Private video" || title == "Deleted video")
                            errors.Add($"Video is unavailable: {title}");
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
                }

                nextPageToken = data.NextPageToken;
                if (string.IsNullOrEmpty(nextPageToken))
                    break;
            }
            catch
            {
                break;
            }
        }

        // Step 2: batch-fetch durations via videos?part=contentDetails (up to 50 IDs per request)
        var durations = new Dictionary<string, int>();
        for (int i = 0; i < videoIds.Count; i += 50)
        {
            var batch  = videoIds.Skip(i).Take(50);
            var key    = GetNextKey();
            var url    = $"{BaseUrl}/videos?part=contentDetails&id={string.Join(",", batch)}&key={key}";

            try
            {
                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    break;

                var data = await response.Content.ReadFromJsonAsync<YouTubeVideoResponse>();
                if (data?.Items == null)
                    continue;

                foreach (var item in data.Items)
                    if (!string.IsNullOrEmpty(item.Id) && item.ContentDetails != null)
                        durations[item.Id] = ParseDuration(item.ContentDetails.Duration);
            }
            catch
            {
                break;
            }
        }

        // Step 3: merge durations into tracks
        var result = tracks.Select(t =>
            durations.TryGetValue(t.SourceId, out var dur)
                ? t with { Duration = dur }
                : t
        ).ToList();

        return (result, errors);
    }

    private static int ParseDuration(string? iso8601)
    {
        if (string.IsNullOrEmpty(iso8601))
            return 0;

        try
        {
            var duration = System.Xml.XmlConvert.ToTimeSpan(iso8601);
            return (int)duration.TotalSeconds;
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
