using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SupaZero.App.ViewModels;

namespace SupaZero.App.Views;

/// <summary>Settings: preview Running Overlay (drag kotak → SettingsPageViewModel.MovePreviewBox), seperti SupaFisch.</summary>
public partial class SettingsView : UserControl
{
    private Point? _grab;

    public SettingsView()
    {
        InitializeComponent();
        var box = this.FindControl<Border>("PreviewBox");
        var area = this.FindControl<Canvas>("PreviewArea");
        if (box is null || area is null) return;
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            _grab = e.GetPosition(box);
            e.Pointer.Capture(box);
            e.Handled = true;
        };
        box.PointerMoved += (_, e) =>
        {
            if (_grab is not { } g || DataContext is not SettingsPageViewModel vm) return;
            var p = e.GetPosition(area);
            vm.MovePreviewBox(p.X - g.X, p.Y - g.Y);
            e.Handled = true;
        };
        box.PointerReleased += (_, e) => { if (_grab is null) return; e.Pointer.Capture(null); End(); e.Handled = true; };
        box.PointerCaptureLost += (_, _) => { if (_grab is not null) End(); };
    }

    private void End()
    {
        _grab = null;
        (DataContext as SettingsPageViewModel)?.CommitPreview();
    }
}
