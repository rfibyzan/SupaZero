using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace SupaZero.App;

/// <summary>Jendela overlay: topmost, tanpa fokus, tembus klik (WS_EX_TRANSPARENT), diposisikan di pojok jendela Roblox.</summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x8000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint h, int i, nint v);

    public OverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } h || h == 0) return;
            long ex = GetWindowLongPtr(h, GwlExStyle);
            SetWindowLongPtr(h, GwlExStyle, (nint)(ex | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate));
        };
    }

    /// <summary>Tempel ke pojok area klien Roblox (piksel layar). corner: 0 kiri atas, 1 kanan atas, 2 kiri bawah, 3 kanan bawah.</summary>
    public void PlaceAt((int X, int Y, int W, int H) client, int corner)
    {
        double scale = RenderScaling > 0 ? RenderScaling : 1;
        int w = (int)Math.Round(Bounds.Width * scale), h = (int)Math.Round(Bounds.Height * scale);
        int margin = (int)(12 * scale), top = (int)(64 * scale); // di bawah topbar Roblox
        int x = corner is 1 or 3 ? client.X + client.W - w - margin : client.X + margin;
        int y = corner is 2 or 3 ? client.Y + client.H - h - margin : client.Y + top;
        var p = new PixelPoint(x, y);
        if (Position != p) Position = p;
    }
}
