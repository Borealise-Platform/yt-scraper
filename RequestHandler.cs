using System.Net;
using System.Text;

namespace BorealiseScrapYt;

static class RequestHandler
{
    private const string Tag = "RequestHandler";

    public static async Task HandleAsync(
        HttpListenerContext ctx,
        YouTubeService      yt,
        CancellationToken   ct)
    {
        var req  = ctx.Request;
        var res  = ctx.Response;
        var path = req.Url?.AbsolutePath ?? "/";
        var qs   = req.Url?.Query ?? "";

        Log.Info(Tag, $"{req.HttpMethod} {path}{qs}");

        res.ContentType = "application/json; charset=utf-8";

        int    statusCode;
        string body;

        try
        {
            if (path == "/search")
            {
                var query = req.QueryString["q"]?.Trim() ?? "";
                var limit = int.TryParse(req.QueryString["limit"], out var l) ? l : 10;

                if (string.IsNullOrEmpty(query))
                {
                    statusCode = 400;
                    body       = JsonHelper.Serialize(new ErrorResponse("Query parameter 'q' is required"));
                }
                else
                {
                    Log.Info(Tag, $"Search: q={query} limit={limit}");
                    var results = await yt.SearchAsync(query, limit, ct);
                    Log.Info(Tag, $"Search: returned {results.Count} results");
                    statusCode  = 200;
                    body        = JsonHelper.Serialize(new SearchResponse(results));
                }
            }
            else if (path == "/video")
            {
                var videoId = req.QueryString["id"]?.Trim() ?? "";

                if (string.IsNullOrEmpty(videoId) || videoId.Length > 20)
                {
                    statusCode = 400;
                    body       = JsonHelper.Serialize(new ErrorResponse("Invalid or missing 'id' parameter"));
                }
                else
                {
                    Log.Info(Tag, $"Video: id={videoId}");
                    var video = await yt.GetVideoAsync(videoId, ct);
                    if (video is null)
                    {
                        Log.Warn(Tag, $"Video not found: {videoId}");
                        statusCode = 404;
                        body       = JsonHelper.Serialize(new ErrorResponse("Video not found or unavailable"));
                    }
                    else
                    {
                        Log.Info(Tag, $"Video: found '{video.Title}' ({video.Duration}s)");
                        statusCode = 200;
                        body       = JsonHelper.Serialize(new VideoResponse(video));
                    }
                }
            }
            else if (path == "/playlist")
            {
                var playlistId = req.QueryString["id"]?.Trim() ?? "";

                if (string.IsNullOrEmpty(playlistId))
                {
                    statusCode = 400;
                    body       = JsonHelper.Serialize(new ErrorResponse("Query parameter 'id' is required (playlist ID or URL)"));
                }
                else
                {
                    Log.Info(Tag, $"Playlist: id={playlistId}");
                    try
                    {
                        var (tracks, errors) = await yt.GetPlaylistAsync(playlistId, ct);
                        Log.Info(Tag, $"Playlist: {tracks.Count} tracks, {errors.Count} errors");
                        if (errors.Count > 0)
                            Log.Warn(Tag, $"Playlist errors: {string.Join("; ", errors)}");
                        statusCode = 200;
                        body       = JsonHelper.Serialize(new PlaylistResponse(tracks, errors));
                    }
                    catch (YoutubeExplode.Exceptions.PlaylistUnavailableException ex)
                    {
                        Log.Warn(Tag, $"Playlist unavailable: {playlistId}: {ex.Message}");
                        statusCode = 404;
                        body       = JsonHelper.Serialize(new ErrorResponse("Playlist not found or unavailable"));
                    }
                }
            }
            else
            {
                Log.Warn(Tag, $"Unknown route: {path}");
                statusCode = 404;
                body       = JsonHelper.Serialize(new ErrorResponse($"Unknown route: {path}"));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Unhandled error on {path}", ex);
            statusCode = 500;
            body       = JsonHelper.Serialize(new ErrorResponse("Internal server error"));
        }

        res.StatusCode = statusCode;
        Log.Info(Tag, $"Response: {statusCode}");
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, ct);
        res.Close();
    }
}
