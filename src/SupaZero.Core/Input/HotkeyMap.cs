namespace SupaZero.Core.Input;

/// <summary>
/// Pemetaan nama tombol hotkey (disimpan di GeneralSettings.MacroHotkey) → virtual-key Win32 untuk RegisterHotKey.
/// Didukung: F1–F24, A–Z, 0–9, Insert/Delete/Home/End/PageUp/PageDown, Pause, ScrollLock, NumPad0–NumPad9,
/// LeftAlt/RightAlt/LeftCtrl/RightCtrl/LeftShift/RightShift.
/// Nama dibandingkan tanpa peduli huruf besar/kecil. Tidak memakai refleksi (aman NativeAOT).
/// </summary>
public static class HotkeyMap
{
    public const string DefaultKey = "F1";

    public static bool TryGetVirtualKey(string? name, out uint vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        string n = name.Trim().ToUpperInvariant();
        if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1); // VK_F1 = 0x70
            return true;
        }
        if (n.Length == 1 && n[0] is >= 'A' and <= 'Z') { vk = n[0]; return true; }
        if (n.Length == 1 && n[0] is >= '0' and <= '9') { vk = n[0]; return true; }
        if (n.StartsWith("NUMPAD", StringComparison.Ordinal) && n.Length == 7 && n[6] is >= '0' and <= '9')
        {
            vk = (uint)(0x60 + (n[6] - '0')); // VK_NUMPAD0 = 0x60
            return true;
        }
        vk = n switch
        {
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "PAUSE" => 0x13,
            "SCROLLLOCK" or "SCROLL" => 0x91,
            // Tombol modifier kiri/kanan (mis. "LeftAlt" seperti di UI referensi). [S] RegisterHotKey untuk
            // modifier tunggal belum diverifikasi di Windows.
            "LEFTALT" => 0xA4,
            "RIGHTALT" => 0xA5,
            "LEFTCTRL" => 0xA2,
            "RIGHTCTRL" => 0xA3,
            "LEFTSHIFT" => 0xA0,
            "RIGHTSHIFT" => 0xA1,
            _ => 0,
        };
        return vk != 0;
    }

    /// <summary>Nama kanonik untuk ditampilkan/disimpan; nama tak dikenal → null.</summary>
    public static string? Normalize(string? name)
    {
        if (!TryGetVirtualKey(name, out _))
            return null;
        string n = name!.Trim().ToUpperInvariant();
        return n switch
        {
            "INSERT" => "Insert",
            "DELETE" => "Delete",
            "HOME" => "Home",
            "END" => "End",
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            "PAUSE" => "Pause",
            "SCROLLLOCK" or "SCROLL" => "ScrollLock",
            "LEFTALT" => "LeftAlt",
            "RIGHTALT" => "RightAlt",
            "LEFTCTRL" => "LeftCtrl",
            "RIGHTCTRL" => "RightCtrl",
            "LEFTSHIFT" => "LeftShift",
            "RIGHTSHIFT" => "RightShift",
            _ when n.StartsWith("NUMPAD", StringComparison.Ordinal) => "NumPad" + n[6],
            _ => n,
        };
    }
}
