using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RIE.Core.Memory;

/// <summary>Attaches read-only to the process (default RobloxPlayerBeta, newest process) and reads its memory.</summary>
public sealed class ProcessMemory : IMemorySource, IDisposable
{
    public const string DefaultProcessName = "RobloxPlayerBeta";

    private nint _handle;

    public bool IsAttached => _handle != 0;
    public int ProcessId { get; private set; }
    public long MainModuleBase { get; private set; }
    public string MainModulePath { get; private set; } = string.Empty;

    /// <summary>Roblox version folder ("version-xxxx") from the exe path, empty if unknown.</summary>
    public string BuildId
    {
        get
        {
            if (string.IsNullOrEmpty(MainModulePath)) return string.Empty;
            string dir = new DirectoryInfo(Path.GetDirectoryName(MainModulePath)!).Name;
            return dir.StartsWith("version-", StringComparison.OrdinalIgnoreCase) ? dir : string.Empty;
        }
    }

    public bool TryAttach(out string status, string processName = DefaultProcessName)
    {
        if (!OperatingSystem.IsWindows()) { status = "Windows only."; return false; }
        Process[] all = Process.GetProcessesByName(processName);
        Process? newest = all.OrderByDescending(SafeStart).FirstOrDefault();
        foreach (var p in all) if (!ReferenceEquals(p, newest)) p.Dispose();
        if (newest is null) { status = $"{processName} is not running."; return false; }
        using (newest)
        {
            if (_handle != 0 && ProcessId == newest.Id) { status = "Already attached."; return true; }
            Detach();
            nint h = NativeMethods.OpenProcess(NativeMethods.ReadOnlyAccess, false, (uint)newest.Id);
            if (h == 0)
            {
                status = "OpenProcess failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            _handle = h;
            ProcessId = newest.Id;
            try
            {
                MainModuleBase = newest.MainModule?.BaseAddress ?? 0;
                MainModulePath = newest.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                MainModuleBase = 0;
                MainModulePath = string.Empty;
            }
            status = MainModuleBase != 0 ? $"Attached PID {ProcessId}." : $"Attached PID {ProcessId}, but the module base could not be read.";
            return MainModuleBase != 0;
        }
    }

    public unsafe bool TryRead(long address, Span<byte> buffer)
    {
        if (_handle == 0 || buffer.Length == 0) return false;
        fixed (byte* p = buffer)
        {
            return NativeMethods.ReadProcessMemory(_handle, (nint)address, p, buffer.Length, out nint read)
                   && read == buffer.Length;
        }
    }

    public void Detach()
    {
        if (_handle != 0) NativeMethods.CloseHandle(_handle);
        _handle = 0;
        ProcessId = 0;
        MainModuleBase = 0;
        MainModulePath = string.Empty;
    }

    public void Dispose() => Detach();

    private static DateTime SafeStart(Process p)
    {
        try { return p.StartTime; } catch { return DateTime.MinValue; }
    }
}
