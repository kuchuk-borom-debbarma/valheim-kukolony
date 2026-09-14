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
sequencing preflight, builds the solution, backs up config, enables benchmark auto-boot into
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

Five screenshots are required: the colony screen, the widget gallery, a turned page, the item
picker, and **a villager with no panel in the way**. The last one matters more than it looks -
every capture before it existed framed the interface, so villagers glowing like ghosts survived
run after run because nothing had ever photographed one.

Overlap, spilling and clipped strings are no longer a reviewer's job: `ScreenAudit` asserts
them in-game, and a deliberately broken fixture proves the audit can fail. What review is for
is everything the audit cannot judge - whether the screen reads as a settlement's control
panel, and whether fixture content is misleading.

That last one exists because every other capture frames the interface, which is how villagers
went on glowing like the ghost they are cloned from, run after run: nothing ever looked at
one. It stands the villager beside the player, since the two share a body rig and having both
in shot is what makes "does this look like a person" answerable at a glance.

The UI phase arms the remove confirmation but never executes it: that phase registers chest
fixtures as colony members, so an executing seam would destroy them mid-run. Removal itself
is proven in the functional phase against real villagers.

The screenshot manifest records filename, dimensions, and capture time. Passing requires
both reports, every required PNG, and visual inspection for clipping, overlap, stale
content, readability, and pagination.

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

### The reload phase asks whether work survived, not just whether fields did

It used to prove that names, homes and bags come back from a save, and said nothing about
whether a villager's *work* does — which is the claim the whole design rests on, because
*facts outrank the recorded state* is what makes reloads repair themselves. So a villager is now
left carrying an undelivered load moments before the save, and the reload phase asserts it
finishes the delivery. The phase is a coroutine for that reason; it was a plain method that
returned before the villager could take a step.

Two things that fixture got wrong first, both of which read as passes:

- **The villager finished before the save.** Setting up a half-done trip does not stop the game,
  and the villager simply walked over and delivered it, so the reload phase found an empty bag
  and a cleared trip and reported a villager that had resumed nothing. It is now held still by
  being made tired — a tired villager yields without touching its trip — and the reload phase
  wakes it deliberately before watching.
- **The delivery was counted as a total, not a difference.** What the chest already held is not
  what this villager delivered, and reading the total passed while the load had in fact been
  delivered before the save.

### The world is emptied before every run

The benchmark world was reused. Eight runs of colonies, chests and villagers accumulated in it,
and later runs began failing at checks the earlier ones had passed — pieces that would not place
because something from a previous run already stood there, a second colony claiming the first
one's structures, records that resolved on one run and were gone on the next.

The symptom is the worst kind to debug: a dozen failures scattered across unrelated checks, none
of them caused by the change being tested. A run that went 194 passed / 0 failed became 126 / 16
with no change to any of the code those checks cover.

So the gate deletes the world's object database before the create stage. The `.fwl2` file, which
carries the name and the seed, is deliberately kept — the terrain must be identical from one run
to the next or a fixture that reached its chest yesterday may not today. A world with a seed and
no database is exactly a freshly created one. The reload stage of course does not wipe anything;
reading back what create wrote is the whole point of it.

### A control that found nothing to do is not a control

A negative control measures the *absence* of something, so anything that stops the scenario
running at all produces the same reading as a pass. The claims control ran two villagers with
reservations switched off and reported zero collisions — not because they did not collide, but
because its chest had stopped being a valid destination and neither villager ever found work.
The number was right and meant nothing.

So **a control asserts its own fixture**. The claims rounds now check that the chest is still
registered and still the answer to *where does wood go* at the start of each round, and report
that as its own named check. Diagnostics alone were not enough: the first version logged one
line per structure into the same variable and showed only the last one, which pointed at an
unrelated kiln for two runs.

**Place fixtures where a fixture has already been proven to work.** The contested chest was put
on untested ground behind the hearth, and was gone by the second round. Every check that passes
places this side of it. That is now the fourth run placement has cost.

## Troubleshooting

- No loader marker: verify Steam launch options and the pinned Doorstop library.
- Wrong world: verify `BenchmarkAutoBoot` and `BenchmarkWorld`.
- Steam batch/mount error: the benchmark bypasses the Steam depot only after confirming
  the benchmark world is local. Cloud worlds retain Valheim's normal Steam save path.
  Inspect the archived log if a local save still lacks a terminal report.
- Stale heartbeat: inspect the last phase/readiness marker in the archived game log.
- `villager spawn begin` is last: inspect the verified NPC prefab contract and spawned ZDO.
- Process dies without terminal report: preserve the archived crash log; the script fails.
  A single missed process match is not a death - Steam exits and re-execs the game while it
  starts - so the runner requires several consecutive misses before giving up.
- Valheim hangs during world load: it writes no heartbeat yet, so the runner watches the
  game log's own timestamp and treats sustained silence as a hang. The log is archived as
  `<stage>.hung.log`. A hang before the first heartbeat is retried once, because nothing
  under test had run; a hang afterwards fails outright.
- Terminal report without exit: wait for save grace, then terminate only that stale process.
- Missing screenshot: inspect capture errors and `screenshots.manifest.json`.
- Reload failure: compare create/reload logs and the persisted colony/ZDO fields.

## Mining

Seven checks, run from the `mine` slice and from the acceptance run through one shared list, so
the two cannot drift apart. The order is the one a failure is most useful in: the classifier
first, because everything below it passes by finding nothing to contradict if the index is empty.

**The one that matters is `CheckMiningBreaksADeposit`**, and it matters because for a while it did
not exist. Everything else strikes the rock through the protocol or asks a predicate directly,
which meant `MineJob.Tick` had never been executed by anything in the suite — it could have failed
on its first line and every check would still have passed.

It stages three rocks and two of them are controls:

| Rock | Why |
|---|---|
| The subject, 8 m off | What the villager should take. Disowned first, because that is the state every world-generated rock is in and damage routed to nobody is absorbed in silence |
| Something harder, **nearer** | Catches a tier gate that stopped working: without one, nearest-wins takes it |
| The same soft rock, outside the work area | Proves deposits do not come apart on their own *and* that the area bounds the job |

And it runs past the life of a claim on purpose — *"its claim was refreshed while it worked"* is
the assertion that needs it, and thirty seconds is how long one lives.

**What is deliberately not checked in game.** The blunt-pickaxe tolerance: since the tier gate
moved ahead of the walk, what reaches that path is damage modifiers reducing a blow to nothing,
and no prefab announces that in advance — so there is no honest way to find a fixture for it, and
staging one would mean faking the hit and testing the fake. It lives in the deterministic suite.
Loose rock is asserted as a *setting*, never as behaviour: what the classifier calls a boulder may
be a crate, so a behavioural check would either name a prefab or photograph a villager smashing a
barrel and call it mining.
