namespace SupaZero.Core.Automation;

/// <summary>Kirim input ke Roblox. Implementasi nyata = SendInput (Win32Input); tes memakai rekaman palsu.</summary>
public interface IInput
{
    /// <summary>True bila jendela Roblox sedang di depan (input hanya dikirim saat ini true).</summary>
    bool IsTargetForeground();

    /// <summary>Klik kiri di koordinat klien jendela Roblox.</summary>
    void ClickClient(double x, double y);

    /// <summary>Geser kursor ke koordinat klien lalu putar roda mouse. Notches &gt; 0 = ke atas.</summary>
    void ScrollClient(double x, double y, int notches);

    /// <summary>Ukuran area klien jendela Roblox (piksel), null bila tidak diketahui.</summary>
    (int W, int H)? ClientSize();

    /// <summary>Geser kursor ke koordinat klien (tanpa klik).</summary>
    void MoveClient(double x, double y);

    /// <summary>Klik kiri di posisi kursor sekarang.</summary>
    void ClickHere();

    void KeyDown(ushort vk);
    void KeyUp(ushort vk);
}
