# Multiplayer

**Decision: the mod must work in multiplayer.** This note records how Valheim arbitrates
ZDO ownership between peers, and what that forces on the colony design.

Read from the decompiled client `assembly_valheim` (Unity 6000.0.61f1). See the caveat in
"Dedicated servers" — one claim here is *not* verified.

## Who simulates what

Two rules from `docs/api-notes.md` combine into the whole multiplayer problem:

- AI ticks only on the ZDO owner (`BaseAI.UpdateAI` returns false on `!IsOwner()`).
- Container writes persist only on the owner (`Container.OnContainerChanged`).

So "who owns a villager" *is* "who is simulating that villager".

## How ownership moves

The server, and only the server, arbitrates. `ZDOMan.Update`:

```csharp
public void Update(float dt)
{
    if (ZNet.instance.IsServer())
        ReleaseZDOS(dt);        // every 2 seconds
    ...
}

private void ReleaseZDOS(float dt)
{
    ReleaseNearbyZDOS(ZNet.instance.GetReferencePosition(), m_sessionID);
    foreach (ZDOPeer peer in m_peers)
        ReleaseNearbyZDOS(peer.m_peer.m_refPos, peer.m_peer.m_uid);
}
```

And the arbitration itself:

```csharp
private void ReleaseNearbyZDOS(Vector3 refPosition, long uid)
{
    Vector2i zone = ZoneSystem.GetZone(refPosition);
    FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, 0, m_tempNearObjects);
    int activatedArea = ZoneSystem.instance.m_activeArea - 1;
    foreach (ZDO zdo in m_tempNearObjects)
    {
        if (!zdo.Persistent) continue;
        Vector2i sector = zdo.GetSector();
        if (zdo.GetOwner() == uid)
        {
            if (!ZNetScene.InActiveArea(sector, zone, activatedArea))
                zdo.SetOwner(0L);                    // I moved away, release
        }
        else if ((!zdo.HasOwner() || !IsInPeerActiveArea(sector, zdo.GetOwner()))
                 && ZNetScene.InActiveArea(sector, zone, activatedArea))
        {
            zdo.SetOwner(uid);                       // nobody near it owns it - I take it
        }
    }
}
```

Three consequences, and the third is the one that makes this workable:

1. **A ZDO near a player is owned by that player.** Ownership follows proximity.
2. **Ownership is stolen from an absent owner.** If we own a villager and another player
   walks up to it, they take it on the next 2s pass.
3. **A ZDO near nobody is never scanned at all.** `ReleaseNearbyZDOS` only iterates
   `FindSectorObjects` around a reference position. A colony with no player nearby is
   outside every peer's scan, so **it silently keeps whichever owner it last had.**

Point 3 is what makes off-screen simulation possible in multiplayer: the owner we assign
sticks until someone walks over.

## The architecture this forces

**The server owns idle colonies.** When no player is nearby, the server (host or
dedicated) holds the villager ZDOs and runs the keep-alive from
`docs/off-screen-simulation.md`. One simulator, no duplicated work, no ownership fights.

**Hand over to a nearby player, deliberately.** When a player approaches, vanilla
reassigns ownership to them and their client simulates locally — which is *correct*, not a
bug to patch around. Local simulation means no latency on villager movement. We should let
this happen rather than pinning ownership to the server.

**Which means every client needs the mod.** The moment a client can own a villager, that
client must know how to tick it, or the villager freezes where it stands. The stub already
has the attribute for this, commented out:

```csharp
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
```

Available levels: `EveryoneMustHaveMod`, `ClientMustHaveMod`, `ServerMustHaveMod`,
`VersionCheckOnly`, `NotEnforced`. `EveryoneMustHaveMod` is the honest one here.

Alternative considered and rejected: pin ownership to the server so clients never need the
mod. That means every villager's movement is server-simulated and network-interpolated to
clients, and it fights `ReleaseNearbyZDOS` every 2 seconds. Not worth it.

## Rules for job code

- **Never write to a ZDO you do not own.** Use the station's own RPC where one exists
  (`InvokeRPC("RPC_AddOre", name, false)`) — it routes to the owner. Fall back to
  `ZNetView.ClaimOwnership()` only when there is no RPC.
- **Assume ownership can change mid-job.** A player walking past transfers the villager to
  their client between one tick and the next. Job state therefore has to live on the ZDO,
  not in C# fields on the component, or the job restarts from scratch on handover. This is
  the strongest argument yet for ZDO-as-state.
- **Re-check `IsOwner()` every tick.** Do not cache it across a job step.

## Config synchronisation

