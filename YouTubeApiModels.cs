using System.Text.Json.Serialization;

namespace BorealiseScrapYt;

sealed class YouTubeSearchResponse
{
    [JsonPropertyName("items")] public List<YouTubeSearchItem>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

sealed class YouTubeSearchItem
{
    [JsonPropertyName("id")] public YouTubeVideoId? Id { get; set; }
    [JsonPropertyName("snippet")] public YouTubeSnippet? Snippet { get; set; }
}

sealed class YouTubeVideoId
{
    [JsonPropertyName("videoId")] public string? VideoId { get; set; }
}

sealed class YouTubeSnippet
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("channelTitle")] public string? ChannelTitle { get; set; }
    [JsonPropertyName("thumbnails")] public YouTubeThumbnails? Thumbnails { get; set; }
}

sealed class YouTubeThumbnails
{
    [JsonPropertyName("medium")] public YouTubeThumbnail? Medium { get; set; }
    [JsonPropertyName("high")] public YouTubeThumbnail? High { get; set; }
}

sealed class YouTubeThumbnail
{
    [JsonPropertyName("url")] public string? Url { get; set; }
}

sealed class YouTubeVideoResponse
{
    [JsonPropertyName("items")] public List<YouTubeVideoItem>? Items { get; set; }
}

sealed class YouTubeVideoItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("snippet")] public YouTubeSnippet? Snippet { get; set; }
    [JsonPropertyName("contentDetails")] public YouTubeContentDetails? ContentDetails { get; set; }
}

sealed class YouTubeContentDetails
{
    [JsonPropertyName("duration")] public string? Duration { get; set; }
}

sealed class YouTubePlaylistResponse
{
    [JsonPropertyName("items")] public List<YouTubePlaylistItem>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

sealed class YouTubePlaylistItem
{
    [JsonPropertyName("snippet")] public YouTubePlaylistSnippet? Snippet { get; set; }
    [JsonPropertyName("contentDetails")] public YouTubePlaylistContentDetails? ContentDetails { get; set; }
}

sealed class YouTubePlaylistContentDetails
{
    [JsonPropertyName("videoId")] public string? VideoId { get; set; }
    [JsonPropertyName("videoPublishedAt")] public string? VideoPublishedAt { get; set; }
}

sealed class YouTubePlaylistSnippet
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("channelTitle")] public string? ChannelTitle { get; set; }
    [JsonPropertyName("thumbnails")] public YouTubeThumbnails? Thumbnails { get; set; }
    [JsonPropertyName("resourceId")] public YouTubeResourceId? ResourceId { get; set; }
}

sealed class YouTubeResourceId
{
    [JsonPropertyName("videoId")] public string? VideoId { get; set; }
}
