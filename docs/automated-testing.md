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
