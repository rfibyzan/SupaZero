using System.Buffers.Binary;
using System.Text;
using RIE.Core.Memory;

namespace SupaZero.Core.Tests;

/// <summary>Memori palsu sparse: alokasi blok di heap simulasi.</summary>
public sealed class FakeMemory : IMemorySource
{
    private readonly SortedDictionary<long, byte[]> _blocks = new();
    private long _next = 0x10_0000_0000;

    public long Alloc(int size)
    {
        long a = _next;
        _blocks[a] = new byte[size];
        _next += (size + 0xFFF) & ~0xFFF;
        return a;
    }

    private (byte[] block, int off) Locate(long address, int len)
    {
        foreach (var (start, b) in _blocks)
            if (address >= start && address + len <= start + b.Length) return (b, (int)(address - start));
        throw new InvalidOperationException($"unmapped {address:X}");
    }

    public bool TryRead(long address, Span<byte> buffer)
    {
        foreach (var (start, b) in _blocks)
        {
            if (address >= start && address + buffer.Length <= start + b.Length)
            {
                b.AsSpan((int)(address - start), buffer.Length).CopyTo(buffer);
                return true;
            }
        }
        return false;
    }

    public void WriteInt64(long a, long v) { var (b, o) = Locate(a, 8); BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o), v); }
    public void WriteInt32(long a, int v) { var (b, o) = Locate(a, 4); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), v); }
    public void WriteFloat(long a, float v) { var (b, o) = Locate(a, 4); BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o), v); }
    public void WriteDouble(long a, double v) { var (b, o) = Locate(a, 8); BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(o), v); }
    public void WriteByte(long a, byte v) { var (b, o) = Locate(a, 1); b[o] = v; }

    /// <summary>Tulis std::string MSVC di alamat <paramref name="a"/> (32 byte: data/ptr @0, len @0x10).</summary>
    public void WriteStdString(long a, string s)
    {
        byte[] raw = Encoding.UTF8.GetBytes(s);
        if (raw.Length <= 15)
        {
            var (b, o) = Locate(a, 16);
            Array.Clear(b, o, 16);
            raw.CopyTo(b, o);
        }
        else
        {
            long heap = Alloc(raw.Length + 1);
            var (hb, ho) = Locate(heap, raw.Length);
            raw.CopyTo(hb, ho);
            WriteInt64(a, heap);
        }
        WriteInt32(a + 0x10, raw.Length);
    }
}
