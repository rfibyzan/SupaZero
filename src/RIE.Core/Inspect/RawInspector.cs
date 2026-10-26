using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RIE.Core.Memory;
using RIE.Core.Roblox;

namespace RIE.Core.Inspect;

/// <summary>One 8-byte row of instance memory.</summary>
public sealed record RawRow(int Offset, string Hex, string Ints, string Floats, string Note, bool Changed)
{
    public string OffsetText => $"+0x{Offset:X3}";
}

public sealed record RawMatch(int Offset, string Kind, string Value)
{
    public override string ToString() => $"+0x{Offset:X} ({Offset})  {Kind}  {Value}";
}

/// <summary>
/// Raw memory inspector (read-only) for finding offsets missing from the dump:
/// shows hex/int/float/pointer, marks changed bytes, and searches for known values.
/// </summary>
public static class RawInspector
{
    public const int MaxLength = 0x4000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[]? Dump(IMemorySource m, long address, int start, int length)
    {
        length = Math.Clamp(length, 8, MaxLength) / 8 * 8;
        if (start < 0 || !MemorySourceExtensions.IsValidAddress(address)) return null;
        return m.TryReadBytes(address + start, length);
    }

    public static List<RawRow> Rows(IMemorySource m, InstanceReader? reader, long address, int start, byte[] data, byte[]? previous)
    {
        var rows = new List<RawRow>(data.Length / 8);
        for (int i = 0; i + 8 <= data.Length; i += 8)
        {
            var s = data.AsSpan(i, 8);
            bool changed = previous is not null && previous.Length >= i + 8 && !s.SequenceEqual(previous.AsSpan(i, 8));
            string hex = Convert.ToHexString(s).ToLowerInvariant();
            hex = string.Join(' ', Enumerable.Range(0, 8).Select(k => hex.Substring(k * 2, 2)));
            int a = BinaryPrimitives.ReadInt32LittleEndian(s), b = BinaryPrimitives.ReadInt32LittleEndian(s[4..]);
            float fa = BinaryPrimitives.ReadSingleLittleEndian(s), fb = BinaryPrimitives.ReadSingleLittleEndian(s[4..]);
            long q = BinaryPrimitives.ReadInt64LittleEndian(s);
            rows.Add(new RawRow(start + i, hex, $"{a}, {b}", $"{Fmt(fa)}, {Fmt(fb)}", Note(m, reader, address + start + i, q), changed));
        }
        return rows;
    }

    private static string Fmt(float f) =>
        !float.IsFinite(f) ? "-" : (f != 0 && (MathF.Abs(f) < 1e-4f || MathF.Abs(f) > 1e7f)) ? "~" : f.ToString("0.####", Inv);

    /// <summary>Guess at the 8-byte contents: pointer to an instance/string, or inline std::string.</summary>
    private static string Note(IMemorySource m, InstanceReader? reader, long at, long q)
    {
        if (MemorySourceExtensions.IsValidAddress(q))
        {
            if (reader is not null)
            {
                string cls = reader.GetClassName(q);
                if (cls.Length > 0) return $"→ {reader.GetName(q)} [{cls}]";
            }
            string ps = RobloxString.ReadStdString(m, q, RobloxString.DefaultLengthOffset, false);
            if (ps.Length is > 0 and <= 80) return $"→ \"{ps}\"";
            return $"ptr 0x{q:X}";
        }
        string inl = RobloxString.ReadStdString(m, at, RobloxString.DefaultLengthOffset, false);
        if (inl.Length is >= 2 and <= 80) return $"str \"{inl}\"";
        if (q == 1 || q == 0x100 || q == 0x101) return "bool?";
        return string.Empty;
    }

    /// <summary>
    /// Search for a value in the data: number → float (±tol), exact int32, double (±tol); "text" or non-numeric text → UTF-8 string
    /// both inline and via std::string pointer.
    /// </summary>
    public static List<RawMatch> Find(IMemorySource m, long address, int start, byte[] data, string query, double tol = 0.01, int max = 200)
    {
        var res = new List<RawMatch>();
        query = query.Trim();
        if (query.Length == 0) return res;
        bool quoted = query.Length >= 2 && query[0] == '"' && query[^1] == '"';
        if (!quoted && double.TryParse(query, NumberStyles.Float, Inv, out double num))
        {
            bool isInt = long.TryParse(query, NumberStyles.Integer, Inv, out long iv) && iv is >= int.MinValue and <= int.MaxValue;
            for (int i = 0; i + 4 <= data.Length && res.Count < max; i += 4)
            {
                var s = data.AsSpan(i);
                if (isInt && BinaryPrimitives.ReadInt32LittleEndian(s) == (int)iv) res.Add(new(start + i, "int32", iv.ToString(Inv)));
                float f = BinaryPrimitives.ReadSingleLittleEndian(s);
                if (float.IsFinite(f) && Math.Abs(f - num) <= tol && !(isInt && f == 0 && iv != 0)) res.Add(new(start + i, "float", f.ToString("0.####", Inv)));
                if (i % 8 == 0 && i + 8 <= data.Length)
                {
                    double d = BinaryPrimitives.ReadDoubleLittleEndian(s);
                    if (double.IsFinite(d) && Math.Abs(d - num) <= tol && d != 0) res.Add(new(start + i, "double", d.ToString("0.####", Inv)));
                }
            }
            return res;
        }
        string text = quoted ? query[1..^1] : query;
        var needle = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i + needle.Length <= data.Length && res.Count < max; i++)
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) res.Add(new(start + i, "string inline", text));
        for (int i = 0; i + 8 <= data.Length && res.Count < max; i += 8)
        {
            long at = address + start + i;
            string s1 = RobloxString.ReadStringAt(m, at);
            if (s1.Contains(text, StringComparison.OrdinalIgnoreCase) && !res.Any(r => r.Offset == start + i))
                res.Add(new(start + i, "std::string", s1.Length > 60 ? s1[..60] + "..." : s1));
        }
        return res;
    }
}
