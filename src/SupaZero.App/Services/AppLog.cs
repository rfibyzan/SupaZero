using System.Globalization;
using SupaZero.Core.Settings;

namespace SupaZero.App.Services;

/// <summary>Daily log file: %AppData%\SupaZero\logs\supazero-YYYYMMDD.log (append, thread-safe, I/O failures swallowed).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    public static string Directory { get; } = Path.Combine(AppSettings.DataDirectory, "logs");

    public static void Line(string text)
    {
        var now = DateTime.Now;
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, "supazero-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"),
                    now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine);
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
