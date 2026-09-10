# Off-screen simulation

**Decision: the colony must keep working when no player is nearby.**

This note records how Valheim's loading actually works, read from the decompiled
`assembly_valheim` (Unity 6000.0.61f1), and what that means for us. Line references are
into `.reference/assembly_valheim.decompiled.cs` (gitignored — regenerate with `ilspycmd`).

## How the game decides what exists

Two independent systems gate whether anything is alive at a location. Both key off a
single point, `ZNet.GetReferencePosition()`, which `Player.LateUpdate` rewrites to the
local player's position every frame.

**1. `ZoneSystem` — terrain and zone roots.**

```csharp
// ZoneSystem.Update, every 0.1s
CreateLocalZones(ZNet.instance.GetReferencePosition());  // pokes zones within m_activeArea
UpdateTTL(0.1f);                                         // unloads zones past m_zoneTTL
```

`CreateLocalZones` walks a square of `m_activeArea` zones around the reference position
and calls `PokeLocalZone`, which resets each zone's TTL to 0. `UpdateTTL` then destroys
any zone whose TTL exceeded `m_zoneTTL`.

**2. `ZNetScene` — GameObjects instantiated from ZDOs.**

```csharp
// ZNetScene.CreateDestroyObjects, 30x per second
Vector2i zone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
ZDOMan.instance.FindSectorObjects(zone, m_activeArea, m_activeDistantArea, near, distant);
CreateObjects(near, distant);
RemoveObjects(near, distant);   // destroys every instance NOT in those two lists
```

`RemoveObjects` earmarks every ZDO in the two lists with the current frame counter, then
destroys any live `ZNetView` whose ZDO was not earmarked. So the lists are not a hint —
they are the complete set of things allowed to exist.

**3. AI only ticks on the ZDO owner.**

```csharp
// BaseAI.UpdateAI
if (!m_nview.IsValid()) return false;
if (!m_nview.IsOwner()) return false;
```

`MonoUpdaters.FixedUpdate` drives `BaseAI.Instances` at a fixed 0.05s step. An AI with no
instantiated GameObject is not in `Instances` and does not tick at all.

## Why this is good news

Three properties make the problem tractable:

**`FindSectorObjects` is public.** We can ask for the ZDOs of any zone we like, not just
the player's.

**`RemoveObjects` works off the lists it is handed.** Appending our colony's ZDOs to the
near list makes them both get created and survive the destroy pass. No transpiler needed
for the destroy logic — the data drives it.

**`UpdateTTL` already has an escape hatch:**

```csharp
if (zone2.Value.m_ttl > m_zoneTTL && !ZNetScene.instance.HaveInstanceInSector(zone2.Key))
```

A zone containing a live non-distant instance is never unloaded. So once we force our
villagers to exist, the zones under them hold themselves open.

## The approach: the villager is the loader

Implemented and verified. **Each villager and the colony's registered structures contribute
positions to a bounded halo of live zones.** The region follows actual work rather than a
speculative world-wide square, and the cost is proportional to what a colony occupies.

Three facts make it work, all read from the game:

**A live instance holds its own zone open.** `ZoneSystem.UpdateTTL` only unloads a zone when
`!ZNetScene.instance.HaveInstanceInSector(zone)`. Once a villager exists somewhere, that
zone will not unload underneath it — the villager anchors itself.

**Unloaded villagers can still be found.**
`ZDOMan.GetAllZDOsWithPrefabIterative(prefab, list, ref index)` walks every ZDO of a prefab
across the whole world, spread over frames, instantiating nothing. That closes the
bootstrap loop: find the ZDO → force its zone → the villager instantiates → it holds itself
open → it moves → the region follows.

**A villager cannot path into unloaded ground.** `Pathfinding.GetPath` snaps to a navmesh
built by `NavMeshBuilder.CollectSources(bounds, layers, PhysicsColliders, …)` — from
colliders actually present. No loaded geometry, no navmesh, no path. Hence the *halo*
rather than just the villager's own zone: it needs somewhere to walk into.

### The patches

