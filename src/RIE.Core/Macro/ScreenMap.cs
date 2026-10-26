using System.Globalization;

namespace RIE.Core.Macro;

/// <summary>Roblox window info: client area origin on screen and its size (physical pixels).</summary>
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
/// Converts GUI AbsolutePosition/AbsoluteSize (relative to the viewport below the topbar) to screen coordinates.
/// GuiInsetY = Roblox topbar height (default 58 px; 0 for a ScreenGui with IgnoreGuiInset).
/// GuiScale = GUI pixel → screen pixel factor (usually 1).
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

    /// <summary>Rect relative to the top-left corner of the Roblox window client area.</summary>
    public static ScreenRect ToClient(double absX, double absY, double w, double h, double guiInsetY = DefaultGuiInsetY, double guiScale = 1) =>
        new(absX * guiScale, (absY + guiInsetY) * guiScale, w * guiScale, h * guiScale);

    /// <summary>Rect in absolute screen coordinates.</summary>
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
