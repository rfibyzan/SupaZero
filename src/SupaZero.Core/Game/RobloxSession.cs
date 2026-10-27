using RIE.Core.Memory;
using RIE.Core.Offsets;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

/// <summary>One READ-ONLY attach session (adapted from RIE RobloxSession): memory + offsets + game reader.</summary>
public sealed class RobloxSession : IDisposable
{
    /// <summary>v0.3.25: 0 = newest RobloxPlayerBeta (old behavior); otherwise ONLY this PID (closed = not attached, never another process).</summary>
    public int PinnedPid { get; private set; }

    public void Pin(int pid)
    {
        if (pid == PinnedPid) return;
        PinnedPid = pid;
        Memory.Detach();
        Roots = DataModelRoots.Empty;
    }

    public ProcessMemory Memory { get; } = new();
    public OffsetsSnapshot? Offsets { get; private set; }
    public InstanceReader? Reader { get; private set; }
    public PropertyReader? Properties { get; private set; }
    public GameReader? Game { get; private set; }
    public DataModelRoots Roots { get; private set; } = DataModelRoots.Empty;
    public string Status { get; private set; } = "Not attached.";

    public bool IsReady => Memory.IsAttached && Reader is not null && Roots.DataModel != 0;
    public int ProcessId => Memory.IsAttached ? Memory.ProcessId : 0;

    /// <summary>Offsets location: settings → %AppData%\SupaZero\offsets.json → RIE offsets → exe folder.</summary>
    public static string? FindOffsets(string? preferred)
    {
        string[] candidates =
        [
            preferred ?? string.Empty,
            Path.Combine(Settings.AppSettings.DataDirectory, "offsets.json"),
            OffsetsSnapshot.UserOffsetsPath,
            Path.Combine(AppContext.BaseDirectory, "offsets.json"),
        ];
        return candidates.FirstOrDefault(p => p.Length > 0 && File.Exists(p));
    }

    public void LoadOffsets(string path)
    {
        Offsets = OffsetsSnapshot.Load(path);
        Reader = new InstanceReader(Memory, Offsets);
        Properties = new PropertyReader(Reader);
        Game = new GameReader(Reader, Properties);
    }

    /// <summary>Attach if not yet attached, refresh DataModel when it changes (lobby ↔ stage teleport). Cheap to call every tick.</summary>
    public bool EnsureReady()
    {
        if (Reader is null) { Status = "Offsets not loaded."; return false; }
        if (Memory.IsAttached && !IsProcessAlive()) { Memory.Detach(); Roots = DataModelRoots.Empty; }
        if (!Memory.IsAttached)
        {
            string st;
            bool ok = PinnedPid != 0 ? Memory.TryAttach(out st, PinnedPid) : Memory.TryAttach(out st);
            if (!ok) { Status = PinnedPid != 0 ? $"{st} (chosen window closed → not attaching to another one)" : st; return false; }
        }
        if (Roots.DataModel == 0 || Reader.GetClassName(Roots.DataModel) != "DataModel" || Roots.PlayerGui == 0)
        {
            Roots = new DataModelLocator(Reader).Locate(Memory.MainModuleBase);
            if (Roots.DataModel == 0)
            {
                Status = $"Attached PID {Memory.ProcessId}, DataModel not found. Check that the offsets match {Memory.BuildId}.";
                return false;
            }
        }
        Status = $"Attached PID {Memory.ProcessId} ({Memory.BuildId}).";
        return true;
    }

    public GameSnapshot ReadSnapshot() => EnsureReady() && Game is not null ? Game.Read(Roots) with { Equipped = Game.ReadPowers(Roots), ActivePower = Game.ActivePower } : new GameSnapshot();

    private bool IsProcessAlive()
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(Memory.ProcessId);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>v0.3.25: share already-loaded offsets with another session (one per carry window).</summary>
    public void UseOffsets(OffsetsSnapshot offsets)
    {
        Offsets = offsets;
        Reader = new InstanceReader(Memory, offsets);
        Properties = new PropertyReader(Reader);
        Game = new GameReader(Reader, Properties);
    }

    /// <summary>v0.3.25: running RobloxPlayerBeta processes with the LocalPlayer name read (read-only) when the offsets allow it.</summary>
    public static List<RobloxProcessInfo> ListProcesses(OffsetsSnapshot? offsets)
    {
        var list = new List<RobloxProcessInfo>();
        foreach (var (pid, start) in ProcessMemory.List())
        {
            string? player = null;
            if (offsets is not null)
            {
                try
                {
                    using var mem = new ProcessMemory();
                    if (mem.TryAttach(out _, pid))
                    {
                        var r = new InstanceReader(mem, offsets);
                        var roots = new DataModelLocator(r).Locate(mem.MainModuleBase);
                        if (roots.LocalPlayer != 0) player = r.GetName(roots.LocalPlayer);
                    }
                }
                catch { player = null; }
            }
            list.Add(new RobloxProcessInfo(pid, start, string.IsNullOrWhiteSpace(player) ? null : player));
        }
        return list;
    }

    public void Dispose() => Memory.Dispose();
}

public sealed record RobloxProcessInfo(int Pid, DateTime Start, string? Player)
{
    public string Label => $"{Player ?? "?"} · PID {Pid} · {(Start == DateTime.MinValue ? "?" : Start.ToString("HH:mm:ss"))}";
}
