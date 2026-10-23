using System.Diagnostics;
using System.Runtime.InteropServices;
using SupaZero.Core.Automation;

namespace SupaZero.Core.Interop;

/// <summary>
/// Input lewat SendInput (sama seperti SupaFisch). Koordinat klien dikonversi ke layar dengan ClientToScreen jendela
/// utama RobloxPlayerBeta. Hanya mengirim input; tidak membaca/menulis memori.
/// </summary>
public sealed partial class Win32Input(Func<int> processId) : IInput
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000;
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

    public void ClickClient(double x, double y)
    {
        nint h = MainWindow();
        if (h == 0) return;
        var p = new Point { X = (int)Math.Round(x), Y = (int)Math.Round(y) };
        if (!ClientToScreen(h, ref p)) return;
        MoveAbsolute(p.X, p.Y);
        Thread.Sleep(25); // Roblox perlu satu frame untuk hover sebelum klik
        MouseButton(MouseLeftDown);
        Thread.Sleep(30);
        MouseButton(MouseLeftUp);
    }

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
        // Roblox membaca scancode (DirectInput), jadi kirim scancode + VK.
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
