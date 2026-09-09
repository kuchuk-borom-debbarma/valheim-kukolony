# NPC design research

Everything needed to build a player-model villager and a composable job system, gathered
before implementation. Sources: the decompiled client assembly, a runtime prefab probe, and
the RagnarsRokare (RRR) MobAI/SlaveGreylings source.

---

## 1. The player body rig — probe results

Prefab contents are asset data, not code, so this was settled by running a probe in game
(`Debug/PrefabProbe.cs`) rather than by reading the assembly. ZNetScene holds 3459 prefabs.

**Only two prefabs in the entire game have a swappable body model:**

```
Player(models=2, isPlayer=True), Player_ragdoll(models=2, isPlayer=True)
```

`models=2` is male and female. So **a player-model NPC must be cloned from `Player`.**
There is no vanilla humanoid NPC carrying that rig.

Component lists, which decide what a clone has to gain and lose:

```
Player   : Transform, CapsuleCollider, Rigidbody, PlayerController, Player,
           ZNetView, ZSyncTransform, ZSyncAnimation, Talker, VisEquipment, Skills, FootStep

Dverger  : Transform, CapsuleCollider, Rigidbody, Humanoid,
           ZNetView, ZSyncTransform, ZSyncAnimation, MonsterAI, CharacterDrop,
           VisEquipment, FootStep, NpcTalk
```

The gap is narrow and precise: a Player clone needs `Player` and `PlayerController`
removed, and `Humanoid` + `MonsterAI` added. Everything else it already has.

### Why the `Player` component cannot stay

`Player.Awake` calls `s_players.Add(this)`, and `IsPlayer()` is a virtual returning true
only on `Player`. `Player.GetAllPlayers()` feeds spawners, event triggers, boss checks and
AI targeting. An NPC left registered as a player would distort all of it.

### The transplant

`Player : Humanoid : Character`, so every field the prefab author set on `Humanoid` and
`Character` already exists on the Player component with real values — health, speed,
effect lists, the animator and collider references. Those transfer by reflection:

```csharp
Player source = clone.GetComponent<Player>();
Humanoid target = clone.AddComponent<Humanoid>();
CopyDeclaredFields(typeof(Character), source, target);
CopyDeclaredFields(typeof(Humanoid),  source, target);
Object.DestroyImmediate(source);
Object.DestroyImmediate(clone.GetComponent<PlayerController>());
Object.DestroyImmediate(clone.GetComponent<Skills>());   // player-only
clone.AddComponent<MonsterAI>();
```

`MonsterAI.Awake` resolves `m_character` via `GetComponent<Character>()`, which the new
Humanoid satisfies.

**This is the highest-risk part of the milestone** and is the reason it gets built and
verified on its own before any job work.

---

## 2. Appearance and clothing — all vanilla, all ZDO-backed

`VisEquipment` writes every appearance property to the ZDO by itself:

```
s_modelIndex   s_skinColor    s_hairColor    s_hairItem     s_beardItem
s_chestItem    s_legItem      s_helmetItem   s_shoulderItem s_utilityItem
s_leftItem     s_rightItem    s_leftBackItem s_rightBackItem s_trinketItem
```

So a randomised appearance **persists and replicates for free** — no work from us.

The setters are all public: `SetModel(int)`, `SetSkinColor(Vector3)`,
`SetHairColor(Vector3)`, `SetHairItem(string)`, `SetBeardItem(string)`,
`SetChestItem(string)`, `SetLegItem(string)`, and so on by prefab name.

### Randomised gear is also vanilla

`Humanoid.GiveDefaultItems()` picks from `m_defaultItems`, `m_randomWeapon`,
`m_randomArmor`, `m_randomShield`, `m_randomSets` and `m_randomItems`, seeded from
`m_seed` so a given creature always rolls the same loadout.

24 vanilla humanoids already use it. Useful references for how sets are structured:

```
Goblin(armor=4, weapon=6)   GoblinBrute(sets=4)   Troll(sets=3)
DvergerMage(sets=3)          DvergerTest(sets=2)   Skeleton(weapon=5)
```

Our villager populates `m_randomSets` with vanilla armour prefabs and calls
`GiveDefaultItems()` once, on the owner, at spawn.

### The RRR caveat on equipping non-players

RRR patch both `Humanoid.EquipItem` and `VisEquipment.AttachItem` because vanilla assumes
a player rig. Their `AttachItem` prefix reimplements attachment: find the `attach` or
`attach_skin` child on the item prefab, instantiate it, disable its colliders, parent it to
the joint.

**We may not need this.** RRR were attaching to *greylings*, which have no player rig.
Our villager clones the Player prefab, so `attach_skin` armour has the right skeleton to
bind to. Treat it as a known fallback rather than something to copy pre-emptively — verify
with the harness first.

Their `Humanoid.EquipItem` prefix is a different matter: vanilla `EquipItem` does
player-specific work, and they bypass it, setting `m_rightItem` directly then calling
`VisEquipment.SetRightItem` plus the private `UpdateEquipmentVisuals`. Expect to need
something similar.

---

## 3. Job architecture — what to take from RRR, and what not to

RRR's `SlaveGreylings` is the closest prior art: creatures that do assigned work. Their
`IBehaviour` is exactly the "input/output puzzle piece" model.

```csharp
public interface IBehaviour
{
    void Configure(MobAIBase aiBase, StateMachine<string, string> brain, string parentState);
    void Update(MobAIBase instance, float dt);
    string StartState { get; }
}
```

