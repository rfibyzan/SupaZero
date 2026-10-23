using System.Text;
using RIE.Core.Offsets;

namespace SupaZero.Core.Tests;

/// <summary>Membangun pohon instance palsu sesuai layout offsets uji.</summary>
public sealed class FakeRoblox
{
    public const long NameOff = 0x60, ChildrenOff = 0x70, ParentOff = 0x50, DescOff = 0x18, ClassNameOff = 0x08;
    public const long TextOff = 0x200, VisibleOff = 0x220, AbsPosOff = 0x230, AbsSizeOff = 0x240, ValueOff = 0x250, PrimOff = 0x260, PrimPosOff = 0x40;
    public const long FakeDmPtrOff = 0x1000, RealDmOff = 0x1C0, LocalPlayerOff = 0x130;

    public FakeMemory Mem { get; } = new();
    public OffsetsSnapshot Offsets { get; }
    public long ModuleBase { get; }
    private readonly Dictionary<long, List<long>> _children = new();
    private readonly Dictionary<string, long> _descriptors = new();

    public FakeRoblox()
    {
        var json = new StringBuilder();
        json.Append("{\"Roblox Version\":\"version-test\",\"Offsets\":{");
        json.Append($"\"Instance\":{{\"Name\":{NameOff},\"ChildrenStart\":{ChildrenOff},\"ChildrenEnd\":8,\"Parent\":{ParentOff},\"ClassDescriptor\":{DescOff},\"ClassName\":{ClassNameOff}}},");
        json.Append($"\"Misc\":{{\"StringLength\":16,\"Value\":{ValueOff}}},");
        json.Append($"\"GuiObject\":{{\"Text\":{TextOff},\"Visible\":{VisibleOff}}},");
        json.Append($"\"GuiBase2D\":{{\"AbsolutePosition\":{AbsPosOff},\"AbsoluteSize\":{AbsSizeOff}}},");
        json.Append($"\"BasePart\":{{\"Primitive\":{PrimOff}}},\"Primitive\":{{\"Position\":{PrimPosOff}}},");
        json.Append($"\"FakeDataModel\":{{\"Pointer\":{FakeDmPtrOff},\"RealDataModel\":{RealDmOff}}},");
        json.Append($"\"Player\":{{\"LocalPlayer\":{LocalPlayerOff}}}");
        json.Append("}}");
        Offsets = OffsetsSnapshot.Parse(json.ToString());
        ModuleBase = Mem.Alloc(0x2000);
    }

    public long New(string name, string className, long parent = 0)
    {
        long inst = Mem.Alloc(0x400);
        long nameStr = inst + 0x300;
        Mem.WriteStdString(nameStr, name);
        Mem.WriteInt64(inst + NameOff, nameStr);   // Name = pointer ke std::string (ReadStringAt deref)
        Mem.WriteInt64(inst + DescOff, Descriptor(className));
        long container = Mem.Alloc(0x20);
        Mem.WriteInt64(inst + ChildrenOff, container);
        _children[inst] = new List<long>();
        if (parent != 0) AddChild(parent, inst);
        return inst;
    }

    public void AddChild(long parent, long child)
    {
        _children[parent].Add(child);
        Mem.WriteInt64(child + ParentOff, parent);
        RewriteChildren(parent);
    }

    public void RemoveChild(long parent, long child)
    {
        _children[parent].Remove(child);
        RewriteChildren(parent);
    }

    private void RewriteChildren(long parent)
    {
        var list = _children[parent];
        Mem.TryReadInt64Raw(parent + ChildrenOff, out long container);
        long vec = Mem.Alloc(Math.Max(16, list.Count * 16));
        for (int i = 0; i < list.Count; i++) Mem.WriteInt64(vec + i * 16, list[i]);
        Mem.WriteInt64(container, vec);
        Mem.WriteInt64(container + 8, vec + list.Count * 16);
    }

    private long Descriptor(string className)
    {
        if (_descriptors.TryGetValue(className, out long d)) return d;
        d = Mem.Alloc(0x40);
        Mem.WriteStdString(d + ClassNameOff, className);
        return _descriptors[className] = d;
    }

    public void SetText(long inst, string text) => Mem.WriteStdString(inst + TextOff, text);

    /// <summary>DataModel standar: game{Workspace, Players{LocalPlayer{PlayerGui}}, ReplicatedStorage}.</summary>
    public (long Dm, long Ws, long Players, long Lp, long Pg, long Rs) BuildGame()
    {
        long dm = New("Game", "DataModel");
        long ws = New("Workspace", "Workspace", dm);
        long players = New("Players", "Players", dm);
        long lp = New("TestUser", "Player", players);
        long pg = New("PlayerGui", "PlayerGui", lp);
        long rs = New("ReplicatedStorage", "ReplicatedStorage", dm);
        Mem.WriteInt64(players + LocalPlayerOff, lp);
        long fake = Mem.Alloc(0x200);
        Mem.WriteInt64(fake + RealDmOff, dm);
        Mem.WriteInt64(ModuleBase + FakeDmPtrOff, fake);
        return (dm, ws, players, lp, pg, rs);
    }
}

internal static class FakeMemoryExt
{
    public static bool TryReadInt64Raw(this FakeMemory m, long a, out long v)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = m.TryRead(a, b);
        v = ok ? BitConverter.ToInt64(b) : 0;
        return ok;
    }
}
