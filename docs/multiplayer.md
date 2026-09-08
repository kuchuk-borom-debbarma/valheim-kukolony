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
  (`InvokeRPC("RPC_AddOre", …)`) — it routes to the owner. Fall back to
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

## Dedicated servers — unverified

**The client assembly cannot answer this.** In the client build,
`ZNet.IsDedicated()` is:

```csharp
public bool IsDedicated() { return false; }
```

Hardcoded. The dedicated server ships its own `assembly_valheim.dll` with different
behaviour, which we have not decompiled. So the following are open questions, not facts:

- Does a dedicated server instantiate `ZNetView` GameObjects at all, or only relay ZDOs?
  If it never instantiates, `BaseAI` never ticks there and the keep-alive cannot work
  server-side — idle colonies would need a different mechanism entirely.
- What is a dedicated server's reference position with no local player?

**This must be answered before building the keep-alive**, because it decides whether the
server-owns-idle-colonies architecture holds. Testing it means installing the Valheim
dedicated server (Steam appid 896660) and reading its assembly.

Host-and-play multiplayer is unaffected by this uncertainty — the host has a local player
and behaves like the single-player case.
