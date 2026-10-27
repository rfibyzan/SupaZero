namespace SupaZero.App.ViewModels;

/// <summary>v0.3.51 (Byzan 28 Oct): compact settings row ("Name [toggle] Open"); Open shows only this section with a back arrow.</summary>
public sealed class SectionItem : ObservableObject
{
    private bool _isOpen;
    public SectionItem(SectionNav nav, string title, string summary)
    {
        Title = title;
        Summary = summary;
        OpenCommand = new RelayCommand(_ => nav.Show(this));
    }
    public string Title { get; }
    public string Summary { get; }
    public bool IsOpen { get => _isOpen; internal set => Set(ref _isOpen, value); }
    public RelayCommand OpenCommand { get; }
}

/// <summary>Which compact section of a tab is open (null = the list of rows).</summary>
public sealed class SectionNav : ObservableObject
{
    private SectionItem? _current;
    private readonly List<SectionItem> _items = [];

    public SectionNav() => BackCommand = new RelayCommand(_ => Show(null));

    public SectionItem Add(string title, string summary = "")
    {
        var s = new SectionItem(this, title, summary);
        _items.Add(s);
        return s;
    }

    public SectionItem? Current => _current;
    public bool IsList => _current is null;
    public bool IsDetail => _current is not null;
    public string CurrentTitle => _current?.Title ?? string.Empty;
    public RelayCommand BackCommand { get; }

    public void Show(SectionItem? item)
    {
        _current = item;
        foreach (var s in _items) s.IsOpen = ReferenceEquals(s, item);
        Raise(nameof(Current)); Raise(nameof(IsList)); Raise(nameof(IsDetail)); Raise(nameof(CurrentTitle));
    }
}
