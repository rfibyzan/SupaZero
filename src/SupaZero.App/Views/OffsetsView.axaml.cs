using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SupaZero.App.ViewModels;

namespace SupaZero.App.Views;

public partial class OffsetsView : UserControl
{
    public OffsetsView() => InitializeComponent();

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp || DataContext is not OffsetsViewModel vm) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select offsets.json",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.Path = path;
            vm.LoadCommand.Execute(null);
        }
    }
}
