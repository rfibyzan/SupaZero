using SupaZero.Core.Game;
using SupaZero.Core.Input;

namespace SupaZero.App.ViewModels;

public sealed class GeneralViewModel(MainViewModel root) : ObservableObject
{
    private bool _isRebinding;
    private string _hotkeyStatus = string.Empty;

    public MainViewModel Root => root;
    public string HotkeyText => _isRebinding ? "..." : root.Settings.General.MacroHotkey;
    public string RebindText => _isRebinding ? "Tekan tombol" : "Rebind";
    public bool IsRebinding => _isRebinding;
    public string HotkeyStatus { get => _hotkeyStatus; private set => Set(ref _hotkeyStatus, value); }
    public string HotkeyHint => $"Tekan {root.Settings.General.MacroHotkey} untuk Start/Stop macro.";

    public Func<string?, bool>? RegisterHotkey { get; set; }
    public Func<string?>? HotkeyError { get; set; }

    public RelayCommand RebindCommand => new(_ =>
    {
        _isRebinding = true;
        root.ActiveRebind = CompleteRebind;
        RaiseHotkey();
    });
    public RelayCommand ToggleCommand => new(_ => root.Runner.Toggle());
    public System.Collections.ObjectModel.ObservableCollection<SupaZero.Core.Game.CalibrationItem> CalibrationItems { get; } = [];
    private string _calibrationSummary = "Belum dikalibrasi. Tekan Kalibrasi di lobby atau saat di dalam stage.";
    public string CalibrationSummary { get => _calibrationSummary; private set => Set(ref _calibrationSummary, value); }

    /// <summary>Kalibrasi cepat: satu bacaan memori, semua pengecekan ditampilkan dan ditulis ke log.</summary>
    public RelayCommand CalibrateCommand => new(_ =>
    {
        var snap = root.Runner.IsRunning ? root.Runner.LastSnapshot ?? root.ReadSnapshot() : root.ReadSnapshot();
        if (snap.Equipped.Count == 0 && root.Powers.SavedLoadout() is { Count: > 0 } saved) snap = snap with { Equipped = saved };
        var items = SupaZero.Core.Game.Calibration.Run(snap, root.Settings);
        CalibrationItems.Clear();
        foreach (var i in items) CalibrationItems.Add(i);
        CalibrationSummary = $"{DateTime.Now:HH:mm:ss} · layar {snap.Screen} · {SupaZero.Core.Game.Calibration.Summary(items)}";
        root.AddLog("Kalibrasi: " + SupaZero.Core.Game.Calibration.Summary(items));
        foreach (var i in items.Where(i => i.State is SupaZero.Core.Game.CheckState.Warn or SupaZero.Core.Game.CheckState.Fail))
            root.AddLog("  " + i);
    });

    public RelayCommand ClearLogCommand => new(_ => root.LogLines.Clear());

    public void ApplyHotkey()
    {
        bool ok = RegisterHotkey?.Invoke(root.Settings.General.MacroHotkey) ?? false;
        HotkeyStatus = ok ? string.Empty : HotkeyError?.Invoke() ?? string.Empty;
    }

    /// <summary>Dipanggil lewat MainViewModel.ActiveRebind saat tombol berikutnya ditekan. True = event ditangani.</summary>
    public bool CompleteRebind(string? name, bool cancel)
    {
        if (!_isRebinding) return false;
        _isRebinding = false;
        if (!cancel && HotkeyMap.Normalize(name) is { } key)
        {
            root.Settings.General = root.Settings.General with { MacroHotkey = key };
            root.Save();
            ApplyHotkey();
        }
        RaiseHotkey();
        return true;
    }

    private void RaiseHotkey()
    {
        Raise(nameof(HotkeyText));
        Raise(nameof(RebindText));
        Raise(nameof(IsRebinding));
        Raise(nameof(HotkeyHint));
    }

    // Kartu status ronde
    public string ScreenText => root.Snapshot.Screen.ToString();
    public string PhaseText => root.StatusText;
    public string AreaText => root.Snapshot.Hud is { AreaTotal: > 0 } h ? $"{h.Area}/{h.AreaTotal}" : "-";
    public string EnemiesText => root.Snapshot.EnemyCount >= 0 ? root.Snapshot.EnemyCount.ToString() : "-";
    public string StagesText => root.Runner.Machine.StagesCleared.ToString();
    public string RetriesText => root.Runner.Machine.Retries.ToString();
    public string HudRawText => root.Snapshot.HudRaw ?? "-";

    // Kartu power terpasang
    public string Power1Name => PowerName(1);
    public string Power1Detail => PowerDetail(1);
    public string Power2Name => PowerName(2);
    public string Power2Detail => PowerDetail(2);

    private EquippedPower? Power(int loadout) => root.Snapshot.Equipped.FirstOrDefault(p => p.Loadout == loadout);
    private string PowerName(int loadout) => Power(loadout)?.Name ?? "-";

    private string PowerDetail(int loadout)
    {
        if (Power(loadout) is not { } p) return root.Snapshot.Attached ? "Belum terbaca (buka menu karakter sekali di lobby)" : "-";
        var parts = new List<string>();
        if (p.Level is { } lv) parts.Add($"Lv.{lv}");
        if (p.Rarity is { Length: > 0 } r) parts.Add(r);
        parts.Add(p.Trait is { } t
            ? $"Trait {t.Name}{(t.Chance is { } c ? $" ({c})" : "")}{(t.Buffs.Count > 0 ? " · " + string.Join(" ", t.Buffs) : "")}"
            : "Trait ? (buka menu Trait sekali)");
        return string.Join(" · ", parts);
    }

    public void Refresh()
    {
        Raise(nameof(Power1Name));
        Raise(nameof(Power1Detail));
        Raise(nameof(Power2Name));
        Raise(nameof(Power2Detail));
        Raise(nameof(ScreenText));
        Raise(nameof(PhaseText));
        Raise(nameof(AreaText));
        Raise(nameof(EnemiesText));
        Raise(nameof(StagesText));
        Raise(nameof(RetriesText));
        Raise(nameof(HudRawText));
    }
}
