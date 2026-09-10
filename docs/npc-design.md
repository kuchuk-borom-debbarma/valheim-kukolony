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
