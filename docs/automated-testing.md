# Automated testing

The required verification command is:

```sh
./scripts/run-colony-acceptance.sh
```

Do not substitute a project build, direct app launch, or manual clicking. The script builds
`Kukolony.sln`, confirms the installed macOS Doorstop library matches the pinned BepInEx
5.4.2350 pack, removes quarantine from the in-scope loader files, launches Valheim through
Steam, polls fresh BepInEx logs, and fails on missing reports or images.

## Deterministic pipeline checks

Before Steam is launched, the runner executes
`Kukolony.DeterministicTests`. This Unity-free executable links the production pipeline
shape validator and proves valid starter shapes plus rejected missing Start, source,
loose-target, and destination connections. It is intentionally separate from the
Valheim harness: pipeline authoring rules must be repeatable even when world loading,
Steam, or rendering is unavailable.

## Three unattended launches

1. **Acceptance run 1:** purge the dedicated test world, create the V2 colony fixture,
   exercise behavior, persist it, save, and quit.
2. **Acceptance run 2:** load the same world, verify colony/member/structure/job/preset and
   per-villager runtime fields came back from disk, purge, save, and quit.
3. **Screenshots:** construct a populated fixture, drive every relevant panel state, capture
   PNGs after completed frames, and quit.

The script backs up and restores the user's BepInEx configuration even on interruption. It
rotates logs into a timestamped `artifacts/colony-*` directory and copies reports and PNGs
to `~/Desktop/kukolony`.

## Acceptance coverage

The in-game report checks:

- placed/network/radius registration and NPC rejection;
- naming, case-insensitive search, capability filtering, sorting, and live-radius
  invalidation without record loss;
- seven concrete job configurations and bounded V2 ZPackage decoding;
- completed/failed count consumption, skip-without-consumption, exhaustion, looping, and
  missing queue entries;
- portable presets stripping exact IDs and local presets retaining them;
- actual haul and transfer inventory movement through owned containers;
- actual verified RPC submission for fireplaces, smelters, charcoal kilns, cooking
  stations, fermenters, and ready beehives;
- full destination and invalid/deleted target handling;
- concurrent target claims with a disabled failing control;
- keep-alive zones with an enabled path and disabled empty control;
- two-run persistence of queue position, attempt, progress, phase, and active target.

A positive mechanism claim needs its negative control. A pass with claims or keep-alive
always enabled does not prove those mechanisms affected the result.

## Screenshot evidence

The game uses `ScreenCapture.CaptureScreenshotAsTexture` after
`WaitForEndOfFrame`, so macOS Screen Recording permission and manual interaction are not
required. Expected images are:

- `colony-panel.png`
- `colony-structures.png`
- `colony-members.png` and `colony-members-page-2.png`
- `colony-member-detail.png`
- `colony-jobs.png` and `colony-job-config.png`
- `colony-structure-picker.png`
- `colony-preset-application.png`
- `colony-picker.png`

The images must be inspected for clipping, overlap, stale content, readability, and visible
pagination—not merely checked for nonzero file size. Fixture villagers are spawned across
frames because constructing several player rigs in one Unity frame can stall macOS.

## Harness components

- `AutoBoot.cs` drives `FejdStartup` into the dedicated local `KukolonyJobs` world.
- `TestWorld.cs` destroys the harness's loaded and known persistent fixtures.
- `ColonySelfTest.cs` owns two-run assertions and real component/RPC smoke tests.
- `PanelScreenshot.cs` drives the UI and writes PNG artifacts.
- `PrefabProbe.cs` verifies component/RPC contracts before station executor work.
- `TestReport.cs` produces a delimited pass/fail block.

All are gated under `9 - Development` and disabled by default.

## Launch and timing rules

Steam's launch option must point at `start_game_bepinex.sh`; launch with
`open 'steam://rungameid/892970'`. Bypassing Steam can start an unmodded game. Wait for
`ZoneSystem.instance.IsActiveAreaLoaded()` plus the harness settle period because a local
player exists before world streaming is complete.

Reuse the fixed-seed local test world. New terrain generation is slow and disposable worlds
pollute saves. Persistence is proven only by a real save/quit and second process—not by
serializing and reading in one session.

## Reading failures

Each run writes a delimited `KUKOLONY SELF TEST` block. A thrown exception, missing loader
marker, timeout, `[FAIL]`, missing `RESULT: PASS`, or missing screenshot fails the
script. Diagnose the earliest error in that run's archived log; later activity often comes
from AI continuing after the harness exception.

The probe and harness use private/publicized game members and are version-sensitive. After a
Valheim update, rerun the probe before changing an executor contract.
