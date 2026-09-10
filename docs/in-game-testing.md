# In-game benchmark reference

Kukolony owns the complete in-world benchmark. The shell script only configures, launches,
watches, relaunches, and copies artifacts. This boundary lets a player run the same test
manually and prevents process automation from racing Unity state.

## Manual use

Set `BenchmarkMode = true` in `BepInEx/config/com.kuku.kukolony.cfg`, then enter any world.
Leave `BenchmarkAutoBoot` false. After the active area loads, the controller waits ten
seconds, runs functional and UI phases, requests a synchronous world save, and exits when
`BenchmarkAutoExit` is true. Existing world objects are never purged; fixtures belong to
the uniquely named benchmark colony. Enter the same world again to run reload verification
and cleanup, then disable benchmark mode for normal play.

## Agent and CI use

Run `./scripts/in-game-test.sh`. It verifies the pinned Doorstop runtime, runs deterministic
pipeline preflight, builds the solution, backs up config, enables benchmark auto-boot into
`KukolonyBenchmark`, and launches through Steam. It waits for create and reload reports and
process exit, restores config on every exit, and copies evidence to `~/Desktop/kukolony`.
`run-colony-acceptance.sh` is only a compatibility alias.

## Lifecycle

Create follows `WaitingForWorld → Settling → Functional → UI → Reporting → Saving → Exiting`.
When the persisted benchmark colony is found, reload follows
`WaitingForWorld → Settling → ReloadVerification → Cleanup → Reporting → Saving → Exiting`.
Only `ColonyBenchmarkController` advances these states or terminates the game.

Every coroutine phase is advanced through a guarded iterator. It emits a heartbeat, has a
realtime deadline, captures exceptions, and creates a terminal failure report. A frozen
Unity main thread cannot update the heartbeat; the shell detects that separately.

## Configuration

- `BenchmarkMode` (false): enables the benchmark.
- `BenchmarkAutoBoot` (false): allows automated menu/world selection.
- `BenchmarkCharacter` (empty): configured or first available character.
- `BenchmarkWorld` (`KukolonyBenchmark`): isolated auto-boot world; ignored for manual entry.
- `BenchmarkRunId` (empty): shared artifact identity; generated when omitted.
- `BenchmarkOutputPath` (`BepInEx/kukolony-benchmarks`): canonical output.
- `BenchmarkSettleSeconds` (10), `BenchmarkPhaseTimeoutSeconds` (120), and
  `BenchmarkSaveGraceSeconds` (15): readiness, phase, and save deadlines.
- `BenchmarkScreenshots` (true): capture UI evidence during create.
- `BenchmarkAutoExit` (true): save and close after reporting.

## Artifacts and markers

The run directory contains `benchmark-create.log`, `benchmark-reload.log`,
`benchmark-report.json`, `heartbeat.txt`, `screenshots.manifest.json`, PNGs, archived game
logs, and `failure.txt` on errors. The terminal marker is
`BENCHMARK TERMINAL <create|reload> <PASS|FAIL> run=<id>`.

Fourteen screenshots are required, including the three pipeline screens: the piece editor,
one piece's settings, and the add-a-piece list.

The UI phase arms the remove confirmation but never executes it: that phase registers chest
fixtures as colony members, so an executing seam would destroy them mid-run. Removal itself
is proven in the functional phase against real villagers.

The screenshot manifest records filename, dimensions, and capture time. Passing requires
both reports, every required PNG, and visual inspection for clipping, overlap, stale
content, readability, pipeline order, validation feedback, and pagination.

Terminal PASS does not cover layout: the shell only proves each PNG exists and is
non-empty. Overflow, overlap, and misleading fixture content are found by reading the
images and by checking element bounds against the panel content column described in
[code-style.md](code-style.md). Any image defect requires a fix and a complete rerun.

## Extending coverage

1. Add assertions to a passive scenario; scenarios never start themselves.
2. Create fixtures through scenario spawn helpers and register persistent fixtures with the
   benchmark colony so reload cleanup owns them.
3. Exercise production operations, never benchmark-only mutation shortcuts.
4. Add a negative control for claims, keep-alive, limits, ownership, or compatibility.
5. Write persisted fields during create and assert them during reload.
6. For UI, add a deterministic panel state, capture, manifest entry, required shell filename,
   and review checklist item.
7. Yield between real character spawns and keep work within the configured deadline.
8. Update this guide and `automated-testing.md` with the new coverage.

## Troubleshooting

- No loader marker: verify Steam launch options and the pinned Doorstop library.
- Wrong world: verify `BenchmarkAutoBoot` and `BenchmarkWorld`.
- Steam batch/mount error: the benchmark bypasses the Steam depot only after confirming
  the benchmark world is local. Cloud worlds retain Valheim's normal Steam save path.
  Inspect the archived log if a local save still lacks a terminal report.
- Stale heartbeat: inspect the last phase/readiness marker in the archived game log.
- `villager spawn begin` is last: inspect the verified NPC prefab contract and spawned ZDO.
- Process dies without terminal report: preserve the archived crash log; the script fails.
- Terminal report without exit: wait for save grace, then terminate only that stale process.
- Missing screenshot: inspect capture errors and `screenshots.manifest.json`.
- Reload failure: compare create/reload logs and the persisted colony/ZDO fields.
