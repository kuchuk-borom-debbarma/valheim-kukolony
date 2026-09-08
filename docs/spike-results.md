# Spike results

Evidence gathered before building on the architecture. Each spike tests assumptions that
`docs/off-screen-simulation.md`, `docs/multiplayer.md` and `docs/api-notes.md` were written
as if settled.

---

## Spike B — taking over the brain

**Date:** 2026-09-09 · **Result: PASS**, with one important correction.

Method: a Harmony prefix on `MonsterAI.UpdateAI` targeting a plain vanilla `Dverger`,
walking it toward the local player via `BaseAI.MoveTo`. Evidence written to
`BepInEx/LogOutput.log` and read back rather than observed on screen.

### What was proved

**1. A prefix on `MonsterAI.UpdateAI` suppresses vanilla behaviour.**

```
[SpikeB] prefix ACTIVE on Dverger id=-393312 pos=(17.1,63.6,-16.6) - vanilla AI suppressed
```

The Dverger stopped behaving like a Dverger and did only what our tick told it to.

**2. The publicized build reaches `protected` members at runtime.** No
`MethodAccessException` — zero exceptions in the whole session. `BaseAI.MoveTo` and
`BaseAI.HavePath` are both `protected`, both called from a static Harmony patch class,
both worked. This confirms `Properties/IgnoreAccessModifiers.cs` and its
`SecurityPermission(SkipVerification)` attribute function on Unity 6 / macOS arm64 / Mono.

This was the assumption most expensive to be wrong about, and it holds.

**3. `MoveTo` drives real pathfinding when called from our own code.** With the player
standing still, the Dverger closed on them monotonically:

```
dist=34.12  self=(8.5,63.0,0.9)    target=(-15.3,59.7,25.2)
dist=32.44  self=(7.9,63.0,2.7)    target=(-15.3,59.6,25.2)
dist=30.75  self=(7.2,63.0,4.5)
dist=28.98  self=(6.5,63.0,6.4)
...
dist=22.31  self=(2.6,63.1,12.8)
```

Roughly 1.7 m/s, `havePath=True` throughout, and it navigated terrain across ~60m without
our code doing any steering. It also correctly stopped inside the 3m stop distance
(`dist=1.46 arrived=True`).

**4. The prefab name `"Dverger"` is correct** — it resolved via
`ZNetScene.instance.GetPrefab("Dverger")`. Other `MonsterAI` prefabs observed in passing:
`Boar`, `Neck`, `Greyling`, `Greydwarf`, `Greydwarf_Elite`, `Greydwarf_Shaman`, `Skeleton`.

### The correction: `MoveTo` returning true does not mean "arrived"

The log showed `arrived=True` at `dist=4.12` with a 3m stop distance, which should not
happen. Reading `BaseAI.MoveTo` explains it — it returns `true` in **four** cases:

```csharp
if (Utils.DistanceXZ(point, transform.position) < Mathf.Max(dist, num)) { StopMoving(); return true; }  // arrived
if (!FindPath(point))    { StopMoving(); return true; }   // NO PATH FOUND
if (m_path.Count == 0)   { StopMoving(); return true; }   // empty path
// ...consumed last waypoint
                           StopMoving(); return true;
```

**`true` means "stopped", not "arrived".** Two of the four cases are pathfinding
*failures* reported as success. In the log this is visible as the two early ticks with
`havePath=False arrived=True` — the path had not been computed yet, so `MoveTo` gave up
and reported completion.

For job code this is a trap: a villager that cannot reach a container would report its
move step complete and the job would carry on as if it were standing there. Every call
site must confirm arrival separately:

```csharp
bool stopped = ai.MoveTo(dt, target, dist, run);
if (stopped)
{
    bool arrived = Utils.DistanceXZ(target, ai.transform.position) < dist;
    if (!arrived) { /* pathing failed - retry, teleport fallback, or abandon */ }
}
```

Also worth knowing: **`FindPath` is throttled internally.** It returns a cached result if
called within 1s, or within 5s when the target has moved less than 1m. Calling `MoveTo`
every tick is therefore cheap — pathfinding is not recomputed 20 times a second.

### Consequences

- `api-notes.md` §3 updated with the return-value caveat.
- The predecessor's teleport-on-timeout fallback is still needed, but for the narrow case
  of genuine pathfinding failure rather than as the primary movement mechanism.
- No change needed to the tick model — a fixed 0.05s prefix works exactly as designed.

---

## Spike A — dedicated server behaviour

**Date:** 2026-09-09 · **Result: PASS** — the architecture survives, for a reason that was
not obvious.

Method: installed the Valheim Dedicated Server (Steam app 896660 — a macOS build does
exist), decompiled `valheim_server/Data/Managed/assembly_valheim.dll`, and diffed it
against the client build.

