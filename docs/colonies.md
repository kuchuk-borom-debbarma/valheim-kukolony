# Colonies

A colony owns the villagers, storage, workstations and homes that belong together. It
exists to answer one question the AI kept getting wrong: *which things are mine?*

## A colony has no radius

**The hearth is a ledger, not a boundary.** Its position means nothing. Members are
assigned explicitly and may be anywhere — a chest across the valley is as much a member as
one at your feet.

A radius was considered and rejected. It invents rules the player fights ("this bed is two
metres outside") and needs retuning whenever a base grows. Without one, a colony's extent
is *emergent*: wherever its members happen to be.

It still exists as a placed object, for concrete reasons rather than aesthetic ones. Colony
data has to live on a **ZDO**, and in Valheim ZDOs belong to objects. A placed marker gives
somewhere to write, findability through `GetAllZDOsWithPrefabIterative` without loading
anything, and something to interact with. The alternatives are worse: `ZoneSystem` global
keys are string-only and global rather than per-colony, and a side file would not
replicate, breaking the ownership model in [multiplayer.md](multiplayer.md).

**Not to be confused with the work post's radius**, which is "how far do I range looking
for loose wood" — a job parameter, unrelated to membership.

## What is stored

On the hearth's ZDO:

```
kukolony.colony.name        string
kukolony.colony.villagers   base64 ZPackage of ZDOIDs
kukolony.colony.containers  "
kukolony.colony.stations    "
kukolony.colony.homes       "
```

Lists go through `ZPackage.Write(ZDOID)` / `ReadZDOID()` / `GetBase64()` — the same
mechanism `Container` uses for an inventory, so it is a proven shape rather than a new one.

**Membership is bidirectional.** The colony holds the authoritative lists, which is what
lets us enumerate members *without loading them*. Each member also records
`kukolony.colony`, so "whose am I?" is one read rather than a search, and a member can
re-register itself if a list goes stale.

## Why this makes a far-away container reachable

This is the whole point, and it was a real bug before.

Every job target used to be picked from *instantiated* objects — `ItemDrop.s_instances`,
the `Piece` registry, `ZNetScene.FindInstance`. Off-screen, a bound chest outside a
villager's halo was invisible, and worse, `ResolveDestinationStep` treated "not
instantiated" as "destroyed" and quietly deposited into a different chest. Correct while a
player watched; wrong the moment they left.

Colonies fix it structurally rather than by clamping a radius:

- `ColonyRegistry` finds every colony in the world without instantiating any, and reads its
  members' positions straight off their ZDOs.
- `KeepAliveDriver` feeds those positions into the zone set alongside the villagers' own,
  so **a member's zone is held open because the colony knows about it**, not because a
  villager happens to be standing near it.
- `ResolveDestinationStep` now distinguishes *unloaded* from *destroyed* and waits rather
  than re-targeting.

Measured: a chest **140m** from the work post — far outside any villager halo — with the
player teleported **500m** away. Two wood delivered to that exact chest in 98s, 33 zones
held.

## Assignment

Villagers take an explicit home and workstation, and fall back when unassigned:

- **Home is a bed.** `VillagerState.HomeBed` holds a bed's ZDOID; the bed's position is
  read from its ZDO, so an unloaded bed still gives somewhere to walk towards. With no bed,
  a villager keeps using the position it was created at, exactly as before.
- **Work falls back within the colony.** An unassigned villager claims the nearest post
  *belonging to its colony*. Membership is a more predictable scoping rule than a distance,
  and a villager never wanders off to another settlement's post.

## The cost, and why it is shown rather than capped

With no radius, nothing bounds keep-alive cost except the zone cap. A chest assigned 5km
away genuinely holds zones open out there.

The answer is visibility, not prohibition: the colony panel shows `holding N of M zones`,
so a player who wants a scattered colony can have one and can see what it costs. The cap
remains the backstop, and it warns on the transition rather than every second.

## Traps worth remembering

- **Never hold a Unity reference to a colony member.** Off-screen, GameObjects are
  destroyed and recreated as zones cycle. An earlier version of the acceptance test held a
  `Container` reference, watched it go null, and reported a *working* haul as a failure.
  Track by ZDOID and re-resolve.
- **Prune on "the ZDO is gone", never on "it is not loaded".** They are different states,
  and confusing them would make a colony forget everything whenever nobody was nearby.
- **Claim ownership before writing membership** — both the colony's ZDO and the member's.
