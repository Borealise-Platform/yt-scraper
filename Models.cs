using System.Text.Json.Serialization;

namespace BorealiseScrapYt;

record YouTubeResult(
    [property: JsonPropertyName("source")]    string  Source,
    [property: JsonPropertyName("sourceId")]  string  SourceId,
    [property: JsonPropertyName("title")]     string  Title,
    [property: JsonPropertyName("artist")]    string  Artist,
    [property: JsonPropertyName("duration")]  int     Duration,
    [property: JsonPropertyName("thumbnail")] string? Thumbnail
);

record SearchResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<YouTubeResult> Results
);

record VideoResponse(
    [property: JsonPropertyName("video")] YouTubeResult Video
);

record PlaylistResponse(
    [property: JsonPropertyName("tracks")] IReadOnlyList<YouTubeResult> Tracks,
    [property: JsonPropertyName("errors")] IReadOnlyList<string>        Errors
);

record ErrorResponse(
    [property: JsonPropertyName("error")] string Error
);

record AppConfig(
    [property: JsonPropertyName("YouTubeApiKeys")] List<string>? YouTubeApiKeys = null
);
