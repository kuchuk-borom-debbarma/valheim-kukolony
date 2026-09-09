# API notes

## Station-job verification gate

Before enabling a station executor, run the in-game PrefabProbe and record the exact
component and vanilla RPC contract here. Use ownership-safe inventory operations and
vanilla RPCs; never mutate guessed internal ZDO keys.

What we will actually call, read from the decompiled `assembly_valheim` (Unity
6000.0.61f1) and `Jotunn.dll` 2.27.1. Line refs point into
`.reference/assembly_valheim.decompiled.cs` (gitignored).

Several of these replace approaches used in the 2024 predecessor. Those are called out.

## 1. Spawning a villager

Jötunn clones a vanilla prefab and registers it as a new creature:

```csharp
CreatureManager.OnVanillaCreaturesAvailable += () => { ... };

var cfg = new CreatureConfig { Name = "Villager", Faction = Character.Faction.Players };
var villager = new CustomCreature("Kukolony_Villager", "Dverger", cfg);
villager.Prefab.AddComponent<VillagerBrain>();
CreatureManager.Instance.AddCreature(villager);
```

`CreatureConfig` exposes `Name`, `Group`, `Faction`, `DropConfigs`, `SpawnConfigs`,
`Consumables`. Setting `Faction = Players` is cleaner than the old mod's approach of
zeroing `m_viewRange`/`m_hearRange`/`m_alertRange` to stop the Dverger being hostile.

Dverger is still the right base: humanoid rig, animations, ragdoll, `Talker`, and a
working `MonsterAI` with `Pathfinding.AgentType.Humanoid`.

Spawn an instance at runtime with `UnityEngine.Object.Instantiate(prefab, pos, rot)` —
`ZNetView.Awake` creates the ZDO. `ZNetScene.SpawnObject` broadcasts an RPC to *everyone*
and is not what we want.

## 2. Villager state

`ZDO` has typed accessors, including one that matters a lot:

```csharp
public void Set(string name, ZDOID id);
public ZDOID GetZDOID(string name);
// plus float / int / long / bool / string / Vector3 / Quaternion / byte[]
```

`Set(string, ZDOID)` lets a villager reference its work post *by identity* rather than by
position. The old mod stored a `Vector3` work post, which breaks if the post is moved or
destroyed. Store the ZDOID and resolve with `ZDOMan.instance.GetZDO(id)`.

Every `Set` overload also takes a precomputed `int` hash — use `"Key".GetStableHashCode()`
cached in a static, not the string overload, on anything touched per tick.

## 3. Movement

`BaseAI` has real pathfinding, `protected` but reachable through our publicized build:

```csharp
protected bool MoveTo(float dt, Vector3 point, float dist, bool run);   // line 4771
protected bool MoveAndAvoid(float dt, Vector3 point, float dist, bool run);
protected bool FindPath(Vector3 target);
protected bool HavePath(Vector3 target);
protected void Follow(GameObject go, float dt);
public void StopMoving();
public void MoveTowards(Vector3 dir, bool run);   // raw steering, no pathfinding
```

**This replaces the old mod's `SetFollowTarget` + teleport-on-timeout.** `MoveTo` drives
the same pathfinder the vanilla AI uses. Verified working — see `spike-results.md`.

**But `MoveTo` returning `true` means "stopped", not "arrived".** It returns `true` in four
cases, and two of them are failures:

```csharp
if (Utils.DistanceXZ(point, transform.position) < Mathf.Max(dist, num)) { StopMoving(); return true; }  // arrived
if (!FindPath(point))  { StopMoving(); return true; }   // NO PATH FOUND
if (m_path.Count == 0) { StopMoving(); return true; }   // empty path
// ...consumed last waypoint                             StopMoving(); return true;
```

So every call site must confirm arrival itself, or an unreachable target will look like a
completed move:

```csharp
bool stopped = ai.MoveTo(dt, target, dist, run);
if (stopped && Utils.DistanceXZ(target, ai.transform.position) >= dist)
{
    // pathing failed - retry, teleport fallback, or abandon the job
}
```

`FindPath` is throttled internally (cached for 1s, or 5s if the target moved under 1m), so
calling `MoveTo` every tick does not recompute paths 20 times a second. Keep a teleport
fallback for genuinely unreachable targets.

## 4. Driving our own AI

`BaseAI` implements `IUpdateAI`. `MonoUpdaters.FixedUpdate` ticks every `BaseAI.Instances`
entry at a fixed **0.05s**:

```csharp
public virtual bool UpdateAI(float dt)
{
    if (!m_nview.IsValid()) return false;
    if (!m_nview.IsOwner()) return false;   // AI runs ONLY on the ZDO owner
    ...
}
```

So the plan is a Harmony **prefix on `MonsterAI.UpdateAI`**: if this is one of our
villagers, run the job tick and return `false` to skip vanilla wander/target logic.

This gives us a fixed timestep with a real `dt` and no threading — which is the whole
answer to the old mod's `async void` + `Task.Delay` problem. No coroutines needed either.

