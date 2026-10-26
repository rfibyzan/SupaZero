# Per-feature layout (v0.3.14)

FarmMachine (the brain, pure) and GameReader (memory reader, read-only) are `partial class`es.
Each feature folder holds pieces of both + its supporting classes. Namespaces do NOT follow folders
(they stay `SupaZero.Core.Automation` / `SupaZero.Core.Game`), so moving a file does not change other code.

| Folder | Contents |
|---|---|
| `Automation/` (core) | `FarmMachine.cs` (Step, phases, utils), `MacroRunner`, `MacroAction`, `IInput` |
| `Game/` (core) | `GameReader.cs` (Read, Rect, IsShown, Text, Find), `GameReader.World.cs` (enemies, pickups, HP, camera, position, area), `GameSnapshot`, `AnimeZeroPaths`, `HudText`, `RobloxSession` |
| `Features/Lobby` | `FarmMachine.Lobby.cs` (walk to room, mode/map/act menu, vote), `GameReader.Lobby.cs` (hub, rooms, circles), `GameReader.LobbyLists.cs` (map/act/difficulty lists) |
| `Features/Combat` | `FarmMachine.Fight.cs`, `.Pickup.cs`, `.Arise.cs`, `GameReader.CombatHud.cs` (skill/ult/dash) |
| `Features/Navigation` | `FarmMachine.Walk.cs`, `Navigator`, `PathPlanner`, `NavMap`, `NavReader` |
| `Features/Rubble` | `GameReader.Rubble.cs` (RubblePending, attack points) |
| `Features/Modes` | `FarmMachine.Modes.cs` (Blessing, Trials, Drop Target), `ModeReader`, `BlessingCatalog` |
| `Features/Power` | `FarmMachine.PowerCheck.cs` (Trait/Spinning check), `Powers`, `SpinWatch`, `RollTitleTracker` |
| `Features/Upgrade` | `FarmMachine.Upgrade.cs`, `GameReader.SkillTree.cs`, `UpgradePlanner` |
| `Features/EndScreen` | `FarmMachine.EndScreen.cs` (Victory/Defeat) |
| `Features/Calibration` | `FarmMachine.Calibration.cs`, `Calibration` |

Tests: `tests/SupaZero.Core.Tests/FarmMachine<Feature>Tests.cs` (partial `FarmMachineTests`, helpers `New`/`Real` in `FarmMachineTests.cs`).

Rule: new feature = new folder + `FarmMachine.<Feature>.cs` + `FarmMachine<Feature>Tests.cs`; game paths only in `AnimeZeroPaths`, backed by snapshot evidence.