Jötunn's `SynchronizationManager` pushes server config to clients, so colony radius, caps
and limits are enforced server-side rather than being per-client. `RegisterCustomConfig`
plus `CustomRPC` for anything we need to sync beyond BepInEx config entries.

## Dedicated servers — verified

**Verified 2026-09-09 against the dedicated server build.** See `spike-results.md` for the
full method. The earlier caveat here — that the client assembly hardcodes
`IsDedicated() => false` and could not answer this — was correct, and the two builds do
differ:

```csharp
// client                          // server
public bool IsDedicated()          public bool IsDedicated()
{ return false; }                  { return true; }
```

**A dedicated server has all the simulation machinery.** `ZNetScene.CreateDestroyObjects`,
`CreateObjectsSorted`, `BaseAI.UpdateAI` and the fixed 0.05s `MonoUpdaters` tick are all
present and unchanged from the client.

**Vanilla just points it at nowhere.** One line exists only in the server build:

```csharp
// Game.FixedUpdate, server build only
ZNet.instance.SetReferencePosition(new Vector3(1000000f, 0f, 1000000f));
```

Every fixed frame the server parks its reference position ~1000 km from the origin, far
outside the ~10.5 km playable radius. Both zone loading and instance creation key off that
position, so the server instantiates nothing from the real world. That is why a vanilla
dedicated server does not simulate creature AI — not that it cannot, but that it is
deliberately aimed at empty space, to keep a headless server from paying for physics,
colliders and AI.

**The server-owned idle colony design survives.** The keep-alive does not depend on the
reference position: it appends colony ZDOs to the lists `CreateObjects`/`RemoveObjects`
consume, and `BaseAI` ticks whatever we bring into existence. On a client the keep-alive
widens an active area that already exists; on a dedicated server it is the only thing
pointing the simulation at the world at all.

One thing left to measure rather than reason about: `CreateObjectsSorted` early-returns on
`!ZoneSystem.instance.IsActiveAreaLoaded()`, which tests zones around the parked position.
If those never load, near-object creation never runs server-side and we route through
`CreateDistantObjects` (no such guard) or override the reference position. A macOS build of
the dedicated server exists and is installed, so this is testable when the keep-alive is
built.

Because the million-coordinate line is a deliberate performance saving, the radius and
colony caps matter more on a dedicated server than on a client.


---

## Dedicated servers — simulation still unverified

Off-screen simulation runs on **the server only**. `ZDOMan.ReleaseNearbyZDOS` is server-side
and AI runs only on the ZDO owner, so a client forcing zones would load every colony in the
world for objects it does not own and cannot tick — while taking on all the side effects of
the keep-alive patches for nothing. `KeepAliveDriver` therefore gates on
`ZNet.instance.IsServer()`.

In single-player and host-and-play the player **is** the server, so this is true for
everyone except a joining client.

### What we could not test, and why

Whether a *dedicated* server actually instantiates GameObjects is still open. Attempting to
run one with BepInEx on macOS failed twice, before BepInEx ever loaded:

```
DllNotFoundException: .../valheim_server/Data/Managed/../lib/libmono-native.dylib
Rethrow as TypeInitializationException: The type initializer for 'Sys' threw an exception.
```

The stock server starts fine (`ZNET START`, world generation), so the injection is the
cause, not the server. Removing `DYLD_LIBRARY_PATH` did not help. **The BepInEx pack ships
only a Linux server script** (`LD_PRELOAD`, `libdoorstop_x64.so`, `valheim_server.x86_64`) —
a strong signal that a modded macOS dedicated server is a path nobody has trodden, and not
one worth debugging, since real servers run Linux or Windows.

Do not repeat this on macOS. Test on Linux if dedicated-server support ever matters.

### What the server assembly does tell us

Decompiling `valheim_server/Data/Managed/assembly_valheim.dll` shows the machinery is all
present and identical to the client — `ZNetScene.CreateDestroyObjects`, the
`IsZoneReadyForType` guard, `BaseAI.UpdateAI`, and the fixed 0.05s `MonoUpdaters` tick. The
only difference is `Game.FixedUpdate` parking the reference position at
`(1000000, 0, 1000000)`.

So the server has everything it needs; the open question is narrowly whether zones out at a
million ever load, which is what decides if vanilla's own creation path runs there at all.

### Why gating is safe regardless

If a dedicated server cannot instantiate, colonies there are broken **with or without** the
gate — a client loading them does not help unless it also owns the ZDOs, and the server
holds ownership of anything near no player. So gating removes wasted client work without
creating a failure mode.

The one case where it could matter: ownership of a *distant* colony sticks with whoever last
held it, which can be a client. If that client is gated off, nothing ticks that colony until
the server takes ownership back. Unmeasured, and worth revisiting alongside proper
dedicated-server testing.
