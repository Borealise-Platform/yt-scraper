namespace BorealiseScrapYt;

static class Log
{
    private static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

    public static void Info (string tag, string msg) => Console.WriteLine ($"[{Ts()}] [INFO ] [{tag}] {msg}");
    public static void Warn (string tag, string msg) => Console.WriteLine ($"[{Ts()}] [WARN ] [{tag}] {msg}");
    public static void Error(string tag, string msg) => Console.Error.WriteLine($"[{Ts()}] [ERROR] [{tag}] {msg}");
    public static void Error(string tag, string msg, Exception ex) =>
        Console.Error.WriteLine($"[{Ts()}] [ERROR] [{tag}] {msg}: {ex.GetType().Name}: {ex.Message}");
}
