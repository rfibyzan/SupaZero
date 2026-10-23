using System.Globalization;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace RIE.Core.Inspect;

public sealed record PlayerRow(long Address, string Name, string DisplayName, string Team, string Health, double Distance, bool IsLocal, long Character)
{
    public string DistanceText => IsLocal ? "kamu" : double.IsNaN(Distance) ? "-" : $"{Distance:0.0}";
}

public sealed record ToolRow(long Address, string Name, string Where, string Info);

/// <summary>Daftar pemain (nama, tim, HP, jarak) dan isi Backpack/Tool dipegang. Hanya membaca.</summary>
public sealed class PlayerList(InstanceReader reader, PropertyReader props)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public long GetCharacter(long player) =>
        reader.Offsets.TryResolve("Player.ModelInstance", out long mo) && reader.Memory.TryReadPointer(player + mo, out long c) && InstanceReader.IsValid(c) ? c : 0;

    public List<PlayerRow> Read(DataModelRoots roots)
    {
        var rows = new List<PlayerRow>();
        if (roots.Players == 0) return rows;
        var finder = new NearbyFinder(reader, props);
        bool hasMe = finder.TryGetLocalPosition(roots, out float mx, out float my, out float mz);
        foreach (long p in reader.GetChildren(roots.Players))
        {
            if (reader.GetClassName(p) != "Player") continue;
            var pp = props.Read(p, "Player");
            string team = string.Empty;
            if (reader.Offsets.TryResolve("Player.Team", out long to) && reader.Memory.TryReadPointer(p + to, out long tm) && InstanceReader.IsValid(tm))
                team = reader.GetName(tm);
            long ch = GetCharacter(p);
            string hp = "-";
            double dist = double.NaN;
            if (ch != 0)
            {
                long hum = reader.FindChildOfClass(ch, "Humanoid");
                if (hum != 0)
                {
                    var h = props.Read(hum, "Humanoid");
                    if (h.TryGetValue("Health", out var cur)) hp = h.TryGetValue("MaxHealth", out var max) ? $"{Short(cur)}/{Short(max)}" : Short(cur);
                }
                long hrp = reader.FindChild(ch, "HumanoidRootPart");
                if (hasMe && props.TryGetWorldPosition(hrp != 0 ? hrp : ch, out float x, out float y, out float z))
                    dist = Math.Sqrt((x - mx) * (double)(x - mx) + (y - my) * (double)(y - my) + (z - mz) * (double)(z - mz));
            }
            rows.Add(new PlayerRow(p, reader.GetName(p), pp.GetValueOrDefault("DisplayName") ?? string.Empty, team, hp, dist, p == roots.LocalPlayer, ch));
        }
        return rows.OrderByDescending(r => r.IsLocal).ThenBy(r => double.IsNaN(r.Distance) ? double.MaxValue : r.Distance).ThenBy(r => r.Name).ToList();
    }

    private static string Short(string v) =>
        double.TryParse(v, NumberStyles.Float, Inv, out double d) ? d.ToString("0.#", Inv) : v;

    /// <summary>Tool di Backpack (hanya tereplikasi untuk pemain lokal) dan Tool yang dipegang (di Character).</summary>
    public List<ToolRow> Tools(long player)
    {
        var rows = new List<ToolRow>();
        if (player == 0) return rows;
        long ch = GetCharacter(player);
        if (ch != 0)
            foreach (long c in reader.GetChildren(ch))
                if (reader.GetClassName(c) is "Tool" or "HopperBin") rows.Add(Row(c, "Dipegang"));
        long bp = reader.FindChildOfClass(player, "Backpack");
        if (bp != 0)
            foreach (long c in reader.GetChildren(bp))
                if (reader.GetClassName(c) is "Tool" or "HopperBin") rows.Add(Row(c, "Backpack"));
        return rows;
    }

    private ToolRow Row(long tool, string where)
    {
        var p = props.Read(tool, "Tool");
        string info = string.Join("  ", new[] { p.GetValueOrDefault("ToolTip"), p.TryGetValue("Enabled", out var e) && e == "false" ? "nonaktif" : null }.Where(s => !string.IsNullOrEmpty(s)));
        return new ToolRow(tool, reader.GetName(tool), where, info);
    }
}
