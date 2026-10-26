using System.Collections.ObjectModel;
using Avalonia.Threading;
using SupaZero.App.Services;
using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Interop;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

/// <summary>Tab pages that share the FarmViewModel (Combat, Lobby).</summary>
public sealed class CombatPageViewModel(FarmViewModel farm) { public FarmViewModel Farm { get; } = farm; }
public sealed class LobbyPageViewModel(FarmViewModel farm) { public FarmViewModel Farm { get; } = farm; }
public sealed class ModesPageViewModel(FarmViewModel farm) { public FarmViewModel Farm { get; } = farm; }

public sealed class TabViewModel(string title, object content, bool showHeader = true)
{
    public string Title { get; } = title;
    public object Content { get; } = content;
    public bool ShowHeader { get; } = showHeader;
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

        General = new GeneralViewModel(this);
        Farm = new FarmViewModel(this);
        Runner.Machine.CalibrationFinished = scan => Dispatcher.UIThread.Post(() => Farm.ApplyCalibration(scan));
        Upgrade = new UpgradeViewModel(this);
        Offsets = new OffsetsViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        Tabs =
        [
            new TabViewModel("General", General),
            new TabViewModel("Combat", new CombatPageViewModel(Farm)),
            new TabViewModel("Lobby", new LobbyPageViewModel(Farm)),
            new TabViewModel("Modes", new ModesPageViewModel(Farm)),
            new TabViewModel("Upgrade", Upgrade),
            new TabViewModel("Offsets", Offsets),
            new TabViewModel("Settings", SettingsPage),
        ];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
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
    public IReadOnlyList<TabViewModel> Tabs { get; }
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
    public bool IsRunning => Runner.IsRunning;
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
        string place = snap.PlaceId == AnimeZeroPaths.PlaceId ? "Anime Zero" : snap.PlaceId == 0 ? "-" : $"place {snap.PlaceId}";
        ConnectionText = Session.IsReady
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

    public async Task SendWebhook(SupaZero.Core.Notify.DiscordWebhook.Embed embed)
    {
        var err = await SupaZero.Core.Notify.DiscordWebhook.SendAsync(Settings.Webhook.Url, embed, Settings.Webhook.Mention);
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
        Save();
        Session.Dispose();
    }
}
