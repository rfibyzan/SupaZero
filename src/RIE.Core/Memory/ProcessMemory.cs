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

    /// <summary>All running processes with this name, oldest first (PID + start time).</summary>
    public static IReadOnlyList<(int Pid, DateTime Start)> List(string processName = DefaultProcessName)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var all = Process.GetProcessesByName(processName);
        try { return all.Select(p => (p.Id, SafeStart(p))).OrderBy(x => x.Item2).ToList(); }
        finally { foreach (var p in all) p.Dispose(); }
    }

    /// <summary>Attach to exactly this PID (no fallback to another process).</summary>
    public bool TryAttach(out string status, int pid, string processName = DefaultProcessName)
    {
        if (!OperatingSystem.IsWindows()) { status = "Windows only."; return false; }
        Process p;
        try { p = Process.GetProcessById(pid); }
        catch { status = $"PID {pid} is not running."; return false; }
        bool nameOk;
        try { nameOk = string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase); } catch { nameOk = false; }
        if (!nameOk) { p.Dispose(); status = $"PID {pid} is not {processName}."; return false; }
        return AttachTo(p, out status);
    }

    public bool TryAttach(out string status, string processName = DefaultProcessName)
    {
        if (!OperatingSystem.IsWindows()) { status = "Windows only."; return false; }
        Process[] all = Process.GetProcessesByName(processName);
        Process? newest = all.OrderByDescending(SafeStart).FirstOrDefault();
        foreach (var p in all) if (!ReferenceEquals(p, newest)) p.Dispose();
        if (newest is null) { status = $"{processName} is not running."; return false; }
        return AttachTo(newest, out status);
    }

    private bool AttachTo(Process newest, out string status)
    {
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
