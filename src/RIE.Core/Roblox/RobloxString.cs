using System.Text;
using RIE.Core.Memory;

namespace RIE.Core.Roblox;

/// <summary>
/// Roblox std::string (MSVC): 16-byte inline buffer @+0, length @+lengthOffset (default 0x10),
/// heap pointer @+0 when length &gt; 15. Derived from OpenFisch RobloxString.
/// </summary>
public static class RobloxString
{
    public const int DefaultLengthOffset = 0x10;
    public const int MaxLength = 4096;

    public static string ReadStdString(IMemorySource m, long address, int lengthOffset = DefaultLengthOffset, bool allowWhitespace = true)
    {
        string s = Core(m, address, lengthOffset, allowWhitespace);
        if (s.Length != 0 || lengthOffset == DefaultLengthOffset) return s;
        return Core(m, address, DefaultLengthOffset, allowWhitespace);
    }

    /// <summary>Address holding a std::string, or a pointer to a std::string (one deref).</summary>
    public static string ReadStringAt(IMemorySource m, long address, int lengthOffset = DefaultLengthOffset, bool allowWhitespace = true)
    {
        string s = ReadStdString(m, address, lengthOffset, allowWhitespace);
        if (s.Length != 0) return s;
        if (!m.TryReadPointer(address, out long p) || p == address) return string.Empty;
        return ReadStdString(m, p, lengthOffset, allowWhitespace);
    }

    private static string Core(IMemorySource m, long address, int lengthOffset, bool allowWhitespace)
    {
        if (!m.TryReadInt32(address + lengthOffset, out int len) || len <= 0 || len > MaxLength) return string.Empty;
        long data = address;
        if (len > 15 && !m.TryReadPointer(address, out data)) return string.Empty;
        byte[]? raw = m.TryReadBytes(data, len);
        if (raw is null) return string.Empty;
        string text = Encoding.UTF8.GetString(raw);
        return IsValid(text, allowWhitespace) ? text : string.Empty;
    }

    public static bool IsValid(string text, bool allowWhitespace)
    {
        if (text.Length == 0) return false;
        foreach (char c in text)
        {
            if (c >= 0x20) continue;
            if (!(allowWhitespace && c is '\t' or '\n' or '\r' or '\v' or '\f')) return false;
        }
        return true;
    }
}
