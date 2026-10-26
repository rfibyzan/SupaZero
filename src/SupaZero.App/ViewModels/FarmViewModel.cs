using System.Collections.ObjectModel;
using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

/// <summary>One skill key row: key badge + Rebind (like the Macro hotkey), enabled toggle, cooldown.</summary>
public sealed class SkillRowViewModel(FarmViewModel owner, int index) : ObservableObject
{
    private bool _rebinding;
    private SkillBinding B => owner.Skills(index);

    public string Name => B.Name;
    public string KeyText => _rebinding ? "..." : B.Key;
    public string RebindText => _rebinding ? "Press a key" : "Rebind";
    public bool Enabled { get => B.Enabled; set => owner.SetSkill(index, B with { Enabled = value }); }
    public decimal CooldownMs { get => B.CooldownMs; set => owner.SetSkill(index, B with { CooldownMs = (int)value }); }

    public RelayCommand RebindCommand => new(_ =>
    {
        _rebinding = true;
        owner.Root.ActiveRebind = (key, cancel) =>
        {
            _rebinding = false;
            if (!cancel && HotkeyMap.Normalize(key) is { } k) owner.SetSkill(index, B with { Key = k });
            Raise(nameof(KeyText));
            Raise(nameof(RebindText));
            return true;
        };
        Raise(nameof(KeyText));
        Raise(nameof(RebindText));
    });
}

/// <summary>v0.3.11: one Mirror Blessing buff priority row (▲▼ order + enabled).</summary>
public sealed class BlessingBuffRowViewModel(FarmViewModel owner, int index) : ObservableObject
{
    private BuffPref P => owner.BlessingBuffList[index];
    public string Rank => $"{index + 1}.";
    public string Label => BlessingCatalog.Label(P.Buff);
    public string CardsText
    {
        get
        {
            var names = owner.BlessingAll.Where(b => b.Buffs.Contains(P.Buff)).Select(b => b.Name).ToList();
            return names.Count > 0 ? string.Join(", ", names) : "no cards read yet";
        }
    }
    public bool Enabled { get => P.Enabled; set => owner.SetBuff(index, P with { Enabled = value }); }
    public RelayCommand UpCommand => new(_ => owner.MoveBuff(index, -1));
    public RelayCommand DownCommand => new(_ => owner.MoveBuff(index, 1));
}

/// <summary>v0.3.11: one Mirror Blessing card row (card data + ▲▼ order + allowed to pick).</summary>
public sealed class BlessingCardRowViewModel(FarmViewModel owner, int index) : ObservableObject
{
    private BlessingPref P => owner.BlessingCardList[index];
    private BlessingInfo? I => owner.BlessingAll.FirstOrDefault(b => b.Name.Equals(P.Name, StringComparison.OrdinalIgnoreCase));
    public string Rank => $"{index + 1}.";
    public string Name => P.Name;
    public string Rarity => I?.Rarity ?? "?";
    public string Detail => I is { } i ? (i.Description.Length > 0 ? i.Description : i.Value) : "not read yet";
    public string BuffText => I is { } i ? string.Join(" · ", i.Buffs.Select(BlessingCatalog.Label)) + (i.HasDrawback ? " · ⚠ drawback" : "") : "";
    public bool Enabled { get => P.Enabled; set => owner.SetCard(index, P with { Enabled = value }); }
    public RelayCommand UpCommand => new(_ => owner.MoveCard(index, -1));
    public RelayCommand DownCommand => new(_ => owner.MoveCard(index, 1));
}

public sealed class FarmViewModel : ObservableObject
{
    private const string CurrentMapLabel = "(automatic per mode)";
    private readonly MainViewModel _root;

    public FarmViewModel(MainViewModel root)
    {
        _root = root;
        var skills = S.Combat.EffectiveSkills.ToList();
        S.Combat = S.Combat with { Skills = skills };
        for (int i = 0; i < skills.Count; i++) SkillRows.Add(new SkillRowViewModel(this, i));
        RebuildMaps();
        RebuildBlessings();
    }

    public MainViewModel Root => _root;
    private AppSettings S => _root.Settings;
    private void Farm(Func<FarmSettings, FarmSettings> f) { S.Farm = f(S.Farm); _root.Save(); }

