# Automated testing — removing the human from the loop

Verifying a Valheim mod normally means: build, launch, click through menus, load a world,
do something, watch, describe what you saw. That does not scale, it is not repeatable, and
"it looked fine" is not evidence.

This project instead makes **the mod test itself**. A run is:

```sh
dotnet build Kukolony.sln -c Debug
open "steam://rungameid/892970"
# ...wait, then read BepInEx/LogOutput.log
```

No clicking, no playing, no describing. The result is a pass/fail block in the log.

This is the standing method for this project. Extend it for each new milestone rather than
going back to manual verification.

## Why it works

Three pieces, all under `Kukolony/Debug/`, all config-gated off by default.

| File | Job |
|---|---|
| `AutoBoot.cs` | Drives the main menu into a world without anyone clicking |
| `VillagerSelfTest.cs` | Runs the scenario and asserts outcomes |
| `TestReport.cs` | Accumulates checks, prints one delimited block |

### Driving the main menu

`FejdStartup` holds the menu state, and its fields are private — reachable because we
build against publicized assemblies (see `modding-basics.md`).

```csharp
FejdStartup startup = FejdStartup.instance;          // public static accessor
List<PlayerProfile> profiles = SaveSystem.GetAllPlayerProfiles();
List<World> worlds = SaveSystem.GetWorldList();

startup.SetSelectedProfile(profile.GetFilename());   // private
startup.m_world = world;                             // private
startup.OnWorldStart();                              // public
```

Wait a few seconds after `FejdStartup.instance` appears before touching it — the profile
and world lists populate asynchronously.

### One world, purged between runs

**Reuse a single test world; do not generate a fresh one per run.** Terrain generation
costs around five minutes, against roughly seventy seconds to load an existing world —
measured at 388s versus 75s for the same scenario. Generating per run also litters the save
folder with throwaway worlds.

Reuse only works if each run starts from a known state, so `Debug/TestWorld.Purge` destroys
every villager, work post, container and loose item near the player before the scenario
builds. Without it, leftovers accumulate and each run quietly tests something different.

The purge is deliberately blunt — it does not try to identify "our" objects. That would be
reckless in a real world, which is why it only ever runs from the harness, in a world the
harness created.

```
[TestWorld] purged 3 villager(s), 1 post(s), 1 prop(s)
```

Destroy through `ZNetScene.Destroy` so the ZDO goes with the object. Only the owner may
destroy a ZDO, and the harness owns everything it spawned.

**Persistence tests are the exception**: a run that must prove state survives a reload
cannot purge. `VillagerSelfTest` deliberately does not.

### Using a world we own

The harness creates its own world rather than borrowing one:

```csharp
World created = new World("KukolonyTest", "kukolony")
{
    m_fileSource = FileHelpers.FileSource.Local,   // don't consume Steam Cloud quota
    m_needsDB = false
};
created.SaveWorldMetaData(DateTime.Now);
```

Two reasons this matters, both learned the hard way:

- **Never write into a world the player cares about.** The first version defaulted to the
  first world in the list, which was a real save.
- **A fixed seed makes runs repeatable.** Same terrain every time; a test that runs on
  different ground each time is not a test.

### Save-and-quit is what makes reload testing possible

```csharp
Game.instance.Logout(save: true, changeToStartScene: false);
Application.Quit();
```

The run terminates itself, which is what lets a script launch it and wait. More
importantly, **saving is what turns a second run into a genuine persistence test** rather
than a repeat of the first. The harness detects which situation it is in by whether a
villager already exists:

- **Run 1** — none found → spawn one, exercise behaviour, save, quit.
- **Run 2** — one found → its name and home came off disk, so assert them.

That two-run structure is the only way to prove ZDO state actually persists. It is worth
preserving in any future scenario.

### Reporting

Results go through `TestReport`, which prints a single delimited block:

```
==================== KUKOLONY SELF TEST ====================
  Run 1 - villager spawned fresh
------------------------------------------------------------
  [PASS] villager prefab is registered
  [PASS] villager has a name - Ingrid
  [PASS] villager is tamed (friendly to the player)
  ....  displaced villager to 40m from home
  [PASS] villager walked home - 40m -> 8m in 24s
------------------------------------------------------------
  RESULT: PASS
============================================================
```

`LogOutput.log` runs to thousands of lines of vanilla output. A delimited block is the
difference between a readable result and archaeology. Failures print at `Error` level so
they surface in a plain grep for errors.

## Running it

### Launch through Steam — never open the app or launcher script directly

The test only runs when BepInEx has injected before Valheim starts. On macOS, Steam is
part of that launch chain: its Valheim launch option must be:

```text
"/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim/start_game_bepinex.sh" %command% -console
```

Start the game with `open "steam://rungameid/892970"`. **Do not** use `open valheim.app`,
or execute `start_game_bepinex.sh` yourself. The script relies on Steam's `%command%` /
`SteamLaunch` handoff; bypassing it starts an unmodded game that reaches the menu normally
but never loads Kukolony or AutoBoot.

