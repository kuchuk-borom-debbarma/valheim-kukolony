# Post-mortem: KukuNPCWorker (2024)

The previous attempt at this mod:
[kuchuk-borom-debbarma/ValheimMod_KukuNPCWorker](https://github.com/kuchuk-borom-debbarma/ValheimMod_KukuNPCWorker),
last commit 2024-04-13. Reference only — it is not a codebase to resume. Jötunn 2.17,
Unity 2020.3.33f, Windows, `net48` with `packages.config`.

This records what it got right, what broke, and what the current design does instead.

## What it did

**Spawn.** A Jötunn `CustomCreature` cloning `Dverger`, with a `KukuWorkerAICore`
component, a forced `Tameable`, and a 10x10 `Container`. Dverger aggression was suppressed
by zeroing `m_alertRange` / `m_hearRange` / `m_viewRange` and clearing `m_aggravatable`.

**State on the ZDO.** Three keys — `WorkPost` (Vec3), `Work` (job name), `WorkTarget`
(e.g. `"Bronze"`).

**Job assignment through `Interactable`.** `Interact()` re-anchored the work post to the
villager's current position; `UseItem()` switched on the held item's `m_shared.m_name`
token (`$item_hammer` → Craft, `$item_wood` → FuelSmelter, and so on).

**AI as composable state machines.** `WorkerAIBase` exposed
`async Task<WorkerStateBase> Loop(ZDOID)`; each job had its own enum state class.
Composition by ownership — `RepairAI` held a `MoveToTargetAI`; `PutItemInSmelterAI` held
three sub-AIs and fell through ground drops → containers.

**Movement.** `MonsterAI.SetFollowTarget(go)`, with a teleport after 10 seconds or if the
target was not in `ZNetScene` memory.

**Work.** Direct ZDO field writes — smelter ore as `ZDO.Set("item" + n, prefabName)` plus
`ZDOVars.s_queued`, fuel via `ZDOVars.s_fuel`.

## What was right, and is kept

- **ZDO as the source of truth for job state.** Persistence and network replication come
  free. Multiplayer makes this mandatory rather than merely convenient — ownership can
  transfer mid-job, so anything in a C# field is lost. See `multiplayer.md`.
- **Small reusable sub-behaviours** — move-to, find-item-and-move, find-container-and-move.
- **Cloning a Dverger** for the model, animations, ragdoll, `Talker`, and working
  pathfinding.
- **`Talker.Say` for feedback.** Cheap and charming.
- **The design in its own comments** — jobs as *verb + item + source + destination*,
  "Fill Smelter Slot 1 with (Copper, Tin) and Coal in Slot 2". That composition idea is
  better than what the code actually shipped, and is the direction we are taking.

## What broke, and the replacement

### `async void` driving the AI

`WorkLoop()` was `async void`, called from `Update()` behind an `AlreadyWorking` flag, and
every `Loop()` awaited `Task.Delay`. Exceptions in `async void` are unobservable and take
the process down — the code carries the comment `//TODO: Fix this crashing the whole game`.
Continuations also resumed with no guarantee the world still looked the same.

**Replacement:** there was never a need for async. `MonoUpdaters.FixedUpdate` already ticks
`BaseAI.Instances` at a fixed 0.05s with a real `dt`. A Harmony prefix on
`MonsterAI.UpdateAI` gives us that timestep synchronously.

### `Physics.OverlapSphere(centralPos, 500f)`

Per villager, roughly once a second, with no layer mask, followed by
`GetComponent`/`InParent`/`InChildren` on every hit. Survivable with one worker;
not with a colony.

### One AI class per job

Twelve `*AI.cs` files each re-implementing find → walk → act.

**Replacement:** jobs assembled from parts, per the comment block above.

### Raw ZDO writes for work

`SmelterHelper` wrote `"item0"`, `"item1"`, `s_queued`, `s_fuel` directly. Those are
`Smelter` internals and are exactly what a game patch renames.

**Replacement:** stations register their own RPCs. `smelterNview.InvokeRPC("RPC_AddOre",
prefabName)` is one line, routes to the owner, and needs no ownership claim. See
`api-notes.md` §7.

### No ownership checks

Almost every mutation wrote to ZDOs with no `IsOwner()` test. `ItemDropHelper.SetStack`
checked; the smelter code did not. Single-player it appears to work. In multiplayer the
writes are silently lost — `Container.OnContainerChanged` only calls `Save()` when
`IsOwner()`.

### Movement by `SetFollowTarget` plus teleport

A 10-second teleport fallback is a reasonable escape hatch, but it was doing the primary
work because there was no real pathfinding call.

**Replacement:** `BaseAI.MoveTo(dt, point, dist, run)` — protected, reachable via our
publicized build, drives the same pathfinder vanilla uses and returns whether it arrived.
Keep teleport as a rare last resort.

### Work post stored as a position

`WorkPost` was a `Vector3`, so moving or destroying the post orphaned the villager.

**Replacement:** `ZDO.Set(string, ZDOID)` / `GetZDOID` — reference the post by identity.

## Outright bugs found while reading

Recorded because they are easy to reintroduce:

- `CraftingStationHelper` — `GetComponentFromPrefab(go, typeof(Piece)) as CraftingStation`
  requests `Piece` and casts to `CraftingStation`, so it is always null and the next line
  throws. Copy-paste slip.
- `PutItemInSmelterAI.FindSmelterThatCanSmelt` uses `.First()`, which throws on an empty
  sequence. Wanted `FirstOrDefault()`.
- `m_dropPrefab.name` dereferenced unguarded in `ContainsItemByDropPrefabName`,
  `GetItemsFromContainer` and `FindItemDropsOfType`, despite the project's own notes
  recording that `m_dropPrefab` is null for world `ItemDrop`s.

## The unsolved problem

The source carries the note:

> MANDATORY TO USE CHUNK LOADER TO KEEP CHUNK IN MEMORY SO THAT AI CAN WORK EVEN WHEN
> PLAYER IS FAR AWAY

It punted off-screen simulation to a third-party mod. That is the problem this project
treats as central rather than external — see `off-screen-simulation.md`.
