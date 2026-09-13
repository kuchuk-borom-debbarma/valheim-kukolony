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

**The kept zones are a set, which is what makes a settlement cheap.** A villager holds the zone
it stands in plus a ring of neighbours — it cannot path into unloaded ground — but twenty
villagers working one settlement share those nine zones rather than paying nine each. Only
villagers genuinely spread across the map cost what they look like they cost.

**The ceiling is a budget, not a rule.** Every object in a held zone is instantiated and ticking,
so `KeepAliveMaxZones` bounds what a settlement can ask of the machine; villagers are taken before
the circles, so what a bound cap drops is an outpost's far edge rather than somebody's legs. Set
it to **0** for no ceiling — a promise about your machine rather than about the mod — and reaching
a ceiling is logged rather than passed over in silence.

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

---

## Fixed: never hand `FindObjects` the game's own visited-sector set

**This is the one that made a travelling villager vanish**, and it is invisible by inspection.

`ZDOMan.FindObjects(zone, list, visitedSectorIndices)` skips any sector already in the set it is
given. The keep-alive append passed `zdoMan.m_visitedSectorIndices` — *the game's own*, which
vanilla has just finished filling in during the very call we postfix. So every sector vanilla
looked at was one we could not enumerate. For a zone vanilla visited but chose to put **nothing**
in the near list, our append therefore found nothing to add, said nothing about it, and everything
in it was destroyed.

The symptom: a villager walking away from its settlement unloaded at about a hundred metres, every
run, while reporting `zoneHeld=True allowed=True`. The halo was right, the allowlist was right, the
record was right, and the append was quietly enumerating an empty set.

Use a private set, cleared per `CreateDestroyObjects` pass, so the append is independent of
vanilla's bookkeeping while still not appending the same zone twice. Measured: `unloadedTimes` went
from 1-and-never-returns to **0**.

## Unobserved movement must turn physics off

Dead reckoning sets a position every tick. The character controller resolves it, and on any slope
it wins: the villager was pushed back exactly as far as it was moved and sat at **0.0 m/s** for
five minutes insisting it was travelling.

So the rigidbody goes kinematic while covering ground unseen, and back the moment it walks again.
Nobody can see this happen — that is the precondition for reckoning at all — so there is nothing
to gain by colliding with scenery and a whole journey to lose.

## Verified

Three consecutive full runs, no failures, 151 in-game checks and 102 deterministic cases each:

| | out | home |
|---|---|---|
| run 1 | 160m to 6m in 126s | 155m to 5m in 94s |
| run 2 | 160m to 6m in 65s | 155m to 6m in 81s |
| run 3 | 160m to 5m in 66s | 155m to 5m in 71s |

Unloaded zero times in all six legs. Before this work a villager stopped existing at about a
hundred metres, every run.

The spread in timings is the navmesh, not the villager: the first journey over new ground waits
for tiles to be built and the next one does not.

## The rescue ladder, in order of how visible it is

Walking is how a villager travels. Everything below it is a rescue, and each rung is only
reached because the one above it failed.

| Rung | When | What a player sees |
|---|---|---|
| Walk | always, first | ordinary movement |
| Put back on the navmesh | stalled 15s, in view | a correction of a metre or two |
| Reckon | stalled 15s, unobserved | nothing - that is the precondition |
| Reckon anyway | stalled 15s, in view, two corrections already failed | a villager crossing ground oddly |

The last rung is a deliberate trade. Being seen is normally the one thing that forbids
reckoning, and a settlement that silently loses a worker to a patch of ground is worse than a
player occasionally noticing one cross it strangely. Real progress resets the counter, so one bad
patch early in a journey does not leave a villager gliding for the rest of it.

**Measured, both legs, repeatably:** out 160m to 8m in 186s, home 152m to 8m in 98s, unloaded
zero times. Before any of this, a villager stopped existing at about a hundred metres, every run.

## Resolved: walking was never broken, it was being interrupted

The last of it, and the measurement that settled it. Asking the pathfinder about the stretch
ahead **while the villager was failing to walk it**:

```
stalled  5s: waypoints=0   fullPath=False  -> PathFailed
stalled 15s: waypoints=0   fullPath=False  -> PathFailed
stalled 20s: waypoints=1   fullPath=False  -> Moving
stalled 25s: waypoints=20  fullPath=True   -> Moving
```

**Twenty-five seconds to build the navmesh for a forty-four metre stretch of ground nobody had
walked** - exactly what one tile per cycle with a five second minimum age comes to. Nothing was
broken. The villager was waiting for the world, and the rescue kept firing a moment before
walking became possible, so the journey was covered without touching the ground.

Patience is now forty-five seconds, which is a measurement rather than a preference.

Three other things had to be true before it worked:

- **Walk towards the next stretch, not the far end.** `GetPath` snaps both ends and fails
  outright if either will not snap, so a destination in unloaded terrain makes the whole question
  unanswerable. See [valheim-findings.md](valheim-findings.md).
- **The next stretch must itself be walkable, and must be progress.** Snapping it to the nearest
  standable ground is right; accepting a snap that lands sideways is not, and sends a villager
  walking perfectly well while getting no closer.
- **Every errand starts on foot.** A villager that arrived mid-rescue used to keep covering
  ground into its next journey - a hundred and fifty metres home in twenty-one seconds without
  touching the ground once.

Measured, both legs, walking: out 160m to 7m in 62s, home 153m to 6m in 37s, unloaded zero times.



**Status: no longer loses villagers; does not yet complete a journey in reasonable time.**

With both fixes above, a villager sent 160m covers about 117m in 300s without ever unloading. That
is roughly **0.39 m/s** against a walking speed of 1.6 — so it is advancing about a quarter as
often as it should. The arithmetic is right (`Reckoning.StepLength` is unit-tested and the AI delta
is passed correctly), which points at how often `TryTakeOver` runs for a villager far from the
player rather than at what it does when it runs.

The benchmark check fails on purpose while this is true.

**Status: reproducible, automated, unfixed.** The benchmark check
`a villager sent out to open country beyond the settlement arrives` fails on purpose while this
is true. It is a real defect, not a flaky test.

What is measured, every run, at the same place:

```
160m to 59m in 300s, unseen=True unloadedTimes=1
```

A villager sent 160m from the settlement covers about a hundred metres, unloads **once**, and is
never instantiated again - so it stops there for good. Its ZDO is intact throughout; this is not
destruction.

What has been ruled out by measurement rather than argument:

| Suspected | Measured |
|---|---|
| The prefab is not kept alive | `allowed=True` at the moment it goes |
| Its zone is not held | `zoneHeld=True` at the moment it goes |
| The append skipped its zone | `skipped=True` originally — removing the skip changed nothing |
| It was killed by something | no damage, no death, ZDO intact |
| Its record went stale behind it | fixed separately; drift stays under a metre |

So the halo contains the zone, the prefab qualifies, the record is current, and the object is
still destroyed-as-in-unloaded. The remaining candidate is the path that brings an **unloaded**
villager back: `KeepAliveDriver` scans for villager ZDOs every `KeepAliveScanSeconds` and feeds
their positions to the halo, and something in that loop is not re-forcing this one.

Worth knowing before picking it up: the reference decompile in `.reference/` is a **different
build** from the installed game — its `FindSectorObjects` takes `(Vector2i, int area)` where the
live one takes `(Vector2s, SimulationDistance)`. Reasoning from it about this code is unsafe;
instrument the running game instead, which is how every line of the table above was settled.
