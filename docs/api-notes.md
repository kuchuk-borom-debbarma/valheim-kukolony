# Verified API notes

These notes describe contracts exercised against the installed Valheim build on
2026-09-10. Re-run the config-gated in-game `PrefabProbe` after game updates. An executor
must not be enabled from guessed field names or raw station ZDO keys.

## Network and persistence

`ZNetView.Awake` creates the instance ZDO. A `ZDOID` is a runtime address: the current
chunked save format assigns new IDs during load. Durable cross-ZDO references therefore
use Kukolony's stable token on the target ZDO, resolved through
`ZDOExtraData.GetAllZDOIDsWithHash`; the resolved runtime ID can then be passed to
`ZDOMan.instance.GetZDO` or `ZNetScene.instance.FindInstance`.

ZDO typed accessors cover strings, primitives, vectors, byte arrays, and ZDOID hash pairs.
Kukolony stores bounded versioned ZPackage payloads as base64 strings and caches stable key
hashes on per-tick paths.

`ZNetView.IsOwner()` decides whether local mutation is authoritative.
`ClaimOwnership()` is used for colony/member metadata and container writes. Prefer a
component's registered RPC for shared station changes.

## Registry-based discovery

```csharp
Piece.GetAllPiecesInRadius(position, radius, output);
ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, output, ref index);
ZNetScene.instance.FindInstance(id);
```

Piece discovery replaces broad `Physics.OverlapSphere` scans. Kukolony then requires a
valid ZNetView and rejects Character and ItemDrop components before capability caching.

## Movement and AI

Villagers use the existing `MonsterAI.UpdateAI(float)` fixed tick. The Harmony prefix
delegates to a synchronous state machine only on the ZDO owner; there are no async tasks or
AI coroutines. Movement goes through the publicized BaseAI path methods wrapped by
`VillagerMovement`, which distinguishes moving, arrived, and failed path results.

## Containers and carried inventory

A villager bag is a collider-less child Container with
`m_rootObjectOverride` pointing at the villager ZNetView. This persists inventory on the
villager ZDO without stealing Hoverable interaction.

For registered containers, **find the slot yourself**:

```csharp
view.ClaimOwnership();                       // writing a container needs owning it
if (!TryFindSlot(destination, item, out int x, out int y)) return Full;
destination.MoveItemToThis(source, item, item.m_stack, x, y);
```

**`MoveItemToThis(source, item, amount, -1, -1)` does not work** and this document used to
prescribe it. The amount overload rejects `(-1, -1)` even after `CanAddItem` has said yes — a
measured API defect, and a silent one: the call returns without moving anything and without
complaining. Prefer an existing compatible stack and fall back to the first empty slot, which is
what `Jobs/Carrying.TryFindSlot` does.

The container saves itself through its own change hook once the write lands, so `container.Save()`
is not needed either.

Capacity is checked with `Inventory.CanAddItem`. A failed or full deposit leaves the carried item
intact. Do not write `ZDOVars.s_items` manually.

## Verified station contracts

The RPC method signatures include a leading sender ID internally. `InvokeRPC` supplies
that sender, so executor call names and arguments are:

| Prefab | Component | Preconditions used | Executor call |
|---|---|---|---|
| `fire_pit` | `Fireplace` | `m_fuelItem`, `CanUseItems` | `InvokeRPC("AddFuelAmount", 1f)` |
| `smelter` | `Smelter` | `IsItemAllowed`, `GetQueueSize`, `GetFuel` | `InvokeRPC("RPC_AddOre", name, false)`, `InvokeRPC("RPC_AddFuel")` |
| `charcoal_kiln` | `Smelter` | same Smelter contract | `InvokeRPC("RPC_AddOre", name, false)` |
| `piece_cookingstation` | `CookingStation` | `IsEverythingCooked`, `IsStationFull`, `IsItemAllowed` | `InvokeRPC("RPC_RemoveDoneItem", position, 1)`, `InvokeRPC("RPC_AddItem", name, false)` |
| `fermenter` | `Fermenter` | `GetStatus`, `IsItemAllowed(hash)` | `InvokeRPC("RPC_Tap")`, `InvokeRPC("RPC_AddItem", hash, false)` |
| `piece_beehive` | `Beehive` | `GetHoneyLevel` | `InvokeRPC("RPC_Extract")` |

Verified internal handlers:

- `Fireplace.RPC_AddFuelAmount(long, float)`
- `Smelter.RPC_AddOre(long, string, bool)`, `RPC_AddFuel(long)`
- `CookingStation.RPC_AddItem(long, string, bool)`,
  `RPC_RemoveDoneItem(long, Vector3, int)`
- `Fermenter.RPC_AddItem(long, int, bool)`, `RPC_Tap(long)`
- `Beehive.RPC_Extract(long)`

Input is consumed only after component compatibility and capacity checks. The RPC is then
submitted through the station's ZNetView. No executor relies on internal queue/fuel/content
ZDO key names.

## Component capabilities

A registered structure caches flags when its loaded GameObject exposes any of:
`Container`, `Fireplace`, `Smelter`, `CookingStation`, `Fermenter`, or
`Beehive`. Cached capability supports unloaded UI filtering, but every actual target is
revalidated for ZDO existence, colony radius, loaded instance, component, and ownership.

## Player-like NPC villager

The probe verified `FallenWarrior` is a genuine `Humanoid` + `MonsterAI` NPC with the
male/female human `VisEquipment` rig. Kukolony clones that prefab and removes event-only
drop, dialogue, and naming components. It never adds or clones `Player`,
`PlayerController`, or `Skills`. Identity, appearance decision, home fallback,
queue/runtime, and bag state are ZDO-backed. All peers need the mod because ZDO ownership
can transfer simulation to any nearby peer.
