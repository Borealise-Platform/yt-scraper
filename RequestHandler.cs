using System.Net;
using System.Text;

namespace BorealiseScrapYt;

/// <summary>
/// Routes incoming HTTP requests to the appropriate <see cref="YouTubeService"/> method
/// and writes a JSON response.
/// </summary>
static class RequestHandler
{
    public static async Task HandleAsync(
        HttpListenerContext ctx,
        YouTubeService      yt,
        CancellationToken   ct)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        res.ContentType = "application/json; charset=utf-8";

        int    statusCode;
        string body;

        try
        {
            var path = req.Url?.AbsolutePath ?? "/";

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
                    var results = await yt.SearchAsync(query, limit, ct);
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
                    var video = await yt.GetVideoAsync(videoId, ct);
                    if (video is null)
                    {
                        statusCode = 404;
                        body       = JsonHelper.Serialize(new ErrorResponse("Video not found or unavailable"));
                    }
                    else
                    {
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
                    try
                    {
                        var (tracks, errors) = await yt.GetPlaylistAsync(playlistId, ct);
                        statusCode = 200;
                        body       = JsonHelper.Serialize(new PlaylistResponse(tracks, errors));
                    }
                    catch (YoutubeExplode.Exceptions.PlaylistUnavailableException)
                    {
                        statusCode = 404;
                        body       = JsonHelper.Serialize(new ErrorResponse("Playlist not found or unavailable"));
                    }
                }
            }
            else
            {
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
            Console.Error.WriteLine($"[RequestHandler] Unhandled error: {ex}");
            statusCode = 500;
            body       = JsonHelper.Serialize(new ErrorResponse("Internal server error"));
        }
        finally
        {
            // Ensure the response is always closed even if we return early above
            // (OperationCanceledException path). The Close() call in the normal path
            // below is only reached when finally runs after the try block completes.
            // We rely on HttpListenerResponse being idempotent on double-close.
        }

        res.StatusCode = statusCode;
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, ct);
        res.Close();
    }
}
