# Kukolony — handoff

Written 2026-09-09. Everything here is meant to let a fresh session (or a different
account) pick the work up without re-deriving it.

**Repo:** `~/projects/personal/valheim-kukolony`
**Branch `main` @ `5f7e739`** — last state verified by actually running the game.
**Branch `panel-layout-group` @ `eb3493d`** — compiles, *never run*. See "In flight".

---

## What Kukolony is

A Valheim colony-management mod. Villagers are player-model NPCs that live in a colony,
hold persistent inventories, take jobs from work posts, and keep working while the player
is nowhere near them.

The three ideas the design rests on:

1. **The ZDO is the villager.** Everything — name, home, job, current step, inventory — lives
   on the ZDO, not on the GameObject. So a villager that is not loaded can still be listed,
   named and assigned from the colony panel. Address things by `ZDOID` through `ZDOMan`,
   never by holding a GameObject reference across time.
2. **The colony is a ledger, not a place.** It has no radius. It stores who belongs to it —
   villagers, containers, workstations, homes — and members can be anywhere on the map.
   This was a deliberate decision: it removed the "far container unreachable" problem
   structurally rather than by clamping a radius.
3. **The villager keeps its own world loaded.** Off-screen simulation works by holding zones
   open around villagers and around colony members, so a villager can path to a chest 140m
   away with the player 500m in the other direction.

---

## BLOCKER: Valheim 1.0 broke the toolchain

The game updated partway through the 2026-09-09 session. Doorstop still injects, but
BepInEx dies before any plugin loads:

```
BepInEx.Preloader.RuntimeFixes.ConsoleSetOutFix.Apply()
  → HarmonyLib ILHook → MonoMod.RuntimeDetour.DetourHelper.GetIdentifiable
  → NullReferenceException  ("IL Compile Error (unknown location)")
```

Full trace: `<Valheim>/valheim.app/Contents/MacOS/preloader_20260909_185800_790.log`.

MonoMod cannot patch the updated Mono runtime. No `BepInEx/LogOutput.log` is produced at
all, which is the symptom to look for — it means the failure is *before* plugins, so it is
not a mod bug.

### Recovery checklist, in the order worth trying

1. **`ApplyRuntimePatches = false`** in `BepInEx/config/BepInEx.cfg`. The crash is inside a
   preloader *runtime fix*, not inside anything essential. One line, and it may be the whole
   fix. Try this first.
2. **Update BepInEx** to the newest 5.4.x, or move to BepInEx 6 bleeding-edge if 1.0 changed
   the Unity/Mono version. Newer MonoMod is the actual fix if (1) only defers the problem.
3. **Re-apply the macOS entitlements and re-sign `valheim.app`.** A game update replaces the
   bundle and drops our signature. Needed: `allow-dyld-environment-variables` and
   `disable-library-validation`. Verify with `codesign --verify`. See `docs/SETUP-macos.md`.
4. **Re-check `doorstop_libs/libdoorstop_x64.dylib`** is still the *universal* 4.5.0 build
   (~104KB). The stock file is x86_64-only and will not inject into the arm64-native game;
   the original is kept beside it as `.orig-4.4.0-x86_64`. As of this writing it survived
   the update.
5. **Re-check `Valheim_Data/Managed/`** — it is a shim of *per-file symlinks* into the
   `.app` bundle, so publicized output lands outside the signed bundle. Confirm the links
   still resolve to the new bundle.
6. **Re-publicize assemblies** and rebuild. `assembly_valheim.dll` changed; if 1.0 renamed or
   reshaped anything we patch, the build will say so. As of 2026-09-09 the project still
   **compiles clean** against the updated assemblies, which is a good sign.
7. **Check Jötunn** (2.27.1) has a 1.0-compatible release.

Then re-run the acceptance suite before trusting anything, including the parts this document
calls verified — they were verified against the pre-1.0 game.

---

## Where the work stands

Verified working, on `main`, by a self-driving in-game test suite:

- Villagers as custom creatures from the player model, with persistent appearance and
  inventory, named and homed on first sight.
- A composable job system — steps wired by JSON into a state machine. One job exists: `haul`.
- Work posts that configure a job: what item, which destination container, what radius.
- Off-screen simulation: **2/2 wood hauled into a chest 140m from the colony with the player
  500m away**, 21 zones held, no leaks, no exceptions.
- Colonies: a hearth that records villagers, containers, workstations and homes, with a
  panel to name a colony, spawn villagers, add nearby buildings, and assign each villager a
  home and a workstation — including villagers that are not loaded.
- Hover text showing a villager's current job and current step.

