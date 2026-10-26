using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    private bool? ReadUltReady(long pg)
    {
        long ult = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudUltimate) : 0;
        if (ult == 0 || !IsShown(ult, pg)) return null;
        // v0.2.68: Locked tampil → belum bisa. Aktif (Progress tampil / tombol bukan "G", mis. "KAISEL") → G lagi = naga.
        // Penuh → ≥ 3 anak "ImageLabel" (kilau). Selain itu kosong/mengisi.
        long lk = reader.FindChild(ult, AnimeZeroPaths.HudUltimateLock);
        if (lk != 0 && IsShown(lk, pg)) return false;
        long prog = reader.FindChild(ult, AnimeZeroPaths.HudUltimateProgress);
        if (prog != 0 && IsShown(prog, pg)) return true;
        long frame = reader.ResolvePath(pg, AnimeZeroPaths.HudUltimateFrame);
        long keyNode = frame != 0 ? reader.ResolvePath(frame, AnimeZeroPaths.HudUltimateKey) : 0;
        string? key = keyNode != 0 ? Text(keyNode)?.Trim() : null;
        if (!string.IsNullOrEmpty(key) && !key.Equals("G", StringComparison.OrdinalIgnoreCase)) return true;
        int sparkles = reader.GetChildren(ult, 128).Count(c => reader.GetName(c) == AnimeZeroPaths.HudUltimateSparkle);
        return sparkles >= 3;
    }

    /// <summary>v0.2.79: true = Dash.Progress tampil (cooldown), false = tombol Dash terbaca tanpa Progress, null = tombol tidak terbaca.</summary>
    private bool? ReadDashCooling(long pg)
    {
        long dash = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudDash) : 0;
        if (dash == 0 || !IsShown(dash, pg)) return null;
        long prog = reader.FindChild(dash, "Progress");
        return prog != 0 && IsShown(prog, pg);
    }

    /// <summary>Cooldown skill dari HUD: tiap skillBox → (tombol, sisa detik atau null bila siap, terkunci).</summary>
    private List<SkillCooldown> ReadSkillCooldowns(long pg)
    {
        var list = new List<SkillCooldown>();
        long skills = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudSkills) : 0;
        if (skills == 0 || !IsShown(skills, pg)) return list;
        foreach (long box in reader.GetChildren(skills, 16))
        {
            if (reader.GetName(box) != AnimeZeroPaths.HudSkillBox) continue;
            long keyNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillKey);
            string? key = keyNode != 0 ? Text(keyNode)?.Trim() : null;
            if (string.IsNullOrEmpty(key)) continue;
            long cdNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillCd);
            string? cd = cdNode != 0 ? Text(cdNode)?.Trim() : null;
            long lockNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillLock);
            list.Add(new SkillCooldown(key, ParseCooldown(cd), lockNode != 0 && IsShown(lockNode, pg)));
        }
        return list;
    }

    /// <summary>"" → null (siap); "1.7"/"2" → detik; v0.2.79: teks lain = NaN (status tidak diketahui → skill tidak ditekan).</summary>
    public static double? ParseCooldown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().TrimEnd('s', 'S').Replace(',', '.');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? (v <= 0 ? null : v) : double.NaN;
    }

    /// <summary>Musuh: Workspace.enemies.&lt;angka&gt; (Model) → HumanoidRootPart posisi, Humanoid Health/MaxHealth.</summary>
    /// <summary>
    /// Posisi rubble yang harus dihancurkan. Aktif bila teks HUD menyebut "rubble" atau RubbleBossBar Enabled.
    /// Posisi = Adornee BillboardGui PlayerGui.RubbleBar (offset Misc.Adornee), cadangan: parent BillboardGui bila di Workspace.
    /// </summary>
    private string? _rubbleInfo;
    private double? _rubbleHp;
}
