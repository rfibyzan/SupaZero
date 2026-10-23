using Avalonia.Media;
using SupaZero.App.Services;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

public sealed class ThemeChoiceViewModel(string name, Action<string> select) : ObservableObject
{
    private bool _isSelected;
    public string Name { get; } = name;
    public IBrush Swatch { get; } = SolidColorBrush.Parse(ThemePalettes.SwatchHex(name));
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand SelectCommand => new(_ => select(Name));
}

public sealed class CustomColorViewModel(string label, Func<string> get, Action<string> set) : ObservableObject
{
    public string Label { get; } = label;
    public string Text
    {
        get => get();
        set
        {
            if (ThemeRgb.TryParse(value, out var c)) { set(c.Hex); Raise(nameof(Swatch)); }
            Raise();
        }
    }
    public IBrush Swatch => ThemeRgb.TryParse(get(), out var c) ? new SolidColorBrush(ThemeService.ToColor(c)) : Brushes.Transparent;
    public void Reload() { Raise(nameof(Text)); Raise(nameof(Swatch)); }
}

/// <summary>Tab Settings: Appearance (sama dengan SupaFisch) + kalibrasi macro.</summary>
public sealed class SettingsPageViewModel : ObservableObject
{
    private readonly MainViewModel _root;

    public SettingsPageViewModel(MainViewModel root)
    {
        _root = root;
        Themes = ThemePalettes.Names.Select(n => new ThemeChoiceViewModel(n, SelectTheme)).ToList();
        CustomColors =
        [
            Row("Background", c => c.Background, (c, v) => c with { Background = v }),
            Row("Surface", c => c.Surface, (c, v) => c with { Surface = v }),
            Row("Border", c => c.Border, (c, v) => c with { Border = v }),
            Row("Accent", c => c.Accent, (c, v) => c with { Accent = v }),
            Row("Text", c => c.Text, (c, v) => c with { Text = v }),
        ];
        SyncThemes();
    }

    private AppSettings S => _root.Settings;
    public MainViewModel Root => _root;
    public IReadOnlyList<ThemeChoiceViewModel> Themes { get; }
    public IReadOnlyList<CustomColorViewModel> CustomColors { get; }
    public bool IsCustom => ThemePalettes.Normalize(S.General.Theme) == ThemePalettes.Custom;

    public static Action<bool>? TransparencyChanged { get; set; }

    public bool TransparentUi
    {
        get => S.General.TransparentUi;
        set { S.General = S.General with { TransparentUi = value }; _root.Save(); Raise(); TransparencyChanged?.Invoke(value); }
    }

    public decimal GuiInsetY
    {
        get => (decimal)S.General.GuiInsetY;
        set { S.General = S.General with { GuiInsetY = (double)value }; _root.ApplyGuiCalibration(); _root.Save(); }
    }

    public decimal TickMs
    {
        get => S.General.TickMs;
        set { S.General = S.General with { TickMs = (int)value }; _root.Save(); }
    }

    public RelayCommand ResetCustomCommand => new(_ =>
    {
        S.CustomTheme = new CustomThemeSettings();
        foreach (var r in CustomColors) r.Reload();
        if (IsCustom) ThemeService.Apply(S);
        _root.Save();
    });

    private CustomColorViewModel Row(string label, Func<CustomThemeSettings, string> get, Func<CustomThemeSettings, string, CustomThemeSettings> with) =>
        new(label, () => get(S.CustomTheme), v =>
        {
            S.CustomTheme = with(S.CustomTheme, v);
            if (IsCustom) ThemeService.Apply(S);
            _root.Save();
        });

    private void SelectTheme(string name)
    {
        S.General = S.General with { Theme = ThemePalettes.Normalize(name) };
        _root.Save();
        SyncThemes();
        Raise(nameof(IsCustom));
        ThemeService.Apply(S);
    }

    private void SyncThemes()
    {
        string current = ThemePalettes.Normalize(S.General.Theme);
        foreach (var t in Themes) t.IsSelected = t.Name == current;
    }
}
