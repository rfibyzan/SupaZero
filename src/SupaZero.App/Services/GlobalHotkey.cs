using System.Runtime.InteropServices;
using Avalonia.Controls;
using SupaZero.Core.Input;

namespace SupaZero.App.Services;

/// <summary>Hotkey global (RegisterHotKey) terikat HWND jendela utama, sama seperti SupaFisch.</summary>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x535A; // "SZ"
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    private readonly Window _window;
    private readonly Action _pressed;
    private readonly Win32Properties.CustomWndProcHookCallback? _hook;
    private nint _hwnd;
    private bool _registered;

    public GlobalHotkey(Window window, Action pressed)
    {
        _window = window;
        _pressed = pressed;
        if (OperatingSystem.IsWindows())
        {
            _hook = WndProc;
            Win32Properties.AddWndProcHookCallback(window, _hook);
        }
    }

    public string? LastError { get; private set; }

    public bool Register(string? keyName)
    {
        Unregister();
        LastError = null;
        if (!OperatingSystem.IsWindows()) { LastError = "Hotkey global hanya tersedia di Windows."; return false; }
        if (!HotkeyMap.TryGetVirtualKey(keyName, out uint vk)) { LastError = $"Tombol '{keyName}' tidak didukung."; return false; }
        _hwnd = _window.TryGetPlatformHandle()?.Handle ?? 0;
        if (_hwnd == 0) { LastError = "Handle jendela belum tersedia."; return false; }
        _registered = RegisterHotKey(_hwnd, HotkeyId, ModNoRepeat, vk);
        if (!_registered) LastError = $"RegisterHotKey gagal untuk '{keyName}' (dipakai aplikasi lain?).";
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && OperatingSystem.IsWindows()) UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            try { _pressed(); } catch (Exception ex) { AppLog.Line("[ERROR] hotkey: " + ex.Message); }
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        if (_hook is not null && OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(_window, _hook);
    }
}
