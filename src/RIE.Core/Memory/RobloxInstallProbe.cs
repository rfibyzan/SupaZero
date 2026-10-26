namespace RIE.Core.Memory;

/// <summary>
/// Passively checks the Roblox version (only reads paths/folders; does NOT launch or touch Roblox).
/// Priority: (1) the CURRENTLY running Roblox version, from the process RobloxPlayerBeta.exe path (supports launchers
/// like Bloxstrap/Fishstrap with the Versions folder elsewhere); (2) fallback to the newest folder
/// %LocalAppData%\Roblox\Versions\version-* that contains RobloxPlayerBeta.exe. Used by the Offsets tab.
/// </summary>
public static class RobloxInstallProbe
{
    public const string PlayerExe = "RobloxPlayerBeta.exe";

    public static string DefaultVersionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");

    /// <summary>Newest version folder name (e.g. "version-9ac8595712b447a7") or null if none.</summary>
    public static string? FindLatestVersion(string? versionsDirectory = null)
    {
        string dir = versionsDirectory ?? DefaultVersionsDirectory;
        try
        {
            if (!Directory.Exists(dir))
                return null;
            return new DirectoryInfo(dir).EnumerateDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, PlayerExe)))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .Select(d => d.Name)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Version folder name from the exe path, e.g. "D:\Games\Fishstrap\Versions\version-9ac8595712b447a7\RobloxPlayerBeta.exe"
    /// → "version-9ac8595712b447a7". Null if the parent folder is not "version-*".
    /// </summary>
    public static string? VersionFromExePath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;
        string normalized = exePath.Trim().Replace('/', '\\');
        int cut = normalized.LastIndexOf('\\');
        if (cut <= 0)
            return null;
        string dir = normalized[..cut];
        string name = dir[(dir.LastIndexOf('\\') + 1)..];
        return name.StartsWith("version-", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    /// <summary>The running Roblox version (RobloxPlayerBeta main module path), or null if not running/unreadable.</summary>
    public static string? FindRunningVersion()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PlayerExe));
        try
        {
            foreach (var p in procs)
            {
                try
                {
                    string? v = VersionFromExePath(ImagePath(p.Id)) ?? VersionFromExePath(p.MainModule?.FileName);
                    if (v is not null)
                        return v;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                               or NotSupportedException or UnauthorizedAccessException)
                {
                }
            }
            return null;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    /// <summary>QueryFullProcessImageName with limited query access (Windows only; otherwise null).</summary>
    private static string? ImagePath(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        nint h = OpenProcess(
            0x1000u, false, (uint)pid);
        if (h == 0)
            return null;
        try
        {
            var buf = new ushort[1024];
            uint size = (uint)buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref size)
                ? new string(Array.ConvertAll(buf[..(int)size], c => (char)c))
                : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Version to compare: the running one if any (isRunning=true), otherwise the newest folder.</summary>
    public static string? ResolveVersion(out bool isRunning, string? versionsDirectory = null)
    {
        string? running = FindRunningVersion();
        isRunning = running is not null;
        return running ?? FindLatestVersion(versionsDirectory);
    }

    /// <summary>
    /// true if the version in offsets.json matches the installed folder (OrdinalIgnoreCase, whitespace trimmed).
    /// Either empty/null → treated as not comparable → true (no false warning).
    /// </summary>
    public static bool VersionMatches(string? offsetsVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(offsetsVersion) || string.IsNullOrWhiteSpace(installedVersion))
            return true;
        return string.Equals(offsetsVersion.Trim(), installedVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint h, uint flags, ushort[] buf, ref uint size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint h);
}
