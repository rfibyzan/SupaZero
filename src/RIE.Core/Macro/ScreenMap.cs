using System.Globalization;

namespace RIE.Core.Macro;

/// <summary>Info jendela Roblox: origin area klien di layar dan ukurannya (piksel fisik).</summary>
public sealed record WindowInfo(int ClientX, int ClientY, int ClientWidth, int ClientHeight, double DpiScale)
{
    public static readonly WindowInfo None = new(0, 0, 0, 0, 1);
    public bool IsValid => ClientWidth > 0 && ClientHeight > 0;
}

public sealed record ScreenRect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Ubah AbsolutePosition/AbsoluteSize GUI (relatif viewport di bawah topbar) ke koordinat layar.
/// GuiInsetY = tinggi topbar Roblox (default 58 px; 0 untuk ScreenGui dengan IgnoreGuiInset).
/// GuiScale = faktor piksel GUI → piksel layar (biasanya 1).
/// </summary>
public static class ScreenMap
{
    public const double DefaultGuiInsetY = 58;

    public static bool TryParseVec2(string? s, out double x, out double y)
    {
        x = y = 0;
        if (s is null) return false;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    /// <summary>Rect relatif ke pojok kiri-atas area klien jendela Roblox.</summary>
    public static ScreenRect ToClient(double absX, double absY, double w, double h, double guiInsetY = DefaultGuiInsetY, double guiScale = 1) =>
        new(absX * guiScale, (absY + guiInsetY) * guiScale, w * guiScale, h * guiScale);

    /// <summary>Rect di koordinat layar absolut.</summary>
    public static ScreenRect ToScreen(ScreenRect client, WindowInfo win) =>
        new(win.ClientX + client.X, win.ClientY + client.Y, client.Width, client.Height);

    public static bool TryFromProperties(IReadOnlyDictionary<string, string> props, double guiInsetY, double guiScale, out ScreenRect client)
    {
        client = new ScreenRect(0, 0, 0, 0);
        if (!props.TryGetValue("AbsolutePosition", out var p) || !props.TryGetValue("AbsoluteSize", out var s)) return false;
        if (!TryParseVec2(p, out double x, out double y) || !TryParseVec2(s, out double w, out double h)) return false;
        client = ToClient(x, y, w, h, guiInsetY, guiScale);
        return true;
    }
}
