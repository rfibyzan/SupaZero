using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public enum MacroPhase { Idle, Lobby, Voting, Fighting, Navigating, Upgrading, Advancing, Retrying, Done }

/// <summary>
/// Otak macro Story: murni (tanpa I/O), menerima <see cref="GameSnapshot"/> + waktu, mengembalikan aksi. Mudah dites.
/// Alur: Lobby (CREATE LOBBY → map/act/difficulty → Start) → StartVote (Yes) → ronde (serang selama ada musuh;
/// area bersih → navigasi ke area berikutnya) → EndScreen menang (upgrade bila perlu → Next) / kalah (Retry s.d. batas).
/// Navigasi antar-area dan penghancuran boulder (HxH Act 1-3) BELUM diimplementasikan: macro memberi catatan dan
/// menunggu pemain menggerakkan karakter. Lihat docs/ROADMAP.md.
/// </summary>
public sealed class StoryMachine(Func<AppSettings> settings)
{
    private readonly Dictionary<string, long> _cooldowns = new(StringComparer.Ordinal);
    private long _lastAttack, _lastSkill;
    private int _skillIndex;
    private int _retries;
    private int _boughtThisStage;
    private bool _upgradeDoneThisStage;
    private bool _pendingBuyConfirm;
    private int? _tokensBeforeBuy;
    private GameScreen _lastScreen = GameScreen.Unknown;

    public MacroPhase Phase { get; private set; } = MacroPhase.Idle;
    public int StagesCleared { get; private set; }
    public int Retries => _retries;
    public string Status { get; private set; } = "Idle";

    public void Reset()
    {
        _cooldowns.Clear();
        _lastAttack = _lastSkill = 0;
        _skillIndex = _retries = _boughtThisStage = 0;
        _upgradeDoneThisStage = _pendingBuyConfirm = false;
        _tokensBeforeBuy = null;
        _lastScreen = GameScreen.Unknown;
        StagesCleared = 0;
        Phase = MacroPhase.Idle;
        Status = "Idle";
    }

    public IReadOnlyList<MacroAction> Step(GameSnapshot s, long nowMs)
    {
        var S = settings();
        var screen = s.Screen;
        if (screen != _lastScreen) OnScreenChanged(_lastScreen, screen);
        _lastScreen = screen;

        return screen switch
        {
            GameScreen.Unknown => Wait("Menunggu Roblox / DataModel."),
            GameScreen.Lobby => LobbyIdle(S, nowMs),
            GameScreen.LobbyMenu => LobbyMenu(s, S, nowMs),
            GameScreen.StartVote => Vote(s, S, nowMs),
            GameScreen.InRound => Fight(s, S, nowMs),
            GameScreen.Victory => Victory(s, S, nowMs),
            GameScreen.Defeat => Defeat(s, S, nowMs),
            _ => [],
        };
    }

    private void OnScreenChanged(GameScreen from, GameScreen to)
    {
        if (to == GameScreen.Victory && from != GameScreen.Victory) StagesCleared++;
        if (to == GameScreen.StartVote || (to == GameScreen.InRound && from is GameScreen.Victory or GameScreen.Defeat))
        {
            _boughtThisStage = 0;
            _upgradeDoneThisStage = false;
            _pendingBuyConfirm = false;
        }
        if (to == GameScreen.Victory) _retries = 0;
    }

    private IReadOnlyList<MacroAction> Wait(string why)
    {
        Phase = MacroPhase.Idle;
        Status = why;
        return [];
    }

    private IReadOnlyList<MacroAction> LobbyIdle(AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        Status = "Di lobby. Buka CREATE LOBBY (tombol pembukanya belum diriset).";
        return Note("lobby-open", now, 5000, "Lobby: buka menu CREATE LOBBY secara manual; macro lanjut otomatis setelah menu terbuka.");
    }

