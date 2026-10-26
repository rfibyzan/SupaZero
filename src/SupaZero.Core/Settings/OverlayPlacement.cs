namespace SupaZero.Core.Settings;

/// <summary>
/// Relative overlay position (0..1) ⇄ pixels, like SupaFisch. Relative is computed against the MOVABLE space (area − overlay size),
/// so 0 = stuck to left/top, 1 = stuck to right/bottom and the overlay never leaves the area. Area = Roblox client minus
/// margins (top is below the topbar). Used by the preview box (Settings → Overlay) and the overlay window.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Margin (DIP) from the Roblox client edge; top = below the topbar.</summary>
    public const double Margin = 12, TopMargin = 64;

    public static double Clamp01(double v, double fallback = 0) =>
        double.IsFinite(v) ? Math.Clamp(v, 0, 1) : Math.Clamp(double.IsFinite(fallback) ? fallback : 0, 0, 1);

    public static double ToOffset(double relative, double areaLength, double itemLength) =>
        Clamp01(relative) * Math.Max(0, areaLength - itemLength);

    public static double ToRelative(double offset, double areaLength, double itemLength)
    {
        double free = areaLength - itemLength;
        if (!(free > 0) || !double.IsFinite(offset)) return 0;
        return Math.Round(Clamp01(offset / free), 4);
    }

    /// <summary>Top-left position (screen pixels) of a w×h overlay in the Roblox client.</summary>
    public static (int X, int Y) ToScreen((int X, int Y, int W, int H) client, double relX, double relY, int w, int h, double scale = 1)
    {
        int m = (int)Math.Round(Margin * scale), top = (int)Math.Round(TopMargin * scale);
        double areaW = client.W - 2 * m, areaH = client.H - top - m;
        return (client.X + m + (int)Math.Round(ToOffset(relX, areaW, w)),
                client.Y + top + (int)Math.Round(ToOffset(relY, areaH, h)));
    }
}