    /// <summary>Modes from the "Mode:" label of the map cards read in game (card order); defaults if never read.</summary>
    public ObservableCollection<string> Modes { get; } = [];
    private const string AllActsLabel = "All acts (in order)";
    public ObservableCollection<string> Acts { get; } = [];
    public IReadOnlyList<string> Difficulties { get; } = Enum.GetNames<Difficulty>();
    public IReadOnlyList<string> AfterWins { get; } = ["Automatic", "Next", "Retry", "Leave"];
    public ObservableCollection<string> Maps { get; } = [];
    public ObservableCollection<SkillRowViewModel> SkillRows { get; } = [];

    public int ModeIndex
    {
        get => Modes.IndexOf(Modes.FirstOrDefault(m => m.Equals(S.Farm.Mode, StringComparison.OrdinalIgnoreCase)) ?? "");
        set
        {
            if (value < 0 || value >= Modes.Count) return;
            string mode = Modes[value];
            if (mode.Equals(S.Farm.Mode, StringComparison.OrdinalIgnoreCase)) return;
            // A map that does not match the new mode → back to "selected map" (the macro picks the map for the mode itself).
            bool keep = S.Farm.KnownMaps?.FirstOrDefault(m => m.Name == S.Farm.Map) is { Mode: { } mm } && mm.Equals(mode, StringComparison.OrdinalIgnoreCase);
            Farm(f => f with { Mode = mode, Map = keep ? f.Map : "" });
            RebuildMaps();
        }
    }

    /// <summary>Acts for the selected map: ONLY from the ActsList that was read (v0.2.82: no 1–5 guessing).</summary>
    private List<int> ActNumbers() =>
        S.Farm.Map.Length > 0 && S.Farm.KnownActs?.GetValueOrDefault(S.Farm.Map) is { Count: > 0 } a ? a : [];

    public int ActIndex
    {
        get => S.Farm.Act == 0 ? 0 : Math.Max(0, ActNumbers().IndexOf(S.Farm.Act) + 1);
        set
        {
            if (value < 0) return;
            var nums = ActNumbers();
            int act = value == 0 ? 0 : nums[Math.Clamp(value - 1, 0, nums.Count - 1)];
            if (act != S.Farm.Act) { Farm(f => f with { Act = act }); RebuildDrops(); }
        }
    }

