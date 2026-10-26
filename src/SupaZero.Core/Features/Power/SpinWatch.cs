using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Satu pembacaan menu Power Spin (Roll) bawaan game.</summary>
public sealed record PowerSpinObs(bool AutoRunning, string? Status, string? Result, string? ResultRarity, bool StoppedOnTarget,
    int? RollsLeft, int? LuckyLeft, IReadOnlyList<string> Pity);

/// <summary>Satu pembacaan layar Trait Reroll.</summary>
public sealed record TraitSpinObs(bool AutoRunning, string? Power, string? Trait, string? Chance, IReadOnlyList<string> Buffs, int? DiceLeft = null);

/// <summary>Laporan auto spin yang sudah berhenti.</summary>
public sealed record SpinReport(bool IsTrait, string Title, string? Result, string? Rarity, string? Chance, IReadOnlyList<string> Buffs,
    string? Power, int? Spins, int? Used, int? LeftAfter, string? Mode, string? StopAt, IReadOnlyList<string> Pity, TimeSpan Duration, bool StoppedOnTarget);

/// <summary>
/// Pantau auto spin bawaan game (hanya membaca GUI). Power: frame AutoStatus terlihat = auto jalan, hilang = selesai.
/// Trait: Trait Reroll.SurfaceGui.MainFrame.Left.Content.AutoStatus ("Auto Rerolling") terlihat atau AutoButton.Sub = "On" = auto jalan.
/// </summary>
public sealed class SpinWatcher
{
    private static readonly Regex SpinNRx = new(@"Spin\s+(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex StopsRx = new(@"Stops at\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex ModeRx = new(@"Auto Spinning:\s*([^•]+)", RegexOptions.IgnoreCase);

    // Power
    private DateTime? _pStart;
    private int _pOff;
    private PowerSpinObs? _pFirst, _pLast;
    private string? _pLastStatus;
    // Trait
    private DateTime? _tStart;
    private int _tOff;
    private TraitSpinObs? _tFirst, _tLast;

    public SpinReport? ObservePower(PowerSpinObs o, DateTime now)
    {
        if (o.AutoRunning)
        {
            if (_pStart is null) { _pStart = now; _pFirst = o; }
            _pOff = 0; _pLast = o;
            if (o.Status is { Length: > 0 }) _pLastStatus = o.Status;
            return null;
        }
        if (_pStart is null) return null;
        // Dua pembacaan berturut-turut tanpa AutoStatus (jeda antar spin bisa sesaat menyembunyikannya).
        if (++_pOff < 2) { _pLast = o with { AutoRunning = false, Status = _pLast?.Status }; return null; }
        var first = _pFirst!; var start = _pStart.Value;
        _pStart = null; _pOff = 0; _pFirst = null;
        string? status = _pLastStatus; _pLastStatus = null;
        string? mode = status is not null && ModeRx.Match(status) is { Success: true } mm ? mm.Groups[1].Value.Trim() : null;
        bool lucky = mode?.Contains("Lucky", StringComparison.OrdinalIgnoreCase) == true;
        int? before = lucky ? first.LuckyLeft : first.RollsLeft, after = lucky ? o.LuckyLeft : o.RollsLeft;
        int? spins = status is not null && SpinNRx.Match(status) is { Success: true } sm ? int.Parse(sm.Groups[1].Value) : null;
        string? stopAt = status is not null && StopsRx.Match(status) is { Success: true } st ? st.Groups[1].Value.Trim() : null;
        int? used = before is { } b && after is { } a && b >= a ? b - a : null;
        return new SpinReport(false, o.StoppedOnTarget ? "Auto spin power: target didapat" : "Auto spin power selesai",
            o.Result, o.ResultRarity, null, [], null, spins, used, after, mode, stopAt, o.Pity, now - start, o.StoppedOnTarget);
    }

    public SpinReport? ObserveTrait(TraitSpinObs o, DateTime now)
    {
        if (o.AutoRunning)
        {
            if (_tStart is null) { _tStart = now; _tFirst = o; }
            _tOff = 0; _tLast = o;
            return null;
        }
        if (_tStart is null) return null;
        if (++_tOff < 2) return null;
        var first = _tFirst!; var start = _tStart.Value;
        // Hasil akhir = pembacaan sekarang (kartu sudah berhenti), cadangan pembacaan terakhir saat auto jalan.
        var fin = o.Trait is not null ? o : _tLast ?? o;
        int? used = first.DiceLeft is { } b && o.DiceLeft is { } a2 && b >= a2 ? b - a2 : null;
        _tStart = null; _tOff = 0; _tFirst = _tLast = null;
        return new SpinReport(true, "Auto reroll trait selesai", fin.Trait, null, fin.Chance, fin.Buffs, fin.Power ?? first.Power,
            null, used, o.DiceLeft ?? fin.DiceLeft, null, null, [], now - start, false);
    }

}

public sealed partial class GameReader
{
    private const string RollMain = "UI Animation (Finished).Roll.SurfaceGui.MainFrame";
    private const string RollReplace = "UI Animation (Finished).Roll.ReplaceConfirm";
    private static readonly Regex NumRx = new(@"(\d[\d,\.]*)");
    private Dictionary<string, string>? _powerRarity;

