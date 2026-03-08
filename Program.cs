using System.Net;
using BorealiseScrapYt;

const string Tag = "Program";

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

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.Info(Tag, "Shutdown requested (Ctrl+C)");
    cts.Cancel();
};

Log.Info(Tag, $"Listening on {prefix}");
Log.Info(Tag, "Routes: GET /search?q=&limit=  |  GET /video?id=  |  GET /playlist?id=");
Log.Info(Tag, "Press Ctrl+C to stop");

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

Log.Info(Tag, "Stopped.");