Borrowed from [ChunkLoader](https://github.com/JFHeim/ChunkLoader), which is the proven
reference for forcing zones active, and verified against our own decompile. All are
postfixes that only widen behaviour, so with an empty zone set the game is exactly stock.

| Patch | Why |
|---|---|
| `ZoneSystem.CreateLocalZones` | `PokeLocalZone` our zones — loads terrain, resets the unload timer |
| `ZNetScene.InActiveArea` ×2 | Our zones count as active, which keeps ownership stable |
| `ZDOMan.FindSectorObjects` | **Append our zones' ZDOs to the create list** |
| `ZNetScene.CreateDestroyObjects` | Prefix/finalizer marking the one call path where the append is correct |

**Three patches a chunk-loader mod uses are deliberately absent**, because each caused real
damage:

- **`ZoneSystem.IsActiveAreaLoaded`** — its only caller is `CreateObjectsSorted`, which
  returns immediately when false. Forcing it false while a colony zone was still loading
  stopped object creation *everywhere*, including around the player. It is not needed
  either: `CreateObjectsSorted` already checks `IsZoneReadyForType` per object.
- **`ZNetScene.OutsideActiveArea`** — not used for loading at all. Its callers are
  `SpawnArea` (already player-gated), a falling-support check, and `WearNTear.UpdateWear`,
  which uses it as the shortcut that stops structures decaying when nobody is around.
  Patching it made colony buildings weather and collapse over long absences.
- **`ZDOMan.FindDistantObjects`** — chunk loaders suppress it because they append the whole
  zone to the near list, so the distant pass would duplicate. We filter, and distant scenery
  is not on the allowlist, so there is nothing to duplicate. Suppressing it only deleted the
  big-tree LOD around every colony.

**`FindSectorObjects` must be scoped to `CreateDestroyObjects`.** It has three other
callers, and appending for them is actively harmful — most severely `ZNetScene.IsAreaReady`,
which returns false if any ZDO in the list lacks an instance. `Game.FindSpawnPoint` and
player teleport gate on it, so an unscoped append turned "is this area ready" into "is every
colony in the world fully instantiated", and could hang world join or a portal.

`FindSectorObjects` is the load-bearing one: `RemoveObjects` destroys any instance *not* in
the lists it is handed, so appending is simultaneously what creates our objects and what
stops them being destroyed.

The `InActiveArea` patches also fix ownership. `ZDOMan.ReleaseNearbyZDOS` uses those same
checks when arbitrating who owns what, and AI only runs on the owner — without them a
kept-alive colony would lose ownership of its villagers and quietly stop.

### Where this improves on a chunk loader

Chunk-loader mods append **every** ZDO in a forced zone, paying for hundreds of trees and
rocks nobody is looking at. We append only ZDOs whose prefab is on an allowlist, built once
by scanning `ZNetScene.m_prefabs` **by component** rather than by name, so modded chests and
stations are covered too. 1267 prefabs qualify; everything else is skipped.

Trees and logs are on the list **only when some colony gathers**, which a job declares for
itself. A gathering job that cannot see what it gathers idles silently off-screen and works
perfectly under observation — the hardest kind of fault to find — but trees are by far the
most numerous thing in the world, so a colony that only hauls and smelts pays nothing for
them. The allowlist is rebuilt when that answer changes, not every frame.

`Piece` is on the list deliberately: walking through a tree that was not loaded is
cosmetic, walking through your wall is not.

This only applies to zones held open *solely* by us. Near a player, the vanilla lists
already contain everything, so nothing is filtered and nothing is destroyed.

### What we get for free

**Keep-alive zones do not breed monsters.** All three spawner types bail without a player
present — `SpawnSystem.UpdateSpawning` returns early when `GetPlayersInZone` is empty, and
`SpawnArea`/`CreatureSpawner` both gate on `Player.IsPlayerInRange`. An unattended colony
costs nothing in spawning and does not quietly lose villagers to wildlife.

### Measured

Three villagers, one post, a bound chest, and the player teleported 500m away — well
outside any active area. `KeepAliveEnabled` exists so the control can be run:

| | wood delivered | zones held open |
|---|---|---|
| Keep-alive on | 2/2 in 26s | 9 |
| Keep-alive off | 0/2 after 180s | 0 |

Nine zones is one 3×3 halo, shared by all three villagers because their halos overlap.

## Verified facts## Verified facts

Field defaults below are the C# initializers; the real values come from the scene prefab,
so read them at runtime rather than assuming.

| Thing | Value |
|---|---|
| `ZoneSystem.m_zoneSize` | 64f |
| `ZoneSystem.m_activeArea` | 1 (initializer) |
| `ZoneSystem.m_activeDistantArea` | 1 (initializer) |
| `ZoneSystem.m_zoneTTL` | 4f |
| `ZNetScene` create/destroy rate | 30/s |
| `BaseAI` tick | fixed 0.05s via `MonoUpdaters.FixedUpdate` |
| `ZDOMan.FindSectorObjects` | public |
| `ZoneSystem.PokeLocalZone` | private (publicized in our build) |
