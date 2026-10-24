namespace SupaZero.Core.Settings;

/// <summary>
/// Posisi overlay relatif (0..1) ⇄ piksel, seperti SupaFisch. Relatif dihitung terhadap ruang GERAK (area − ukuran overlay),
/// jadi 0 = menempel kiri/atas, 1 = menempel kanan/bawah dan overlay tidak pernah keluar area. Area = klien Roblox dikurangi
/// margin (atas di bawah topbar). Dipakai kotak preview (Settings → Overlay) dan jendela overlay.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Margin (DIP) dari tepi klien Roblox; atas = di bawah topbar.</summary>
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

    /// <summary>Posisi kiri-atas (piksel layar) overlay berukuran w×h di klien Roblox.</summary>
    public static (int X, int Y) ToScreen((int X, int Y, int W, int H) client, double relX, double relY, int w, int h, double scale = 1)
    {
        int m = (int)Math.Round(Margin * scale), top = (int)Math.Round(TopMargin * scale);
        double areaW = client.W - 2 * m, areaH = client.H - top - m;
        return (client.X + m + (int)Math.Round(ToOffset(relX, areaW, w)),
                client.Y + top + (int)Math.Round(ToOffset(relY, areaH, h)));
    }
}
