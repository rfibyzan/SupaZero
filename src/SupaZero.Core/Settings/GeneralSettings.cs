namespace SupaZero.Core.Settings;

/// <summary>Settings → Appearance → Custom colours (same as SupaFisch).</summary>
public sealed record CustomThemeSettings(
    string Background = "#0A0A0A",
    string Surface = "#141414",
    string Border = "#262626",
    string Accent = "#3B82F6",
    string Text = "#EDEDED");

public sealed record GeneralSettings(
    string MacroHotkey = "F1",
    string Theme = "Black",
    bool TransparentUi = false,
    /// <summary>Macro loop interval (ms). Each tick: read memory snapshot → decide action → send input.</summary>
    int TickMs = 100,
    /// <summary>Roblox topbar height for AbsolutePosition → client conversion (same as RIE calibration; 58 default).</summary>
    double GuiInsetY = 58,
    /// <summary>GUI Y inset of the lobby hub (Areas button, teleport cards). Verified 0.</summary>
    double LobbyInsetY = 0,
    double GuiScale = 1,
    /// <summary>Status overlay on top of the Roblox window while the macro runs.</summary>
    bool ShowOverlay = true,
    /// <summary>Relative overlay position (0..1) in the Roblox window: 0 = stuck to left/top, 1 = right/bottom (see OverlayPlacement).</summary>
    double OverlayX = 1,
    double OverlayY = 0);
