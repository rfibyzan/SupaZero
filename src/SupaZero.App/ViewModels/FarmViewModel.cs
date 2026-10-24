using System.Collections.ObjectModel;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

/// <summary>Satu baris tombol skill: badge tombol + Rebind (seperti Macro hotkey), toggle aktif, cooldown.</summary>
public sealed class SkillRowViewModel(FarmViewModel owner, int index) : ObservableObject
{
    private bool _rebinding;
    private SkillBinding B => owner.Skills(index);

    public string Name => B.Name;
    public string KeyText => _rebinding ? "..." : B.Key;
    public string RebindText => _rebinding ? "Tekan tombol" : "Rebind";
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

public sealed class FarmViewModel : ObservableObject
{
    private const string CurrentMapLabel = "(otomatis sesuai mode)";
    private readonly MainViewModel _root;

    public FarmViewModel(MainViewModel root)
    {
        _root = root;
        var skills = S.Combat.EffectiveSkills.ToList();
        S.Combat = S.Combat with { Skills = skills };
        for (int i = 0; i < skills.Count; i++) SkillRows.Add(new SkillRowViewModel(this, i));
        RebuildMaps();
    }

    public MainViewModel Root => _root;
    private AppSettings S => _root.Settings;
    private void Farm(Func<FarmSettings, FarmSettings> f) { S.Farm = f(S.Farm); _root.Save(); }