Each behaviour registers **its own substates** under a parent state in a shared
hierarchical state machine, and the caller wires where it exits:

```csharp
// SearchForItemsBehaviour
public IEnumerable<ItemDrop.ItemData> Items { get; set; }   // Input
public ItemDrop.ItemData FoundItem { get; private set; }    // Output
public string SuccessState { get; set; }                    // where to go when done
public string FailState { get; set; }
public string Postfix { get; set; }   // lets one behaviour appear twice in one machine
```

### Worth adopting

- **Explicit Input / Output / Settings sections** on every fragment. It makes the
  composition contract obvious and is exactly the puzzle-piece model.
- **Caller-supplied `SuccessState` / `FailState`.** The fragment does not know what comes
  next; the job wires it. This is what makes fragments reusable.
- **A `Postfix` discriminator** so the same fragment can appear more than once in one job
  (our haul job needs two move-to steps).
- **Reverse-patching `BaseAI.UpdateAI`'s housekeeping.** RRR reimplement the base
  bookkeeping — takeoff/landing, jump timer, regeneration, `m_alerted` sync — so they can
  skip vanilla `MonsterAI` without losing it. **Our current `MonsterAiTickPatch` drops all
  of that**, which is a real gap: our villagers never regenerate health. Fix by adopting
  this technique.

### Deliberately not adopting

- **State in C# fields.** RRR keep AI state in `MobAIBase` objects held by a `MobManager`,
  keyed by a ZDO GUID. That state is lost when a mob unloads or changes owner. Our
  multiplayer requirement (`multiplayer.md`) makes that unacceptable — ownership can move
  mid-job, so job progress must live on the ZDO.
- **The `Stateless` library.** RRR depend on it for hierarchical state machines. It is a
  real dependency to ship, and its `StateMachine<string,string>` holds state in memory,
  which conflicts with the point above. A small purpose-built step runner that stores the
  current step index on the ZDO fits our constraints better.

### Identifying our creatures

RRR tag any creature with a ZDO string (`Z_CharacterId`), so any tamed animal can be made
a worker. Flexible, but it costs a ZDO string read per creature per tick.

We identify by component (`TryGetComponent<Villager>`), which is cheaper and sufficient
while villagers are a prefab we own. Revisit if we ever want to employ vanilla creatures.

---

## 4. Mechanics the job steps need

**Ground items have a registry.** `ItemDrop.s_instances` is a private static list — no
`Physics.OverlapSphere` needed.

**Pickup requires ZDO ownership.** `ItemDrop.CanPickup()` returns `m_nview.IsOwner()`.
Vanilla requests it with `ItemDrop.RequestOwn()`, which invokes `RPC_RequestOwn` with
exponential backoff (`0.2 * 2^n`, capped at 30s). A pickup step must request ownership and
wait, not assume it.

**Carried items do not persist by default.** `Humanoid.m_inventory` is
`new Inventory("Inventory", null, 8, 4)` — every humanoid has 32 slots — but only `Player`
saves its inventory, and `Container` saves via `ZDOVars.s_items`. **A villager carrying
wood would lose it on reload or ownership transfer.**

Options: add a `Container` component to the villager (persists free, but brings a second
`Hoverable`/`Interactable` and the container UI), or serialise the carried item ourselves.
`ItemDrop.SaveToZDO(index, itemData, zdo)` / `LoadFromZDO` exist for exactly this and keep
full fidelity — durability, quality, variant, crafter, custom data.

**Containers auto-save, but only for the owner.** `Container.OnContainerChanged` calls
`Save()` only when `IsOwner()`.


---

## 5. Implementation findings

Three things only surfaced by building it. All are the kind of detail that costs an
afternoon if you meet them without knowing what you are looking at.

**The Player prefab is stored inactive.** The game activates player objects explicitly when
spawning them. A clone inherits `activeSelf == false`, an inactive instance never runs
`Awake`, its `ZNetView` never creates a ZDO, and the villager is completely inert while
looking perfectly well-formed in the component list. Fix: `prefab.SetActive(true)` after
cloning. Jotunn keeps prefabs under an inactive container, so this does not wake the
prefab itself.

**The Player prefab is not persistent.** `ZNetView.m_persistent` is false, because player
characters live in their own profile rather than as world ZDOs. Inheriting that is fatal:
`ZNetScene.RemoveObjects` destroys the ZDO of any non-persistent object leaving the active
area. Fix: set `m_persistent = true` (base was `persistent=False type=Prioritized`).

**A new player starts in rags, and the clone inherits the kit.**
`Humanoid.GiveDefaultItems()` hands out `m_defaultItems` on spawn, which for the Player
prefab is the starting rags. Those equip *over* whatever appearance we chose, so villagers
silently changed clothes on their first reload. Fix: clear `m_defaultItems`,
`m_randomWeapon`, `m_randomArmor`, `m_randomShield`, `m_randomSets` and `m_randomItems` on
the clone, leaving VisEquipment as the only thing that dresses a villager.

Field transplant volume, for reference: **172 Character fields and 47 Humanoid fields**
copied from the Player component.

### Verifying appearance

`VisEquipment` stores equipment slots as the prefab name's **stable hash**, not the name —
`zdo.GetString(ZDOVars.s_chestItem)` returns empty and reads as "not dressed". Use
`GetInt`. Its live `m_chestItem`/`m_hairItem` fields sync from the ZDO over several frames,
so sampling them early also reads empty. Compare ZDO hashes across runs; that is what
proves an appearance is stable.
