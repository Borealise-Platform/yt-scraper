using System.Net;
using BorealiseScrapYt;

// ---------------------------------------------------------------------------
// CLI args: --port / -p  (default 5001)
// ---------------------------------------------------------------------------
int port = 5001;
for (int i = 0; i < args.Length - 1; i++)
{
    if ((args[i] == "--port" || args[i] == "-p") && int.TryParse(args[i + 1], out var p))
    {
        port = p;
        break;
    }
}

// ---------------------------------------------------------------------------
// HTTP listener
// ---------------------------------------------------------------------------
var prefix = $"http://localhost:{port}/";
using var listener = new HttpListener();
listener.Prefixes.Add(prefix);
listener.Start();

using var yt  = new YouTubeService();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"[borealise-scrap-yt] Listening on {prefix}");
Console.WriteLine("  GET /search?q=<query>&limit=<n>    — search videos");
Console.WriteLine("  GET /video?id=<videoId>            — get video info");
Console.WriteLine("  GET /playlist?id=<playlistId>      — fetch all playlist tracks");
Console.WriteLine("  Press Ctrl+C to stop.");

// ---------------------------------------------------------------------------
// Main loop
// ---------------------------------------------------------------------------
while (!cts.Token.IsCancellationRequested)
{
    HttpListenerContext ctx;
    try
    {
        ctx = await listener.GetContextAsync();
    }
    catch (HttpListenerException) when (cts.IsCancellationRequested)
    {
        break;
    }

    _ = Task.Run(() => RequestHandler.HandleAsync(ctx, yt, cts.Token), cts.Token);
}

Console.WriteLine("[borealise-scrap-yt] Stopped.");
