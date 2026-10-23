using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using RIE.Core.Memory;
using RIE.Core.Offsets;
using RIE.Core.Roblox;

namespace RIE.Core.Properties;

/// <summary>
/// Tata letak Attributes (eksperimen, read-only). Mengikuti metode GetAttribute di offsets.imtheo.lol/docs/methods:
/// Component = [inst + ComponentMap]; vector {Start,End} stride 0x10; Entry = [Start + i]; Listing = [Entry + 0x10];
/// per langkah Size: nama di [Listing + step + Key], nilai di Listing + step + Value.
/// Size 0 = hanya slot Key (ukuran entri belum diketahui).
/// </summary>
public sealed record AttributeLayout(long ComponentMap, long Key, long Value, long Size, string Source = "")
{
    public static string UserPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobloxInstanceExplorer", "attribute-layout.json");

    public bool IsUsable => ComponentMap > 0 && Key >= 0 && Value >= 0;

    /// <summary>Dari dump offsets bila tersedia (Instance.ComponentMap + Attribute.Key/Value/Size).</summary>
    public static AttributeLayout? FromOffsets(OffsetsSnapshot o)
    {
        if (!o.TryResolve("Instance.ComponentMap", out long cm) && !o.TryResolve("Instance.AttributeContainer", out cm)) return null;
        if (!o.TryResolveAny(out long key, "Attribute.Key", "Attribute.Name")) return null;
        o.TryResolveAny(out long val, "Attribute.Value");
        o.TryResolveAny(out long size, "Attribute.Size");
        return new AttributeLayout(cm, key, val, size, "dump offsets");
    }

    public static AttributeLayout? LoadUser(string? path = null)
    {
        try
        {
            path ??= UserPath;
            if (!File.Exists(path)) return null;
            var l = JsonSerializer.Deserialize<AttributeLayout>(File.ReadAllText(path));
            return l is null ? null : l with { Source = "hasil cari (tersimpan)" };
        }
        catch { return null; }
    }

    public void SaveUser(string? path = null)
    {
        path ??= UserPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this with { Source = "" }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Dump diprioritaskan; bila kosong, pakai hasil cari yang disimpan user.</summary>
    public static AttributeLayout? Resolve(OffsetsSnapshot o) => FromOffsets(o) ?? LoadUser();

    public override string ToString() =>
        $"ComponentMap 0x{ComponentMap:X}, Key 0x{Key:X}, Value 0x{Value:X}, Size 0x{Size:X}" + (Source.Length > 0 ? $" ({Source})" : "");
}

public sealed record AttributeEntry(string Name, string Value, long ValueAddress);

public static class AttributeReader
{
    private const int MaxComponents = 64;
    private const int MaxPerListing = 32;

    /// <summary>Nama di slot: pointer ke std::string (seperti docs), atau std::string inline.</summary>
    public static string NameAt(IMemorySource m, long slot, int lenOff)
    {
        if (m.TryReadPointer(slot, out long p))
        {
            string s = RobloxString.ReadStdString(m, p, lenOff, false);
            if (Plausible(s)) return s;
        }
        string inl = RobloxString.ReadStdString(m, slot, lenOff, false);
        return Plausible(inl) ? inl : string.Empty;
    }

    private static bool Plausible(string s) => s.Length is > 0 and <= 128;

    private static IEnumerable<long> Listings(IMemorySource m, long inst, long componentMap)
    {
        if (!m.TryReadPointer(inst + componentMap, out long comp)) yield break;
        if (!m.TryReadPointer(comp, out long start) || !m.TryReadPointer(comp + 8, out long end)) yield break;
        if (end <= start || end - start > MaxComponents * 0x10) yield break;
        for (long i = 0; i < end - start; i += 0x10)
        {
            if (!m.TryReadPointer(start + i, out long entry)) continue;
            if (m.TryReadPointer(entry + 0x10, out long listing)) yield return listing;
        }
    }

    public static List<AttributeEntry> ReadAll(IMemorySource m, long inst, AttributeLayout l, int lenOff = RobloxString.DefaultLengthOffset)
    {
        var res = new List<AttributeEntry>();
        if (!l.IsUsable) return res;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (long listing in Listings(m, inst, l.ComponentMap))
        {
            int n = l.Size > 0 ? MaxPerListing : 1;
            for (int i = 0; i < n; i++)
            {
                long b = listing + i * l.Size;
                string name = NameAt(m, b + l.Key, lenOff);
                if (name.Length == 0) break;
                long va = b + l.Value;
                if (seen.Add(name)) res.Add(new AttributeEntry(name, FormatValue(m, va, lenOff), va));
            }
        }
        return res;
    }

