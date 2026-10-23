using Avalonia;

namespace SupaZero.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.AppLog.Line("[SupaZero] Application start.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.AppLog.Line("[ERROR] " + e.ExceptionObject);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
