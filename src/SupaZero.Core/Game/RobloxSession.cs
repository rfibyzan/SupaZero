using RIE.Core.Memory;
using RIE.Core.Offsets;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

/// <summary>Satu sesi attach READ-ONLY (diadaptasi dari RIE RobloxSession): memori + offsets + pembaca game.</summary>
public sealed class RobloxSession : IDisposable
{
    public ProcessMemory Memory { get; } = new();
    public OffsetsSnapshot? Offsets { get; private set; }
    public InstanceReader? Reader { get; private set; }
    public PropertyReader? Properties { get; private set; }
    public GameReader? Game { get; private set; }
    public DataModelRoots Roots { get; private set; } = DataModelRoots.Empty;
    public string Status { get; private set; } = "Belum attach.";

    public bool IsReady => Memory.IsAttached && Reader is not null && Roots.DataModel != 0;
    public int ProcessId => Memory.IsAttached ? Memory.ProcessId : 0;

    /// <summary>Lokasi offsets: settings → %AppData%\SupaZero\offsets.json → offsets RIE → folder exe.</summary>
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

    /// <summary>Attach bila belum, refresh DataModel bila berubah (teleport lobby ↔ stage). Murah dipanggil tiap tick.</summary>
    public bool EnsureReady()
    {
        if (Reader is null) { Status = "Offsets belum dimuat."; return false; }
        if (Memory.IsAttached && !IsProcessAlive()) { Memory.Detach(); Roots = DataModelRoots.Empty; }
        if (!Memory.IsAttached)
        {
            if (!Memory.TryAttach(out string st)) { Status = st; return false; }
        }
        if (Roots.DataModel == 0 || Reader.GetClassName(Roots.DataModel) != "DataModel" || Roots.PlayerGui == 0)
        {
            Roots = new DataModelLocator(Reader).Locate(Memory.MainModuleBase);
            if (Roots.DataModel == 0)
            {
                Status = $"Attached PID {Memory.ProcessId}, DataModel tidak ditemukan. Cek offsets cocok dengan {Memory.BuildId}.";
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

    public void Dispose() => Memory.Dispose();
}
