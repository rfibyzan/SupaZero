using System.Text;
using RIE.Core.Memory;

namespace RIE.Core.Roblox;

/// <summary>
/// std::string Roblox (MSVC): buffer inline 16 byte @+0, panjang @+lengthOffset (default 0x10),
/// pointer heap @+0 bila panjang &gt; 15. Diturunkan dari OpenFisch RobloxString.
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

    /// <summary>Alamat berisi std::string, atau pointer ke std::string (satu deref).</summary>
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
