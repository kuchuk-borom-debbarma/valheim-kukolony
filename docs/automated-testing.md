# Automated testing policy

The required full verification command is `./scripts/in-game-test.sh`. It runs fast
Unity-free pipeline checks, builds the solution, then launches the real benchmark twice
through Steam. A direct build or manual clicking is not equivalent.

The deterministic project proves pure pipeline ordering and invalid customisation rejection.
The in-game benchmark proves ZDO persistence, ownership, inventories, station RPCs, real
villagers, structures, queues, presets, paired controls, and rendered UI. Persistence only
passes after save, process exit, and fresh reload.

A change is complete only when deterministic preflight and the warning-free solution build
pass, create and reload reports contain terminal PASS markers, all expected screenshots are
manifested, and every PNG is visually inspected. A positive mechanism claim requires a
disabled or failing control. Any image defect requires a fix and complete rerun.

See [in-game-testing.md](in-game-testing.md) for configuration, lifecycle, artifacts,
extension rules, safety guarantees, exact commands, and troubleshooting.
