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

    public void Refresh()
    {
        Raise(nameof(ScreenText));
        Raise(nameof(PhaseText));
        Raise(nameof(AreaText));
        Raise(nameof(EnemiesText));
        Raise(nameof(StagesText));
        Raise(nameof(RetriesText));
        Raise(nameof(HudRawText));
    }
}
