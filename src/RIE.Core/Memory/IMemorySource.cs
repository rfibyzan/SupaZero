using System.Buffers.Binary;

namespace RIE.Core.Memory;

/// <summary>READ-ONLY memory source. No write operations anywhere in the solution.</summary>
public interface IMemorySource
{
    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes; false on failure/partial read.</summary>
    bool TryRead(long address, Span<byte> buffer);
}

public static class MemorySourceExtensions
{
    /// <summary>Plausible x64 user-mode address range.</summary>
    public static bool IsValidAddress(long address) => (ulong)address > 0xFFFF && (ulong)address < 0x800000000000;

    public static bool TryReadInt64(this IMemorySource m, long address, out long value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        if (!IsValidAddress(address) || !m.TryRead(address, b)) return false;
        value = BinaryPrimitives.ReadInt64LittleEndian(b);
        return true;
    }

    public static bool TryReadPointer(this IMemorySource m, long address, out long value) =>
        m.TryReadInt64(address, out value) && IsValidAddress(value) || Fail(out value);

    public static bool TryReadInt32(this IMemorySource m, long address, out int value)
    {
        Span<byte> b = stackalloc byte[4];
        value = 0;
        if (!IsValidAddress(address) || !m.TryRead(address, b)) return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(b);
        return true;
    }

    public static bool TryReadFloat(this IMemorySource m, long address, out float value)
    {
        Span<byte> b = stackalloc byte[4];
        value = 0;
        if (!IsValidAddress(address) || !m.TryRead(address, b)) return false;
        value = BinaryPrimitives.ReadSingleLittleEndian(b);
        return true;
    }

    public static bool TryReadDouble(this IMemorySource m, long address, out double value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        if (!IsValidAddress(address) || !m.TryRead(address, b)) return false;
        value = BinaryPrimitives.ReadDoubleLittleEndian(b);
        return true;
    }

    public static bool TryReadByte(this IMemorySource m, long address, out byte value)
    {
        Span<byte> b = stackalloc byte[1];
        value = 0;
        if (!IsValidAddress(address) || !m.TryRead(address, b)) return false;
        value = b[0];
        return true;
    }

    public static byte[]? TryReadBytes(this IMemorySource m, long address, int count)
    {
        if (!IsValidAddress(address) || count <= 0 || count > 1024 * 1024) return null;
        var buf = new byte[count];
        return m.TryRead(address, buf) ? buf : null;
    }

    private static bool Fail(out long value) { value = 0; return false; }
}
