namespace RIE.Core.Memory;

/// <summary>
/// Cek versi Roblox secara pasif (hanya membaca path/folder; TIDAK menjalankan atau menyentuh Roblox).
/// Prioritas: (1) versi Roblox yang SEDANG berjalan, dari path RobloxPlayerBeta.exe proses (mendukung launcher
/// seperti Bloxstrap/Fishstrap dengan folder Versions di lokasi lain); (2) fallback folder terbaru
/// %LocalAppData%\Roblox\Versions\version-* yang berisi RobloxPlayerBeta.exe. Dipakai tab Offsets.
/// </summary>
public static class RobloxInstallProbe
{
    public const string PlayerExe = "RobloxPlayerBeta.exe";

    public static string DefaultVersionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");

    /// <summary>Nama folder versi terbaru (mis. "version-9ac8595712b447a7") atau null bila tidak ada.</summary>
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
    /// Nama folder versi dari path exe, mis. "D:\Games\Fishstrap\Versions\version-9ac8595712b447a7\RobloxPlayerBeta.exe"
    /// → "version-9ac8595712b447a7". Null bila folder induk bukan "version-*".
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

    /// <summary>Versi Roblox yang sedang berjalan (path modul utama RobloxPlayerBeta), atau null bila tidak ada/tak terbaca.</summary>
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

    /// <summary>QueryFullProcessImageName dengan akses query terbatas (hanya Windows; selain itu null).</summary>
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

    /// <summary>Versi untuk dibandingkan: yang sedang berjalan bila ada (isRunning=true), selain itu folder terbaru.</summary>
    public static string? ResolveVersion(out bool isRunning, string? versionsDirectory = null)
    {
        string? running = FindRunningVersion();
        isRunning = running is not null;
        return running ?? FindLatestVersion(versionsDirectory);
    }

    /// <summary>
    /// true bila versi di offsets.json sama dengan folder terpasang (OrdinalIgnoreCase, spasi dipangkas).
    /// Salah satu kosong/null → dianggap tidak bisa dibandingkan → true (tidak memunculkan peringatan palsu).
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
