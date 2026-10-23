using System.Globalization;

namespace SupaZero.Core.Settings;

/// <summary>Warna RGB 24-bit (tanpa alpha).</summary>
public readonly record struct ThemeRgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>"#RRGGBB" / "RRGGBB" / "#RGB" → warna; selain itu false.</summary>
    public static bool TryParse(string? text, out ThemeRgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;
        color = new ThemeRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static ThemeRgb Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Warna tidak valid: '{text}'.");

    /// <summary>Campur linear: t = 0 → this, t = 1 → other.</summary>
    public ThemeRgb Mix(ThemeRgb other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte L(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return new ThemeRgb(L(R, other.R, t), L(G, other.G, t), L(B, other.B, t));
    }

    /// <summary>Luminans relatif (sRGB, WCAG).</summary>
    public double Luminance
    {
        get
        {
            static double C(byte v) { double c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            return 0.2126 * C(R) + 0.7152 * C(G) + 0.0722 * C(B);
        }
    }
}

/// <summary>
/// Palet satu tema: lima warna dasar (sama dengan kolom "Custom colours" heritage: Background, Surface, Border, Accent,
/// Text) + turunan yang dipakai UI port. Turunan dihitung, bukan dari decompile [S].
/// </summary>
public sealed record ThemePalette(string Name, ThemeRgb Background, ThemeRgb Surface, ThemeRgb Border, ThemeRgb Accent, ThemeRgb Text)
{
    /// <summary>Tema terang bila latar lebih terang dari teks.</summary>
    public bool IsLight => Background.Luminance > Text.Luminance;

    public ThemeRgb SurfaceInner => Surface.Mix(Text, IsLight ? 0.03 : 0.04);
    public ThemeRgb Muted => Text.Mix(Background, 0.45);
    public ThemeRgb AccentDim => Surface.Mix(Accent, IsLight ? 0.16 : 0.12);
    public ThemeRgb Strip => Surface.Mix(Accent, 0.05);
    public ThemeRgb Hover => Surface.Mix(Text, IsLight ? 0.05 : 0.06);
    public ThemeRgb AccentHover => Accent.Mix(IsLight ? new ThemeRgb(0, 0, 0) : new ThemeRgb(255, 255, 255), 0.15);
    public ThemeRgb AccentPressed => Accent.Mix(new ThemeRgb(0, 0, 0), 0.15);

    /// <summary>
    /// Tombol utama (Button.accent / segmen aktif). Aksen gelap/berwarna (luminans &lt; 0.4) dipakai langsung; aksen terang
    /// (mis. tema Black, abu terang) TIDAK dipakai sebagai latar tombol: tombol jadi abu gelap dengan teks terang, dan saat
    /// hover yang berubah latarnya (lebih terang), bukan teksnya (permintaan pengguna 22 Okt 2026).
    /// </summary>
    public bool ButtonUsesAccent => Accent.Luminance < 0.4;
    public ThemeRgb ButtonBg => ButtonUsesAccent ? Accent : Surface.Mix(Text, IsLight ? 0.10 : 0.14);
    public ThemeRgb ButtonHover => ButtonUsesAccent ? AccentHover : Surface.Mix(Text, IsLight ? 0.16 : 0.24);
    public ThemeRgb ButtonPressed => ButtonUsesAccent ? AccentPressed : Surface.Mix(Text, IsLight ? 0.06 : 0.09);
    public ThemeRgb ButtonDisabled => Surface.Mix(Text, IsLight ? 0.05 : 0.07);
    public ThemeRgb OnButton => ButtonUsesAccent ? OnAccent : Text;

    /// <summary>Teks di atas aksen (badge, tombol aksen): gelap atau putih, mana yang kontrasnya lebih tinggi.</summary>
    public ThemeRgb OnAccent
    {
        get
        {
            var dark = new ThemeRgb(0x0B, 0x0D, 0x0D);
            var light = new ThemeRgb(0xFF, 0xFF, 0xFF);
            double l = Accent.Luminance;
            double cDark = (l + 0.05) / (dark.Luminance + 0.05);
            double cLight = (light.Luminance + 0.05) / (l + 0.05);
            return cDark >= cLight ? dark : light;
        }
    }
}

/// <summary>
/// Tema SupaZero (sama dengan SupaFisch) (permintaan pengguna 22 Okt 2026): layout mengikuti heritage (strip tab atas), tetapi palet hanya
/// Black (default, hitam netral), White (terang), dan Custom. Tidak ada aksen hijau.
/// Nama lama dipetakan: Dark/Abyss/Midnight/Slate/Pink → Black, Light/Daylight → White.
/// </summary>
public static class ThemePalettes
{
    public const string Black = "Black";
    public const string White = "White";
    public const string Custom = "Custom";

    public static IReadOnlyList<string> Names { get; } = [Black, White, Custom];

    public static ThemePalette BlackPalette { get; } = new(Black,
        ThemeRgb.Parse("#0A0A0A"), ThemeRgb.Parse("#141414"), ThemeRgb.Parse("#262626"), ThemeRgb.Parse("#E5E5E5"), ThemeRgb.Parse("#EDEDED"));
    public static ThemePalette WhitePalette { get; } = new(White,
        ThemeRgb.Parse("#F5F5F5"), ThemeRgb.Parse("#FFFFFF"), ThemeRgb.Parse("#DADADA"), ThemeRgb.Parse("#171717"), ThemeRgb.Parse("#171717"));

    /// <summary>Warna titik penanda tiap tombol tema.</summary>
    public static string SwatchHex(string name) => Normalize(name) switch
    {
        White => "#FFFFFF",
        Custom => new CustomThemeSettings().Accent,
        _ => "#0A0A0A",
    };

    /// <summary>Nama tema dinormalisasi (case-insensitive). Nama lama dipetakan; tak dikenal → Black.</summary>
    public static string Normalize(string? name)
    {
        var n = name?.Trim();
        var hit = Names.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        if (string.Equals(n, "Light", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "Daylight", StringComparison.OrdinalIgnoreCase))
            return White;
        return Black;
    }

    /// <summary>Palet custom; nilai hex tidak valid jatuh ke default <see cref="CustomThemeSettings"/>.</summary>
    public static ThemePalette FromCustom(CustomThemeSettings? custom)
    {
        var d = new CustomThemeSettings();
        custom ??= d;
        static ThemeRgb Pick(string v, string fallback) => ThemeRgb.TryParse(v, out var c) ? c : ThemeRgb.Parse(fallback);
        return new ThemePalette(Custom,
            Pick(custom.Background, d.Background), Pick(custom.Surface, d.Surface), Pick(custom.Border, d.Border),
            Pick(custom.Accent, d.Accent), Pick(custom.Text, d.Text));
    }

    public static ThemePalette Resolve(string? name, CustomThemeSettings? custom) => Normalize(name) switch
    {
        White => WhitePalette,
        Custom => FromCustom(custom),
        _ => BlackPalette,
    };
}