Note: `steamcmd` from Homebrew is broken on macOS 27 — it self-updates then dies with
`Fatal Error: Failed to load steamconsole.dylib`. Install through the Steam client GUI
instead (Library → Tools).

### The two builds genuinely differ

```csharp
// client                          // server
public bool IsDedicated()          public bool IsDedicated()
{ return false; }                  { return true; }
```

So the earlier caveat was justified — the client assembly could not have answered this.

### The finding

**A dedicated server has all the simulation machinery. Vanilla just points it at nowhere.**

The server build contains, unchanged from the client:

- `ZNetScene.CreateDestroyObjects` — same body, same `FindSectorObjects` call
- `ZNetScene.CreateObjectsSorted` — including the `IsActiveAreaLoaded()` early-return
- `BaseAI.UpdateAI` — same validity and ownership gates
- `MonoUpdaters` — still ticks `BaseAI.Instances` at a fixed 0.05s

What differs is a single line in `Game.FixedUpdate`, present **only** in the server build:

```csharp
private void FixedUpdate()
{
    if (ZNet.m_loadError) { ... }
    ZNet.instance.SetReferencePosition(new Vector3(1000000f, 0f, 1000000f));
}
```

Every fixed frame, the server parks its reference position roughly 1000 km from the world
origin — far outside the ~10.5 km playable radius. Since both `ZoneSystem.CreateLocalZones`
and `ZNetScene.CreateDestroyObjects` key off that position, the server loads zones and
instantiates objects around a point where nothing exists.

That is why a vanilla dedicated server does not simulate creature AI: not because it
cannot, but because it is deliberately aimed at empty space.

### Consequence for the design

**Server-owned idle colonies survive.** The keep-alive from `off-screen-simulation.md`
does not depend on the reference position — it appends colony ZDOs to the lists that
`CreateObjects`/`RemoveObjects` consume. That mechanism works identically on a dedicated
server, and `BaseAI` will tick whatever we bring into existence.

It also reframes what the keep-alive *is*. On a client it widens an active area that
already exists. On a dedicated server it is the only thing pointing the simulation at the
world at all.

### The remaining unknown, now cheap to test

`CreateObjectsSorted` early-returns when `!ZoneSystem.instance.IsActiveAreaLoaded()`, which
checks the zones around the parked reference position. Whether zones at (1000000, 1000000)
ever finish loading decides whether near-object creation runs on a server at all.

If they never load, options are: patch `IsActiveAreaLoaded`, route colony objects through
`CreateDistantObjects` (which has no such guard), or override the reference position
ourselves.

This no longer blocks anything — we have a runnable macOS server, so it can be measured
when the keep-alive is built rather than reasoned about now.

### Performance note

The million-coordinate line is a deliberate optimisation: it keeps a headless server from
paying for physics, colliders and AI. Our keep-alive removes that saving for colony zones
specifically. That is the intended trade, but it means the radius and colony caps in
`off-screen-simulation.md` matter more on a dedicated server than on a client.


---

## Milestone 1a — villager as a custom creature

**Date:** 2026-09-09 · **Result: PASS**, both runs, fully unattended.

First verification run using the self-driving harness — see
[automated-testing.md](automated-testing.md).

```
==================== KUKOLONY SELF TEST ====================
  Run 1 - villager spawned fresh
------------------------------------------------------------
  [PASS] villager prefab is registered
  [PASS] spawned villager carries the Villager component
  [PASS] villager present in world
  [PASS] villager has a valid ZDO
  [PASS] villager has a name - Ingrid
  [PASS] villager has a home - (125.9, 86.8, -2.7)
  [PASS] villager is tamed (friendly to the player)
  ....  displaced villager to 40m from home
  [PASS] villager walked home - 40m -> 8m in 24s
------------------------------------------------------------
  RESULT: PASS
============================================================
```

Relaunched into the same world:

```
==================== KUKOLONY SELF TEST ====================
  Run 2 - villager loaded from save
------------------------------------------------------------
  [PASS] villager present in world
  [PASS] villager has a valid ZDO
  [PASS] villager has a name - Ingrid
  [PASS] villager has a home - (125.9, 86.8, -2.7)
  [PASS] villager is tamed (friendly to the player)
  ....  name and home above were restored from the save file
------------------------------------------------------------
  RESULT: PASS
============================================================
```

Same name, same home, across a process restart. The ZDO state model works.

Behaviour observed en route, and worth noting because it is the design working as
intended: the villager briefly reported `stuck` immediately after being displaced —

```
Villager 'Ingrid' is now stuck
Villager 'Ingrid' cannot path home, 40m away. Retrying.
Villager 'Ingrid' is now walking home (38m away)
```

`MoveTo` returned "stopped" before a path existed. Because `VillagerMovement` distinguishes
`PathFailed` from `Arrived`, the villager retried instead of believing it had arrived —
exactly the trap documented in Spike B, caught in production code by the wrapper built to
catch it.