    /// <summary>Nilai Variant tidak diketahui tipenya: string bila terbaca, selain itu tebakan angka + hex.</summary>
    public static string FormatValue(IMemorySource m, long addr, int lenOff)
    {
        string s = RobloxString.ReadStringAt(m, addr, lenOff, true);
        if (s.Length > 0) return s;
        var raw = m.TryReadBytes(addr, 16);
        if (raw is null) return "?";
        double d = BinaryPrimitives.ReadDoubleLittleEndian(raw);
        float f = BinaryPrimitives.ReadSingleLittleEndian(raw);
        var parts = new List<string>();
        if (raw[0] <= 1 && raw.Skip(1).Take(7).All(x => x == 0)) parts.Add(raw[0] == 1 ? "true?" : "false/0?");
        if (double.IsFinite(d) && Math.Abs(d) is > 1e-9 and < 1e12) parts.Add("d=" + d.ToString("G9", CultureInfo.InvariantCulture));
        if (float.IsFinite(f) && Math.Abs(f) is > 1e-6f and < 1e9f) parts.Add("f=" + f.ToString("G7", CultureInfo.InvariantCulture));
        parts.Add(Convert.ToHexString(raw, 0, 16));
        return string.Join("  ", parts);
    }

    /// <summary>
    /// Cari tata letak dengan satu nama Attribute yang diketahui ada di instance ini (mis. buat di game sendiri
    /// atau lihat di Studio). Nilai (opsional) dipakai untuk menemukan offset Value.
    /// </summary>
    public static AttributeLayout? Discover(IMemorySource m, long inst, string name, string? value, out string log,
        int lenOff = RobloxString.DefaultLengthOffset, int maxComponentMap = 0x800, int maxKey = 0x800)
    {
        log = string.Empty;
        if (string.IsNullOrWhiteSpace(name)) { log = "Isi nama Attribute."; return null; }
        name = name.Trim();
        for (long cm = 0; cm < maxComponentMap; cm += 8)
        {
            foreach (long listing in Listings(m, inst, cm))
            {
                for (long k = 0; k < maxKey; k += 8)
                {
                    if (NameAt(m, listing + k, lenOff) != name) continue;
                    long size = FindSize(m, listing, k, lenOff);
                    long key = size > 0 ? k % size : k;
                    long entryBase = listing + (k - key);
                    long valOff = FindValue(m, entryBase, key, size, value, lenOff, out bool exact);
                    log = $"Ketemu: ComponentMap 0x{cm:X}, Key 0x{key:X}, Size {(size > 0 ? $"0x{size:X}" : "belum diketahui (cuma 1 Attribute terlihat)")}, " +
                          $"Value 0x{valOff:X}{(exact ? "" : " (tebakan, isi nilai untuk memastikan)")}.";
                    return new AttributeLayout(cm, key, valOff, size, "hasil cari");
                }
            }
        }
        log = $"Nama \"{name}\" tidak ketemu di struktur ComponentMap instance ini (scan 0..0x{maxComponentMap:X}). Pastikan instance-nya benar dan Attribute ada di sisi klien.";
        return null;
    }

    /// <summary>Jarak terkecil S sehingga ada nama valid lain di k+S atau k-S.</summary>
    private static long FindSize(IMemorySource m, long listing, long k, int lenOff)
    {
        for (long s = 0x18; s <= 0x200; s += 8)
        {
            if (NameAt(m, listing + k + s, lenOff).Length > 0) return s;
            if (k >= s && NameAt(m, listing + k - s, lenOff).Length > 0) return s;
        }
        return 0;
    }

    private static long FindValue(IMemorySource m, long entryBase, long key, long size, string? value, int lenOff, out bool exact)
    {
        exact = false;
        long limit = size > 0 ? size : key + 0x80;
        if (!string.IsNullOrWhiteSpace(value))
        {
            string v = value.Trim();
            bool isNum = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double num);
            bool isBool = bool.TryParse(v, out bool bv);
            for (long o = 0; o < limit; o += 8)
            {
                if (o == key) continue;
                long a = entryBase + o;
                if (RobloxString.ReadStringAt(m, a, lenOff, true) == v) { exact = true; return o; }
                if (isNum && m.TryReadDouble(a, out double d) && Math.Abs(d - num) < 1e-6) { exact = true; return o; }
                if (isNum && m.TryReadFloat(a, out float f) && Math.Abs(f - num) < 1e-4) { exact = true; return o; }
                if (isBool && m.TryReadInt64(a, out long bl) && bl == (bv ? 1 : 0)) { exact = true; return o; }
            }
        }
        return key + 8;
    }
}
