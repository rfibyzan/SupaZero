using System.Collections.ObjectModel;
using Avalonia.Threading;
using SupaZero.App.Services;
using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Interop;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

/// <summary>Modes tab (shares the FarmViewModel). v0.3.51: compact rows, each opened on its own page.</summary>
public sealed class ModesPageViewModel
{
    public ModesPageViewModel(FarmViewModel farm)
    {
        Farm = farm;
        DropTrials = Nav.Add("Drop Target & Trials", "Item to farm and the Trials floor.");
        Lobby = Nav.Add("Lobby", "Auto create room, codes, vote, retries.");
        Blessing = Nav.Add("Mirror Blessing", "Evil Reflection buff and card priority.");
    }
    public FarmViewModel Farm { get; }
    public SectionNav Nav { get; } = new();
    public SectionItem DropTrials { get; }
    public SectionItem Lobby { get; }
    public SectionItem Blessing { get; }
}
/// <summary>v0.3.32: Carry tab, only shown in Carry app mode (damage target + carry windows).</summary>
public sealed class CarryPageViewModel(FarmViewModel farm) { public FarmViewModel Farm { get; } = farm; }

public sealed class TabViewModel(string title, object content, bool showHeader = true)
{
    public string Title { get; } = title;
    public object Content { get; } = content;
    public bool ShowHeader { get; } = showHeader;
}

/// <summary>v0.3.25: one running Roblox window in the carry checklist.</summary>
public sealed class CarryWindowItem(RobloxProcessInfo info) : ObservableObject
{
    private bool _isChecked;
    public RobloxProcessInfo Info { get; } = info;
    public string Label => Info.Label;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Root VM: top tabs (General, Story, Upgrade, Settings), read-only memory session, macro runner, log.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly object _readGate = new();
    private readonly DispatcherTimer _timer;
    private int _selectedTabIndex;
    private string _connectionText = "Not attached yet.";
    private string _statusText = "Idle";
    private GameSnapshot _snapshot = new();

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Session = new RobloxSession();
        LoadOffsets();
        Input = new Win32Input(() => Session.ProcessId);
        Runner = new MacroRunner(ReadSnapshot, Input, () => Settings);
        Overlay = new OverlayViewModel(this);
        Runner.RunningChanged += running => { if (running) Overlay.StartedAt = DateTime.Now; };
        Runner.Log += AddLog;
        Runner.Machine.ActCleared = (map, act) => Dispatcher.UIThread.Post(() =>
        {
            var prog = new Dictionary<string, int>(Settings.Farm.ActProgress ?? new(), StringComparer.OrdinalIgnoreCase);
            if (prog.GetValueOrDefault(map) >= act) return;
            prog[map] = act;
            Settings.Farm = Settings.Farm with { ActProgress = prog };
            Save();
            AddLog($"Cleared: {map} Act {act}.");
        });
        Runner.RunningChanged += _ => Dispatcher.UIThread.Post(() => { Raise(nameof(IsRunning)); Raise(nameof(RunStateText)); });
        // Discord webhook: round result + macro stopped.
        Runner.Machine.RoundEnded = r =>
        {
            var w = Settings.Webhook;
            if ((r.Victory ? w.OnVictory : w.OnDefeat) && SupaZero.Core.Notify.DiscordWebhook.IsValidUrl(w.Url))
                _ = SendWebhook(SupaZero.Core.Notify.DiscordWebhook.ForRound(r));
        };
        Runner.RunningChanged += running =>
        {
            var w = Settings.Webhook;
            if (!running && w.OnStop && SupaZero.Core.Notify.DiscordWebhook.IsValidUrl(w.Url))
                _ = SendWebhook(new("Macro stopped", Runner.LastStopReason ?? "Stopped manually.", SupaZero.Core.Notify.DiscordWebhook.Gray,
                    [new("Stages cleared", Runner.Machine.StagesCleared.ToString(), true)]));
        };

