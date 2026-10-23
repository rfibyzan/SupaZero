using System.Globalization;
using SupaZero.Core.Settings;

namespace SupaZero.App.Services;

/// <summary>Log file harian: %AppData%\SupaZero\logs\supazero-YYYYMMDD.log (append, thread-safe, gagal I/O ditelan).</summary>
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
        catch { /* logging tidak boleh menjatuhkan app */ }
    }
}
