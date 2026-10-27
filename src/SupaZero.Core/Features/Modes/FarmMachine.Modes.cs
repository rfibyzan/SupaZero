using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private readonly Dictionary<string, int> _targetTries = new(StringComparer.Ordinal);

    private static string Wave(GameSnapshot s) => s.RunWave is { Length: > 0 } w ? w + " · " : "";

    /// <summary>
    /// Enemies that may be attacked. v0.2.81 (no guessing): while BossBar shows [IMMUNE], only models with a definite mark are skipped
    /// (<see cref="EnemyInfo.ImmuneMark"/>: 2 Highlights). The old guess "near Markers.BossSpawn 12 studs" was removed.
    /// </summary>
    private int _waveArea = -1; // v0.3.12: HUD area whose enemy wave was seen (rubble after it may be hit)
    public const double RubbleNearPlayer = 250, RubbleNearArea = 80;

    /// <summary>Rubble waiting near the player (≤ 250 studs) or near the current area box (≤ 80 studs from its edge).</summary>
    /// <summary>
    /// v0.3.29 (Byzan 27 Oct 23:10, West City Act 1; RIE World 23:14:02: Border2 open, Border3 Blocker solid + RubbleBar inside Area 3 box): rubble already broken, HUD "Enter area 3/5" 89 studs away, but the macro
    /// kept going for a still-solid Blocker further on ("Path to rubble not found"). With "Enter area N" a rubble only blocks the way when it
    /// is closer to the player than area N's entry point; a rubble beyond it belongs to a later area. Area point unreadable = not attacked.
    /// </summary>
    internal static bool RubbleBeforeNextArea(GameSnapshot s, Vec3 rp) =>
        s.PlayerPos is { } p && s.NextAreaPos is { } n
        && Navigator.Distance2D(p.X, p.Z, rp.X, rp.Z) < Navigator.Distance2D(p.X, p.Z, n.X, n.Z);

    internal static bool RubbleNear(GameSnapshot s, Vec3 rp) =>
        (s.PlayerPos is { } p && Navigator.Distance2D(p.X, p.Z, rp.X, rp.Z) <= RubbleNearPlayer)
        || (s.CurrentArea is { } a && a.Contains(rp, -RubbleNearArea));

    public static List<EnemyInfo> Targets(GameSnapshot s)
    {
        var alive = s.Enemies.Where(e => e.Alive).ToList();
        return s.BossImmune ? alive.Where(e => !e.ImmuneMark).ToList() : alive;
    }

    /// <summary>Click reward card <paramref name="want"/> until it shows UNTARGET (= targeted). null = done / not applicable.</summary>
    private List<MacroAction>? DropTarget(GameSnapshot s, IReadOnlyList<RewardCard> cards, GuiRect? view, string want, string key, long now)
    {
        if (string.IsNullOrWhiteSpace(want) || cards.Count == 0) return null;
        var c = cards.FirstOrDefault(x => Matches(x.Name, want.Trim()));
        if (c is null)
        {
            _pending.AddRange(Note(key + "-none", now, 30000, $"Drop Target '{want}' not in Rewards (available: {string.Join(", ", cards.Where(x => x.Targetable).Select(x => x.Name))})."));
            return null;
        }
        if (!c.Targetable || c.Targeted || c.Hit is null) { if (c.Targeted) _targetTries.Remove(key); return null; }
        int n = _targetTries.GetValueOrDefault(key);
        if (n >= 3)
        {
            _pending.AddRange(Note(key + "-fail", now, 30000, $"Drop Target {c.Name} still not read as targeted after 3 clicks → continuing to Start."));
            return null;
        }
        var act = ClickEntry(s, new LobbyEntry(c.Name, false, false, c.Hit), view, key, $"Drop Target {c.Name}", now);
        if (act is { Count: > 0 } && act[0] is ClickAction) { _targetTries[key] = n + 1; Status = $"Drop Target: {c.Name}."; }
        return act ?? [];
    }

    /// <summary>Pick a Mirror Blessing card: name order from settings, then highest rarity.</summary>
    private List<MacroAction> PickBlessing(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Fighting;
        var f = S.Farm;
        var all = BlessingCatalog.All(f.KnownBlessings);
        var pick = BlessingCatalog.Choose(s.Blessings, BlessingCatalog.EffectiveBuffs(f.BlessingBuffs),
            BlessingCatalog.EffectiveCards(f.BlessingCards, f.BlessingPriority, all), f.KnownBlessings);
        if (pick?.Pick is not { } r) return Note("bless-none", now, 5000, "Mirror Blessing cards read but the PICK button was not.");
        Status = $"Mirror Blessing: {pick.Name} ({pick.Rarity}, {string.Join("/", BlessingCatalog.BuffsOf(pick).Select(BlessingCatalog.Label))}) from {string.Join(" / ", s.Blessings.Select(b => b.Name))}";
        if (!InWindow(s, r.CenterX, r.CenterY)) return Note("bless-out", now, 5000, $"PICK button for {pick.Name} outside the window.");
        return Ready("bless-pick", now, 1500) ? [new MoveAction([], "Pick card"), new ClickAction(r.CenterX, r.CenterY, $"PICK {pick.Name}")] : [];
    }

    /// <summary>Name order only (legacy compatible): no buffs, no match → highest rarity.</summary>
    public static BlessingOffer? ChooseBlessing(IReadOnlyList<BlessingOffer> offers, IReadOnlyList<string> priority) =>
        BlessingCatalog.Choose(offers, [], priority.Select(n => new BlessingPref(n, true)).ToList());

    /// <summary>Trials panel: open the Floor Rewards tab → set floor (if configured) → Drop Target → Start. The paid floor skip is never clicked.</summary>
    private List<MacroAction> TrialsMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var t = s.Trials!;
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Trials panel open (Auto create room off).");
        if (!t.FloorTabOpen)
        {
            if (t.FloorTab is not { } tab) return Note("tr-tab", now, 5000, "Trials Floor Rewards tab not read.");
            Status = "Trials: opening Floor Rewards tab.";
            return Ready("tr-tab", now, 1500) && InWindow(s, tab.CenterX, tab.CenterY) ? [new ClickAction(tab.CenterX, tab.CenterY, "Tab Floor Rewards")] : [];
        }
        if (f.TrialsFloor > 0 && t.SelectedFloor is int cur && cur != f.TrialsFloor)
        {
            var btn = cur < f.TrialsFloor ? t.Increase : t.Decrease;
            Status = $"Trials: floor {cur} → {f.TrialsFloor}.";
            if (btn is not { } b) return Note("tr-floor", now, 5000, "Trials floor +/- buttons not read.");
            if (cur < f.TrialsFloor && t.HighestFloor is int hi && cur > hi)
                return Stop($"Floor {f.TrialsFloor} not unlocked yet (highest {hi}).");
            return Ready("tr-floor", now, 350) ? [new ClickAction(b.CenterX, b.CenterY, cur < f.TrialsFloor ? "Floor +" : "Floor -")] : [];
        }
        if (DropTarget(s, t.Rewards, t.RewardsView, f.DropTarget, "trials-target", now) is { } tg) return tg;
        _selectedMap = "Trials";
        _selectedAct = t.SelectedFloor ?? 0;
        if (t.Start is not { } st) return Note("tr-start", now, 5000, "Trials Start button not read.");
        Status = $"Trials floor {t.SelectedFloor?.ToString() ?? "?"} → Start.";
        return Ready("tr-start", now, 3000) && InWindow(s, st.CenterX, st.CenterY) ? [new ClickAction(st.CenterX, st.CenterY, "Start Trials")] : [];
    }

    // ---------- End screen ----------
}