    public SpinWatcher Spins { get; } = new();
    /// <summary>Laporan auto spin yang baru selesai (diambil sekali oleh UI lalu dikosongkan).</summary>
    public SpinReport? PendingSpin { get; set; }

    private static int? FirstInt(string? s) =>
        s is not null && NumRx.Match(s) is { Success: true } m && int.TryParse(m.Groups[1].Value.Replace(",", "").Replace(".", ""), out int v) ? v : null;

    private void WatchSpins(long pg)
    {
        var now = DateTime.UtcNow;
        try
        {
            if (ReadPowerSpin(pg) is { } p && Spins.ObservePower(p, now) is { } r1) PendingSpin = r1;
            if (ReadTraitSpin(pg) is { } t && Spins.ObserveTrait(t, now) is { } r2) PendingSpin = r2;
        }
        catch { /* pembacaan GUI gagal sesaat: abaikan */ }
    }

    private PowerSpinObs? ReadPowerSpin(long pg)
    {
        long main = reader.ResolvePath(pg, RollMain);
        if (main == 0) return null;
        long auto = reader.FindChild(main, "AutoStatus");
        bool running = auto != 0 && IsVisibleFlag(auto) && IsShown(auto, pg);
        string? status = running ? Plain(reader.ResolvePath(auto, "Fill.Amount")) : null;
        long rep = reader.ResolvePath(pg, RollReplace);
        bool onTarget = rep != 0 && IsVisibleFlag(rep);
        string? result, rarity = null;
        if (onTarget)
        {
            result = Plain(reader.ResolvePath(rep, "Panel.CharacterName.Txt")) ?? Plain(reader.ResolvePath(rep, "Panel.CharacterName"));
            rarity = Plain(reader.ResolvePath(rep, "Panel.Rarity"))?.Split('|')[0].Trim();
        }
        else result = Plain(reader.ResolvePath(main, "CharacterInfo.TitleShadow.Title"));
        if (rarity is null && result is not null) rarity = PowerRarity(main, result);
        int? rolls = FirstInt(Plain(reader.ResolvePath(main, "SpinOptions.NormalSpinButton.SpinsLeft.Fill.Amount")));
        int? lucky = FirstInt(Plain(reader.ResolvePath(main, "SpinOptions.LuckySpinButton.SpinsLeft.Fill.Amount")));
        var pity = new List<string>();
        foreach (string op in new[] { "Op1", "Op2", "Op3" })
            if (Plain(reader.ResolvePath(main, $"PityDisplay.{op}.Fill.Amount")) is { } pt) pity.Add(pt);
        return new PowerSpinObs(running, status, result, rarity, onTarget, rolls, lucky, pity);
    }

    /// <summary>Nama power → rarity dari tabel peluang (CharacterChances.Fade.Holder.Container.&lt;Rarity&gt;.Holder.Fill.Slot*.Fill.Title).</summary>
    private string? PowerRarity(long main, string name)
    {
        if (_powerRarity is null || !_powerRarity.ContainsKey(name))
        {
            var map = _powerRarity ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long cont = reader.ResolvePath(main, "CharacterChances.Fade.Holder.Container");
            foreach (long r in cont != 0 ? reader.GetChildren(cont, 32) : new List<long>())
            {
                long fill = reader.ResolvePath(r, "Holder.Fill");
                foreach (long s in fill != 0 ? reader.GetChildren(fill, 32) : new List<long>())
                    if (reader.GetName(s).StartsWith("Slot", StringComparison.Ordinal) && Plain(reader.ResolvePath(s, "Fill.Title")) is { } t)
                        map[t] = reader.GetName(r);
            }
            _powerRarity = map;
        }
        return _powerRarity.TryGetValue(name, out var v) ? v : null;
    }

    private TraitSpinObs? ReadTraitSpin(long pg)
    {
        long root = reader.ResolvePath(pg, TraitRoot);
        if (root == 0) return null;
        bool shown = IsShown(root, pg);
        long st = reader.FindChild(root, "AutoStatus");
        bool running = shown && ((st != 0 && IsVisibleFlag(st))
            || string.Equals(Plain(reader.ResolvePath(root, "Buttons.AutoButton.AutoButton.Sub")), "On", StringComparison.OrdinalIgnoreCase));
        string? header = shown ? Plain(reader.ResolvePath(root, TraitPowerName)) : null;
        string? power = header is null ? null : CounterRx.Replace(header, string.Empty).Trim();
        var t = power is null ? null : Powers.TraitFor(power);
        int? dice = shown ? FirstInt(Plain(reader.ResolvePath(root, "DicesDisplay.Txt"))) : null;
        return new TraitSpinObs(running, power, t?.Name, t?.Chance, t?.Buffs ?? [], dice);
    }
}
