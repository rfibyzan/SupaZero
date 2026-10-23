using RIE.Core.Memory;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

/// <summary>Tab Offsets (tata letak sama dengan SupaFisch): status, versi, cek cocok dengan Roblox, file manual.</summary>
public sealed class OffsetsViewModel : ObservableObject
{
    public const string ProviderUrl = "https://offsets.imtheo.lol/offsets.json";
    private readonly MainViewModel _root;
    private bool _busy;
    private string _path;
    private string? _lastError;

    public OffsetsViewModel(MainViewModel root)
    {
        _root = root;
        _path = root.Session.Offsets?.FilePath ?? root.Settings.OffsetsPath ?? string.Empty;
        root.OffsetsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RaiseAll);
    }

    private RIE.Core.Offsets.OffsetsSnapshot? O => _root.Session.Offsets;

    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(UpdateText)); } }
    public string UpdateText => Busy ? "Mengunduh..." : "Unduh terbaru";
    public string StatusTitle => _lastError is not null ? "Gagal" : O is null ? "Offsets belum dimuat" : Mismatch ? "Offsets tidak cocok" : "Offsets siap";
    public bool StatusIsError => _lastError is not null || O is null || Mismatch;
    public string StatusDetail => _lastError ?? (O is null ? "Unduh terbaru atau pilih file offsets.json." : O.FilePath);
    public string Version => O?.RobloxVersion is { Length: > 0 } v ? v : "-";
    public string Updated => O is not null && File.Exists(O.FilePath) ? File.GetLastWriteTime(O.FilePath).ToString("dd MMM yyyy HH:mm") : "-";
    public string Total => O?.TotalOffsets.ToString() ?? "-";

    private string? Installed => RobloxInstallProbe.ResolveVersion(out _);
    private bool Mismatch => O is not null && !RobloxInstallProbe.VersionMatches(O.RobloxVersion, Installed);
    public bool HasWarning => Mismatch;
    public string Warning => $"Versi Roblox terpasang {Installed ?? "?"} berbeda dengan offsets {Version}. Unduh terbaru setelah dumper rilis.";

    public string Path { get => _path; set => Set(ref _path, value ?? string.Empty); }

    public RelayCommand LoadCommand => new(_ =>
    {
        _lastError = null;
        if (!File.Exists(Path)) { _lastError = "File tidak ditemukan: " + Path; RaiseAll(); return; }
        _root.Settings.OffsetsPath = Path;
        _root.Save();
        _root.LoadOffsets(Path);
    });

    public RelayCommand UpdateCommand => new(async _ =>
    {
        Busy = true;
        _lastError = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            string json = await http.GetStringAsync(ProviderUrl);
            string path = System.IO.Path.Combine(AppSettings.DataDirectory, "offsets.json");
            Directory.CreateDirectory(AppSettings.DataDirectory);
            await File.WriteAllTextAsync(path, json);
            _root.Settings.OffsetsPath = path;
            _root.Save();
            Path = path;
            _root.LoadOffsets(path);
        }
        catch (Exception ex) { _lastError = "Unduh gagal: " + ex.Message; }
        finally { Busy = false; RaiseAll(); }
    });

    private void RaiseAll()
    {
        if (O is not null) Path = O.FilePath;
        foreach (var n in new[] { nameof(StatusTitle), nameof(StatusIsError), nameof(StatusDetail), nameof(Version), nameof(Updated), nameof(Total), nameof(HasWarning), nameof(Warning) })
            Raise(n);
    }
}
