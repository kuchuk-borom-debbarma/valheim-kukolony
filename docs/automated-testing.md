# Automated testing policy

The required full verification command is `./scripts/in-game-test.sh`. It runs fast
Unity-free sequencing checks, builds the solution, then launches the real benchmark twice
through Steam. A direct build or manual clicking is not equivalent.

The deterministic project proves each job's sequencing: a full work cycle per shape, and
resuming from the middle after a reload, which is where a state machine most easily goes
wrong.
The in-game benchmark proves ZDO persistence, ownership, inventories, station RPCs, real
villagers, structures, queues, presets, paired controls, and rendered UI. Persistence only
passes after save, process exit, and fresh reload.

A change is complete only when deterministic preflight and the warning-free solution build
pass, create and reload reports contain terminal PASS markers, all expected screenshots are
manifested, and every PNG is visually inspected. A positive mechanism claim requires a
disabled or failing control. Any image defect requires a fix and complete rerun.

See [in-game-testing.md](in-game-testing.md) for configuration, lifecycle, artifacts,
extension rules, safety guarantees, exact commands, and troubleshooting.

---

## The benchmark player must be immortal, and invisible

The run stands still in the Meadows for several minutes of real time, much of it at night. The
player was being killed by whatever wandered past.

A death is not a small thing here. Respawning moves the player to the world spawn, and
`ZNet.GetReferencePosition()` follows the player — so **every zone around the settlement unloads
and every villager in it stops existing**, in the middle of whatever was being checked. The suite
was intermittent for reasons that had nothing to do with what it was testing, and hours went into
chasing hauling bugs that were really a dead viking.

What it looks like in the log is two `Starting respawn` lines in a single create phase. Nothing
else mentions it at all.

So the controller sets god mode **and** ghost mode before the run. Ghost as well as god, because
an immortal player is still a target: creatures walk to it, crowd the settlement, and stand in
the villagers' way.