Before waiting for a test result, confirm `BepInEx/LogOutput.log` is newly written and
contains both `Loading [Kukolony 0.0.1]` and `Kukolony 0.0.1 loaded`. If either line is
absent, stop there: it is a loader/launch problem, not a failed mod test. See
[`SETUP-macos.md`](SETUP-macos.md#bepinex-on-apple-silicon) for the Doorstop replacement
that a BepInEx pack update can overwrite.

```sh
# build and deploy
dotnet build Kukolony.sln -c Debug

# clear the log so the next run reads clean
V="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
mv -f "$V/BepInEx/LogOutput.log" "$V/BepInEx/LogOutput.prev.log"

# launch, then poll until the report appears or the game exits
open "steam://rungameid/892970"
for i in $(seq 1 90); do
  grep -q "KUKOLONY SELF TEST" "$V/BepInEx/LogOutput.log" 2>/dev/null && break
  [ $i -gt 4 ] && ! pgrep -f "valheim.app/Contents/MacOS/Valheim" >/dev/null && break
  sleep 5
done

awk '/KUKOLONY SELF TEST/,/^  *=+$/' "$V/BepInEx/LogOutput.log"
```

Run it **twice** when persistence is in scope. First run creates and exercises; second run
reloads and asserts.

Timings observed: fresh world generation ≈ 60–90 s, reload ≈ 30–60 s.

Configuration lives in `BepInEx/config/com.kuku.kukolony.cfg` under `9 - Development`:

| Setting | Meaning |
|---|---|
| `AutoTestEnabled` | Master switch. Off by default. |
| `AutoTestWorld` | Defaults to `KukolonyTest`, created if missing. |
| `AutoTestCharacter` | Empty means first available. |
| `AutoTestQuitWhenDone` | Save and exit after reporting. Set false to inspect by hand. |

## Traps

Each of these produced a wrong result before being fixed.

**Do not decide anything until the world has finished streaming in.** `Player.m_localPlayer`
becomes non-null well before nearby zones finish loading. The first version found a
villager, called it a reload run, and then failed when that villager's zone unloaded
moments later. Wait for `ZoneSystem.instance.IsActiveAreaLoaded()` **and** a settle delay
before reading world state.

**Never hold a Unity object reference across time.** A villager whose zone unloads is
destroyed, and a destroyed object compares equal to null. Re-resolve before failing a
check, or the harness reports a bug that is not there.

**Do not put varying values in a state label.** Logging on state *change* is what keeps a
colony from flooding the log at 20 Hz. Embedding a live distance in the label makes every
tick a new state and defeats the throttle entirely. Keep the label stable; pass detail
separately.

**Enable the level you are logging at.** The Valheim BepInEx pack hides `LogDebug` by
default, so anything you need to see in a run must be `LogInfo` or higher.

## Maintenance

The harness pokes private `FejdStartup` state, so it is **version-sensitive and expected to
break on game updates**. That is an acceptable cost: the build/launch/verify loop gets run
dozens of times per milestone, and it is the only way to make results repeatable.

If it breaks after an update, re-decompile and check `FejdStartup.m_world`,
`SetSelectedProfile` and `OnWorldStart` first — those are the coupling points.

## What this cannot cover

Automation proves correctness, not feel. Whether a villager *looks* right, whether hover
text reads well, whether the pacing is satisfying — those still need a person. Use the
harness to make the correctness questions cheap, so human attention goes to the questions
only a human can answer.

## Seeing the UI: screenshots from inside the game

The harness can prove every rule behind a button, but not whether a panel *reads* well -
whether columns line up, whether buttons overlap, whether a band of empty space looks
like something failed to draw. Those are the defects that only show up when looked at.

`DebugScreenshotEnabled` builds a populated colony (hearth, post, chest, three beds, five
villagers), assigns everyone, opens the panel and photographs it, then quits:

```
DebugScreenshotEnabled = true
DebugScreenshotPath = /tmp/kukolony-shots
```

It writes two frames - the panel at rest and the panel mid-assignment, which is the busiest
it ever gets. The game captures itself rather than going through the OS: `screencapture`
needs Screen Recording permission that a shell session does not have, and
`ScreenCapture.CaptureScreenshotAsTexture` needs no permission at all and captures exactly
what a player sees. The capture has to happen after `WaitForEndOfFrame`, and the PNG is
written with `File.WriteAllBytes` rather than `ScreenCapture.CaptureScreenshot`, whose path
is relative to whatever the working directory happens to be.

Four layout defects and one naming defect were found this way, none of which any assertion
would have caught: overlapping add-buttons, ragged row columns (one label per row means a
short name drags the following text left), a tall empty band below the rows whenever the
picker was closed, a pager with no page indicator, and two villagers in one colony both
named "Leif" - which matters because the panel identifies a villager by name and nothing
else.

## The reusable world accumulates, and a green run can be the reason

Reusing one world is much faster than generating one per run, but `TestWorld.Purge` was
only destroying what was *loaded*. Anything a previous run left in a zone that was not
loaded at that moment survived - and then the keep-alive loaded its zone and resurrected it
mid-test. The prop sweep also only reached 80m, while the haul test places its destination
chest at 140m, so chests piled up at the destination run after run.

This is worth stating plainly because the failure was not "the test broke": it was **the
test passing for the wrong reason**. Eight stale villagers held 39 of the 48 available
zones open, and the far-chest delivery may have been riding on zones that had nothing to do
with the colony under test. With the world genuinely clean the same test holds 21 zones and
still passes (2/2 in 62s, corridor 10/10) - but that is now a fact about the mod rather
than a fact about the litter.

`Purge` therefore sweeps ZDOs world-wide by prefab (villagers, posts, hearths), loaded or
not, and props out to 220m. `ReportCorridor` asserts that the chest's own zone and every
zone between it and the colony are held, so a stalled haul distinguishes a held-zone gap
from anything else instead of being diagnosed by guesswork.
