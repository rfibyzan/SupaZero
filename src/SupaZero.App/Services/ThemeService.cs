using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SupaZero.Core.Settings;

namespace SupaZero.App.Services;

/// <summary>
/// Applies a <see cref="ThemePalette"/> to the app resources (Of* keys + Fluent brush overrides). All views use
/// DynamicResource, so theme changes show immediately without a restart.
/// </summary>
public static class ThemeService
{
    public static ThemePalette Current { get; private set; } = ThemePalettes.BlackPalette;

    /// <summary>Raised after the palette changes (e.g. the main window refreshes its Transparent UI background).</summary>
    public static event Action<ThemePalette>? Changed;

    public static void Apply(AppSettings settings) =>
        Apply(ThemePalettes.Resolve(settings.General.Theme, settings.CustomTheme));

    public static void Apply(ThemePalette p)
    {
        var app = Application.Current;
        if (app is null) return;
        var r = app.Resources;

        void Set(string key, ThemeRgb c) => r[key] = new SolidColorBrush(ToColor(c));

        Set("OfBg", p.Background);
        Set("OfCard", p.Surface);
        Set("OfCardInner", p.SurfaceInner);
        Set("OfBorder", p.Border);
        Set("OfAccent", p.Accent);
        Set("OfAccentDim", p.AccentDim);
        Set("OfAccentPressed", p.AccentPressed);
        Set("OfAccentHover", p.AccentHover);
        Set("OfOnAccent", p.OnAccent);
        Set("OfButtonBg", p.ButtonBg);
        Set("OfButtonHover", p.ButtonHover);
        Set("OfButtonPressed", p.ButtonPressed);
        Set("OfButtonDisabled", p.ButtonDisabled);
        Set("OfOnButton", p.OnButton);
        Set("OfText", p.Text);
        Set("OfMuted", p.Muted);
        Set("OfStrip", p.Strip);
        Set("OfHover", p.Hover);
        Set("OfInset", p.Background.Mix(p.Surface, 0.5));
        r["OfOverlayBg"] = new SolidColorBrush(ToColor(p.Background, 0xCC));

        // Fluent brush overrides (looked up by Fluent templates via DynamicResource).
        Set("ToggleSwitchFillOn", p.Accent);
        Set("ToggleSwitchFillOnPointerOver", p.AccentHover);
        Set("ToggleSwitchFillOnPressed", p.AccentPressed);
        Set("ToggleSwitchKnobFillOn", p.OnAccent);
        Set("ToggleSwitchKnobFillOnPointerOver", p.OnAccent);
        Set("ToggleSwitchKnobFillOnPressed", p.OnAccent);
        Set("CheckBoxCheckBackgroundFillChecked", p.Accent);
        Set("CheckBoxCheckBackgroundFillCheckedPointerOver", p.AccentHover);
        Set("CheckBoxCheckBackgroundStrokeChecked", p.Accent);
        Set("CheckBoxCheckGlyphForegroundChecked", p.OnAccent);
        Set("RadioButtonOuterEllipseCheckedFill", p.Accent);
        Set("RadioButtonOuterEllipseCheckedStroke", p.Accent);
        Set("SliderThumbBackground", p.Accent);
        Set("SliderTrackValueFill", p.Accent);
        Set("ProgressBarForeground", p.Accent);
        Set("TextControlBorderBrushFocused", p.Accent);
        Set("SystemControlHighlightAccentBrush", p.Accent);

        app.RequestedThemeVariant = p.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        Current = p;
        Changed?.Invoke(p);
    }

    public static Color ToColor(ThemeRgb c, byte alpha = 0xFF) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
