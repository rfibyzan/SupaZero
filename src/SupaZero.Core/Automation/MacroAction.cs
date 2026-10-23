namespace SupaZero.Core.Automation;

/// <summary>Aksi input yang diputuskan <see cref="StoryMachine"/>; dieksekusi <see cref="MacroRunner"/> lewat <see cref="IInput"/>.</summary>
public abstract record MacroAction(string Reason);

/// <summary>Klik kiri di koordinat KLIEN jendela Roblox.</summary>
public sealed record ClickAction(double X, double Y, string Reason) : MacroAction(Reason);

/// <summary>Scroll roda mouse di koordinat KLIEN (kursor digeser ke sana dulu). Notches &gt; 0 = ke atas, &lt; 0 = ke bawah.</summary>
public sealed record ScrollAction(double X, double Y, int Notches, string Reason) : MacroAction(Reason);

/// <summary>Klik kiri di posisi kursor sekarang (serangan dasar).</summary>
public sealed record AttackAction(string Reason) : MacroAction(Reason);

/// <summary>Tekan tombol (virtual-key) selama <paramref name="HoldMs"/>.</summary>
public sealed record KeyAction(ushort VirtualKey, int HoldMs, string Reason) : MacroAction(Reason);

/// <summary>Tahan tepat tombol gerak ini (W/A/S/D); tombol gerak lain dilepas. Kosong = lepas semua.</summary>
public sealed record MoveAction(IReadOnlyList<ushort> Keys, string Reason) : MacroAction(Reason);

/// <summary>Tidak ada input; hanya catatan log (mis. menunggu pemain, fitur belum ada).</summary>
public sealed record NoteAction(string Reason) : MacroAction(Reason);

/// <summary>Hentikan macro (Story selesai, retry habis, dsb.).</summary>
public sealed record StopAction(string Reason) : MacroAction(Reason);

public static class VirtualKeys
{
    public const ushort W = 0x57, A = 0x41, S = 0x53, D = 0x44, Q = 0x51, T = 0x54, Space = 0x20;

    /// <summary>"1" → 0x31, "Q" → 0x51. Hanya huruf/angka tunggal.</summary>
    public static bool TryParse(string? key, out ushort vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        string k = key.Trim().ToUpperInvariant();
        if (k.Length == 1 && (char.IsAsciiLetterUpper(k[0]) || char.IsAsciiDigit(k[0]))) { vk = k[0]; return true; }
        return false;
    }
}
