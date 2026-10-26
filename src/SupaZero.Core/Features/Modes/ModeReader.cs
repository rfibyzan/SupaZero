namespace SupaZero.Core.Game;

/// <summary>
/// Pembacaan mode non-Story (snapshot RIE Byzan 24 Okt 2026): Drop Target di panel Rewards, kartu Mirror Blessing,
/// HUD wave raid (RunSide), BossBar [IMMUNE], panel Trials, tombol &amp; drop EndScreen. Semua READ-ONLY.
/// Catatan inset: semua ScreenGui di snapshot melaporkan AbsolutePosition dalam koordinat klien (root 0,0 tinggi 1009
/// atau 0,58 tinggi 951), jadi rect di sini memakai inset 0 (<see cref="ClientRect"/>).
/// </summary>
public sealed partial class GameReader
{
    private GuiRect? ClientRect(long node) => HubRect(node);
    private GuiRect? ShownClientRect(long node, long pg) => node != 0 && IsShown(node, pg) ? ClientRect(node) : null;

    /// <summary>Icon material (rbxassetid) → nama, dari kartu Rewards yang pernah terbaca + tabel bawaan.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _materialIcons = new(AnimeZeroPaths.MaterialIcons, StringComparer.Ordinal);
    /// <summary>v0.3.5: nama key drop EndScreen ("material119") → nama material, dipelajari saat icon-nya cocok dengan kartu Rewards.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _materialKeys = new(StringComparer.Ordinal);
    /// <summary>Naik tiap ada nama material baru (icon atau key) → aplikasi menyimpan ke settings.</summary>
    public int MaterialVersion { get; private set; }
    /// <summary>Isi "icon:rbxassetid://…" / "key:materialNNN" → nama, untuk disimpan antar-sesi.</summary>
    public Dictionary<string, string> LearnedMaterials()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in _materialIcons) if (!AnimeZeroPaths.MaterialIcons.ContainsKey(kv.Key)) d["icon:" + kv.Key] = kv.Value;
        foreach (var kv in _materialKeys) d["key:" + kv.Key] = kv.Value;
        return d;
    }
    public void SeedMaterials(IReadOnlyDictionary<string, string>? saved)
    {
        if (saved is null) return;
        foreach (var kv in saved)
        {
            if (kv.Key.StartsWith("icon:", StringComparison.Ordinal)) _materialIcons.TryAdd(kv.Key[5..], kv.Value);
            else if (kv.Key.StartsWith("key:", StringComparison.Ordinal)) _materialKeys.TryAdd(kv.Key[4..], kv.Value);
        }
    }
    private void Learn(System.Collections.Concurrent.ConcurrentDictionary<string, string> d, string k, string v)
    {
        if (d.TryGetValue(k, out var old) && old == v) return;
        d[k] = v; MaterialVersion++;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unknownIcons = new(StringComparer.Ordinal);
    /// <summary>Drop terakhir yang namanya belum diketahui (key + icon) untuk log diagnosa.</summary>
    public string? UnknownMaterial { get; set; }

    private string? Prop(long node, string name) =>
        node != 0 && props.Read(node).TryGetValue(name, out var v) && v.Length > 0 ? v : null;

    private string? TextAt(long root, string path) => root != 0 ? CleanText(reader.ResolvePath(root, path)) : null;

    /// <summary>Kartu Rewards (lobby Create Room atau panel Trials). <paramref name="nested"/> = RewardN.RewardN.Main (lobby) vs RewardN.Main (Trials).</summary>
    private List<RewardCard> ReadRewards(long scroll, bool nested, long pg)
    {
        var list = new List<RewardCard>();
        if (scroll == 0) return list;
        foreach (long card in reader.GetChildren(scroll, 64))
        {
            string name = reader.GetName(card);
            if (!name.StartsWith("Reward", StringComparison.Ordinal)) continue;
            long btn = nested ? reader.FindChild(card, name) : card;
            long main = btn != 0 ? reader.FindChild(btn, "Main") : 0;
            if (main == 0) continue;
            string? title = TextAt(main, "TxtShadow.Txt");
            string? icon = Prop(reader.FindChild(main, "Icon"), "Image");
            if (title is not null && icon is not null) Learn(_materialIcons, icon, title);
            long pill = reader.FindChild(main, "TargetPill");
            bool targetable = pill != 0 && IsShown(pill, pg);
            string? pillText = targetable ? TextAt(pill, "Pill.Txt(Shadow).Txt") : null;
            list.Add(new RewardCard(
                title ?? name,
                targetable,
                pillText?.Contains("UNTARGET", StringComparison.OrdinalIgnoreCase) == true,
                targetable ? ClientRect(reader.FindChild(pill, "Hit")) : null,
                TextAt(main, "AmountShadow.Amount")));
        }
        return list;
    }

    private List<BlessingOffer> ReadBlessings(long pg)
    {
        var list = new List<BlessingOffer>();
        long picker = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.BlessingPicker) : 0;
        if (picker == 0 || !IsShown(picker, pg)) return list;
        long holder = reader.ResolvePath(picker, "Holder.List");
        foreach (long offer in reader.GetChildren(holder, 16))
        {
            if (!reader.GetName(offer).StartsWith("Offer", StringComparison.Ordinal) || !IsShown(offer, pg)) continue;
            long body = reader.FindChild(offer, "Body");
            if (body == 0) continue;
            string? heading = TextAt(body, "TextHolder.Heading");
            if (heading is null) continue;
            list.Add(new BlessingOffer(heading, TextAt(body, "Rarity.Txt") ?? "", TextAt(body, "TextHolder.Value"),
                ShownClientRect(reader.FindChild(body, "PickButton"), pg) ?? ClientRect(offer), TextAt(body, "TextHolder.Description")));
        }
        return list;
    }

    private TrialsInfo? ReadTrials(long pg)
    {
        long root = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.TrialsRoot) : 0;
        if (root == 0 || !IsShown(root, pg)) return null;
        long room = reader.FindChild(root, "RoomRewards");
        bool roomShown = room != 0 && IsShown(room, pg);
        long rewards = roomShown ? reader.ResolvePath(root, "Rewards.Container.ScrollingFrame") : 0;
        return new TrialsInfo(
            ClientRect(reader.ResolvePath(root, "Tabs.RoomRewards")),
            roomShown,
            roomShown ? ParseInt(TextAt(room, "CurrentRoom.Title")) : null,
            roomShown ? ParseInt(TextAt(room, "HighestRoom.Title")) : null,
            roomShown ? ClientRect(reader.ResolvePath(room, "LeftButtons.Decrease")) : null,
            roomShown ? ClientRect(reader.ResolvePath(room, "LeftButtons.Increase")) : null,
            ClientRect(reader.ResolvePath(root, "Buttons.StaerButton.StaerButton")),
            ReadRewards(rewards, false, pg),
            rewards != 0 ? ClientRect(rewards) : null)
        {
            Leave = reader.ResolvePath(root, "Buttons.LeaveButton.LeaveButton") is var lv and not 0 && IsShown(lv, pg)
                && TextAt(lv, "Txt(Shadow)")?.Trim() == "Leave" ? ClientRect(lv) : null,
        };
    }

    /// <summary>Drop layar hasil: anak RewardsFrame.CutFrame.Holder yang tampil (money, gems, materialNNN, unit...) + jumlah.</summary>
    private List<string> ReadDrops(long end, long pg)
    {
        var list = new List<string>();
        long holder = end != 0 ? reader.ResolvePath(end, AnimeZeroPaths.EndDropsHolder) : 0;
        if (holder == 0) return list;
        foreach (long item in reader.GetChildren(holder, 128))
        {
            string cls = reader.GetClassName(item);
            if (cls != "ImageButton" || !IsShown(item, pg)) continue;
            string key = reader.GetName(item);
            long main = reader.FindChild(item, "Main");
            string? amount = TextAt(main, "AmountShadow.Amount");
            string? icon = Prop(reader.FindChild(main, "Icon"), "Image");
            string name;
            if (icon is not null && _materialIcons.TryGetValue(icon, out var n)) { name = n; if (key.StartsWith("material", StringComparison.Ordinal)) Learn(_materialKeys, key, n); }
            else if (_materialKeys.TryGetValue(key, out var kn)) name = kn;
            else
            {
                name = AnimeZeroPaths.DropLabel(key);
                if (key.StartsWith("material", StringComparison.Ordinal) && icon is not null && _unknownIcons.TryAdd(icon, 0)) UnknownMaterial = $"{key} icon {icon}";
            }
            list.Add(amount is null ? name : $"{name} {amount}");
        }
        return list;
    }

    /// <summary>Tombol EndScreen: ButtonsHolder.&lt;X&gt;Button.Button, hanya bila tampil (Next tidak ada di raid).</summary>
    private GuiRect? EndButton(long end, long pg, string name) =>
        end != 0 ? ShownClientRect(reader.ResolvePath(end, AnimeZeroPaths.EndButtonsHolder + "." + name + ".Button"), pg) : null;

    private (string? Wave, int? Alive) ReadRunSide(long pg)
    {
        long c = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.RunSideContainer) : 0;
        if (c == 0 || !IsShown(c, pg)) return (null, null);
        return (TextAt(c, "Title"), ParseInt(TextAt(c, "Alive.Value")));
    }

    private (string? Name, string? Tag) ReadBoss(long pg)
    {
        long h = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.BossHeader) : 0;
        if (h == 0 || !IsShown(h, pg)) return (null, null);
        return (TextAt(h, "Title"), TextAt(h, "Tag")?.Trim('[', ']', ' '));
    }

    /// <summary>Workspace.World.Markers.BossSpawn dan kotak Areas.&lt;n&gt; (Part: posisi + Size).</summary>
    private (Settings.Vec3? Boss, AreaBox? Area) ReadMarkers(long ws, int area)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return (null, null);
        long boss = reader.ResolvePath(world, "Markers.BossSpawn");
        Settings.Vec3? bossPos = boss != 0 && props.TryGetWorldPosition(boss, out float bx, out float by, out float bz) ? new(bx, by, bz) : null;
        AreaBox? box = null;
        long areas = reader.FindChild(world, AnimeZeroPaths.WorldAreas);
        long part = areas != 0 ? reader.FindChild(areas, Math.Max(1, area).ToString()) : 0;
        if (part != 0 && props.TryGetWorldPosition(part, out float x, out float y, out float z))
        {
            var size = ParseVec(Prop(part, "Size"));
            box = new AreaBox(new(x, y, z), size ?? new(0, 0, 0));
        }
        return (bossPos, box);
    }

    private static Settings.Vec3? ParseVec(string? s)
    {
        if (s is null) return null;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return double.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out double a)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out double b)
            && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out double c) ? new(a, b, c) : null;
    }

    private GameSnapshot ReadModes(GameSnapshot snap, long pg, long ws, long lobby, bool lobbyShown, long end, bool endShown)
    {
        var (wave, alive) = ReadRunSide(pg);
        var (bossName, bossTag) = ReadBoss(pg);
        var (bossSpawn, areaBox) = snap.InRound ? ReadMarkers(ws, snap.Hud?.Area ?? 1) : (null, null);
        long rewards = lobbyShown ? reader.ResolvePath(lobby, AnimeZeroPaths.LobbyRewardsScroll) : 0;
        long tip = lobbyShown ? reader.ResolvePath(lobby, AnimeZeroPaths.LobbyRewardsTip) : 0;
        return snap with
        {
            LobbyRewards = ReadRewards(rewards, true, pg),
            LobbyRewardsView = rewards != 0 ? ClientRect(rewards) : null,
            LobbyTargetTip = tip != 0 && IsShown(tip, pg) ? CleanText(tip) : null,
            Blessings = ReadBlessings(pg),
            Trials = lobbyShown ? null : ReadTrials(pg),
            RunWave = wave,
            RunAlive = alive,
            BossName = bossName,
            BossTag = bossTag,
            BossSpawn = bossSpawn,
            CurrentArea = areaBox,
            Nav = snap.InRound ? ReadNav(ws) : null,
            NextButton = endShown ? EndButton(end, pg, "NextButton") : null,
            RetryButton = endShown ? EndButton(end, pg, "RetryButton") : null,
            LeaveButton = endShown ? EndButton(end, pg, "LeaveButton") : null,
            NextButtonText = endShown ? TextAt(end, AnimeZeroPaths.EndButtonsHolder + ".NextButton.MainText") : null,
            Drops = endShown ? ReadDrops(end, pg) : [],
        };
    }
}
