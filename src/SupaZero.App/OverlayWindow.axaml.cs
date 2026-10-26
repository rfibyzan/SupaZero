using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace SupaZero.App;

/// <summary>Overlay window: topmost, non-focusable, click-through (WS_EX_TRANSPARENT), positioned at a corner of the Roblox window.</summary>
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

    /// <summary>Place within the Roblox client area (screen pixels) at the relative 0..1 position (Settings → Overlay).</summary>
    public void PlaceAt((int X, int Y, int W, int H) client, double relX, double relY)
    {
        double scale = RenderScaling > 0 ? RenderScaling : 1;
        int w = (int)Math.Round(Bounds.Width * scale), h = (int)Math.Round(Bounds.Height * scale);
        var (x, y) = Core.Settings.OverlayPlacement.ToScreen(client, relX, relY, w, h, scale);
        var p = new PixelPoint(x, y);
        if (Position != p) Position = p;
    }
}
