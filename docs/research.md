# Phase 1 research (RIE, Roblox account AuralNova)

Summary from the old Project (the original notes/research.md lives in the old Hark account).

Phase 1 research done:
- Dungeon: enemies in Workspace.enemies (models named by number, HRP, Humanoid); dead enemy = Model removed; Health not readable in RIE. Area cleared = ChildCount 0. Workspace.World: Areas 1-5, Spawns 1-5, Navigation (obstacle parts), TpBack.
- HUD: Players.AuralNova.PlayerGui.RoundHUD.Container.TopPart.RemainingFrame.EnemyCount.Text (e.g. "Easy · Area 3/5 · 8 left"), Timer.Text, WaveTemplate.Title.Text, RespawnText.
- StartVote "Start Game?" Yes ~(960,232), 48 s timer, appears at the start of every stage.
- Victory screen: EndScreen, Retry/Next/Leave buttons, boss HP BossBar. Defeat: ResultText "DEFEAT" (EnemyCount stays "Story complete", not a victory signal).
- Keybinds: left click basic attack, Q dash, 1/2/3 skills, T switch power (Sukuna & Katakuri, detected via the Ultimate icon asset id).
- Lobby CREATE LOBBY in PlayerGui["UI Animation (Finished)"].Quque.MainFrame: map Content.MapsList..., Act Content.ActsList...Act1/2/3, difficulty Easy/Medium/Hard/Nightmare (centers ~(1033,589)/(1147,589)/(1261,589)/(1374,589)), Start = StaerButton (1364,934), Leave (1639,934); coordinates need the window offset. Verify via MapLine.Tag and ActLine.Tag. The 16:46 diff (Medium->Easy) I declared wrong; do not use it as evidence for Act 2.
- Upgrade: SkillTree per unit (UI Animation (Working).SkillTree), tokens in Header.Holder.Txt, 4 columns ATK/CRIT/DEF/SPD x 5 slots; buy = click slot (CanBuy.Visible) then BuyFrame.BuyButton; verify tokens drop and CanBuy disappears.

Note: every stage requires character movement; HxH Act 1-3 has blocking boulders that must be destroyed.


## YouTube + wiki research (23 Oct 2026)

- Modes in the Create Lobby menu: **Story** (acts unlock in order; Sakamoto map ends with boss Uzuki, HxH with rubble), **Defend** (Sakamoto Defense), **Infinite** (endless waves, boss every 10/8/6/5 waves by difficulty, materials from wave 16), **Raid** (Spirited Away & Shadow Gate Nightmare-only, 5 runs then a 30-minute cooldown; Evil Reflection Easy–Nightmare), **Trials** (tower of 5 rooms per floor, boss in room 5, Next Floor button), **Tidal High Rise** (climb before the water rises).
- Create Lobby menu: mode → map → act → difficulty; has a reward preview and Drop Target (x3 Nightmare, x2 Hard).
- Difficulty: Easy (HP 0.85x) up to Nightmare (2x); Story Easy 1,500 coins + 50 gems, Nightmare 15,000 coins + 350 gems.
- Combat: only 2 enemies can attack at once; guard bar + perfect block; **T** switches weapon (+20% dmg for 4 s); rubble max 30% HP per hit (don't use ultimates on rubble).
- There is a start vote before the first Story wave.
- Matchmaking can join open rooms from other lobby servers (toggle in the queue).
- Sources: animezeroroblox.wiki/guide/game-modes, animezero-wiki.wiki, video "The BEST Beginner's Guide In Anime Zero" (MK, 9 Oct 2026).

## Byzan's RIE verification (23 Oct 2026)

- Create Room circle = `Workspace.Systems.QuqueRooms.<n>.JoinHitbox` (Part, Anchored, CanCollide/CanTouch false, Size 18×25×25, Transparency 1). Example room 1: Position (-83.382, 5.926, 202.381).
- Room contents: `Barrier` (Folder), `Capsule` (Model), `Inside`/`Outside`/`RingVFX` (Part), `RoomGui` (BillboardGui → Container).
- `Workspace.Systems` also contains `AreasTeleportLocation`, `Cams`, `DeliverySpawns`, `GuideSpots`, `NPCS`.
- RoomGui: an empty room shows "Start Here"; a room already created by another player shows "Join Here", a mode+map card (e.g. Story / Hxh Chapter 3), "Players 3/4", and a countdown (e.g. 28s).
- Byzan reported: sometimes the character is already in the circle but the Create Room menu does not open → the macro moves to another circle after the timeout.
- Areas button = `PlayerGui.LobbyHUD.LeftSideButtons.AreasButton` (ImageButton, AbsPos 24.989,516.355, AbsSize 88.977×106.558). Story card = `PlayerGui.Finished UI.Areas.MainFrame.Content.Clipping.Container.MATCHES_Row1.Story.Story` (ImageButton inside the Story Frame).
- RIE markers with Inset Y 58 land ~58 px too low for LobbyHUD & Finished UI (hitting the Spinning button) → these GUIs do not use the topbar inset; SupaZero uses LobbyInsetY = 0 for the hub.
