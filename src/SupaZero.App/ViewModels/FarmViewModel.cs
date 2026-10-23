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
    private const string CurrentMapLabel = "(map yang terpilih di lobby)";
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

    public IReadOnlyList<string> Modes { get; } = Enum.GetNames<FarmMode>();
    public IReadOnlyList<string> Acts { get; } = ["Act 1", "Act 2", "Act 3", "Act 4", "Act 5"];
    public IReadOnlyList<string> Difficulties { get; } = Enum.GetNames<Difficulty>();
    public IReadOnlyList<string> AfterWins { get; } = ["Otomatis", "Next", "Retry", "Leave"];
    public ObservableCollection<string> Maps { get; } = [];
    public ObservableCollection<SkillRowViewModel> SkillRows { get; } = [];

    public int ModeIndex { get => (int)S.Farm.Mode; set => Farm(f => f with { Mode = (FarmMode)Math.Clamp(value, 0, Modes.Count - 1) }); }
    public int ActIndex { get => Math.Clamp(S.Farm.Act - 1, 0, Acts.Count - 1); set => Farm(f => f with { Act = Math.Clamp(value, 0, Acts.Count - 1) + 1 }); }
    public int DifficultyIndex { get => (int)S.Farm.Difficulty; set => Farm(f => f with { Difficulty = (Difficulty)Math.Clamp(value, 0, 3) }); }
    public int AfterWinIndex { get => (int)S.Farm.AfterWin; set => Farm(f => f with { AfterWin = (AfterWin)Math.Clamp(value, 0, 3) }); }

    public string? SelectedMap
    {
        get => S.Farm.Map.Length == 0 ? CurrentMapLabel : Maps.FirstOrDefault(m => Strip(m) == S.Farm.Map) ?? S.Farm.Map;
        set
        {
            if (value is null) return;
            string name = value == CurrentMapLabel ? string.Empty : Strip(value);
            if (name != S.Farm.Map) Farm(f => f with { Map = name });
        }
    }

    public string MapsHint => S.Farm.KnownMaps is { Count: > 0 } k
        ? $"{k.Count(m => !m.Locked)} terbuka, {k.Count(m => m.Locked)} terkunci (terbaca dari menu Create Room)."
        : "Buka menu Create Room sekali di game: daftar map + status terkunci akan terbaca otomatis.";

    public bool AutoCreateLobby { get => S.Farm.AutoCreateLobby; set => Farm(f => f with { AutoCreateLobby = value }); }
    public bool AutoVoteStart { get => S.Farm.AutoVoteStart; set => Farm(f => f with { AutoVoteStart = value }); }
    public bool RetryOnDefeat { get => S.Farm.RetryOnDefeat; set => Farm(f => f with { RetryOnDefeat = value }); }
    public decimal MaxRetries { get => S.Farm.MaxRetriesPerStage; set => Farm(f => f with { MaxRetriesPerStage = (int)value }); }
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
        var maps = _root.Snapshot.LobbyMaps;
        if (maps.Count == 0) return;
        var known = maps.Select(m => new KnownMap(m.Name, m.Locked)).ToList();
        if (S.Farm.KnownMaps is { } old && old.SequenceEqual(known)) return;
        Farm(f => f with { KnownMaps = known });
        RebuildMaps();
    }

    private void RebuildMaps()
    {
        Maps.Clear();
        Maps.Add(CurrentMapLabel);
        foreach (var m in S.Farm.KnownMaps ?? []) Maps.Add(m.Locked ? $"{m.Name} (terkunci)" : m.Name);
        if (S.Farm.Map.Length > 0 && !Maps.Any(m => Strip(m) == S.Farm.Map)) Maps.Add(S.Farm.Map);
        Raise(nameof(SelectedMap));
        Raise(nameof(MapsHint));
    }

    private static string Strip(string s) => s.Replace(" (terkunci)", string.Empty).Trim();
}