### In flight — `panel-layout-group`

The colony panel's layout was rewritten from hand-measured absolute offsets to a
`VerticalLayoutGroup` + `ContentSizeFitter` stack. **It compiles and has never been run.**
Every prior layout change was verified by screenshot, and this one could not be, because
BepInEx stopped loading. Either finish verifying it or `git branch -D` it — do not merge it
on the strength of a green compile.

---

## How to test

The harness drives the game itself; there is no manual clicking. Config lives in
`BepInEx/config/com.kuku.kukolony.cfg`:

| Flag | What it does |
|---|---|
| `AutoTestEnabled` | Villager suite |
| `HaulTestEnabled` | Full acceptance suite — the one that matters |
| `DebugScreenshotEnabled` | Builds a colony, opens the panel, photographs it, quits |
| `DebugScreenshotPath` | Where the PNGs go |

Launch with `open "steam://rungameid/892970"` and wait for `RESULT: PASS` in
`BepInEx/LogOutput.log`. `AutoBoot` drives past the main menu; `TestWorld.Purge` resets one
reusable world (~75s per run instead of ~388s generating a new one).

**Do not launch the BepInEx start script directly** — it parses `%command%` differently from
Steam and BepInEx will not start, which looks exactly like the 1.0 breakage and will waste
your time. Launch through Steam.

Two hard-won lessons, both written up in `docs/automated-testing.md`:

- **A test that has never failed proves nothing.** Claims, keep-alive and target contention
  were each only trusted after a paired *control* run with the feature disabled.
- **A green run can be the litter.** `TestWorld.Purge` only destroyed *loaded* objects, so
  leftovers in unloaded zones survived and were then resurrected by our own keep-alive —
  eight stale villagers holding 39 of 48 zones. The far-chest haul was partly passing on
  debris. Purge now sweeps ZDOs world-wide by prefab and reaches past the 140m chest.

The panel is verified by screenshot from inside the game (`ScreenCapture.CaptureScreenshotAsTexture`),
because macOS blocks `screencapture` without Screen Recording permission. Looking at the
result found five defects no assertion caught — overlapping buttons, ragged columns, an empty
band, a missing page indicator, and two villagers both named "Leif".

---

## Invariants worth not rediscovering

- **Claim ownership before writing to a ZDO** (`SetOwner(ZDOMan.GetSessionID())`). A write by
  a non-owner lands locally and is clobbered on the next sync. `ZDO.Set(..., okForNotOwner)`
  ignores that parameter.
- **AI only ticks on the owner.** `BaseAI.UpdateAI` returns early otherwise.
- **The `Player` prefab is stored inactive and non-persistent.** Clones never run `Awake`
  (so no ZDO) until `SetActive(true)`, and need `m_persistent = true` or the ZDO is
  discarded. Also clear `m_defaultItems`/`m_randomSets` or villagers re-equip starting rags
  on every reload.
- **`VisEquipment` stores hashes, not strings.** Reading appearance with `GetString` returns
  nothing — this produced a false "appearance is re-rolling" alarm once.
- **Never hold a component reference across zone cycling.** Track by `ZDOID` and re-resolve.
  This rule is in `docs/code-style.md` and was still broken twice, once by a test that then
  reported a working feature as broken.
- **`ZNetScene.CreateDestroyObjects` is load-bearing.** Patches there are scoped with a flag
  set by a prefix and cleared by a *Finalizer*, so an exception cannot strand it. Two code
  reviews found bugs where widening this froze colonies, decayed buildings forever, or
  stalled world join.

---

## Open items

- **Dedicated server is unverified.** BepInEx injection kills mono on the macOS server
  (`libmono-native.dylib` DllNotFoundException), and the pack's server script is Linux-only.
  Test on Linux if it ever matters.
- **The suite only walks the happy path.** World join, portals, config toggles and world
  unload are untested, and that is exactly where the keep-alive patches have bitten before.
- **Only one job exists.** The job system is built to be composable but has never composed
  anything but `haul`, so we do not truly know it is a state machine rather than a haul
  pipeline in a job-shaped coat. A `craft` job — "if the job is to craft, we should be able
  to specify what to craft" — is the next real feature and would exercise the workstation
  assignment that currently has UI and storage but no behaviour behind it.

## Docs

`docs/`: `README`, `off-screen-simulation`, `multiplayer`, `jobs`, `colonies`, `api-notes`,
`npc-design`, `modding-basics`, `code-style`, `automated-testing`, `predecessor-postmortem`,
`SETUP-macos`, `spike-results`, and this file.
