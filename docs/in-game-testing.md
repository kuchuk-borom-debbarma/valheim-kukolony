# In-game benchmark

Run `./scripts/in-game-test.sh`. It enables `BenchmarkMode` in the Kukolony BepInEx
config, starts Valheim through Steam, and drives three deterministic stages in the
`KukolonyJobs` world: create/save, reload verification, and UI screenshots.

The mod waits ten seconds after the active area loads, writes each stage log and PNG into
`BepInEx/kukolony-benchmarks/<timestamp>`, saves, and calls `Application.Quit()`.
The script watches completion markers, restores the original config, and copies artifacts
to `~/Desktop/kukolony`. A missing `RESULT: PASS`, screenshot marker, or PNG fails the run.

Use the deterministic console suite for fast pipeline validation; use this benchmark for
real ZDO ownership, Valheim RPC, persistence, UI, and screenshot evidence.