        Rotation = new CarryRotation(() => Settings);
        Rotation.Log += AddLog;
        Rotation.RunningChanged += _ => Dispatcher.UIThread.Post(() => { Raise(nameof(IsRunning)); Raise(nameof(RunStateText)); Raise(nameof(CanEditCarryWindows)); Raise(nameof(CanSwitchMode)); });
        Runner.RunningChanged += _ => Dispatcher.UIThread.Post(() => { Raise(nameof(CanEditCarryWindows)); Raise(nameof(CanSwitchMode)); });
        RefreshCarryWindowsCommand = new RelayCommand(_ => RefreshCarryWindows());
        ConfigureCodes(Runner.Machine);
        Rotation.ConfigureMachine = ConfigureCodes;
        _ = FetchCodes();

        General = new GeneralViewModel(this);
        Farm = new FarmViewModel(this);
        Runner.Machine.CalibrationFinished = scan => Dispatcher.UIThread.Post(() => Farm.ApplyCalibration(scan));
        Upgrade = new UpgradeViewModel(this);
        Offsets = new OffsetsViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _carryTab = new TabViewModel("Carry", new CarryPageViewModel(Farm));
        Tabs =
        [
            new TabViewModel("General", General),
            new TabViewModel("Modes", new ModesPageViewModel(Farm)),
            new TabViewModel("Upgrade", Upgrade),
            new TabViewModel("Offsets", Offsets),
            new TabViewModel("Settings", SettingsPage),
        ];

