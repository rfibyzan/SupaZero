using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SupaZero.App.Services;
using SupaZero.App.ViewModels;

namespace SupaZero.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private GlobalHotkey? _hotkey;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            _hotkey = new GlobalHotkey(this, () => Dispatcher.UIThread.Post(_vm.Runner.Toggle));
            _vm.General.RegisterHotkey = _hotkey.Register;
            _vm.General.HotkeyError = () => _hotkey.LastError;
            _vm.General.ApplyHotkey();
        };
        Closing += (_, _) =>
        {
            _hotkey?.Dispose();
            _vm.Dispose();
            AppLog.Line("[SupaZero] Application exit.");
        };
        ThemeService.Changed += _ => ApplyTransparency(_vm.Settings.General.TransparentUi);
        ThemeService.Apply(_vm.Settings);
        SettingsPageViewModel.TransparencyChanged = ApplyTransparency;
        ApplyTransparency(_vm.Settings.General.TransparentUi);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm.ActiveRebind is null) return;
        bool cancel = e.Key == Key.Escape;
        string? name = cancel ? null : KeyNames.From(e.Key == Key.System ? Key.LeftAlt : e.Key);
        if (_vm.TryCompleteRebind(name, cancel)) e.Handled = true;
    }

    private void ApplyTransparency(bool on)
    {
        TransparencyLevelHint = on
            ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]
            : [WindowTransparencyLevel.None];
        Background = new SolidColorBrush(ThemeService.ToColor(ThemeService.Current.Background, on ? (byte)0xD9 : (byte)0xFF));
    }
}
