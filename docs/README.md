# Kukolony engineering guide

Kukolony is organized around a persistent Colony Hearth rather than work posts. The hearth
ZDO owns the colony name, villager membership, registered structures, editable typed jobs,
and presets. Each villager ZDO owns its ordered queue and runtime position.

Read these first:

- [Colonies](colonies.md) — root ownership, live radius, and membership.
- [Structure registry](structure-registry.md) — eligibility, capabilities, target modes,
  and picker behavior.
- [Jobs and queues](jobs.md) — execution and persistence rules.
- [Job catalog](job-catalog.md) — configuration card for every concrete job.
- [Automated testing](automated-testing.md) — required build, Steam launch, persistence,
  and screenshot path.
- [Multiplayer](multiplayer.md) and [off-screen simulation](off-screen-simulation.md) —
  ownership and zone lifetime constraints.
- [API notes](api-notes.md) — verified game contracts.
- [Code style](code-style.md) — dependency and implementation rules.

Core rules are stable: persistent state belongs in ZDOs; the current owner performs writes;
station changes use verified vanilla RPCs; container writes use ownership plus Inventory
and Container APIs; AI is a synchronous fixed-tick state machine with no async tasks or AI
coroutines; world discovery uses registries; dependencies point inward; and shared
abstractions are extracted only after concrete jobs demonstrate the shape.

The retired pre-release work-post, bed-assignment, radius-less member ledger, and
player-authored JSON job model are intentionally not migrated.
