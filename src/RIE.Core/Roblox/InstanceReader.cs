using RIE.Core.Memory;
using RIE.Core.Offsets;

namespace RIE.Core.Roblox;

/// <summary>
/// Baca struktur Instance: Name, ClassName, Parent, Children. Toleran: offset hilang / alamat tak valid → kosong/0.
/// Jalur diturunkan dari OpenFisch RobloxMemoryReader (children = vektor slot 16 byte, kandidat fallback nama/kelas).
/// </summary>
public sealed class InstanceReader(IMemorySource memory, OffsetsSnapshot offsets)
{
    public const int DefaultMaxChildren = 5000;
    private static readonly long[] NameFallbacks = { 0x98, 0x90, 0xA0, 0x88, 0x80, 0x60, 0x68, 0x70 };
    private static readonly long[] DescriptorFallbacks = { 0x18, 0x10, 0x20, 0x28 };

    public IMemorySource Memory => memory;
    public OffsetsSnapshot Offsets => offsets;

    public static bool IsValid(long address) => MemorySourceExtensions.IsValidAddress(address);

    public int StringLengthOffset =>
        offsets.TryResolve("StringLength", out long o) && o is > 0 and <= 0x100 ? (int)o : RobloxString.DefaultLengthOffset;

    public string GetName(long instance)
    {
        if (!IsValid(instance)) return string.Empty;
        int lo = StringLengthOffset;
        bool hasContainer = offsets.TryResolve("Instance.NameContainer", out long containerOff);
        bool hasName = offsets.TryResolve("Instance.Name", out long nameOff);
        if (hasContainer && hasName && memory.TryReadPointer(instance + containerOff, out long container))
        {
            string s = RobloxString.ReadStringAt(memory, container + nameOff, lo, false);
            if (s.Length != 0) return s;
        }
        var cands = new List<long>();
        if (hasName) cands.Add(nameOff);
        if (hasContainer) cands.Add(containerOff);
        foreach (long f in NameFallbacks) if (!cands.Contains(f)) cands.Add(f);
        foreach (long c in cands)
        {
            string s = RobloxString.ReadStringAt(memory, instance + c, lo, false);
            if (s.Length != 0) return s;
        }
        return string.Empty;
    }

    public string GetClassName(long instance)
    {
        if (!IsValid(instance)) return string.Empty;
        int lo = StringLengthOffset;
        var descs = new List<long>();
        if (offsets.TryResolve("ClassDescriptor", out long d) && d != 0) descs.Add(d);
        foreach (long f in DescriptorFallbacks) if (!descs.Contains(f)) descs.Add(f);
        var names = new List<long>();
        if (offsets.TryResolve("ClassDescriptorToClassName", out long cn) && cn != 0) names.Add(cn);
        if (!names.Contains(8)) names.Add(8);
        foreach (long dOff in descs)
        {
            if (!memory.TryReadPointer(instance + dOff, out long desc)) continue;
            foreach (long nOff in names)
            {
                string s = RobloxString.ReadStringAt(memory, desc + nOff, lo, false);
                if (s.Length != 0) return s;
            }
        }
        return string.Empty;
    }

    public long GetParent(long instance)
    {
        if (!IsValid(instance) || !offsets.TryResolve("Parent", out long off)) return 0;
        return memory.TryReadPointer(instance + off, out long p) ? p : 0;
    }

    /// <summary>[inst+ChildrenStart] → {begin@0, end@ChildrenEnd}, slot 16 byte (shared_ptr).</summary>
    public List<long> GetChildren(long instance, int max = DefaultMaxChildren)
    {
        var result = new List<long>();
        if (!IsValid(instance)) return result;
        if (!offsets.TryResolve("Children", out long startOff) || !offsets.TryResolve("ChildrenEnd", out long endOff)) return result;
        if (!memory.TryReadPointer(instance + startOff, out long container)) return result;
        if (!memory.TryReadPointer(container, out long begin) || !memory.TryReadPointer(container + endOff, out long end)) return result;
        long span = end - begin;
        if (span <= 0 || span >= 0x100001 || span % 0x10 != 0) return result;
        long count = Math.Min(span / 0x10, max);
        for (long i = 0; i < count; i++)
        {
            if (!memory.TryReadPointer(begin + i * 0x10, out long child)) break;
            result.Add(child);
        }
        return result;
    }

    public long FindChild(long parent, string name)
    {
        foreach (long c in GetChildren(parent))
            if (GetName(c).Equals(name, StringComparison.OrdinalIgnoreCase)) return c;
        return 0;
    }

    public long FindChildOfClass(long parent, string className)
    {
        foreach (long c in GetChildren(parent))
            if (GetClassName(c).Equals(className, StringComparison.OrdinalIgnoreCase)) return c;
        return 0;
    }

    /// <summary>Path lengkap "game.Players.Foo.PlayerGui.Main" lewat rantai Parent (maks 128 level).</summary>
    public string GetFullPath(long instance, long dataModel = 0)
    {
        var parts = new List<string>();
        var seen = new HashSet<long>();
        long cur = instance;
        while (IsValid(cur) && seen.Add(cur) && parts.Count < 128)
        {
            if (cur == dataModel) { parts.Add("game"); break; }
            parts.Add(GetName(cur) is { Length: > 0 } n ? n : $"<{cur:X}>");
            cur = GetParent(cur);
        }
        parts.Reverse();
        return string.Join('.', parts);
    }

    /// <summary>Resolve path bertitik relatif ke <paramref name="root"/> ("Players.LocalPlayer.PlayerGui" tidak didukung; pakai nama).</summary>
    public long ResolvePath(long root, string path)
    {
        long cur = root;
        foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg.Equals("game", StringComparison.OrdinalIgnoreCase) && cur == root) continue;
            cur = FindChild(cur, seg);
            if (cur == 0) return 0;
        }
        return cur;
    }
}
