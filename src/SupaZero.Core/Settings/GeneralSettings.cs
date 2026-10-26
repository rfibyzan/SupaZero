namespace SupaZero.Core.Settings;

/// <summary>Settings → Appearance → Custom colours (sama dengan SupaFisch).</summary>
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
    /// <summary>Jeda loop macro (ms). Tiap tick: baca snapshot memori → putuskan aksi → kirim input.</summary>
    int TickMs = 100,
    /// <summary>Tinggi topbar Roblox untuk konversi AbsolutePosition → klien (sama dengan kalibrasi RIE; 58 default).</summary>
    double GuiInsetY = 58,
    /// <summary>Inset Y GUI hub lobby (tombol Areas, kartu teleport). Terverifikasi 0.</summary>
    double LobbyInsetY = 0,
    double GuiScale = 1,
    /// <summary>Overlay status di atas jendela Roblox selama macro berjalan.</summary>
    bool ShowOverlay = true,
    /// <summary>Posisi overlay relatif (0..1) di jendela Roblox: 0 = menempel kiri/atas, 1 = kanan/bawah (lihat OverlayPlacement).</summary>
    double OverlayX = 1,
    double OverlayY = 0);
