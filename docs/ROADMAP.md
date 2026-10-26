# SupaZero Roadmap

Status: v0.2.0 (23 Oct 2026), AutoFarm. Not tested live yet.

## Done
- Read-only attach + auto-reattach on lobby ↔ stage teleport.
- AutoFarm: Story/Infinite/Defend/Raid/Trials modes, after victory Automatic/Next/Retry/Leave.
- Lobby: HUD Areas button → Story "Click to Teleport" card → circle detection ("Start Here" text in Workspace) → walk there; recorded position is only a fallback.
- Walk to the next area: camera-relative W/A/S/D (Navigator), no teleport.
- Stuck → jump + attack (HxH rubble).
- Detection of map/act/mode lists in Create Room + locked status (heuristic), maps saved for the dropdown.
- Per-row skill keys: Rebind like the hotkey, toggle, individual cooldowns.
- Offsets tab like SupaFisch (download, version, match check against installed Roblox, manual file).
- SkillTree upgrade: column priority, BuyButton, token-drop verification.

## Needs verification in RIE ([V] in `AnimeZeroPaths.cs`)
- HUD text "Areas", "Click to Teleport" card + "Story" title, and the teleport card. (Circle already verified: Systems.QuqueRooms.*.JoinHitbox.)
- StartVote + Yes, EndScreen + Next/Retry/Leave, ResultText, BossBar.
- `ModesList`, `MapsList`, `ActsList`: entry structure, locked ("*Lock*") and selected ("*Select*") markers.
- `Workspace.World.Spawns/Areas[n]` really is the point of area n. (S1 24 Oct: Spawns.n sits at the end of area n-1 facing area n; AreaEntries.n = the "AREA n" marker point, used first since v0.2.47.)
- SkillTree column contents.

## Not done yet
1. Turn/rotate the camera when the path is not straight (currently straight to the target + jump).
2. Face the character toward the nearest enemy / rubble.
3. SkillTree opener button: LobbyHUD Units (v0.2.92, verified in RIE).
4. Trials: Next Floor button; Raid: attempt cooldown detection.
