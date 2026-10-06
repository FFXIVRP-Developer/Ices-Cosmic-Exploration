# ICE fork

Fork of [Ices-Cosmic-Exploration](https://github.com/LeontopodiumNivale14/Ices-Cosmic-Exploration) (branch `Main-Branch`),
on branch `fork/guards`. Kept as close to upstream as possible: the fork's code lives in `ICE/Fork/` and
`ICE/ConfigFiles/Config_Fork.cs`; upstream files only gain one-line hooks, each marked `// Fork: item N`.

## Items

1. **Relic level gate.** A Cosmic relic tool is never handed in while the class it belongs to cannot wear what the
   hand-in gives (upstream hands it in as soon as the research is done, with no level check). What it gives comes from
   the game data: `WKSCosmoToolClass` (row = job - 7) lists each research stage's tool (v0.1-v0.8, Cosmic, v1.1-v1.4,
   Stellar, Stellar v1.x, Hyper, Hyper v1.x, of Stars). The research stage counts the hand-ins done: at stage N the
   hand-in gives stage N's tool, and the class's current tool on hand (worn, armoury chest, bags) must be exactly stage
   N-1's. No tool on hand is only allowed at stage 0 (the first tool). An older tool on hand while the current one is
   stored away, research or inventory not loaded, or no stage data: held. The class judged is always the one whose
   relic is exchanged, never the class worn alone (a Lv 100 class worn does not hand in a Lv 50 Culinarian's relic):
   checked at the hub decision (ICE's job and the class worn), when the hand-in starts, before the class is picked and
   before the confirmation. The NPC's class list is read by name (the class's name, abbreviation or one of its tools),
   not by counting positions as upstream does, and the confirmation is refused when it names another class's tool.
   YesAlready is held while any hand-in step is queued (upstream only holds it for taking or abandoning a mission), so
   it cannot confirm before the guard; it is given back as soon as none is. A hand-in that must not go on is aborted,
   the menus shut and ICE starts over. This covers every way into the hand-in, the debug "Relic Turnin" button
   included. `ForkRelicGuard.cs`.2. **No crafting while mounted.** A crafting mission (dual missions too) is not taken while mounted, and Artisan is not
   told to craft until the character is on foot: the guard dismounts and waits. `ForkMountGuard.cs`.
3. **AutoRetainer.** While ICE runs, AutoRetainer is suppressed (its IPC) and its "Artisan integration" held off on its
   live config (that integration stops Artisan whenever ventures are ready and a bell is in reach, suppressed or not;
   mid-mission ICE looped). Held as well whenever the game has a mission in progress, even with ICE stopped by hand;
   the retainer stop is only queued once the mission is turned in and re-checks before each step. Both are given back
   when ICE is idle and no mission runs; the held value is kept in ICE's config, so a crash or
   reload still gives it back. Between missions, with a retainer ready and a summoning bell within 150 m, ICE walks
   there, lets AutoRetainer collect them, closes the bell and carries on (no travel beyond that; 10 min between stops;
   each step gives up after a timeout instead of stalling ICE). Setting: "Retainers between missions" under
   "Turnin if relic is complete". `ForkAutoRetainer.cs`.
4. **IPC** (prefix `ICE.`, next to upstream's): `IsFork()`, `IsBusy()`, `RelicHandInBlocker(uint job)` ("" = allowed),
   `IsInRetainerBreak()`, `GetSetting(string)` / `SetSetting(string, bool)` for `TurninRelic`, `StopAfterCurrent`
   (read only) and `RetainersBetweenMissions`. `ForkIpc.cs`.

## Hook points (for merges from upstream)

| File | Hook |
|---|---|
| `ICE.cs` | `Load`: `ForkIpc = new(); Fork.ForkAutoRetainer.Init(); Fork.ForkRelicGuard.Init();` · `Dispose`: `Fork.ForkAutoRetainer.Dispose`, `Fork.ForkRelicGuard.Dispose` |
| `Task_CheckState.cs` | `HubActivityCheck`: retainer stop at the top; `&& ForkRelicGuard.AllowsHandIn(jobId)` on `TurninRelic` |
| `Task_RelicTurnin.cs` | `RegisterJob`, before the class entry click, before the Yes click: `Blocker` → `AbortHandIn`; the class entry from `SelectRelicEntry` (by name); before Yes: `OtherClassInPrompt` |
| `Task_CheckMissions.cs` | `Insert_GrabMissionTask`: `ForkMountGuard.ReadyForMission` before `GrabMission` |
| `Task_Craft.cs` | `ThrottleArtisanTaskV2`: `ForkMountGuard.ReadyToCraft` before `CraftItem` |
| `Task_DualClass.cs` | after the crafter job swap: `ForkMountGuard.ReadyToCraft` |
| `Mission_Setup.cs` | `Fork.ForkUi.Draw()` under the relic turn-in setting |

## Build and load

`dotnet build ICE/ICE.csproj -c Release -p:DalamudLibPath=<Me>\XIVLauncher\addon\Hooks\dev\` → `ICE\bin\Release\ICE.dll`.
Same internal name as the store ICE: add `ICE\bin\Release\ICE.dll` as a dev plugin location and disable the installed ICE.
