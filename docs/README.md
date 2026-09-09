# Kukolony

The current colony model uses a live-radius structure registry and colony-owned concrete
job queues. See [Colonies](colonies.md), [Structure registry](structure-registry.md), and
[Job catalog](job-catalog.md). Work posts and JSON job graphs are retired.

A colony management system for Valheim.

Your base is already a village — it has smelters, kilns, cooking stations, workbenches,
and chests full of ore that somebody has to walk back and forth between. Right now that
somebody is you. Kukolony adds villagers who live at your base and do that work, so the
base keeps running as a place rather than a chore list.

The mod is built with [Jötunn](https://github.com/Valheim-Modding/Jotunn) on BepInEx.

## Scope

This is a long project, so it is deliberately staged. The first milestone is small and
boring on purpose: **one NPC, standing at a work post, doing one job correctly.**
Everything else is layered on top of that once it is solid.

### Milestone 1 — a villager that works

- Spawn an NPC villager (cloned from a vanilla creature to inherit its model,
  animations, ragdoll and pathfinding).
- Give it a **work post**: an anchor point in the world that defines where it lives and
  the radius it operates in.
- Give it **one job** end to end — most likely hauling: find an item, walk to it, pick it
  up, put it in an appropriate container.
- Make that job survive save/reload and rejoin.

If that works reliably, the rest is repetition.

### Milestone 2 — more jobs

Fuelling and loading smelters and kilns, cooking, crafting, repairing pieces. These share
almost all their structure with hauling, which is the point — see *Composable jobs* below.

### Milestone 3 — the colony layer

Multiple villagers per post, job priorities, a real assignment UI, villager identity
(names, needs, downtime), and whatever makes it feel like a settlement rather than a pool
of workers.

## Design principles

These come from a previous attempt at this mod
([KukuNPCWorker](https://github.com/kuchuk-borom-debbarma/ValheimMod_KukuNPCWorker),
2024). Some of its ideas were good and are kept; the rest are here as things not to
repeat.

**State lives in the ZDO.** A villager's job, work post and current target are stored on
its ZDO, not in a C# field. Valheim then handles persistence and network replication for
free — the villager remembers what it was doing across a save, a reload, or a host
migration. This worked well before and is the foundation.

**Composable jobs, not one class per job.** The previous version had a separate AI class
per job, and each one re-implemented "find a thing, walk to the thing, do something to the
thing". A job should instead be assembled from parts — roughly *verb + item + source +
destination*:

> take `Wood` from `any container` and put it in `the nearest kiln`

That way a new job is configuration, not a new file.

**No `async void`.** The previous version drove its AI from `Update()` through
`async void` + `Task.Delay`, which swallowed exceptions and could take the game down with
it. Jobs here tick — coroutines, or a plain state machine with a cooldown timestamp.

**Respect ZDO ownership.** Villagers modify shared world objects (containers, smelters).
Every write must go through the owning client, or multiplayer silently breaks. Ownership
is checked, not assumed.

**Use game methods over raw ZDO keys.** Writing internal ZDO fields directly (the smelter
ore queue, fuel counters) works until the next game patch renames one. Prefer the public
path even when it is more work.

**Scanning is not free.** A colony means many villagers scanning at once. World queries
are budgeted: small radii, layer masks, cached results, staggered ticks.

## The open problem: off-screen simulation

Valheim only simulates objects near a player. Walk away from your base and the colony
freezes. This is the central design question of a colony mod and it is not yet decided.
The options:

1. **Simulate from ZDO data only**, with no instantiated GameObjects, so work continues
   without the zone being loaded.
2. **Keep the zone alive** ourselves, at a performance and correctness cost.
3. **Accept it** and design around it — the colony works while you are home, and you come
   back to a base that has been paused rather than productive.

This gets decided before Milestone 2, because it changes how jobs are written.

## Environment

Development is on macOS (arm64). The Valheim install and its managed assemblies are at:

```
~/Library/Application Support/Steam/steamapps/common/Valheim/valheim.app/Contents/Resources/Data/Managed
```

Note this differs from the path in the Valheim modding wiki, which points at
`Contents/MacOS` and is out of date. Most community guides assume Windows — Visual Studio,
the CabbageCrow publicizer `.exe`, and the PowerShell publish scripts all need
substitutes here.

## Related docs

- [Off-screen simulation](off-screen-simulation.md) — the central technical problem
- [Multiplayer](multiplayer.md) — ZDO ownership and what it forces on job code
- [API notes](api-notes.md) — the calls we actually use
- [Modding basics](modding-basics.md) — conventions and traps
- [Post-mortem](predecessor-postmortem.md) — lessons from the 2024 attempt
- [macOS setup](SETUP-macos.md) — toolchain and game paths

## References

- [Jötunn documentation](https://valheim-modding.github.io/Jotunn/)
- [Valheim modding wiki](https://github.com/Valheim-Modding/Wiki/wiki)
- [Best practices](https://github.com/Valheim-Modding/Wiki/wiki/Best-Practices)
- [KukuNPCWorker](https://github.com/kuchuk-borom-debbarma/ValheimMod_KukuNPCWorker) — the 2024 predecessor, reference only