    private IReadOnlyList<MacroAction> LobbyMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var st = S.Story;
        if (!st.AutoCreateLobby)
        {
            Status = "CREATE LOBBY terbuka (Auto create lobby mati).";
            return [];
        }
        var actions = new List<MacroAction>();
        string wantAct = "Act" + st.Act;
        if (!string.IsNullOrWhiteSpace(st.Map) && !Matches(s.LobbyMapTag, st.Map))
        {
            Status = $"Map terpilih '{s.LobbyMapTag ?? "?"}', target '{st.Map}'.";
            return Note("lobby-map", now, 4000, $"Pilih map '{st.Map}' di MapsList (klik map belum diotomasi; MapLine.Tag = '{s.LobbyMapTag ?? "?"}').");
        }
        if (!Matches(s.LobbyActTag, wantAct) && !Matches(s.LobbyActTag, st.Act.ToString()))
        {
            Status = $"Act terpilih '{s.LobbyActTag ?? "?"}', target {wantAct}.";
            return Note("lobby-act", now, 4000, $"Pilih {wantAct} di ActsList (klik act belum diotomasi; ActLine.Tag = '{s.LobbyActTag ?? "?"}').");
        }
        var (dx, dy) = AnimeZeroPaths.DifficultyCenters[(int)st.Difficulty];
        if (Ready("lobby-diff", now, 6000))
            actions.Add(new ClickAction(dx, dy, $"Difficulty {st.Difficulty}"));
        if (s.LobbyStart is { } start && Ready("lobby-start", now, 3000))
            actions.Add(new ClickAction(start.CenterX, start.CenterY, "Start (CREATE LOBBY)"));
        Status = $"CREATE LOBBY: {st.Map} {wantAct} {st.Difficulty} → Start.";
        return actions;
    }

    private IReadOnlyList<MacroAction> Vote(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Voting;
        Status = "Start Game? → Yes";
        if (!S.Story.AutoVoteStart || !Ready("vote-yes", now, 2000)) return [];
        var (x, y) = s.StartVoteYes is { } r ? (r.CenterX, r.CenterY) : (AnimeZeroPaths.StartVoteYesX, AnimeZeroPaths.StartVoteYesY);
        return [new ClickAction(x, y, "Vote Yes")];
    }

    private IReadOnlyList<MacroAction> Fight(GameSnapshot s, AppSettings S, long now)
    {
        var hud = s.Hud;
        int left = s.EnemyCount >= 0 ? s.EnemyCount : hud?.EnemiesLeft ?? -1;
        string area = hud is { AreaTotal: > 0 } ? $"Area {hud.Area}/{hud.AreaTotal}" : "Area ?";
        if (left == 0)
        {
            Phase = MacroPhase.Navigating;
            Status = $"{area} bersih → pindah ke area berikutnya.";
            return Note("nav", now, 6000, $"{area} bersih. Navigasi + boulder belum otomatis: gerakkan karakter ke area berikutnya.");
        }
        Phase = MacroPhase.Fighting;
        Status = $"{area} · {(left < 0 ? "?" : left.ToString())} musuh";
        var c = S.Combat;
        var actions = new List<MacroAction>();
        if (now - _lastAttack >= Math.Max(30, c.AttackIntervalMs))
        {
            _lastAttack = now;
            actions.Add(new AttackAction("Serangan dasar"));
        }
        if (c.UseSkills && now - _lastSkill >= Math.Max(200, c.SkillIntervalMs))
        {
            var keys = c.SkillKeys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(k => VirtualKeys.TryParse(k, out var vk) ? vk : (ushort)0).Where(v => v != 0).ToList();
            if (keys.Count > 0)
            {
                _lastSkill = now;
                var vk = keys[_skillIndex++ % keys.Count];
                actions.Add(new KeyAction(vk, 40, $"Skill {(char)vk}"));
            }
        }
        return actions;
    }

    private IReadOnlyList<MacroAction> Victory(GameSnapshot s, AppSettings S, long now)
    {
        var up = S.Upgrade;
        if (up.Enabled && !_upgradeDoneThisStage)
        {
            var upgrade = Upgrade(s, up, now);
            if (upgrade is not null) return upgrade;
        }
        if (!S.Story.AutoNext)
        {
            Phase = MacroPhase.Idle;
            Status = "Menang. Auto Next mati.";
            return [];
        }
        if (s.NextButton is null)
        {
            Phase = MacroPhase.Done;
            Status = "Menang, tombol Next tidak ada (Story selesai?).";
            return Note("no-next", now, 5000, "Tombol Next tidak terbaca di EndScreen.");
        }
        Phase = MacroPhase.Advancing;
        Status = $"Menang (stage ke-{StagesCleared}) → Next";
        return Ready("next", now, 2500) ? [new ClickAction(s.NextButton.Value.CenterX, s.NextButton.Value.CenterY, "Next stage")] : [];
    }

    /// <summary>null = upgrade selesai/tidak bisa, lanjut ke Next.</summary>
    private IReadOnlyList<MacroAction>? Upgrade(GameSnapshot s, UpgradeSettings up, long now)
    {
        Phase = MacroPhase.Upgrading;
        if (!s.SkillTreeOpen)
        {
            // Tombol pembuka SkillTree belum diriset: lewati upgrade stage ini agar macro tidak macet.
            _upgradeDoneThisStage = true;
            return [new NoteAction("SkillTree tidak terbuka (tombol pembukanya belum diriset) → upgrade stage ini dilewati.")];
        }
        if (_pendingBuyConfirm)
        {
            // Verifikasi: token turun → pembelian berhasil.
            if (s.Tokens is int t && _tokensBeforeBuy is int b && t < b)
            {
                _boughtThisStage++;
                _pendingBuyConfirm = false;
            }
            else if (!Ready("buy-timeout", now, 3000)) return [];
            else _pendingBuyConfirm = false; // timeout: anggap gagal, coba pilihan berikutnya
        }
        if (_boughtThisStage >= up.MaxBuysPerStage || s.Tokens is 0)
        {
            _upgradeDoneThisStage = true;
            return null;
        }
        if (s.BuyButton is { } buy && Ready("buy", now, 800))
        {
            _pendingBuyConfirm = true;
            _tokensBeforeBuy = s.Tokens;
            _cooldowns["buy-timeout"] = now;
            Status = $"Upgrade: beli (token {s.Tokens?.ToString() ?? "?"})";
            return [new ClickAction(buy.CenterX, buy.CenterY, "BuyButton")];
        }
        var slot = UpgradePlanner.Next(s.Slots, up.EffectivePriority, up.Mode, _boughtThisStage);
        if (slot is null)
        {
            _upgradeDoneThisStage = true;
            return null;
        }
        Status = $"Upgrade: pilih {slot.Column} #{slot.Index + 1}";
        return Ready("slot", now, 800) ? [new ClickAction(slot.Rect!.Value.CenterX, slot.Rect.Value.CenterY, $"Slot {slot.Column} #{slot.Index + 1}")] : [];
    }

    private IReadOnlyList<MacroAction> Defeat(GameSnapshot s, AppSettings S, long now)
    {
        var st = S.Story;
        if (st.RetryOnDefeat && _retries < st.MaxRetriesPerStage && s.RetryButton is { } retry)
        {
            Phase = MacroPhase.Retrying;
            Status = $"DEFEAT → Retry ({_retries + 1}/{st.MaxRetriesPerStage})";
            if (!Ready("retry", now, 3000)) return [];
            _retries++;
            return [new ClickAction(retry.CenterX, retry.CenterY, "Retry")];
        }
        Phase = MacroPhase.Done;
        Status = "DEFEAT, retry habis → macro berhenti.";
        return [new StopAction(Status)];
    }

    private IReadOnlyList<MacroAction> Note(string key, long now, int everyMs, string text)
    {
        if (!Ready("note:" + key, now, everyMs)) return [];
        return [new NoteAction(text)];
    }

    private bool Ready(string key, long now, int cooldownMs)
    {
        if (_cooldowns.TryGetValue(key, out long last) && now - last < cooldownMs) return false;
        _cooldowns[key] = now;
        return true;
    }

    private static bool Matches(string? actual, string want) =>
        actual is not null && actual.Replace(" ", "").Contains(want.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}
