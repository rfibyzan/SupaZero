using System.Runtime.InteropServices;

namespace RIE.Core.Memory;

/// <summary>
/// P/Invoke minimal. Sengaja HANYA OpenProcess (VM_READ | QUERY_LIMITED_INFORMATION), ReadProcessMemory, CloseHandle.
/// Jangan menambah WriteProcessMemory / VirtualAllocEx / CreateRemoteThread.
/// </summary>
internal static partial class NativeMethods
{
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint ReadOnlyAccess = PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadProcessMemory(nint process, nint address, byte* buffer, nint size, out nint read);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
