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

## The approach

A "colony zone keep-alive", scoped to registered work posts rather than global:

1. **Postfix `ZoneSystem.Update`** — call `PokeLocalZone` for each zone within a small
   radius of every registered work post, so the zone roots stay loaded.
2. **Postfix `ZNetScene.CreateDestroyObjects`** is too late (the destroy pass already ran).
   Instead **prefix** it, or patch `FindSectorObjects`' result, to append
   `FindSectorObjects(colonyZone, radius, 0, near)` for each work post before
   `CreateObjects`/`RemoveObjects` see the lists.
3. Villager AI then ticks normally through `BaseAI`, because the GameObject genuinely
   exists. **We do not simulate anything ourselves** — we widen what the game considers
   live and let vanilla do the work.

This is the key insight: "off-screen simulation" here is not writing a headless simulator.
It is extending the active area to include the colony.

## Costs and constraints, stated plainly

**This is expensive and it is the main risk in the whole project.** Every kept-alive zone
is real terrain, real physics, real colliders, and every creature in it ticks AI. A
careless radius multiplies the player's load by the number of colonies. Mitigations to
design in from the start: the smallest radius that covers a work post, a hard cap on
simultaneously-loaded colonies, and a config to disable it.

**Ownership.** AI runs only on the ZDO owner. Villagers must be owned by the machine doing
the keep-alive, and every write to a shared object (container, smelter) must go through
its owner. See the ownership note in `docs/README.md`.

**Multiplayer is in scope.** Ownership arbitration decides who simulates an idle colony,
and the answer is "the server, until a player walks over". See `docs/multiplayer.md` for
how `ZDOMan.ReleaseNearbyZDOS` behaves and what it forces on job code.

**Dedicated server behaviour is unverified** and blocks this design. The client assembly
hardcodes `ZNet.IsDedicated() => false`, so it cannot tell us whether a dedicated server
instantiates GameObjects at all. If it does not, `BaseAI` never ticks there and idle
colonies need a different mechanism. Answer this before building the keep-alive.

## Verified facts

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
