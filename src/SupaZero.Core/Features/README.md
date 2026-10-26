# Struktur per fitur (v0.3.14)

FarmMachine (otak, murni) dan GameReader (pembaca memori, read-only) adalah `partial class`.
Setiap folder fitur berisi potongan keduanya + kelas pendukungnya. Namespace TIDAK ikut folder
(tetap `SupaZero.Core.Automation` / `SupaZero.Core.Game`), jadi memindah file tidak mengubah kode lain.

| Folder | Isi |
|---|---|
| `Automation/` (inti) | `FarmMachine.cs` (Step, fase, util), `MacroRunner`, `MacroAction`, `IInput` |
| `Game/` (inti) | `GameReader.cs` (Read, Rect, IsShown, Text, Find), `GameReader.World.cs` (musuh, pickup, HP, kamera, posisi, area), `GameSnapshot`, `AnimeZeroPaths`, `HudText`, `RobloxSession` |
| `Features/Lobby` | `FarmMachine.Lobby.cs` (jalan ke room, menu mode/map/act, vote), `GameReader.Lobby.cs` (hub, room, lingkaran), `GameReader.LobbyLists.cs` (daftar map/act/difficulty) |
| `Features/Combat` | `FarmMachine.Fight.cs`, `.Pickup.cs`, `.Arise.cs`, `GameReader.CombatHud.cs` (skill/ult/dash) |
| `Features/Navigation` | `FarmMachine.Walk.cs`, `Navigator`, `PathPlanner`, `NavMap`, `NavReader` |
| `Features/Rubble` | `GameReader.Rubble.cs` (RubblePending, titik serang) |
| `Features/Modes` | `FarmMachine.Modes.cs` (Blessing, Trials, Drop Target), `ModeReader`, `BlessingCatalog` |
| `Features/Power` | `FarmMachine.PowerCheck.cs` (cek Trait/Spinning), `Powers`, `SpinWatch`, `RollTitleTracker` |
| `Features/Upgrade` | `FarmMachine.Upgrade.cs`, `GameReader.SkillTree.cs`, `UpgradePlanner` |
| `Features/EndScreen` | `FarmMachine.EndScreen.cs` (Victory/Defeat) |
| `Features/Calibration` | `FarmMachine.Calibration.cs`, `Calibration` |

Tes: `tests/SupaZero.Core.Tests/FarmMachine<Fitur>Tests.cs` (partial `FarmMachineTests`, helper `New`/`Real` di `FarmMachineTests.cs`).

Aturan: fitur baru = folder baru + `FarmMachine.<Fitur>.cs` + `FarmMachine<Fitur>Tests.cs`; path game hanya di `AnimeZeroPaths` dengan bukti snapshot.
