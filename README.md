# SupaZero

Macro **AutoFarm Anime Zero** (Roblox, place `114574503491412`): Story (lanjut stage demi stage), Infinite, Defend, Raid, Trials. Jalan sendiri ke lingkaran Create Room, pilih mode/map/act/difficulty, bertarung, jalan ke area berikutnya (serang rubble bila tersangkut), upgrade SkillTree, lalu Next/Retry. UI sama dengan SupaFisch (C# .NET 8 + Avalonia, tema Black/White/Custom).

![General](docs/img/general.png)

## Batasan (wajib)

- Memori Roblox **hanya dibaca** (reader dari RobloxInstanceExplorer). Input lewat **SendInput** saja.
- TANPA WriteProcessMemory, injeksi, DLL, FFlags, atau bypass anti-cheat.
- Input hanya dikirim saat jendela Roblox di depan (alt-tab = macro otomatis menahan input).

## Struktur

| Folder | Isi |
|---|---|
| `src/RIE.Core` | Salinan reader read-only RobloxInstanceExplorer v3.12 (ProcessMemory, offsets, InstanceReader, PropertyReader). |
| `src/SupaZero.Core` | `Game/` baca keadaan game → `GameSnapshot`; `Automation/` `FarmMachine` (otak macro, murni & dites), `Navigator` (W/A/S/D ke tujuan dari posisi karakter + arah kamera), `UpgradePlanner`, `MacroRunner` (loop); `Interop/Win32Input` (SendInput); `Settings/`. |
| `src/SupaZero.App` | UI Avalonia gaya SupaFisch: tab General, AutoFarm, Upgrade, Offsets, Settings. |
| `tests/SupaZero.Core.Tests` | xUnit: parser HUD, FarmMachine, Navigator, planner upgrade, GameReader dengan memori palsu. |
| `docs/` | `research.md` (hasil riset RIE fase 1), `ROADMAP.md`. |

## Menjalankan (PowerShell)

```powershell
git clone https://github.com/rfibyzan/SupaZero.git
cd SupaZero
dotnet run --project src/SupaZero.App -c Release
```

Tes: `dotnet test tests/SupaZero.Core.Tests`

Offsets: tab **Offsets** → **Unduh terbaru** (disimpan ke `%AppData%\SupaZero\offsets.json`), atau otomatis memakai `offsets.json` milik RIE bila ada. Pengaturan di `%AppData%\SupaZero\settings.json`, log di `%AppData%\SupaZero\logs`.

## Alur macro

1. **Lobby**: jalan (W/A/S/D, tanpa teleport) ke lingkaran Create Room yang kamu rekam di tab AutoFarm.
2. **Create Room**: pilih Mode → Map (berhenti bila map/act terkunci) → Act → Difficulty → Start (`StaerButton`).
3. **Start Game?** → Yes.
4. **Ronde**: selama `Workspace.enemies` berisi musuh → klik kiri + tombol skill sesuai cooldown masing-masing.
5. **Area bersih** → jalan ke `Workspace.World.Spawns/Areas[n+1]`; tersangkut 1,5 dtk → lompat + serang (rubble HxH).
6. **EndScreen menang** → upgrade SkillTree (bila aktif dan terbuka) → Next (Story) / Retry (mode lain) / pilihan manual.
7. **DEFEAT** (`ResultText`) → Retry sampai batas, lalu berhenti.

Hotkey default **F1** (Start/Stop), bisa diganti di tab General.
