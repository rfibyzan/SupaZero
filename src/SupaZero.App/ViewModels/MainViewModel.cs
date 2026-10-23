using System.Collections.ObjectModel;
using Avalonia.Threading;
using SupaZero.App.Services;
using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Interop;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

public sealed class TabViewModel(string title, object content, bool showHeader = true)
{
    public string Title { get; } = title;
    public object Content { get; } = content;
    public bool ShowHeader { get; } = showHeader;
}

/// <summary>Root VM: tab atas (General, Story, Upgrade, Settings), sesi memori read-only, runner macro, log.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly object _readGate = new();
    private readonly DispatcherTimer _timer;
    private int _selectedTabIndex;
    private string _connectionText = "Belum attach.";
    private string _statusText = "Idle";
    private GameSnapshot _snapshot = new();

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Session = new RobloxSession();
        LoadOffsets();
        Runner = new MacroRunner(ReadSnapshot, new Win32Input(() => Session.ProcessId), () => Settings);
        Runner.Log += AddLog;
        Runner.Machine.ActCleared = (map, act) => Dispatcher.UIThread.Post(() =>
        {
            var prog = new Dictionary<string, int>(Settings.Farm.ActProgress ?? new(), StringComparer.OrdinalIgnoreCase);
            if (prog.GetValueOrDefault(map) >= act) return;
            prog[map] = act;
            Settings.Farm = Settings.Farm with { ActProgress = prog };
            Save();
            AddLog($"Tuntas: {map} Act {act}.");
        });
        Runner.RunningChanged += _ => Dispatcher.UIThread.Post(() => { Raise(nameof(IsRunning)); Raise(nameof(RunStateText)); });

        General = new GeneralViewModel(this);
        Farm = new FarmViewModel(this);
        Upgrade = new UpgradeViewModel(this);
        Offsets = new OffsetsViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        Tabs =
        [
            new TabViewModel("General", General),
            new TabViewModel("AutoFarm", Farm),
            new TabViewModel("Upgrade", Upgrade),
            new TabViewModel("Offsets", Offsets),
            new TabViewModel("Settings", SettingsPage),
        ];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public AppSettings Settings { get; }
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

    /// <summary>Rebind tombol yang sedang menunggu input (hotkey macro atau tombol skill). null = tidak ada.</summary>
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
    /// <summary>Nama &amp; trait power yang dipelajari dari game, disinkronkan ke settings.</summary>
    public PowerKnowledge Powers
    {
        get
        {
            if (_powers is not null) return _powers;
            _powers = new PowerKnowledge();
            foreach (var (k, v) in Settings.PowerNames) _powers.Names[k] = v;
            foreach (var (k, v) in Settings.PowerTraits) _powers.Traits[k] = v;
            return _powers;
        }
    }

    private void SyncPowers()
    {
        var p = Powers;
        if (!p.Dirty) return;
        p.Dirty = false;
        lock (_readGate)
        {
            Settings.PowerNames = new Dictionary<string, string>(p.Names);
            Settings.PowerTraits = new Dictionary<string, PowerTrait>(p.Traits);
        }
        Save();
        AddLog("Data power/trait diperbarui dari game.");
    }

    public void LoadOffsets(string? explicitPath = null)
    {
        string? path = explicitPath ?? RobloxSession.FindOffsets(Settings.OffsetsPath);
        if (path is null) { AddLog("offsets.json tidak ditemukan."); OffsetsChanged?.Invoke(); return; }
        try
        {
            lock (_readGate) Session.LoadOffsets(path);
            ApplyGuiCalibration();
            AddLog("Offsets dimuat: " + path);
        }
        catch (Exception ex) { AddLog("Gagal memuat offsets: " + ex.Message); }
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
        }
    }

    /// <summary>Dipakai runner (thread latar) dan timer UI; dikunci agar dua pembaca tidak berebut sesi.</summary>
    public GameSnapshot ReadSnapshot()
    {
        lock (_readGate) return Session.ReadSnapshot();
    }

    private void Refresh()
    {
        // Saat macro jalan, snapshot terbaru datang dari runner; saat berhenti, baca sendiri (1x per tick timer).
        var snap = Runner.IsRunning ? Runner.LastSnapshot ?? new GameSnapshot() : ReadSnapshot();
        Snapshot = snap;
        string place = snap.PlaceId == AnimeZeroPaths.PlaceId ? "Anime Zero" : snap.PlaceId == 0 ? "-" : $"place {snap.PlaceId}";
        ConnectionText = Session.IsReady
            ? $"{Session.Status} · {place} · {(snap.LocalPlayer.Length > 0 ? snap.LocalPlayer : "?")} · layar {snap.Screen}"
            : Session.Status;
        StatusText = Runner.IsRunning ? $"{Runner.Machine.Phase}: {Runner.Machine.Status}" : $"Layar: {snap.Screen}";
        SyncPowers();
        General.Refresh();
        Farm.Refresh();
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
