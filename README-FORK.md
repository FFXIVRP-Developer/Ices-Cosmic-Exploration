# ICE fork

Fork of [Ices-Cosmic-Exploration](https://github.com/LeontopodiumNivale14/Ices-Cosmic-Exploration) (branch `Main-Branch`),
on branch `fork/guards`. Kept as close to upstream as possible: the fork's code lives in `ICE/Fork/` and
`ICE/ConfigFiles/Config_Fork.cs`; upstream files only gain one-line hooks, each marked `// Fork: item N`.

## Items

1. **Relic level gate.** A Cosmic relic tool is never handed in for its next stage while the class cannot wear that
   stage yet (upstream hands it in as soon as the research is done, with no level check). The class's stage is its
   best Cosmic "Prototype" main tool in the worn gear, armoury chest or bags; the next stage is the next Prototype
   equip level in the game data for that class (v0.1-v0.8: Lv 10/30/50/60/70/80/90/100). With no tool owned, research
   stage 0 means the hand-in gives v0.1 (needs Lv 10); past stage 0 the tool is somewhere unseen and the hand-in is held.
   Held when in doubt (inventory or research data not loaded). Checked at the hub decision (ICE's job and the class worn), when the hand-in starts, and right before each
   irreversible click at the NPC; a hand-in that must not go on is aborted, the menus shut and ICE starts over.
   This covers every way into the hand-in, the debug "Relic Turnin" button included. `ForkRelicGuard.cs`.
2. **No crafting while mounted.** A crafting mission (dual missions too) is not taken while mounted, and Artisan is not
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
4. **IPC** (prefix `ICE.`, next to upstream's): `IsFork()`, `RelicHandInBlocker(uint job)` ("" = allowed),
   `IsInRetainerBreak()`, `GetSetting(string)` / `SetSetting(string, bool)` for `TurninRelic`, `StopAfterCurrent`
   (read only) and `RetainersBetweenMissions`. `ForkIpc.cs`.

## Hook points (for merges from upstream)

| File | Hook |
|---|---|
| `ICE.cs` | `Load`: `ForkIpc = new(); Fork.ForkAutoRetainer.Init();` · `Dispose`: `Fork.ForkAutoRetainer.Dispose` |
| `Task_CheckState.cs` | `HubActivityCheck`: retainer stop at the top; `&& ForkRelicGuard.AllowsHandIn(jobId)` on `TurninRelic` |
| `Task_RelicTurnin.cs` | `RegisterJob`, before the class entry click, before the Yes click: `Blocker` → `AbortHandIn` |
| `Task_CheckMissions.cs` | `Insert_GrabMissionTask`: `ForkMountGuard.ReadyForMission` before `GrabMission` |
| `Task_Craft.cs` | `ThrottleArtisanTaskV2`: `ForkMountGuard.ReadyToCraft` before `CraftItem` |
| `Task_DualClass.cs` | after the crafter job swap: `ForkMountGuard.ReadyToCraft` |
| `Mission_Setup.cs` | `Fork.ForkUi.Draw()` under the relic turn-in setting |

## Build and load

`dotnet build ICE/ICE.csproj -c Release -p:DalamudLibPath=<Me>\XIVLauncher\addon\Hooks\dev\` → `ICE\bin\Release\ICE.dll`.
Same internal name as the store ICE: add `ICE\bin\Release\ICE.dll` as a dev plugin location and disable the installed ICE.
