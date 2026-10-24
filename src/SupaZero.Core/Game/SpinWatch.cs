using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Satu pembacaan menu Power Spin (Roll) bawaan game.</summary>
public sealed record PowerSpinObs(bool AutoRunning, string? Status, string? Result, string? ResultRarity, bool StoppedOnTarget,
    int? RollsLeft, int? LuckyLeft, IReadOnlyList<string> Pity);

/// <summary>Satu pembacaan layar Trait Reroll.</summary>
public sealed record TraitSpinObs(bool Open, string? Power, string? Trait, string? Chance, IReadOnlyList<string> Buffs);

/// <summary>Laporan auto spin yang sudah berhenti.</summary>
public sealed record SpinReport(bool IsTrait, string Title, string? Result, string? Rarity, string? Chance, IReadOnlyList<string> Buffs,
    string? Power, int? Spins, int? Used, int? LeftAfter, string? Mode, string? StopAt, IReadOnlyList<string> Pity, TimeSpan Duration, bool StoppedOnTarget);

/// <summary>
/// Pantau auto spin bawaan game (hanya membaca GUI). Power: frame AutoStatus terlihat = auto jalan, hilang = selesai.
/// Trait: layar Trait Reroll terbuka dan hasil trait berubah berulang (auto reroll); hasil diam ≥ TraitIdle atau layar ditutup = selesai.
/// </summary>
public sealed class SpinWatcher
{
    public static readonly TimeSpan TraitIdle = TimeSpan.FromSeconds(8);
    public const int TraitMinChanges = 3;
    private static readonly Regex SpinNRx = new(@"Spin\s+(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex StopsRx = new(@"Stops at\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex ModeRx = new(@"Auto Spinning:\s*([^•]+)", RegexOptions.IgnoreCase);

    // Power
    private DateTime? _pStart;
    private int _pOff;
    private PowerSpinObs? _pFirst, _pLast;
    private string? _pLastStatus;
    // Trait
    private DateTime? _tStart, _tChangedAt;
    private int _tChanges;
    private TraitSpinObs? _tLast;

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
        if (o.Open && o.Trait is not null)
        {
            if (_tLast is null || _tLast.Power != o.Power) { _tLast = o; _tStart = now; _tChangedAt = now; _tChanges = 0; return null; }
            if (_tLast.Trait != o.Trait || _tLast.Chance != o.Chance)
            {
                _tChanges++; _tChangedAt = now; _tLast = o;
                return null;
            }
            _tLast = o;
            if (_tChanges < TraitMinChanges || now - _tChangedAt!.Value < TraitIdle) return null;
        }
        else if (_tLast is null || _tChanges < TraitMinChanges) { if (!o.Open) ResetTrait(); return null; }

        var last = _tLast!; var start = _tStart ?? now; var end = _tChangedAt ?? now;
        ResetTrait();
        // Tetap ingat power yang sama agar trait diam tidak dilaporkan lagi.
        if (o.Open) { _tLast = last; _tStart = now; _tChangedAt = now; }
        return new SpinReport(true, "Auto reroll trait selesai", last.Trait, null, last.Chance, last.Buffs, last.Power,
            null, null, null, null, null, [], end - start, false);
    }

    private void ResetTrait() { _tLast = null; _tStart = _tChangedAt = null; _tChanges = 0; }
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
        if (!IsShown(root, pg)) return new TraitSpinObs(false, null, null, null, []);
        string? header = Plain(reader.ResolvePath(root, TraitPowerName));
        string? power = header is null ? null : CounterRx.Replace(header, string.Empty).Trim();
        var t = power is null ? null : Powers.TraitFor(power);
        return new TraitSpinObs(true, power, t?.Name, t?.Chance, t?.Buffs ?? []);
    }
}
