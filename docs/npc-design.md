# NPC design and verified player-like rig

Kukolony villagers look like players but are real NPCs. This distinction is an engine
contract, not cosmetic wording: a villager prefab must never contain `Player`,
`PlayerController`, or `Skills`, because those components enroll it in player lifecycle,
input, spawning, and global player registries.

## Runtime probe result

The config-gated `PrefabProbe` inspected actual prefab assets rather than guessing from
the managed assembly. The installed 2026-09-10 build exposes `FallenWarrior` as:

```
Transform, CapsuleCollider, Rigidbody, Humanoid, ZNetView, ZSyncTransform,
ZSyncAnimation, MonsterAI, CharacterDrop, VisEquipment, FootStep,
WarriorNames, NpcTalk
```

Its `VisEquipment` has the male/female human body models and player-compatible attachment
skeleton. It contains no player controller or player state. `ShadowPerson` is another
true NPC candidate, but `FallenWarrior` has the most complete authored humanoid movement
and visual contract for a worker.

The earlier probe conclusion that only `Player` had this rig was incomplete. Cloning the
full Player prefab and transplanting 219 inherited fields created a hybrid object whose
Awake/lifecycle assumptions could freeze Unity. `ComponentTransplant` and that design were
deleted.

## Carried items

A villager's bag is a Container mounted on a collider-less child, which supplies the grid,
the interaction UI, and ownership-safe access. Persistence is not Container's: it is
documented as flushing the inventory to `ZDOVars.s_items` on every change, but measured
in-game it never wrote for this component — with the villager owned and the container
reporting itself as owner, the record stayed empty after a change and after an explicit
Save, and everything a villager carried was gone after a real save and relaunch.

`VillagerInventory` therefore persists the bag itself, to its own key on the villager ZDO,
using the same write that villager queue state already survives on. It restores on attach
and writes on every inventory change, owner only. The benchmark asserts both the immediate
write and survival across save, exit, and reload, so a regression here fails loudly rather
than quietly eating a haul.

## Creation and removal

Players add villagers from the hearth and remove them from the member detail view;
`VillagerLifecycle` owns both. It lives in `Villagers` rather than alongside the other
colony operations because `Colonies` knows nothing about villagers and must stay that way.

A spawner only places and registers. Name, appearance, taming and bag all self-initialise on
the first owned AI tick. Placement is the one thing it must get right: a villager's home is
taken from where it stands on that tick, so the spawn point is permanent. Ground snapping
uses the reporting `GetSolidHeight` overload — the plain one returns the input height on a
miss, which would strand a villager in mid-air, and the reporting form also refuses
colliders with a rigidbody so a villager cannot spawn onto a cart or a boat.

## Stripping the ghost

The rig this clones is a spectral undead, and villagers inherited all of it: two point lights
and four particle systems, including the drifting blue motes that read as transparency. Those
are removed when the prefab is configured, while the human body, skeleton and animation that
made the rig worth cloning stay. Breath particles go with them - they are part of the same
set, and a colonist that fogs the air only in cold biomes is not worth keeping them for.

What is attached is asset data, which the managed assembly cannot answer, so the strip reports
what it found as well as removing it. It also reports any renderer still drawing with a
see-through shader rather than rewriting the material: swapping one blind is how a villager
ends up invisible. There were none.

## Production prefab

`VillagerPrefab` clones `FallenWarrior`, removes only event dialogue/name/drop behaviours,
clears inherited combat equipment, and adds `Villager`. It retains the authored
`Humanoid`, `MonsterAI`, physics, animation, network, footsteps, and human visual graph.
Registration fails closed if the required NPC components or vanilla despawn-policy fields
change.

The ZNetView is persistent, default-priority, and non-distant. Event/day despawn is cleared
both on the template and through the component's persisted policy. The benchmark asserts
that real instances are persistent, execute concurrently, enter the save snapshot, reload
under their stable identity, and retain their queue/runtime state.

## Appearance and clothing

`VisEquipment` persists and replicates model, skin, hair, beard, and equipment-slot hashes
on the villager ZDO. The owner rolls these once; `kukolony.appearance` records that the
choice was made. Clothing is cosmetic player-compatible gear selected by
`VillagerAppearance`; inherited warrior weapons and armor are cleared so vanilla default
items cannot overwrite it on a later spawn.

Use ZDO integer hashes—not string reads—to verify a rendered equipment slot. Live
`VisEquipment` fields may trail the ZDO by several frames while models attach.

## Outfits

An **outfit** is a preference: one item name per slot — head, chest, legs, cape, utility,
and the two hands. Outfits belong to the colony and are shared by name, the way job presets
are, so dressing a dozen villagers alike is one edit. A villager records which outfit it
wears on its own ZDO; an unknown name falls back to the colony's first rather than to
nothing.

**Only slots an outfit names are managed.** A villager already rolls clothes when it is
born, so an outfit that owned every slot would strip those the moment it was assigned.
Naming a slot is what hands it over, and the starter outfit names nothing — an existing
colony looks exactly as it did.

`VillagerWardrobe` writes the visible slots from what the bag actually holds, on every owned
tick. It is a mirror, not a step: the answer changes when an equip job brings a helmet back
or a job takes the axe out of the bag, and writing a ZDO field that already holds the same
value costs nothing. A named slot whose item the villager does not own is bared, which is
the visible signal that the outfit is not yet satisfied.

**The item never enters the creature's own inventory.** That is a safety choice rather than
a shortcut: the routine that equips a creature's best weapon runs on load through a path
this mod does not suppress, and would quietly strip a tool placed there. Truth lives in the
persisted bag; the visible slot mirrors it, and a mirror can be rebuilt.

Quality and variant are taken from the villager's own copy of the item rather than assumed.
They choose which model is shown, so an upgraded chestpiece does not look like a fresh one.
The setters' shapes were read from the shipped assembly, not the decompiled reference: three
of them take arguments the reference does not show.

The **Fetch outfit** job brings back what a villager lacks, one piece per cycle, choosing a
registered container that holds it. What to fetch comes from the outfit rather than the
job's item filters, so one job serves a colony whose villagers are dressed differently.

## AI and inventory rules

Villagers run synchronously from the vanilla `MonsterAI.UpdateAI(float)` fixed tick and
only on the current ZDO owner. There are no behavior tasks, async loops, or coroutines.
Movement goes through `VillagerMovement`, which distinguishes arrival from path failure.

The carried bag is a collider-less child `Container` whose root is the villager ZNetView.
That reuses vanilla inventory serialization without adding a second interactable. Ground
items use the `ItemDrop` registry and ownership request contract; containers save only
after ownership-safe Inventory operations.

## Persistent identity

Modern chunked saves assign new runtime ZDOIDs when loading. A raw `ZDOID` is therefore an
address for the current process, not durable identity. Kukolony assigns a random stable
token on each colony-owned ZDO and resolves it through `ZDOExtraData`'s indexed string
registry. Runtime IDs are cached and validated per `ZDOMan` instance. This applies to
members, structure targets, job/preset exact targets, colony back-pointers, and active job
targets.