    private void RebuildActs()
    {
        var nums = ActNumbers();
        if (S.Farm.Act > 0 && nums.Count > 0 && !nums.Contains(S.Farm.Act)) Farm(f => f with { Act = 0 });
        var want = new List<string> { AllActsLabel };
        want.AddRange(nums.Select(n => "Act " + n));
        if (!Acts.SequenceEqual(want))
        {
            // Clear() drops the ComboBox to SelectedIndex -1 (empty Act box); refill, then re-set the selection after layout.
            Acts.Clear();
            foreach (var x in want) Acts.Add(x);
        }
        Raise(nameof(ActIndex));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Raise(nameof(ActIndex)), Avalonia.Threading.DispatcherPriority.Background);
        RebuildDrops();
    }
    public string ActProgressText => S.Farm.ActProgress is { Count: > 0 } p
        ? "Cleared: " + string.Join(", ", p.Select(kv => $"{kv.Key} Act {kv.Value}"))
        : ActNumbers().Count == 0 && S.Farm.Map.Length > 0
            ? "Acts for this map not read yet → run Action calibration (General tab)."
            : "All acts: starts at Act 1, advances automatically after each win. An act the map does not have → macro stops.";
    public RelayCommand ResetActProgressCommand => new(_ => { Farm(f => f with { ActProgress = null }); Raise(nameof(ActProgressText)); });
    public int DifficultyIndex { get => (int)S.Farm.Difficulty; set => Farm(f => f with { Difficulty = (Difficulty)Math.Clamp(value, 0, 3) }); }
    public int AfterWinIndex { get => (int)S.Farm.AfterWin; set => Farm(f => f with { AfterWin = (AfterWin)Math.Clamp(value, 0, 3) }); }

    public string? SelectedMap
    {
        get => S.Farm.Map.Length == 0 ? CurrentMapLabel : Maps.FirstOrDefault(m => Strip(m) == S.Farm.Map) ?? S.Farm.Map;
        set
        {
            if (value is null) return;
            string name = value == CurrentMapLabel ? string.Empty : Strip(value);
            if (name != S.Farm.Map) { Farm(f => f with { Map = name }); RebuildActs(); }
        }
    }

    public string MapsHint => S.Farm.KnownMaps is not { Count: > 0 }
        ? "Open the Create Room menu once in game: the map list + locked status are read automatically."
        : !S.Farm.KnownMaps.Any(m => m.Mode is { Length: > 0 })
            ? "Map modes not read yet. Open the Create Room menu once more so maps are grouped by mode."
            : MapsForMode().ToList() is { Count: > 0 } k
                ? $"Map {S.Farm.Mode}: {k.Count(m => !m.Locked)} unlocked, {k.Count(m => m.Locked)} locked."
                : $"No maps yet for mode {S.Farm.Mode}.";

    /// <summary>Maps of the selected mode. Maps without a mode label are only included when no map has a read mode yet.</summary>
    private IEnumerable<KnownMap> MapsForMode()
    {
        var all = S.Farm.KnownMaps ?? [];
        bool anyMode = all.Any(m => m.Mode is { Length: > 0 });
        return all.Where(m => anyMode ? m.Mode is { } mm && mm.Equals(S.Farm.Mode, StringComparison.OrdinalIgnoreCase) : true);
    }

    public bool AutoCreateLobby { get => S.Farm.AutoCreateLobby; set => Farm(f => f with { AutoCreateLobby = value }); }
    public bool AutoVoteStart { get => S.Farm.AutoVoteStart; set => Farm(f => f with { AutoVoteStart = value }); }
    public bool RetryOnDefeat { get => S.Farm.RetryOnDefeat; set => Farm(f => f with { RetryOnDefeat = value }); }
    public bool InfiniteUnlimitedRetry { get => S.Farm.InfiniteUnlimitedRetry; set => Farm(f => f with { InfiniteUnlimitedRetry = value }); }
    public decimal MaxRetries { get => S.Farm.MaxRetriesPerStage; set => Farm(f => f with { MaxRetriesPerStage = (int)value }); }
    // v0.3.4: Drop Target is picked from materials proven targetable on the selected map/act (calibration), not typed.
    private const string DropOffLabel = "(off)";
    public ObservableCollection<string> DropChoices { get; } = [];

    /// <summary>Materials per act of the selected map. Act "All" → union of all acts of that map.</summary>
    private List<string> DropsForSelection()
    {
        var kd = S.Farm.KnownDrops;
        if (kd is null || S.Farm.Map.Length == 0) return [];
        IEnumerable<int> acts = S.Farm.Act > 0 ? [S.Farm.Act] : ActNumbers();
        return acts.SelectMany(a => kd.GetValueOrDefault(SupaZero.Core.Automation.FarmMachine.DropKey(S.Farm.Map, a)) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool DropsKnown() => S.Farm.KnownDrops is { } kd && S.Farm.Map.Length > 0 &&
        (S.Farm.Act > 0 ? [S.Farm.Act] : ActNumbers()).Any(a => kd.ContainsKey(SupaZero.Core.Automation.FarmMachine.DropKey(S.Farm.Map, a)));

    private void RebuildDrops()
    {
        var want = new List<string> { DropOffLabel };
        want.AddRange(DropsForSelection());
        if (!DropChoices.SequenceEqual(want))
        {
            DropChoices.Clear();
            foreach (var x in want) DropChoices.Add(x);
        }
        Raise(nameof(SelectedDrop));
        Raise(nameof(DropHint));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Raise(nameof(SelectedDrop)), Avalonia.Threading.DispatcherPriority.Background);
    }

    public string? SelectedDrop
    {
        get => S.Farm.DropTarget.Length == 0 ? DropOffLabel
            : DropChoices.FirstOrDefault(d => d.Equals(S.Farm.DropTarget, StringComparison.OrdinalIgnoreCase)) ?? DropOffLabel;
        set
        {
            if (value is null) return;
            string v = value == DropOffLabel ? "" : value;
            if (v != S.Farm.DropTarget) { Farm(f => f with { DropTarget = v }); Raise(nameof(DropHint)); }
        }
    }

    public string DropHint
    {
        get
        {
            if (S.Farm.Map.Length == 0) return "Pick a map (and act) on the General tab first; the drop list follows the map/act.";
            if (!DropsKnown()) return $"Drop {S.Farm.Map} {(S.Farm.Act > 0 ? "Act " + S.Farm.Act : "")} not read yet → run Action calibration (or open Create Room on Hard/Nightmare for this act).";
            string act = S.Farm.Act > 0 ? $"Act {S.Farm.Act}" : "all acts (combined)";
            string stale = S.Farm.DropTarget.Length > 0 && !DropsForSelection().Contains(S.Farm.DropTarget, StringComparer.OrdinalIgnoreCase)
                ? $" Old target \"{S.Farm.DropTarget}\" is not here → not used, pick again." : "";
            return $"Targetable materials on {S.Farm.Map} {act}. Only applies on Hard (x2) / Nightmare (x3).{stale}";
        }
    }
    // ---- v0.3.11 Mirror Blessing: card data + buff priority + card order (not comma-separated text) ----
    public ObservableCollection<BlessingBuffRowViewModel> BlessingBuffRows { get; } = [];
    public ObservableCollection<BlessingCardRowViewModel> BlessingCardRows { get; } = [];
    public List<BlessingInfo> BlessingAll => BlessingCatalog.All(S.Farm.KnownBlessings);
    public List<BuffPref> BlessingBuffList => BlessingCatalog.EffectiveBuffs(S.Farm.BlessingBuffs);
    public List<BlessingPref> BlessingCardList => BlessingCatalog.EffectiveCards(S.Farm.BlessingCards, S.Farm.BlessingPriority, BlessingAll);

    private void RebuildBlessings()
    {
        int nb = BlessingBuffList.Count, nc = BlessingCardList.Count;
        BlessingBuffRows.Clear();
        for (int i = 0; i < nb; i++) BlessingBuffRows.Add(new BlessingBuffRowViewModel(this, i));
        BlessingCardRows.Clear();
        for (int i = 0; i < nc; i++) BlessingCardRows.Add(new BlessingCardRowViewModel(this, i));
        Raise(nameof(BlessingPreview));
    }

    public void SetBuff(int i, BuffPref p) { var l = BlessingBuffList; l[i] = p; Farm(f => f with { BlessingBuffs = l }); Raise(nameof(BlessingPreview)); }
    public void SetCard(int i, BlessingPref p) { var l = BlessingCardList; l[i] = p; Farm(f => f with { BlessingCards = l }); Raise(nameof(BlessingPreview)); }
    public void MoveBuff(int i, int d)
    {
        var l = BlessingBuffList; int j = i + d;
        if (j < 0 || j >= l.Count) return;
        (l[i], l[j]) = (l[j], l[i]);
        Farm(f => f with { BlessingBuffs = l });
        RebuildBlessings();
    }
    public void MoveCard(int i, int d)
    {
        var l = BlessingCardList; int j = i + d;
        if (j < 0 || j >= l.Count) return;
        (l[i], l[j]) = (l[j], l[i]);
        Farm(f => f with { BlessingCards = l });
        RebuildBlessings();
    }
    public RelayCommand ResetBlessingCommand => new(_ => { Farm(f => f with { BlessingBuffs = null, BlessingCards = null }); RebuildBlessings(); });

    /// <summary>Offers currently shown in game → the card that would be picked with the current settings.</summary>
    public string BlessingPreview
    {
        get
        {
            var offers = _root.Snapshot.Blessings;
            if (offers.Count == 0) return "Rule: top enabled buff → card order → highest rarity → largest number. Disabled cards are only picked when all offers are disabled.";
            var pick = BlessingCatalog.Choose(offers, BlessingBuffList, BlessingCardList, S.Farm.KnownBlessings);
            return $"Current offers: {string.Join(" / ", offers.Select(o => $"{o.Name} {o.Value}"))} → pick {pick?.Name}.";
        }
    }

    private string? _blessSig;
    /// <summary>Cards read on the choice screen are saved (name, rarity, description, value); new cards go to the bottom of the order.</summary>
    private void LearnBlessings(IReadOnlyList<BlessingOffer> offers)
    {
        if (offers.Count == 0) return;
        string sig = string.Join("|", offers.Select(o => $"{o.Name}:{o.Rarity}:{o.Description}:{o.Value}"));
        if (sig == _blessSig) return;
        _blessSig = sig;
        var known = new List<BlessingInfo>(S.Farm.KnownBlessings ?? []);
        bool changed = false;
        foreach (var o in offers)
        {
            var info = new BlessingInfo(o.Name.Trim(), o.Rarity.Trim(), o.Description?.Trim() ?? "", o.Value?.Trim() ?? "");
            if (info.Name.Length == 0) continue;
            int k = known.FindIndex(b => b.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase));
            var seen = BlessingCatalog.Seen.FirstOrDefault(b => b.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase));
            if (k >= 0) { if (known[k] != info) { known[k] = info; changed = true; } }
            else if (seen != info) { known.Add(info); changed = true; }
        }
        if (changed)
        {
            bool newName = offers.Any(o => !BlessingCardList.Any(c => c.Name.Equals(o.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            Farm(f => f with { KnownBlessings = known });
            if (newName) _root.AddLog("Mirror Blessing: new cards saved → " + string.Join(", ", offers.Select(o => o.Name)));
            RebuildBlessings();
        }
    }
    public decimal TrialsFloor { get => S.Farm.TrialsFloor; set => Farm(f => f with { TrialsFloor = (int)value }); }
    public string CircleText => S.Farm.LobbyCircle is { } c ? "Fallback: " + c : "Manual fallback: not recorded";
    public string DetectedText => _root.Snapshot.StartRooms is { Count: > 0 } r
        ? $"{r.Count} room(s): {r.Count(x => !x.Occupied)} empty, {r.Count(x => x.Occupied)} occupied"
        : _root.Snapshot.StartCircles.Count is int n and > 0 ? $"Detected {n} \"Start Here\" circle(s)" : "No circle detected yet";
    public string AreasButtonPath { get => S.Farm.AreasButtonPath; set { Farm(f => f with { AreasButtonPath = value ?? "" }); _root.ApplyGuiCalibration(); } }
    public string StoryCardPath { get => S.Farm.StoryCardPath; set { Farm(f => f with { StoryCardPath = value ?? "" }); _root.ApplyGuiCalibration(); } }
    public string HubPickText => _root.Session.Game is { } g
        ? $"Areas → {g.LastAreasPick ?? "-"}\nStory → {g.LastCardPick ?? "-"}"
        : "Areas → -\nStory → -";
    public bool SkipOccupiedRooms { get => S.Farm.SkipOccupiedRooms; set => Farm(f => f with { SkipOccupiedRooms = value }); }
    public decimal JoinTimeoutSec { get => S.Farm.JoinTimeoutSec; set => Farm(f => f with { JoinTimeoutSec = (int)value }); }

    public RelayCommand RecordCircleCommand => new(_ =>
    {
        if (_root.Snapshot.PlayerPos is { } p)
        {
            Farm(f => f with { LobbyCircle = p });
            _root.AddLog($"Create Room circle position recorded: {p}.");
        }
        else _root.AddLog("Record failed: character position not readable (attach + offsets first).");
        Raise(nameof(CircleText));
    });

    public bool AutoShiftLock { get => S.Combat.AutoShiftLock; set { S.Combat = S.Combat with { AutoShiftLock = value }; _root.Save(); } }
    public bool AutoArise { get => S.Combat.AutoArise; set { S.Combat = S.Combat with { AutoArise = value }; _root.Save(); } }
    public decimal AttackIntervalMs { get => S.Combat.AttackIntervalMs; set { S.Combat = S.Combat with { AttackIntervalMs = (int)value }; _root.Save(); } }

    public bool AutoNavigate { get => S.Navigation.AutoNavigate; set { S.Navigation = S.Navigation with { AutoNavigate = value }; _root.Save(); } }
    public bool AttackWhenStuck { get => S.Navigation.AttackWhenStuck; set { S.Navigation = S.Navigation with { AttackWhenStuck = value }; _root.Save(); } }
    public bool ChaseEnemies { get => S.Navigation.ChaseEnemies; set { S.Navigation = S.Navigation with { ChaseEnemies = value }; _root.Save(); } }
    public bool AimAtEnemy { get => S.Navigation.AimAtEnemy; set { S.Navigation = S.Navigation with { AimAtEnemy = value }; _root.Save(); } }
    public bool RoamWhenSkillsCooling { get => S.Navigation.RoamWhenSkillsCooling; set { S.Navigation = S.Navigation with { RoamWhenSkillsCooling = value }; _root.Save(); } }
    public decimal AttackRange { get => (decimal)S.Navigation.AttackRange; set { S.Navigation = S.Navigation with { AttackRange = (double)value }; _root.Save(); } }
    public decimal ArriveRadius { get => (decimal)S.Navigation.ArriveRadius; set { S.Navigation = S.Navigation with { ArriveRadius = (double)value }; _root.Save(); } }

    internal SkillBinding Skills(int i) => S.Combat.EffectiveSkills[i];

    internal void SetSkill(int i, SkillBinding b)
    {
        var list = S.Combat.EffectiveSkills.ToList();
        list[i] = b;
        S.Combat = S.Combat with { Skills = list };
        _root.Save();
    }

    /// <summary>Save the latest map list from the snapshot (while the Create Room menu is open).</summary>
    public void Refresh()
    {
        Raise(nameof(DetectedText));
        Raise(nameof(HubPickText));
        var snap = _root.Snapshot;
        LearnBlessings(snap.Blessings);
        if (snap.Blessings.Count > 0) Raise(nameof(BlessingPreview));
        // v0.2.82: ActsList changes gradually after a map is clicked → acts are saved once (map, act list) is stable ≥ 1 s.
        string actSig = $"{snap.LobbyMapTag}|{string.Join(",", snap.LobbyActs.Select(a => a.Name))}";
        if (actSig != _actSig) { _actSig = actSig; _actSigAt = DateTime.UtcNow; }
        if (snap.LobbyMapTag is { Length: > 0 } tag && snap.LobbyActs.Count > 0 && DateTime.UtcNow - _actSigAt >= TimeSpan.FromSeconds(1))
        {
            var nums = snap.LobbyActs.Select(a => SupaZero.Core.Automation.FarmMachine.ActNumber(a.Name)).Where(n => n > 0).Distinct().Order().ToList();
            string map = snap.LobbyMaps.FirstOrDefault(m => tag.Contains(m.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? tag.Trim();
            var ka = new Dictionary<string, List<int>>(S.Farm.KnownActs ?? new(), StringComparer.OrdinalIgnoreCase);
            if (nums.Count > 0 && !(ka.GetValueOrDefault(map) is { } old0 && old0.SequenceEqual(nums)))
            {
                ka[map] = nums;
                Farm(f => f with { KnownActs = ka });
                RebuildActs();
            }
        }
        // v0.3.4: drops are read directly while the Create Room menu is open on Hard/Nightmare (stable ≥ 1 s).
        string? dtag = snap.LobbyDifficultyTag?.Trim('[', ']', ' ');
        int curAct = SupaZero.Core.Automation.FarmMachine.ActNumber(snap.LobbyActTag);
        if (snap.LobbyMapTag is { Length: > 0 } mtag && curAct > 0 && snap.LobbyRewards.Count > 0 &&
            (dtag?.Equals("Hard", StringComparison.OrdinalIgnoreCase) == true || dtag?.Equals("Nightmare", StringComparison.OrdinalIgnoreCase) == true))
        {
            string dsig = $"{mtag}|{snap.LobbyActTag}|{dtag}|{string.Join(",", snap.LobbyRewards.Select(r => $"{r.Name}:{r.Targetable}"))}";
            if (dsig != _dropSig) { _dropSig = dsig; _dropSigAt = DateTime.UtcNow; }
            else if (DateTime.UtcNow - _dropSigAt >= TimeSpan.FromSeconds(1))
            {
                string map = snap.LobbyMaps.FirstOrDefault(m => mtag.Contains(m.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? mtag.Trim();
                string key = SupaZero.Core.Automation.FarmMachine.DropKey(map, curAct);
                var names = snap.LobbyRewards.Where(r => r.Targetable).Select(r => r.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var kd = new Dictionary<string, List<string>>(S.Farm.KnownDrops ?? new(), StringComparer.OrdinalIgnoreCase);
                if (!(kd.GetValueOrDefault(key) is { } oldD && oldD.SequenceEqual(names)))
                {
                    kd[key] = names;
                    Farm(f => f with { KnownDrops = kd });
                    RebuildDrops();
                }
            }
        }
        var maps = snap.LobbyMaps;
        if (maps.Count == 0) return;
        // v0.2.82: merge, don't overwrite — the map list only holds the selected mode's maps; other modes' maps are kept.
        var fresh = maps.Select(m => new KnownMap(m.Name, m.Locked, m.Mode)).ToList();
        var freshModes = fresh.Select(m => m.Mode).Where(m => m is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = (S.Farm.KnownMaps ?? [])
            .Where(o => !fresh.Any(n => n.Name.Equals(o.Name, StringComparison.OrdinalIgnoreCase)) && o.Mode is not null && !freshModes.Contains(o.Mode))
            .Concat(fresh).ToList();
        if (S.Farm.KnownMaps is { } old && old.SequenceEqual(known)) return;
        Farm(f => f with { KnownMaps = known });
        RebuildMaps();
    }

    private string? _actSig;
    private string? _dropSig;
    private DateTime _dropSigAt;
    private DateTime _actSigAt;

    /// <summary>v0.2.82: the action calibration result replaces the saved map and act lists (acts of the maps that were read).</summary>
    public void ApplyCalibration(SupaZero.Core.Automation.CalibrationScan scan)
    {
        if (scan.Maps.Count > 0) Farm(f => f with { KnownMaps = scan.Maps.ToList() });
        if (scan.Acts.Count > 0)
        {
            var ka = new Dictionary<string, List<int>>(S.Farm.KnownActs ?? new(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in scan.Acts) ka[kv.Key] = kv.Value;
            Farm(f => f with { KnownActs = ka });
        }
        if (scan.Drops.Count > 0)
        {
            var kd = new Dictionary<string, List<string>>(S.Farm.KnownDrops ?? new(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in scan.Drops) kd[kv.Key] = kv.Value;
            Farm(f => f with { KnownDrops = kd });
        }
        RebuildMaps();
        RebuildDrops();
        _root.AddLog($"Action calibration saved: {scan.Maps.Count} map(s), acts for {scan.Acts.Count} map(s), drops for {scan.Drops.Count} act(s)." +
            (scan.Notes.Count > 0 ? " Notes: " + string.Join(" ", scan.Notes) : ""));
    }

    private void RebuildMaps()
    {
        // Remove leftovers of the v0.2.4 bug (MapsList container read as a map).
        if (S.Farm.KnownMaps is { } km && km.Any(m => IsJunk(m.Name)))
            Farm(f => f with { KnownMaps = km.Where(m => !IsJunk(m.Name)).ToList() is { Count: > 0 } k ? k : null });
        if (IsJunk(S.Farm.Map)) Farm(f => f with { Map = "" });
        RebuildModes();
        Maps.Clear();
        Maps.Add(CurrentMapLabel);
        foreach (var m in MapsForMode())
            Maps.Add(m.Locked ? $"{m.Name} (locked)" : m.Name);
        // A saved map that does not belong to this mode → back to automatic.
        if (S.Farm.Map.Length > 0 && !Maps.Any(m => Strip(m) == S.Farm.Map)) Farm(f => f with { Map = "" });
        Raise(nameof(SelectedMap));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Raise(nameof(SelectedMap)));
        Raise(nameof(MapsHint));
        RebuildActs();
    }

    private void RebuildModes()
    {
        var known = (S.Farm.KnownMaps ?? []).Select(m => m.Mode).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (known.Count == 0) known = Enum.GetNames<FarmMode>().ToList();
        if (!known.Any(m => m.Equals(S.Farm.Mode, StringComparison.OrdinalIgnoreCase))) known.Add(S.Farm.Mode);
        if (Modes.SequenceEqual(known)) return;
        Modes.Clear();
        foreach (var m in known) Modes.Add(m);
        Raise(nameof(ModeIndex));
    }

    private static bool IsJunk(string n) => n is "ScrollingBar" or "List" or "UIListLayout" or "UIGridLayout" or "FadeSpacer";

    private static string Strip(string s) => s.Replace(" (locked)", string.Empty).Trim();
}