## 5. Interaction

Three tiny interfaces, all implemented on a plain `MonoBehaviour`:

```csharp
public interface Interactable { bool Interact(Humanoid user, bool hold, bool alt);
                                bool UseItem(Humanoid user, ItemDrop.ItemData item); }
public interface Hoverable    { string GetHoverText(); string GetHoverName(); }
public interface TextReceiver { string GetText(); void SetText(string text); }
```

`Hoverable` is what the old mod was missing — it had no way to show a villager's current
job on hover. `TextReceiver` gets the vanilla rename dialog for free; `Tameable` uses it
that way, and also has `m_randomStartingName` for villager names.

## 6. Finding work — do not use OverlapSphere

The game keeps static registries, and they are all cheaper than a physics query:

```csharp
Piece.GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces);   // iterates s_allPieces
Piece.GetAllComfortPiecesInRadius(...);
WearNTear.GetAllInstances();
CraftingStation.FindStationsInRange(string name, Vector3 point, float range, List<CraftingStation>);
CraftingStation.FindClosestStationInRange(string name, Vector3 point, float range);
CraftingStation.GetCraftingStation(Vector3 point);
BaseAI.GetAllInstances();
```

`Piece.GetAllPiecesInRadius` is a linear walk over a static list with a distance check —
no colliders, no allocation beyond the output list.

**This replaces `Physics.OverlapSphere(centralPos, 500f)`**, which was the single biggest
performance problem in the predecessor. Note these are still O(all pieces), so cache
results per job cycle rather than calling every tick.

## 7. Doing work — go through RPCs, not raw ZDO writes

The important discovery. Vanilla adds ore to a smelter like this:

```csharp
// Smelter.OnAddOre, line 122435
user.GetInventory().RemoveItem(item, 1);
m_nview.InvokeRPC("RPC_AddOre", item.m_dropPrefab.name);
```

Registered in `Smelter.Awake`:

```csharp
m_nview.Register<string>("RPC_AddOre", RPC_AddOre);
m_nview.Register("RPC_AddFuel", RPC_AddFuel);
m_nview.Register("RPC_EmptyProcessed", RPC_EmptyProcessed);
```

So a villager loading a smelter is one line, routed to the ZDO owner, with no ownership
claim and no knowledge of internal keys:

```csharp
smelterNview.InvokeRPC("RPC_AddOre", prefabName);
```

**This replaces the old mod's `ZDO.Set("item" + queueSize, name)` + `ZDOVars.s_queued`
writes** — the version-fragile code that broke on game updates. Capacity is still ours to
check first: `Smelter.m_maxOre` (10), `m_maxFuel` (10), `m_fuelPerProduct` (4),
`m_secPerProduct` (10f).

Assume every interactive station has the same shape. Read its `Awake` for the registered
RPC names before writing any ZDO key by hand.

## 8. Containers

`Container.Awake` wires `m_inventory.m_onChanged` to `OnContainerChanged`:

```csharp
private void OnContainerChanged()
{
    if (!m_loading && IsOwner()) Save();   // Save() writes ZDOVars.s_items
}
```

So mutating `container.GetInventory()` persists and replicates **automatically — but only
if we own the ZDO.** Call `ZNetView.ClaimOwnership()` first, or the change is silently
lost. This is the concrete form of the ownership rule.

Transfers should use the vanilla helper rather than hand-rolled stack juggling:

```csharp
bool Inventory.MoveItemToThis(Inventory from, ItemDrop.ItemData item, int amount, int x, int y);
void Inventory.MoveItemToThis(Inventory from, ItemDrop.ItemData item);
int  Inventory.StackAll(Inventory from, bool message = false);
```

**This replaces the predecessor's `StoreItem`**, a borrowed clone-one-at-a-time loop with
a manual "sanity check that nothing was lost".

For the villager's own carry inventory, `Humanoid.GetInventory()` already exists, along
with `Pickup(GameObject)` and `PickupPrefab(GameObject, stackSize)`.

## 9. Ownership rules

```csharp
bool ZNetView.IsOwner();
void ZNetView.ClaimOwnership();
bool ZDO.IsOwner();
void ZDO.SetOwner(long uid);
```

- AI ticks only on the owner (`BaseAI.UpdateAI`).
- Container writes persist only on the owner (`Container.OnContainerChanged`).
- Prefer an existing RPC over claiming ownership; claim only when there is no RPC.

## 10. Still to work out

- Which prefab the work post should be. A `CustomPiece` with `Piece` + `Container` +
  `Interactable` is the obvious shape, but it needs an asset, which means Unity.
  Milestone 1 may be able to use an existing piece to avoid that.
- `CookingStation` and `Fermenter` RPC surfaces — same treatment as `Smelter`, not yet read.
- Whether villagers should carry items in a `Container` component or `Humanoid`'s
  inventory. `Humanoid` is more natural and gets pickup animations.