        if (Settings.Combat.CarryMode) Tabs.Insert(1, _carryTab);
        SetModeCommand = new RelayCommand(p => CarryMode = p is string m && m == "Carry");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        RefreshCarryWindows();
    }

    public AppSettings Settings { get; }
    public Win32Input Input { get; }
    public OverlayViewModel Overlay { get; }
    /// <summary>Each timer tick while the macro runs: (Roblox client area on screen, or null).</summary>
    public event Action<(int X, int Y, int W, int H)?>? OverlayTick;
    public RobloxSession Session { get; }
    public MacroRunner Runner { get; }
    public GeneralViewModel General { get; }
    public FarmViewModel Farm { get; }
    public OffsetsViewModel Offsets { get; }
    public UpgradeViewModel Upgrade { get; }
    public SettingsPageViewModel SettingsPage { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    private readonly TabViewModel _carryTab;
    public RelayCommand SetModeCommand { get; }

    /// <summary>v0.3.32 (Byzan 27 Oct): app mode Normal/Carry, switched in the title bar. Carry mode adds the Carry tab and enables
    /// the damage target + carry window rotation. Locked while the macro runs.</summary>
    public bool CarryMode
    {
        get => Settings.Combat.CarryMode;
        set
        {
            if (value == Settings.Combat.CarryMode || IsRunning) { Raise(nameof(CarryMode)); Raise(nameof(NormalMode)); return; }
            var current = SelectedTab;
            Settings.Combat = Settings.Combat with { CarryMode = value };
            if (value && !Tabs.Contains(_carryTab)) Tabs.Insert(1, _carryTab);
            if (!value) Tabs.Remove(_carryTab);
            int idx = value ? 1 : Math.Max(0, Tabs.IndexOf(current));
            _selectedTabIndex = -1;
            SelectedTabIndex = idx;
            Save();
            AddLog(value ? "Mode: Carry (damage target + carry windows active)." : "Mode: Normal (carry settings ignored).");
            Raise(nameof(CarryMode)); Raise(nameof(NormalMode));
        }
    }
    public bool NormalMode => !CarryMode;
    public ObservableCollection<string> LogLines { get; } = [];

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!Set(ref _selectedTabIndex, value)) return;
            Raise(nameof(SelectedTab));
            Raise(nameof(HeaderTitle));
            Raise(nameof(ShowHeader));
        }
    }

    public TabViewModel SelectedTab => Tabs[Math.Clamp(_selectedTabIndex, 0, Tabs.Count - 1)];
    public string HeaderTitle => SelectedTab.Title;
    public bool ShowHeader => SelectedTab.ShowHeader;
    public bool IsRunning => Runner.IsRunning || Rotation.IsRunning;
    public CarryRotation Rotation { get; }
    public ObservableCollection<CarryWindowItem> CarryWindows { get; } = [];
    public RelayCommand RefreshCarryWindowsCommand { get; }
    public bool CanEditCarryWindows => !IsRunning;
    public bool CanSwitchMode => !IsRunning;

    /// <summary>v0.3.25: re-list RobloxPlayerBeta windows (keeps the checks of PIDs still running).</summary>
    public void RefreshCarryWindows()
    {
        if (IsRunning) return;
        var keep = CarryWindows.Where(w => w.IsChecked).Select(w => w.Info.Pid).ToHashSet();
        List<RobloxProcessInfo> list;
        lock (_readGate) list = RobloxSession.ListProcesses(Session.Offsets);
        CarryWindows.Clear();
        foreach (var p in list) CarryWindows.Add(new CarryWindowItem(p) { IsChecked = keep.Contains(p.Pid) });
        AddLog(list.Count == 0 ? "Carry windows: no RobloxPlayerBeta running." : $"Carry windows: {string.Join(", ", list.Select(p => p.Label))}.");
    }

    // v0.3.26 auto redeem codes
    private readonly object _codesGate = new();
    private List<string> _webCodes = [];
    private DateTime _webCodesAt = DateTime.MinValue;
    private readonly HashSet<string> _codesTriedSession = new(StringComparer.OrdinalIgnoreCase);
    private string _codesInfo = "Codes: loading the list from beebom.com…";
    public string CodesInfo { get => _codesInfo; private set { if (Set(ref _codesInfo, value)) Farm?.RaiseCodesInfo(); } }

    private async Task FetchCodes()
    {
        try
        {
            var list = await SupaZero.Core.Features.Codes.CodeList.FetchBeebomAsync();
            lock (_codesGate) { _webCodes = list; _webCodesAt = DateTime.Now; }
            Dispatcher.UIThread.Post(() => { CodesInfo = $"Codes: {list.Count} active on beebom.com ({DateTime.Now:HH:mm})."; AddLog($"Codes list: {list.Count} from beebom.com."); });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => { CodesInfo = "Codes: beebom.com unreachable, only extra codes are used."; AddLog("Codes list failed: " + ex.Message); });
        }
    }

    private void ConfigureCodes(FarmMachine m)
    {
        m.CodesFor = player =>
        {
            lock (_codesGate)
            {
                var done = Settings.Farm.DoneCodes?.GetValueOrDefault(player) ?? [];
                return _webCodes.Concat(SupaZero.Core.Features.Codes.CodeList.ParseManual(Settings.Farm.ExtraCodes))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(c => !done.Contains(c, StringComparer.OrdinalIgnoreCase) && !_codesTriedSession.Contains(player + "|" + c))
                    .ToList();
            }
        };
        m.CodeTried = (player, code, outcome, text) =>
        {
            lock (_codesGate)
            {
                if (outcome is SupaZero.Core.Features.Codes.CodeOutcome.Redeemed or SupaZero.Core.Features.Codes.CodeOutcome.AlreadyUsed)
                {
                    var all = new Dictionary<string, List<string>>(Settings.Farm.DoneCodes ?? new(), StringComparer.OrdinalIgnoreCase);
                    var list = new List<string>(all.GetValueOrDefault(player) ?? []);
                    if (!list.Contains(code, StringComparer.OrdinalIgnoreCase)) list.Add(code);
                    all[player] = list;
                    Settings.Farm = Settings.Farm with { DoneCodes = all };
                }
                else _codesTriedSession.Add(player + "|" + code); // failed / no answer: try again after restarting SupaZero
            }
            Dispatcher.UIThread.Post(Save);
        };
    }

    /// <summary>Hotkey: carry windows checked → carry rotation over them; none checked → single macro on the newest window (old behavior).</summary>
    public void ToggleMacro()
    {
        if (!IsRunning && DateTime.Now - _webCodesAt > TimeSpan.FromMinutes(30)) _ = FetchCodes();
        if (Runner.IsRunning) { Runner.Stop(); return; }
        if (Rotation.IsRunning) { Rotation.Stop(); return; }
        var picks = Settings.Combat.CarryMode ? CarryWindows.Where(w => w.IsChecked).Select(w => w.Info).ToList() : [];
        if (picks.Count == 0) { Runner.Start(); return; }
        if (Session.Offsets is not { } off) { AddLog("Carry rotation: offsets not loaded."); return; }
        if (Settings.Combat.CarryDamage <= 0) AddLog("Carry rotation: 'Carry: stop at damage' is 0 → every window fights until the round ends.");
        Rotation.Start(picks, off);
    }
    public string RunStateText => IsRunning ? "Running" : "Stopped";
    public string ConnectionText { get => _connectionText; private set => Set(ref _connectionText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public GameSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }

    /// <summary>Key rebind currently waiting for input (macro hotkey or skill key). null = none.</summary>
    public Func<string?, bool, bool>? ActiveRebind { get; set; }

    public bool TryCompleteRebind(string? keyName, bool cancel)
    {
        var r = ActiveRebind;
        if (r is null) return false;
        ActiveRebind = null;
        return r(keyName, cancel);
    }

    public event Action? OffsetsChanged;

    private PowerKnowledge? _powers;
    /// <summary>Power names &amp; traits learned from the game, synced to settings.</summary>
    public PowerKnowledge Powers
    {
        get
        {
            if (_powers is not null) return _powers;
            _powers = new PowerKnowledge();
            foreach (var (k, v) in Settings.PowerNames) _powers.Names[k] = v;
            foreach (var (k, v) in Settings.PowerTraits) _powers.Traits[k] = v;
            _powers.Loadout = Settings.PowerLoadout?.ToList() ?? [];
            return _powers;
        }
    }

    private string? _lastTraitDebug, _lastPowerDebug;

    /// <summary>The game's built-in Power auto spin / Trait reroll finished → log + Discord webhook.</summary>
    private void CheckSpin()
    {
        if (Session.Game is not { PendingSpin: { } r } g) return;
        g.PendingSpin = null;
        AddLog($"{r.Title}: {(r.IsTrait ? $"{r.Power} = {r.Result} {r.Chance}" : $"{r.Result} {r.Rarity}")}".TrimEnd());
        var w = Settings.Webhook;
        if (w.OnSpin && SupaZero.Core.Notify.DiscordWebhook.IsValidUrl(w.Url))
            _ = SendWebhook(SupaZero.Core.Notify.DiscordWebhook.ForSpin(r));
    }

    private void SyncPowers()
    {
        if (Session.Game?.TraitDebug is { } dbg && dbg != _lastTraitDebug) { _lastTraitDebug = dbg; AddLog(dbg); }
        if (Session.Game?.PowerDebug is { } pd && pd != _lastPowerDebug) { _lastPowerDebug = pd; AddLog(pd); }
        var p = Powers;
        if (!p.Dirty) return;
        p.Dirty = false;
        lock (_readGate)
        {
            Settings.PowerNames = new Dictionary<string, string>(p.Names);
            Settings.PowerTraits = new Dictionary<string, PowerTrait>(p.Traits);
            Settings.PowerLoadout = p.Loadout.ToList();
        }
        Save();
        AddLog("Power/trait data updated from the game.");
    }

    public void LoadOffsets(string? explicitPath = null)
    {
        string? path = explicitPath ?? RobloxSession.FindOffsets(Settings.OffsetsPath);
        if (path is null) { AddLog("offsets.json not found."); OffsetsChanged?.Invoke(); return; }
        try
        {
            lock (_readGate) Session.LoadOffsets(path);
            ApplyGuiCalibration();
            AddLog("Offsets loaded: " + path);
        }
        catch (Exception ex) { AddLog("Failed to load offsets: " + ex.Message); }
        OffsetsChanged?.Invoke();
    }

    public void ApplyGuiCalibration()
    {
        if (Session.Game is { } g)
        {
            g.GuiInsetY = Settings.General.GuiInsetY;
            g.GuiScale = Settings.General.GuiScale;
            g.LobbyInsetY = Settings.General.LobbyInsetY;
            g.Powers = Powers;
            g.AreasButtonPath = Settings.Farm.AreasButtonPath;
            g.StoryCardPath = Settings.Farm.StoryCardPath;
            g.SeedMaterials(Settings.Farm.KnownMaterialNames);
        }
    }

    /// <summary>Used by the runner (background thread) and the UI timer; locked so two readers don't fight over the session.</summary>
    public GameSnapshot ReadSnapshot()
    {
        lock (_readGate) return Session.ReadSnapshot();
    }

    private object? _matSeeded;
    private int _matVer = -1;

    /// <summary>v0.3.5: material names (icon/key materialNNN) learned by the reader are saved to settings; a new reader is seeded from settings.</summary>
    private void SyncMaterials()
    {
        if (Session.Game is not { } g) return;
        if (!ReferenceEquals(g, _matSeeded)) { g.SeedMaterials(Settings.Farm.KnownMaterialNames); _matSeeded = g; _matVer = g.MaterialVersion; }
        else if (g.MaterialVersion != _matVer)
        {
            _matVer = g.MaterialVersion;
            var all = new Dictionary<string, string>(Settings.Farm.KnownMaterialNames ?? new(), StringComparer.Ordinal);
            foreach (var kv in g.LearnedMaterials()) all[kv.Key] = kv.Value;
            Settings.Farm = Settings.Farm with { KnownMaterialNames = all };
            Save();
        }
        if (g.UnknownMaterial is { } u)
        {
            g.UnknownMaterial = null;
            AddLog($"Drop name unknown yet: {u}. Open the Create Room menu for that map/act once (Rewards panel) so its name gets read.");
        }
    }

    private void Refresh()
    {
        // While the macro runs, the latest snapshot comes from the runner; when stopped, read it ourselves (once per timer tick).
        var snap = Runner.IsRunning ? Runner.LastSnapshot ?? new GameSnapshot() : ReadSnapshot();
        Snapshot = snap;
        if (snap.LocalPlayer.Length > 0) _lastAccount = snap.LocalPlayer;
        string place = snap.PlaceId == AnimeZeroPaths.PlaceId ? "Anime Zero" : snap.PlaceId == 0 ? "-" : $"place {snap.PlaceId}";
        ConnectionText = Rotation.IsRunning
            ? $"Carry rotation: {Rotation.Alts.Count} windows · front {Rotation.FrontName ?? "-"}"
            : Session.IsReady
            ? $"{Session.Status} · {place} · {(snap.LocalPlayer.Length > 0 ? snap.LocalPlayer : "?")} · screen {snap.Screen}"
            : Session.Status;
        StatusText = Runner.IsRunning ? $"{Runner.Machine.Phase}: {Runner.Machine.Status}" : $"Screen: {snap.Screen}";
        SyncPowers();
        CheckSpin();
        General.Refresh();
        Farm.Refresh();
        SyncMaterials();
        if (Runner.IsRunning)
        {
            Overlay.Refresh(snap);
            OverlayTick?.Invoke(Input.ClientScreenRect());
        }
    }

    private string? _lastAccount;

    public async Task SendWebhook(SupaZero.Core.Notify.DiscordWebhook.Embed embed)
    {
        // v0.3.50: name the Roblox account (LocalPlayer) so notifications from several accounts can be told apart.
        string? account = Runner.LastSnapshot?.LocalPlayer is { Length: > 0 } lp ? lp : Snapshot.LocalPlayer is { Length: > 0 } sp ? sp : _lastAccount;
        if (account is { Length: > 0 }) _lastAccount = account;
        var err = await SupaZero.Core.Notify.DiscordWebhook.SendAsync(Settings.Webhook.Url, embed, Settings.Webhook.Mention, account);
        Dispatcher.UIThread.Post(() => AddLog(err is null ? $"Webhook sent: {embed.Title}." : err));
    }

    public void AddLog(string line)
    {
        AppLog.Line(line);
        string text = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Insert(0, text);
            while (LogLines.Count > 500) LogLines.RemoveAt(LogLines.Count - 1);
        });
    }

    public void Save()
    {
        try { Settings.Save(); } catch (Exception ex) { AppLog.Line("[ERROR] save: " + ex.Message); }
    }

    public void Dispose()
    {
        _timer.Stop();
        Runner.Dispose();
        Rotation.Dispose();
        Save();
        Session.Dispose();
    }
}
