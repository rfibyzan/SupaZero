# SupaZero

Macro **Anime Zero** (Roblox, place `114574503491412`): otomatis main Story stage demi stage, upgrade SkillTree tiap selesai stage bila perlu, sampai seluruh Story selesai. UI sama dengan SupaFisch (C# .NET 8 + Avalonia, tema Black/White/Custom).

![General](docs/img/general.png)

## Batasan (wajib)

- Memori Roblox **hanya dibaca** (reader dari RobloxInstanceExplorer). Input lewat **SendInput** saja.
- TANPA WriteProcessMemory, injeksi, DLL, FFlags, atau bypass anti-cheat.
- Input hanya dikirim saat jendela Roblox di depan (alt-tab = macro otomatis menahan input).

## Struktur

| Folder | Isi |
|---|---|
| `src/RIE.Core` | Salinan reader read-only RobloxInstanceExplorer v3.12 (ProcessMemory, offsets, InstanceReader, PropertyReader). |
| `src/SupaZero.Core` | `Game/` baca keadaan game → `GameSnapshot`; `Automation/` `StoryMachine` (otak macro, murni & dites), `UpgradePlanner`, `MacroRunner` (loop); `Interop/Win32Input` (SendInput); `Settings/`. |
| `src/SupaZero.App` | UI Avalonia gaya SupaFisch: tab General, Story, Upgrade, Settings. |
| `tests/SupaZero.Core.Tests` | xUnit: parser HUD, state machine, planner upgrade, GameReader dengan memori palsu. |
| `docs/` | `research.md` (hasil riset RIE fase 1), `ROADMAP.md`. |

## Menjalankan (PowerShell)

```powershell
git clone https://github.com/rfibyzan/SupaZero.git
cd SupaZero
dotnet run --project src/SupaZero.App -c Release
```

Tes: `dotnet test tests/SupaZero.Core.Tests`

Offsets: Settings → Offsets → **Unduh terbaru** (disimpan ke `%AppData%\SupaZero\offsets.json`), atau otomatis memakai `offsets.json` milik RIE bila ada. Pengaturan di `%AppData%\SupaZero\settings.json`, log di `%AppData%\SupaZero\logs`.

## Alur macro

1. **Lobby**: CREATE LOBBY terbuka → cek Map/Act (`MapLine.Tag`, `ActLine.Tag`) → klik Difficulty → Start (`StaerButton`).
2. **Start Game?** → Yes.
3. **Ronde**: selama `Workspace.enemies` berisi musuh → klik kiri + skill bergiliran (default 1,2,3).
4. **Area bersih** → navigasi ke area berikutnya (**belum otomatis**, lihat ROADMAP).
5. **EndScreen menang** → upgrade SkillTree (bila diaktifkan dan terbuka) → Next.
6. **DEFEAT** (`ResultText`) → Retry sampai batas, lalu berhenti.

Hotkey default **F1** (Start/Stop), bisa diganti di tab General.