    /// <summary>Mode dari label "Mode:" kartu map yang terbaca di game (urutan kartu); bawaan bila belum pernah terbaca.</summary>
    public ObservableCollection<string> Modes { get; } = [];
    private const string AllActsLabel = "Semua act (berurutan)";
    public ObservableCollection<string> Acts { get; } = [];
    public IReadOnlyList<string> Difficulties { get; } = Enum.GetNames<Difficulty>();
    public IReadOnlyList<string> AfterWins { get; } = ["Otomatis", "Next", "Retry", "Leave"];
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
            // Map yang tidak cocok dengan mode baru → kembali ke "map terpilih" (macro pilih map sesuai mode sendiri).
            bool keep = S.Farm.KnownMaps?.FirstOrDefault(m => m.Name == S.Farm.Map) is { Mode: { } mm } && mm.Equals(mode, StringComparison.OrdinalIgnoreCase);
            Farm(f => f with { Mode = mode, Map = keep ? f.Map : "" });
            RebuildMaps();
        }
    }

    /// <summary>Act untuk map terpilih: dari ActsList yang pernah terbaca, kalau belum → 1–5.</summary>
    private List<int> ActNumbers() =>
        S.Farm.Map.Length > 0 && S.Farm.KnownActs?.GetValueOrDefault(S.Farm.Map) is { Count: > 0 } a ? a : [1, 2, 3, 4, 5];

    public int ActIndex
    {
        get => S.Farm.Act == 0 ? 0 : Math.Max(0, ActNumbers().IndexOf(S.Farm.Act) + 1);
        set
        {
            if (value < 0) return;
            var nums = ActNumbers();
            int act = value == 0 ? 0 : nums[Math.Clamp(value - 1, 0, nums.Count - 1)];
            if (act != S.Farm.Act) Farm(f => f with { Act = act });
        }
    }

    private void RebuildActs()
    {
        var nums = ActNumbers();
        if (S.Farm.Act > 0 && !nums.Contains(S.Farm.Act)) Farm(f => f with { Act = nums.Where(n => n <= f.Act).DefaultIfEmpty(nums[0]).Max() });
        var want = new List<string> { AllActsLabel };
        want.AddRange(nums.Select(n => "Act " + n));
        if (!Acts.SequenceEqual(want))
        {
            // Clear() membuat ComboBox jatuh ke SelectedIndex -1 (kotak Act kosong); isi ulang lalu set ulang pilihan setelah layout.
            Acts.Clear();
            foreach (var x in want) Acts.Add(x);
        }
        Raise(nameof(ActIndex));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Raise(nameof(ActIndex)), Avalonia.Threading.DispatcherPriority.Background);
    }
    public string ActProgressText => S.Farm.ActProgress is { Count: > 0 } p
        ? "Tuntas: " + string.Join(", ", p.Select(kv => $"{kv.Key} Act {kv.Value}"))
        : "Semua act: mulai dari Act 1, lanjut otomatis tiap menang. Act yang tidak ada di map disesuaikan otomatis.";
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
        ? "Buka menu Create Room sekali di game: daftar map + status terkunci akan terbaca otomatis."
        : !S.Farm.KnownMaps.Any(m => m.Mode is { Length: > 0 })
            ? "Mode map belum terbaca. Buka menu Create Room sekali lagi agar map terkelompok per mode."
            : MapsForMode().ToList() is { Count: > 0 } k
                ? $"Map {S.Farm.Mode}: {k.Count(m => !m.Locked)} terbuka, {k.Count(m => m.Locked)} terkunci."
                : $"Belum ada map untuk mode {S.Farm.Mode}.";

    /// <summary>Map milik mode terpilih. Map tanpa label mode hanya ikut bila belum ada satu pun map yang mode-nya terbaca.</summary>
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
    public string DropTarget { get => S.Farm.DropTarget; set => Farm(f => f with { DropTarget = value ?? "" }); }
    public string[] KnownMaterials { get; } = [.. SupaZero.Core.Game.AnimeZeroPaths.MaterialIcons.Values.Order()];
    public string BlessingPriority { get => S.Farm.BlessingPriority; set => Farm(f => f with { BlessingPriority = value ?? "" }); }
    public decimal TrialsFloor { get => S.Farm.TrialsFloor; set => Farm(f => f with { TrialsFloor = (int)value }); }
    public string CircleText => S.Farm.LobbyCircle is { } c ? "Cadangan: " + c : "Cadangan manual: belum direkam";
    public string DetectedText => _root.Snapshot.StartRooms is { Count: > 0 } r
        ? $"{r.Count} room: {r.Count(x => !x.Occupied)} kosong, {r.Count(x => x.Occupied)} terisi"
        : _root.Snapshot.StartCircles.Count is int n and > 0 ? $"Terdeteksi {n} lingkaran \"Start Here\"" : "Lingkaran belum terdeteksi";
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
            _root.AddLog($"Posisi lingkaran Create Room direkam: {p}.");
        }
        else _root.AddLog("Gagal rekam: posisi karakter tidak terbaca (attach + offsets dulu).");
        Raise(nameof(CircleText));
    });

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

    /// <summary>Simpan daftar map terbaru dari snapshot (saat menu Create Room terbuka).</summary>
    public void Refresh()
    {
        Raise(nameof(DetectedText));
        Raise(nameof(HubPickText));
        var snap = _root.Snapshot;
        if (snap.LobbyMapTag is { Length: > 0 } tag && snap.LobbyActs.Count > 0)
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
        var maps = snap.LobbyMaps;
        if (maps.Count == 0) return;
        var known = maps.Select(m => new KnownMap(m.Name, m.Locked, m.Mode)).ToList();
        if (S.Farm.KnownMaps is { } old && old.SequenceEqual(known)) return;
        Farm(f => f with { KnownMaps = known });
        RebuildMaps();
    }

    private void RebuildMaps()
    {
        // Buang sisa bug v0.2.4 (wadah MapsList terbaca sebagai map).
        if (S.Farm.KnownMaps is { } km && km.Any(m => IsJunk(m.Name)))
            Farm(f => f with { KnownMaps = km.Where(m => !IsJunk(m.Name)).ToList() is { Count: > 0 } k ? k : null });
        if (IsJunk(S.Farm.Map)) Farm(f => f with { Map = "" });
        RebuildModes();
        Maps.Clear();
        Maps.Add(CurrentMapLabel);
        foreach (var m in MapsForMode())
            Maps.Add(m.Locked ? $"{m.Name} (terkunci)" : m.Name);
        // Map tersimpan yang bukan milik mode ini → kembali ke otomatis.
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

    private static string Strip(string s) => s.Replace(" (terkunci)", string.Empty).Trim();
}
