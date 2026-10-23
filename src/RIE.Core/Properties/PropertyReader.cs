using System.Globalization;
using RIE.Core.Memory;
using RIE.Core.Roblox;

namespace RIE.Core.Properties;

/// <summary>
/// Baca properti per class yang offset-nya tersedia. Semua nilai dikembalikan sebagai string invarian
/// (stabil untuk diff). Properti yang offset-nya tidak ada di offsets.json dilewati.
/// Attributes (eksperimen): dibaca bila <see cref="Attributes"/> diisi (dari dump atau hasil cari), dengan kunci "@Nama".
/// </summary>
public sealed class PropertyReader(InstanceReader reader)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly HashSet<string> GuiObjectClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Frame", "TextLabel", "TextButton", "TextBox", "ImageLabel", "ImageButton",
        "ScrollingFrame", "ViewportFrame", "CanvasGroup", "VideoFrame",
    };
    private static readonly HashSet<string> TextClasses = new(StringComparer.OrdinalIgnoreCase) { "TextLabel", "TextButton", "TextBox" };
    private static readonly HashSet<string> ImageClasses = new(StringComparer.OrdinalIgnoreCase) { "ImageLabel", "ImageButton" };
    private static readonly HashSet<string> LayerGuiClasses = new(StringComparer.OrdinalIgnoreCase) { "ScreenGui", "BillboardGui", "SurfaceGui" };
    private static readonly HashSet<string> BasePartClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Part", "MeshPart", "UnionOperation", "IntersectOperation", "NegateOperation", "WedgePart", "CornerWedgePart",
        "TrussPart", "SpawnLocation", "Seat", "VehicleSeat", "SkateboardPlatform", "PartOperation",
    };

    /// <summary>Tata letak Attributes; null = tidak dibaca.</summary>
    public AttributeLayout? Attributes { get; set; }

    public static bool IsGuiObject(string cls) => GuiObjectClasses.Contains(cls);
    public static bool IsTextClass(string cls) => TextClasses.Contains(cls);
    public static bool IsBasePart(string cls) => BasePartClasses.Contains(cls);

    private IMemorySource M => reader.Memory;

    public SortedDictionary<string, string> Read(long instance, string? className = null)
    {
        var props = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!InstanceReader.IsValid(instance)) return props;
        string cls = className ?? reader.GetClassName(instance);

        if (GuiObjectClasses.Contains(cls))
        {
            if (TryByte("GuiObject.Visible", instance, out byte vis)) props["Visible"] = Bool(vis);
            ReadAbsolute(instance, props);
            if (TryUDim2("GuiObject.Position", instance, out string pos)) props["Position"] = pos;
            if (TryUDim2("GuiObject.Size", instance, out string size)) props["Size"] = size;
            if (TextClasses.Contains(cls) && TryString("GuiObject.Text", instance, out string text)) props["Text"] = text;
            if (ImageClasses.Contains(cls) && TryString("GuiObject.Image", instance, out string img)) props["Image"] = img;
            if (TextClasses.Contains(cls)) Color("GuiObject.TextColor3", instance, "TextColor3", props);
            Color("GuiObject.BackgroundColor3", instance, "BackgroundColor3", props);
            Float("GuiObject.BackgroundTransparency", instance, "BackgroundTransparency", props);
            Int("GuiObject.ZIndex", instance, "ZIndex", props);
            Int("GuiObject.LayoutOrder", instance, "LayoutOrder", props);
            Float("GuiObject.Rotation", instance, "Rotation", props);
        }
        else if (LayerGuiClasses.Contains(cls))
        {
            if (cls.Equals("ScreenGui", StringComparison.OrdinalIgnoreCase) && TryByte("GuiObject.ScreenGui_Enabled", instance, out byte en))
                props["Enabled"] = Bool(en);
            ReadAbsolute(instance, props);
        }
        else if (BasePartClasses.Contains(cls))
        {
            ReadBasePart(instance, props);
            if (cls is "Seat" or "VehicleSeat") ReadPtrPath("Seat.Occupant", instance, "Occupant", props);
            if (cls == "VehicleSeat")
            {
                Float("VehicleSeat.ThrottleFloat", instance, "ThrottleFloat", props);
                Float("VehicleSeat.SteerFloat", instance, "SteerFloat", props);
            }
        }
        else
        {
            switch (cls)
            {
                case "Humanoid": ReadHumanoid(instance, props); break;
                case "Camera": ReadCamera(instance, props); break;
                case "Player": ReadPlayer(instance, props); break;
                case "Model": ReadPtrPath("Model.PrimaryPart", instance, "PrimaryPart", props); break;
                case "ProximityPrompt": ReadPrompt(instance, props); break;
                case "Animator": ReadAnimator(instance, props); break;
                case "AnimationTrack": ReadTrack(instance, props); break;
                case "Animation": StrProp("Misc.AnimationId", instance, "AnimationId", props); break;
                case "Sound":
                    StrProp("Sound.SoundId", instance, "SoundId", props);
                    Float("Sound.Volume", instance, "Volume", props);
                    Float("Sound.PlaybackSpeed", instance, "PlaybackSpeed", props);
                    Flag("Sound.IsPlaying", instance, "IsPlaying", props);
                    Flag("Sound.Looped", instance, "Looped", props);
                    break;
                case "Tool":
                    Flag("Tool.Enabled", instance, "Enabled", props);
                    StrProp("Tool.Tooltip", instance, "ToolTip", props);
                    break;
                case "Workspace":
                    if (Off("Workspace.DistributedGameTime", out long dg) && M.TryReadDouble(instance + dg, out double gt) && double.IsFinite(gt) && gt >= 0 && gt < 1e9)
                        props["DistributedGameTime"] = gt.ToString("0.###", Inv);
                    ReadPtrPath("Workspace.CurrentCamera", instance, "CurrentCamera", props);
                    break;
                case "Lighting": Float("Lighting.ClockTime", instance, "ClockTime", props); break;
                case "MouseService":
                    if (Off("MouseService.MousePosition", out long mp) && TryVec2(instance + mp, out string mpos)) props["MousePosition"] = mpos;
                    break;
                case "DataModel":
                    if (Off("DataModel.PlaceId", out long pl) && M.TryReadInt64(instance + pl, out long place)) props["PlaceId"] = place.ToString(Inv);
                    if (Off("DataModel.GameId", out long gi) && M.TryReadInt64(instance + gi, out long game)) props["GameId"] = game.ToString(Inv);
                    break;
                default: ReadValueBase(instance, cls, props); break;
            }
        }
        if (Attributes is { IsUsable: true } al)
            foreach (var a in AttributeReader.ReadAll(M, instance, al, reader.StringLengthOffset))
                props["@" + a.Name] = a.Value;
        return props;
    }

    private void ReadAbsolute(long inst, IDictionary<string, string> props)
    {
        if (!reader.Offsets.TryResolve("AbsolutePosition", out long posOff) || posOff == 0) return;
        if (TryVec2(inst + posOff, out string p)) props["AbsolutePosition"] = p;
        // Beberapa dump menulis AbsoluteSize = 0 (tidak dikenal) → fallback pos+8 seperti OpenFisch.
        long sizeOff = reader.Offsets.TryResolve("AbsoluteSize", out long s) && s != 0 ? s : posOff + 8;
        if (TryVec2(inst + sizeOff, out string sz)) props["AbsoluteSize"] = sz;
    }

    private void ReadBasePart(long inst, IDictionary<string, string> props)
    {
        if (reader.Offsets.TryResolve("BasePart.Primitive", out long primOff) && M.TryReadPointer(inst + primOff, out long prim))
        {
            if (reader.Offsets.TryResolve("Primitive.Position", out long po) && TryVec3(prim + po, out string pos)) props["Position"] = pos;
            if (reader.Offsets.TryResolve("Primitive.Size", out long so) && TryVec3(prim + so, out string size)) props["Size"] = size;
            if (Off("Primitive.AssemblyLinearVelocity", out long vo) && TryVec3(prim + vo, out string vel)) props["Velocity"] = vel;
            if (Off("Primitive.Rotation", out long ro))
            {
                long a = prim + ro; // 3x3 row-major, LookVector = -kolom Z
                if (M.TryReadFloat(a + 8, out float r02) && M.TryReadFloat(a + 20, out float r12) && M.TryReadFloat(a + 32, out float r22)
                    && Sane(r02) && Sane(r12) && Sane(r22) && MathF.Abs(r02 * r02 + r12 * r12 + r22 * r22 - 1) < 0.05f)
                    props["LookVector"] = $"{F(-r02)}, {F(-r12)}, {F(-r22)}";
            }
            if (Off("Primitive.Flags", out long fo) && M.TryReadByte(prim + fo, out byte flags))
            {
                FlagBit("PrimitiveFlags.Anchored", flags, "Anchored", props);
                FlagBit("PrimitiveFlags.CanCollide", flags, "CanCollide", props);
                FlagBit("PrimitiveFlags.CanTouch", flags, "CanTouch", props);
            }
        }
        Color("BasePart.Color3", inst, "Color", props);
        if (reader.Offsets.TryResolve("BasePart.Transparency", out long to) && M.TryReadFloat(inst + to, out float t) && float.IsFinite(t))
            props["Transparency"] = F(t);
    }

    private bool Off(string key, out long off) => reader.Offsets.TryResolve(key, out off);

    private void Float(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long o) && M.TryReadFloat(inst + o, out float f) && Sane(f)) props[name] = F(f);
    }

    private void Flag(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long o) && M.TryReadByte(inst + o, out byte b)) props[name] = Bool(b);
    }

    private void Vec3(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long o) && TryVec3(inst + o, out string v)) props[name] = v;
    }

    private void ReadPtrPath(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long o)) props[name] = M.TryReadPointer(inst + o, out long p) ? reader.GetFullPath(p) : "nil";
    }

    private void Int(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long o) && o != 0 && M.TryReadInt32(inst + o, out int v) && Math.Abs((long)v) < 10_000_000) props[name] = v.ToString(Inv);
    }

    private void FlagBit(string key, byte flags, string name, IDictionary<string, string> props)
    {
        if (Off(key, out long mask) && mask is > 0 and < 256) props[name] = Bool((byte)(flags & mask));
    }

    /// <summary>Color3 = 3 float 0..1 → "r, g, b" (0..255). Dilewati bila nilainya di luar rentang (offset meragukan).</summary>
    private void Color(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (!Off(key, out long o) || o < 0x20) return;
        long a = inst + o;
        if (!M.TryReadFloat(a, out float r) || !M.TryReadFloat(a + 4, out float g) || !M.TryReadFloat(a + 8, out float b)) return;
        if (!In01(r) || !In01(g) || !In01(b)) return;
        props[name] = $"{(int)MathF.Round(r * 255)}, {(int)MathF.Round(g * 255)}, {(int)MathF.Round(b * 255)}";
    }

    private static bool In01(float f) => float.IsFinite(f) && f >= 0 && f <= 1.0001f;

    /// <summary>String inline (std::string) atau via pointer.</summary>
    private void StrProp(string key, long inst, string name, IDictionary<string, string> props)
    {
        if (!Off(key, out long o)) return;
        string s = RobloxString.ReadStringAt(M, inst + o, reader.StringLengthOffset, true);
        if (s.Length > 0) props[name] = s;
    }

    private void ReadPrompt(long p, IDictionary<string, string> props)
    {
        StrProp("ProximityPrompt.ActionText", p, "ActionText", props);
        StrProp("ProximityPrompt.ObjectText", p, "ObjectText", props);
        Float("ProximityPrompt.HoldDuration", p, "HoldDuration", props);
        Float("ProximityPrompt.MaxActivationDistance", p, "MaxActivationDistance", props);
        if (Off("ProximityPrompt.KeyCode", out long ko) && M.TryReadInt32(p + ko, out int kc) && kc is >= 0 and < 1024)
            props["KeyCode"] = $"{kc} ({KeyName(kc)})";
        Flag("ProximityPrompt.Enabled", p, "Enabled", props);
        Flag("ProximityPrompt.RequiresLineOfSight", p, "RequiresLineOfSight", props);
    }

    /// <summary>Nama Enum.KeyCode umum (huruf, angka, F1-F12, tombol khusus).</summary>
    public static string KeyName(int code) => code switch
    {
        >= 97 and <= 122 => ((char)(code - 32)).ToString(),
        >= 48 and <= 57 => ((char)code).ToString(),
        >= 282 and <= 293 => "F" + (code - 281),
        8 => "Backspace", 9 => "Tab", 13 => "Return", 27 => "Escape", 32 => "Space", 127 => "Delete",
        273 => "Up", 274 => "Down", 275 => "Right", 276 => "Left",
        303 => "RightShift", 304 => "LeftShift", 305 => "RightControl", 306 => "LeftControl", 307 => "RightAlt", 308 => "LeftAlt",
        0 => "Unknown",
        _ => "?",
    };

    private void ReadTrack(long t, IDictionary<string, string> props)
    {
        Float("AnimationTrack.Speed", t, "Speed", props);
        Float("AnimationTrack.TimePosition", t, "TimePosition", props);
        Flag("AnimationTrack.Looped", t, "Looped", props);
        Flag("AnimationTrack.IsPlaying", t, "IsPlaying", props);
        if (Off("AnimationTrack.Animation", out long ao) && M.TryReadPointer(t + ao, out long anim))
        {
            var tmp = new Dictionary<string, string>();
            StrProp("Misc.AnimationId", anim, "AnimationId", tmp);
            if (tmp.TryGetValue("AnimationId", out var id)) props["AnimationId"] = id;
        }
    }

    /// <summary>
    /// Track aktif di Animator (bukan anak di pohon instance). Struktur ActiveAnimations tidak didokumentasikan dump,
    /// jadi dicoba dua bentuk (vector shared_ptr dan linked list) dan hanya diterima bila tiap elemen ber-class AnimationTrack.
    /// </summary>
    public List<long> GetActiveTracks(long animator, int max = 32) => GetActiveTracks(animator, max, out _);

    /// <summary>
    /// Mengikuti metode resmi dump (offsets.imtheo.lol/docs/methods): Head = [animator + ActiveAnimations];
    /// Node = [Head]; selama Node != Head: track = [Node + 0x10], Node = [Node]. Elemen tidak valid dilewati.
    /// Track diterima bila class-nya AnimationTrack atau back-pointer AnimationTrack.Animator menunjuk animator ini.
    /// </summary>
    public List<long> GetActiveTracks(long animator, int max, out string debug)
    {
        var result = new List<long>();
        debug = "offset Animator.ActiveAnimations tidak ada";
        if (!Off("Animator.ActiveAnimations", out long off)) return result;
        long a = animator + off;
        if (!M.TryReadPointer(a, out long head)) { debug = $"[+0x{off:X}] bukan pointer"; return result; }
        int nodes = 0, skipped = 0;
        var seen = new HashSet<long>();
        if (M.TryReadPointer(head, out long node))
            while (node != 0 && node != head && seen.Add(node) && nodes < 256 && result.Count < max)
            {
                nodes++;
                if (M.TryReadPointer(node + 0x10, out long tr) && IsTrack(tr, animator)) result.Add(tr); else skipped++;
                if (!M.TryReadPointer(node, out node)) break;
            }
        if (result.Count == 0)
        {
            // cadangan: std::vector<shared_ptr<AnimationTrack>> {begin, end}
            if (M.TryReadPointer(a + 8, out long end) && end > head && (end - head) % 16 == 0 && (end - head) / 16 <= max)
                for (long p = head; p < end; p += 16)
                    if (M.TryReadPointer(p, out long tr) && IsTrack(tr, animator)) result.Add(tr);
        }
        debug = $"head 0x{head:X}, node {nodes}, dilewati {skipped}, track {result.Count}";
        return result;
    }

    private bool IsTrack(long p, long animator)
    {
        if (!InstanceReader.IsValid(p)) return false;
        if (reader.GetClassName(p) == "AnimationTrack") return true;
        return Off("AnimationTrack.Animator", out long ao) && M.TryReadPointer(p + ao, out long back) && back == animator;
    }

    /// <summary>
    /// Posisi dunia untuk proyeksi ke layar: BasePart → Position; Model → PrimaryPart; Humanoid → RootPart;
    /// ProximityPrompt/ClickDetector/Attachment/Sound → induk BasePart.
    /// </summary>
    public bool TryGetWorldPosition(long inst, out float x, out float y, out float z, int depth = 0)
    {
        x = y = z = 0;
        if (!InstanceReader.IsValid(inst) || depth > 3) return false;
        string cls = reader.GetClassName(inst);
        if (BasePartClasses.Contains(cls))
        {
            return Off("BasePart.Primitive", out long po) && M.TryReadPointer(inst + po, out long prim) &&
                   Off("Primitive.Position", out long pp) && M.TryReadFloat(prim + pp, out x) && M.TryReadFloat(prim + pp + 4, out y) &&
                   M.TryReadFloat(prim + pp + 8, out z) && Sane(x) && Sane(y) && Sane(z);
        }
        long next = 0;
        if (cls == "Model" && Off("Model.PrimaryPart", out long mp)) M.TryReadPointer(inst + mp, out next);
        else if (cls == "Humanoid" && Off("Humanoid.HumanoidRootPart", out long rp)) M.TryReadPointer(inst + rp, out next);
        else if (cls is "ProximityPrompt" or "ClickDetector" or "Attachment" or "Sound" or "BillboardGui" or "ParticleEmitter") next = reader.GetParent(inst);
        return next != 0 && TryGetWorldPosition(next, out x, out y, out z, depth + 1);
    }

    private void ReadAnimator(long a, IDictionary<string, string> props)
    {
        if (!Off("Animator.ActiveAnimations", out _)) return;
        var tracks = GetActiveTracks(a, 32, out string dbg);
        props["ActiveTracks"] = tracks.Count.ToString(Inv);
        props["(Debug) ActiveAnimations"] = dbg;
        for (int i = 0; i < tracks.Count; i++)
        {
            var t = new Dictionary<string, string>();
            ReadTrack(tracks[i], t);
            string id = t.TryGetValue("AnimationId", out var v) ? v : "?";
            string playing = t.TryGetValue("IsPlaying", out var ip) ? (ip == "true" ? "main" : "berhenti") : "?";
            string pos = t.TryGetValue("TimePosition", out var tp) ? tp : "?";
            props[$"Track{i + 1}"] = $"{id} | {playing} | t={pos}";
        }
    }

    private void ReadHumanoid(long h, IDictionary<string, string> props)
    {
        Float("Humanoid.Health", h, "Health", props);
        Float("Humanoid.MaxHealth", h, "MaxHealth", props);
        Float("Humanoid.Walkspeed", h, "WalkSpeed", props);
        Float("Humanoid.JumpPower", h, "JumpPower", props);
        Float("Humanoid.HipHeight", h, "HipHeight", props);
        Vec3("Humanoid.MoveDirection", h, "MoveDirection", props);
        Flag("Humanoid.Jump", h, "Jump", props);
        Flag("Humanoid.Sit", h, "Sit", props);
        Flag("Humanoid.IsWalking", h, "IsWalking", props);
        Flag("Humanoid.PlatformStand", h, "PlatformStand", props);
        Flag("Humanoid.AutoRotate", h, "AutoRotate", props);
        Float("Humanoid.JumpHeight", h, "JumpHeight", props);
        Vec3("Humanoid.MoveToPoint", h, "MoveToPoint", props);
        ReadPtrPath("Humanoid.HumanoidRootPart", h, "RootPart", props);
        if (Off("Humanoid.RigType", out long rt) && M.TryReadByte(h + rt, out byte rig) && rig <= 1) props["RigType"] = rig == 0 ? "R6" : "R15";
        ReadPtrPath("Humanoid.SeatPart", h, "SeatPart", props);
        if (Off("Humanoid.HumanoidState", out long so) && M.TryReadPointer(h + so, out long st) &&
            Off("Humanoid.HumanoidStateID", out long sid) && M.TryReadByte(st + sid, out byte id))
            props["StateId"] = $"{id} ({StateName(id)})";
        if (Off("Humanoid.FloorMaterial", out long fm) && M.TryReadInt32(h + fm, out int mat)) props["FloorMaterial"] = mat.ToString(Inv);
    }

    private static string StateName(byte id) => id switch
    {
        0 => "FallingDown", 1 => "Ragdoll", 2 => "GettingUp", 3 => "Jumping", 4 => "Swimming", 5 => "Freefall",
        6 => "Flying", 7 => "Landed", 8 => "Running", 10 => "RunningNoPhysics", 11 => "StrafingNoPhysics",
        12 => "Climbing", 13 => "Seated", 14 => "PlatformStanding", 15 => "Dead", 16 => "Physics", 18 => "None",
        _ => "?",
    };

    private void ReadCamera(long c, IDictionary<string, string> props)
    {
        Vec3("Camera.Position", c, "Position", props);
        if (Off("Camera.Rotation", out long ro))
        {
            // Matriks rotasi 3x3 row-major; LookVector = -kolom Z.
            long a = c + ro;
            if (M.TryReadFloat(a + 8, out float r02) && M.TryReadFloat(a + 20, out float r12) && M.TryReadFloat(a + 32, out float r22)
                && Sane(r02) && Sane(r12) && Sane(r22))
                props["LookVector"] = $"{F(-r02)}, {F(-r12)}, {F(-r22)}";
        }
        if (Off("Camera.FieldOfView", out long fo) && M.TryReadFloat(c + fo, out float fov) && Sane(fov))
            props["FieldOfView"] = F(fov < 3.2f ? fov * 180f / MathF.PI : fov); // internal radian
        if (Off("Camera.ViewportSize", out long vo) && TryVec2(c + vo, out string vs)) props["ViewportSize"] = vs;
        ReadPtrPath("Camera.CameraSubject", c, "CameraSubject", props);
    }

    private void ReadPlayer(long p, IDictionary<string, string> props)
    {
        if (Off("Player.UserId", out long uo) && M.TryReadInt64(p + uo, out long uid)) props["UserId"] = uid.ToString(Inv);
        if (Off("Player.DisplayName", out long dn)) props["DisplayName"] = RobloxString.ReadStdString(M, p + dn, reader.StringLengthOffset, true);
        ReadPtrPath("Player.ModelInstance", p, "Character", props);
        ReadPtrPath("Player.Team", p, "Team", props);
    }

    private void ReadValueBase(long inst, string cls, IDictionary<string, string> props)
    {
        if (!reader.Offsets.TryResolveAny(out long off, "Misc.Value", "ValueBase.Value")) return;
        long a = inst + off;
        switch (cls)
        {
            case "IntValue":
                if (M.TryReadInt64(a, out long i)) props["Value"] = i.ToString(Inv);
                break;
            case "NumberValue":
                if (M.TryReadDouble(a, out double d)) props["Value"] = d.ToString("R", Inv);
                break;
            case "BoolValue":
                if (M.TryReadByte(a, out byte b)) props["Value"] = Bool(b);
                break;
            case "StringValue":
                props["Value"] = RobloxString.ReadStdString(M, a, reader.StringLengthOffset, true);
                break;
            case "ObjectValue":
                props["Value"] = M.TryReadPointer(a, out long p) ? reader.GetFullPath(p) : "nil";
                break;
            case "Vector3Value":
                if (TryVec3(a, out string v3)) props["Value"] = v3;
                break;
        }
    }

    private bool TryByte(string key, long inst, out byte v)
    {
        v = 0;
        return reader.Offsets.TryResolve(key, out long o) && M.TryReadByte(inst + o, out v);
    }

    private bool TryString(string key, long inst, out string v)
    {
        v = string.Empty;
        if (!reader.Offsets.TryResolve(key, out long o)) return false;
        v = RobloxString.ReadStringAt(M, inst + o, reader.StringLengthOffset, true);
        return true;
    }

    private bool TryUDim2(string key, long inst, out string v)
    {
        v = string.Empty;
        if (!reader.Offsets.TryResolve(key, out long o)) return false;
        long a = inst + o;
        if (!M.TryReadFloat(a, out float sx) || !M.TryReadInt32(a + 4, out int ox) ||
            !M.TryReadFloat(a + 8, out float sy) || !M.TryReadInt32(a + 12, out int oy)) return false;
        if (!float.IsFinite(sx) || !float.IsFinite(sy)) return false;
        v = $"{{{F(sx)}, {ox}}}, {{{F(sy)}, {oy}}}";
        return true;
    }

    private bool TryVec2(long a, out string v)
    {
        v = string.Empty;
        if (!M.TryReadFloat(a, out float x) || !M.TryReadFloat(a + 4, out float y) || !Sane(x) || !Sane(y)) return false;
        v = $"{F(x)}, {F(y)}";
        return true;
    }

    private bool TryVec3(long a, out string v)
    {
        v = string.Empty;
        if (!M.TryReadFloat(a, out float x) || !M.TryReadFloat(a + 4, out float y) || !M.TryReadFloat(a + 8, out float z)) return false;
        if (!Sane(x) || !Sane(y) || !Sane(z)) return false;
        v = $"{F(x)}, {F(y)}, {F(z)}";
        return true;
    }

    private static bool Sane(float f) => float.IsFinite(f) && MathF.Abs(f) < 1e7f;
    private static string F(float f) => Math.Round(f, 3).ToString("0.###", Inv);
    private static string Bool(byte b) => b != 0 ? "true" : "false";
}
