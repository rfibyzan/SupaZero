using System.Diagnostics;
using System.Runtime.InteropServices;
using SupaZero.Core.Automation;

namespace SupaZero.Core.Interop;

/// <summary>
/// Input via SendInput (same as SupaFisch). Client coordinates are converted to screen with ClientToScreen of the
/// RobloxPlayerBeta main window. Only sends input; never reads/writes memory.
/// </summary>
public sealed partial class Win32Input(Func<int> processId) : IInput
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000, MouseWheel = 0x0800, MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    private const int WheelDelta = 120;
    private const uint KeyUpFlag = 0x0002, ScanCodeFlag = 0x0008;
    private const int SmXVirtual = 76, SmYVirtual = 77, SmCxVirtual = 78, SmCyVirtual = 79;

    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [LibraryImport("user32.dll", SetLastError = true)] private static partial uint SendInput(uint count, ref Input inputs, int size);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClientToScreen(nint hWnd, ref Point p);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ScreenToClient(nint hWnd, ref Point p);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point p);
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetClientRect(nint hWnd, out Rect32 r);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] private static partial uint MapVirtualKeyW(uint code, uint mapType);

    public bool IsTargetForeground()
    {
        if (!OperatingSystem.IsWindows()) return false;
        int pid = processId();
        if (pid == 0) return false;
        nint fg = GetForegroundWindow();
        return fg != 0 && GetWindowThreadProcessId(fg, out uint fpid) != 0 && fpid == (uint)pid;
    }

    /// <summary>Hover delay before clicking (ms).</summary>
    public int HoverMs { get; set; } = 80;

    public void ClickClient(double x, double y)
    {
        nint h = MainWindow();
        if (h == 0) return;
        var p = new Point { X = (int)Math.Round(x), Y = (int)Math.Round(y) };
        if (!ClientToScreen(h, ref p)) return;
        // Roblox only fires hover (MouseEnter) after mouse movement OVER the button; jumping straight there and clicking
        // often doesn't register (Byzan, 23 Oct 2026: teleport cards only reacted after being hovered). So: jump
        // near the target → nudge in a few small steps onto the target → wait for hover → click.
        MoveAbsolute(p.X - 6, p.Y - 4);
        Thread.Sleep(30);
        for (int i = 1; i <= 3; i++)
        {
            MoveAbsolute(p.X - 6 + 6 * i / 3, p.Y - 4 + 4 * i / 3);
            Thread.Sleep(20);
        }
        MoveAbsolute(p.X + 1, p.Y);
        Thread.Sleep(20);
        MoveAbsolute(p.X, p.Y);
        Thread.Sleep(HoverMs);
        MouseButton(MouseLeftDown);
        Thread.Sleep(30);
        MouseButton(MouseLeftUp);
    }

    /// <summary>Client area of the Roblox window on screen (pixels).</summary>
    public (int X, int Y, int W, int H)? ClientScreenRect()
    {
        nint h = MainWindow();
        if (h == 0 || !GetClientRect(h, out var r)) return null;
        var p = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(h, ref p)) return null;
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        return w > 0 && ht > 0 ? (p.X, p.Y, w, ht) : null;
    }

    public (int W, int H)? ClientSize()
    {
        nint h = MainWindow();
        if (h == 0 || !GetClientRect(h, out var r)) return null;
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        return w > 0 && ht > 0 ? (w, ht) : null;
    }

    public void MoveClient(double x, double y)
    {
        nint h = MainWindow();
        if (h == 0) return;
        var p = new Point { X = (int)Math.Round(x), Y = (int)Math.Round(y) };
        if (!ClientToScreen(h, ref p)) return;
        MoveAbsolute(p.X, p.Y);
    }

    public void ScrollClient(double x, double y, int notches)
    {
        nint h = MainWindow();
        if (h == 0 || notches == 0) return;
        var p = new Point { X = (int)Math.Round(x), Y = (int)Math.Round(y) };
        if (!ClientToScreen(h, ref p)) return;
        // Roblox scrolls the ScrollingFrame under the cursor → the cursor must be over the list first (with hover).
        MoveAbsolute(p.X - 4, p.Y);
        Thread.Sleep(30);
        MoveAbsolute(p.X, p.Y);
        Thread.Sleep(HoverMs);
        int step = Math.Sign(notches);
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            var w = new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = MouseWheel, MouseData = unchecked((uint)(step * WheelDelta)) } } };
            SendInput(1, ref w, Marshal.SizeOf<Input>());
            Thread.Sleep(40);
        }
    }

    public void RightDown() => MouseButton(MouseRightDown);
    public void RightUp() => MouseButton(MouseRightUp);

    public void ClickHere()
    {
        MouseButton(MouseLeftDown);
        Thread.Sleep(20);
        MouseButton(MouseLeftUp);
    }

    public void KeyDown(ushort vk) => Key(vk, false);
    public void KeyUp(ushort vk) => Key(vk, true);

    private nint MainWindow()
    {
        try
        {
            using var p = Process.GetProcessById(processId());
            return p.MainWindowHandle;
        }
        catch { return 0; }
    }

    public (double X, double Y)? CursorClient()
    {
        nint h = MainWindow();
        if (h == 0 || !GetCursorPos(out var p) || !ScreenToClient(h, ref p)) return null;
        return (p.X, p.Y);
    }

    public void MoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var i = new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = MouseMove } } };
        SendInput(1, ref i, Marshal.SizeOf<Input>());
    }

    private static void MoveAbsolute(int x, int y)
    {
        int vx = GetSystemMetrics(SmXVirtual), vy = GetSystemMetrics(SmYVirtual);
        int vw = Math.Max(1, GetSystemMetrics(SmCxVirtual)), vh = Math.Max(1, GetSystemMetrics(SmCyVirtual));
        var i = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = (int)((x - vx) * 65535.0 / (vw - 1)),
                    Dy = (int)((y - vy) * 65535.0 / (vh - 1)),
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk,
                },
            },
        };
        SendInput(1, ref i, Marshal.SizeOf<Input>());
    }

    private static void MouseButton(uint flag)
    {
        var i = new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = flag } } };
        SendInput(1, ref i, Marshal.SizeOf<Input>());
    }

    private static void Key(ushort vk, bool up)
    {
        // Roblox reads scancodes (DirectInput), so send scancode + VK.
        var i = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    Vk = vk,
                    Scan = (ushort)MapVirtualKeyW(vk, 0),
                    Flags = ScanCodeFlag | (up ? KeyUpFlag : 0),
                },
            },
        };
        SendInput(1, ref i, Marshal.SizeOf<Input>());
    }
}
