# What must be measured before this is built

This mod's method is to **probe rather than reason from code**, and it keeps being right. Measuring
found the `RPC_Pick` arity that silently did nothing, the plant grace period that answered "healthy"
to everything, the fireplace state that meant the opposite of what the code assumed, and the
630-vs-170 scenery split. Reasoning from names nearly shipped a villager eating through
`Humanoid.ConsumeItem`, which is eleven bytes that do nothing.

So: none of the following is a design question. They are facts about Valheim that nobody has read
yet, and the design branches on them.

They are listed in order of how much they would cost to discover late.

---

## 1. Do hostiles spawn and tick in a zone with no player in it?

**The largest unknown in the feature.** Settlement defence assumes a raid can happen at an
unattended base. The keep-alive holds the zones open, but "loaded" and "the spawn system considers
this somewhere events happen" are not the same claim, and Valheim's raid system is explicitly built
around players.

- If hostiles do not spawn off-screen, **off-screen defence has nothing to defend against**, and
  the decision to let villagers die while you sleep becomes moot.
- If they do, every other question about off-screen combat becomes real.

*How:* hold a zone open with no player near it, watch for spawns, and read what the spawner
actually keys on.

## 2. Does damage apply to something nobody is observing?

Health lives on the ZDO, which is promising. But the path from a swing to a health write runs
through `Character`/`HitData` code that normally has an observer. If it does not work off-screen,
combat is a thing that only happens where somebody is standing, and that has to be known before
anything is built on top of it.

## 3. Can `FallenWarrior` swim?

`Character.m_canSwim` is per-prefab asset data and cannot be read from the managed assembly. It
decides whether "swims if it can, recovered if not" resolves to swimming or to a recovery net — and
if it cannot swim, a villager that goes over the side sinks, so the net is not optional.

Also read `m_swimSpeed` and `m_swimDepth` while there.

## 4. Vanilla `MonsterAI`, or drive attacks directly?

The one genuinely open *design* question, and it should be settled by measurement rather than
argument:

- Let `MonsterAI` run while fighting and take over again after — cheap, inherits real combat
  behaviour, but vanilla also wanders, picks its own targets and flees, and those must not fight
  the job system.
- Drive attacks from the villager's own tick through `Humanoid` — full control, and everything
  about spacing, facing and swing timing has to be rewritten.

*How:* re-enable the prefix for one villager holding a weapon and watch what vanilla actually does
with a tamed `FallenWarrior`. If it fights sensibly and returns control cleanly, option one. The
answer is an afternoon's measurement and it decides the shape of the whole system.

## 5. What does a portal actually refuse, and where?

Villagers are tamed and portals refuse tamed creatures. Find the check, find whether it is on the
teleport path or the interact path, and find where the item restriction is applied — because the
same code is what a config switch would reuse to apply the ore rule to a villager's bag.

## 6. Does a ZDO's sector keep up with a villager on a fast longship?

Valheim decides what exists by ZDO sector, and this mod has already paid for that lesson once — a
villager moved without its record told being "a body the world loses track of", three hundred
metres into a journey. A boat moves faster than a villager ever has.

## 7. What does a party actually cost?

There is no cap on party size, by decision. Nothing in the design bounds what five, or twenty,
villagers following one player costs — in pathing, in combat target scans, or in keep-alive zones
(which they keep, at sea, by decision).

*How:* build the party with one villager, then measure with ten before building anything else on
top of it. A cost discovered after four features are stacked on this is a cost that cannot be
designed away.

## 8. Does armour on a `Humanoid` villager actually reduce damage?

Armour being real assumes the game applies a `Humanoid`'s worn items to incoming damage the way it
does for a player. Villagers wear things through `VisEquipment`, which is the *visual* path. If the
damage calculation reads from somewhere else, "armour is real now" needs a different implementation
than "keep doing what we do and turn the numbers on".
