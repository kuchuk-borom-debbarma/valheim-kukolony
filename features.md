# Kukolony — current features

Kukolony turns a Valheim base into a small working settlement. It is still in active
development, but the features below are implemented and have been exercised by the
in-game acceptance suite.

## What you can do now

### Build a colony

Two build pieces are available from the hammer:

- **Colony hearth** — the settlement's management point. It costs 10 Wood and 10 Stone.
- **Work post** — a villager's work anchor. It costs 5 Wood and 2 Stone.

Use a colony hearth to open its management panel. From there you can name the colony,
create villagers, add nearby containers, beds, and work posts to it, and assign each
villager a home and a work post. Membership is explicit rather than radius-based, so a
storage chest can belong to the colony even when it is far from the hearth.

### Set up villagers

Villagers use the Valheim player model, have varied names and appearance, and carry their
own persistent inventory. Their identity, assignment, work progress, and inventory survive
save/load and multiplayer ownership changes.

Hovering a villager shows its name, assigned job, and live activity — for example,
“looking for Wood,” “walking to Chest,” or “storing Wood.”

### Automate hauling

`haul` is the currently available job. Configure a work post to select:

- the item to collect;
- a working radius; and
- either a specific destination chest or automatic container selection.

Villagers assigned to that post repeat the loop: find a dropped matching item, walk to it,
pick it up, find the destination, and deposit it. Multiple villagers avoid choosing the same
loose item where possible, while still being able to share a destination chest.

The work-post panel includes searchable item selection and lists eligible nearby containers,
so the choices shown in the UI match the places a villager can actually use.

### Keep distant work running

With off-screen keep-alive enabled (the default), Kukolony keeps the relevant Valheim zones
loaded around villagers and explicitly registered colony members. This allows a colony to
continue its hauling work while no player is nearby. The current acceptance test verified
wood hauled to a colony chest 140 m away while the player was 500 m from the colony.

## Custom jobs

Jobs are data files in `BepInEx/config/Kukolony/jobs/`. On first launch, Kukolony writes a
working `haul.json` example. The current job-step building blocks are:

`find_ground_item` → `move_to_target` → `pick_up_item` → `resolve_destination` →
`move_to_target` → `deposit_item`

This is an advanced configuration surface for now: only the supplied hauling workflow has
been validated end to end. Invalid job files are rejected with a useful log message and do
not stop valid jobs from loading.

## What is not available yet

Kukolony is not yet a complete colony-management mod. In particular, villagers cannot yet
fuel smelters or kilns, cook, craft, repair, farm, fight, or meet needs. Workstation
assignment is already represented in the colony UI, but has no workstation job behind it.

Dedicated-server simulation and several edge cases (world joins, portals, config changes,
and zone unloading) still need broader testing. For multiplayer, every player should have
the mod installed; villagers simulate on the peer that owns them.

## Current status

The project is in an early, test-backed development stage rather than a release-ready public
mod. The implemented haul loop, colony management UI, persistence, and off-screen zone
handling are real; broader automation and polished player-facing progression are next.

For technical detail, see [Jobs](docs/jobs.md), [Colonies](docs/colonies.md),
[off-screen simulation](docs/off-screen-simulation.md), and
[multiplayer](docs/multiplayer.md).
