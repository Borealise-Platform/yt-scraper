using System.Text.Json;
using System.Text.Json.Serialization;
using YoutubeExplode.Common;

namespace BorealiseScrapYt;

static class ThumbnailHelper
{
    /// <summary>Pick the thumbnail with the largest pixel area.</summary>
    public static string? GetBest(IReadOnlyList<Thumbnail> thumbnails)
    {
        if (thumbnails.Count == 0) return null;
        return thumbnails
            .OrderByDescending(t => t.Resolution.Area)
            .First()
            .Url;
    }
}

static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
