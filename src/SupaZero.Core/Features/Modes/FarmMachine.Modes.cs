using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private readonly Dictionary<string, int> _targetTries = new(StringComparer.Ordinal);

    private static string Wave(GameSnapshot s) => s.RunWave is { Length: > 0 } w ? w + " · " : "";

    /// <summary>
    /// Musuh yang boleh diserang. v0.2.81 (jangan menebak): saat BossBar [IMMUNE], yang dilewati hanya model bertanda pasti
    /// (<see cref="EnemyInfo.ImmuneMark"/>: 2 Highlight). Tebakan lama "dekat Markers.BossSpawn 12 stud" dihapus.
    /// </summary>
    private int _waveArea = -1; // v0.3.12: area HUD yang wave musuhnya terlihat (rubble sesudahnya boleh dipukul)
    public const double RubbleNearPlayer = 250, RubbleNearArea = 80;

    /// <summary>Rubble menunggu dekat pemain (≤ 250 stud) atau dekat kotak area sekarang (≤ 80 stud dari tepi).</summary>
    internal static bool RubbleNear(GameSnapshot s, Vec3 rp) =>
        (s.PlayerPos is { } p && Navigator.Distance2D(p.X, p.Z, rp.X, rp.Z) <= RubbleNearPlayer)
        || (s.CurrentArea is { } a && a.Contains(rp, -RubbleNearArea));

    public static List<EnemyInfo> Targets(GameSnapshot s)
    {
        var alive = s.Enemies.Where(e => e.Alive).ToList();
        return s.BossImmune ? alive.Where(e => !e.ImmuneMark).ToList() : alive;
    }

    /// <summary>Klik kartu reward <paramref name="want"/> sampai bertanda UNTARGET (= ditarget). null = sudah / tidak berlaku.</summary>
    private List<MacroAction>? DropTarget(GameSnapshot s, IReadOnlyList<RewardCard> cards, GuiRect? view, string want, string key, long now)
    {
        if (string.IsNullOrWhiteSpace(want) || cards.Count == 0) return null;
        var c = cards.FirstOrDefault(x => Matches(x.Name, want.Trim()));
        if (c is null)
        {
            _pending.AddRange(Note(key + "-none", now, 30000, $"Drop Target '{want}' tidak ada di Rewards (ada: {string.Join(", ", cards.Where(x => x.Targetable).Select(x => x.Name))})."));
            return null;
        }
        if (!c.Targetable || c.Targeted || c.Hit is null) { if (c.Targeted) _targetTries.Remove(key); return null; }
        int n = _targetTries.GetValueOrDefault(key);
        if (n >= 3)
        {
            _pending.AddRange(Note(key + "-fail", now, 30000, $"Drop Target {c.Name} belum terbaca ditarget setelah 3 klik → lanjut Start."));
            return null;
        }
        var act = ClickEntry(s, new LobbyEntry(c.Name, false, false, c.Hit), view, key, $"Drop Target {c.Name}", now);
        if (act is { Count: > 0 } && act[0] is ClickAction) { _targetTries[key] = n + 1; Status = $"Drop Target: {c.Name}."; }
        return act ?? [];
    }

    /// <summary>Pilih kartu Mirror Blessing: urutan nama di pengaturan, lalu rarity tertinggi.</summary>
    private List<MacroAction> PickBlessing(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Fighting;
        var f = S.Farm;
        var all = BlessingCatalog.All(f.KnownBlessings);
        var pick = BlessingCatalog.Choose(s.Blessings, BlessingCatalog.EffectiveBuffs(f.BlessingBuffs),
            BlessingCatalog.EffectiveCards(f.BlessingCards, f.BlessingPriority, all), f.KnownBlessings);
        if (pick?.Pick is not { } r) return Note("bless-none", now, 5000, "Kartu Mirror Blessing terbaca tapi tombol PICK tidak.");
        Status = $"Mirror Blessing: {pick.Name} ({pick.Rarity}, {string.Join("/", BlessingCatalog.BuffsOf(pick).Select(BlessingCatalog.Label))}) dari {string.Join(" / ", s.Blessings.Select(b => b.Name))}";
        if (!InWindow(s, r.CenterX, r.CenterY)) return Note("bless-out", now, 5000, $"Tombol PICK {pick.Name} di luar jendela.");
        return Ready("bless-pick", now, 1500) ? [new MoveAction([], "Pilih kartu"), new ClickAction(r.CenterX, r.CenterY, $"PICK {pick.Name}")] : [];
    }

    /// <summary>Urutan nama saja (kompatibel lama): tanpa buff, tidak cocok → rarity tertinggi.</summary>
    public static BlessingOffer? ChooseBlessing(IReadOnlyList<BlessingOffer> offers, IReadOnlyList<string> priority) =>
        BlessingCatalog.Choose(offers, [], priority.Select(n => new BlessingPref(n, true)).ToList());

    /// <summary>Panel Trials: buka tab Floor Rewards → atur floor (bila diset) → Drop Target → Start. Skip floor berbayar tidak pernah diklik.</summary>
    private List<MacroAction> TrialsMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var t = s.Trials!;
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Panel Trials terbuka (Auto create room mati).");
        if (!t.FloorTabOpen)
        {
            if (t.FloorTab is not { } tab) return Note("tr-tab", now, 5000, "Tab Floor Rewards Trials tidak terbaca.");
            Status = "Trials: buka tab Floor Rewards.";
            return Ready("tr-tab", now, 1500) && InWindow(s, tab.CenterX, tab.CenterY) ? [new ClickAction(tab.CenterX, tab.CenterY, "Tab Floor Rewards")] : [];
        }
        if (f.TrialsFloor > 0 && t.SelectedFloor is int cur && cur != f.TrialsFloor)
        {
            var btn = cur < f.TrialsFloor ? t.Increase : t.Decrease;
            Status = $"Trials: floor {cur} → {f.TrialsFloor}.";
            if (btn is not { } b) return Note("tr-floor", now, 5000, "Tombol +/- floor Trials tidak terbaca.");
            if (cur < f.TrialsFloor && t.HighestFloor is int hi && cur > hi)
                return Stop($"Floor {f.TrialsFloor} belum terbuka (tertinggi {hi}).");
            return Ready("tr-floor", now, 350) ? [new ClickAction(b.CenterX, b.CenterY, cur < f.TrialsFloor ? "Floor +" : "Floor -")] : [];
        }
        if (DropTarget(s, t.Rewards, t.RewardsView, f.DropTarget, "trials-target", now) is { } tg) return tg;
        _selectedMap = "Trials";
        _selectedAct = t.SelectedFloor ?? 0;
        if (t.Start is not { } st) return Note("tr-start", now, 5000, "Tombol Start Trials tidak terbaca.");
        Status = $"Trials floor {t.SelectedFloor?.ToString() ?? "?"} → Start.";
        return Ready("tr-start", now, 3000) && InWindow(s, st.CenterX, st.CenterY) ? [new ClickAction(st.CenterX, st.CenterY, "Start Trials")] : [];
    }

    // ---------- Layar akhir ----------
}
