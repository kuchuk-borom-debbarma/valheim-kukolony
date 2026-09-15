# Combat

The largest new system in the mod, and the one with the most ways to go wrong quietly.

## What already exists

**Villagers are cloned from `FallenWarrior`** — a genuine `Humanoid` + `MonsterAI` creature that
can already fight. `VillagerPrefab.ClearInheritedCombatGear` strips its `m_randomSets` and
`m_randomItems` on purpose. **The rig was disarmed, not absent.** Arming it is re-enabling
something that works, which is a much better starting position than building targeting from
nothing.

**And vanilla combat is currently suppressed wholesale.** `MonsterAiTickPatch` returns `false` from
a prefix on `MonsterAI.UpdateAI`, which skips target seeking, attacking, fleeing — and, as the file
itself notes, `BaseAI.UpdateAI`'s housekeeping including **regeneration**. So today a villager
cannot fight, cannot run, and cannot heal, while being `MakeTame()`d and therefore a valid target
for everything hostile in the world.

> **This file is the riskiest edit in the feature.** It has already produced one fault where a
> destroyed component threw inside the prefix, took out `MonoUpdaters.FixedUpdate`, and stopped
> the AI tick for *every creature in the world* — six thousand stack traces and a villager that
> stood still for two minutes looking exactly like a broken job. Anything added here needs the
> same paranoia the null guard at the top of it has.

Two ways to let fighting back in, and the choice is a real one:

1. **Let vanilla `MonsterAI` run when the villager is fighting**, and take over again when it is
   not. Cheap, and inherits real Valheim combat behaviour for free. Risky: vanilla also wanders,
   seeks its own targets and flees, and those must not fight the job system.
2. **Drive attacks directly** from the villager's own tick, using `Humanoid` attack APIs, and never
   hand control back. Full control, no surprises from vanilla, and considerably more to write —
   including everything about spacing, facing and swing timing that `MonsterAI` already solves.

**Undecided.** It should be decided by measurement, not argument — see [`unknowns.md`](unknowns.md).

## Stances

Set per villager, shown on its screen. **New villagers default to Defensive.**

| Stance | Engages |
|---|---|
| **Aggressive** | Anything hostile within its work radius. |
| **Defensive** | What attacks it, what attacks its player, and what its player attacks. |
| **Passive** | Nothing. Disengages and moves toward its player. |

Defensive is the default because **a companion that starts fights you were walking past is worse
than one that does nothing**, and because the first thing anybody does with a new feature is take
it somewhere dangerous by accident.

Stance applies at home too. A Passive villager in a raid keeps working and gets eaten, which is a
legitimate thing to choose and must be possible to choose by accident only once.

## Targeting

Fighting is **a reflex, not a job** — asked on the work tick before the queue, alongside eating and
resting, and pre-empting both. The ordering that falls out:

```
fighting  ->  eating  ->  resting  ->  the job queue
```

A villager being hit has nothing useful to offer any job; one that is starving has nothing to
offer either, but it can at least still be hit. Fighting outranks everything.

**Friendly fire does not exist in either direction.** A villager cannot hurt the player and the
player cannot hurt a villager. You can swing freely in a crowded fight.

## Weapons

Fetched exactly as axes and pickaxes are, through the existing `Errand`: a villager with no weapon
walks to the nearest registered chest it may take from and takes the best one there.

**So arming a party is "put swords in a chest"**, and the flag rule holds without a new
mechanism — a chest marked *villagers may not use what is here* keeps its swords, which is exactly
what a player means by putting their own sword in one.

"Best" needs defining from asset data rather than a name list, the way every other classifier in
this mod is built (`Choppable`, `Mineable`, `Forageable` and the food ranking all read the assets).
The obvious candidate is the weapon's own damage, read off `m_shared`.

## Armour

**Worn gear protects now.** This overturns `docs/system-design.md`:

> "Wearing something is a picture, not protection, so a villager in wolf armour is dressed rather
> than armoured — this costs nothing and breaks no boundary."

That is no longer true, and the sentence must be corrected rather than left to contradict this.

**Existing villagers keep the outfit they already rolled, and it now protects them.** The rollout
was chosen with its cost stated: survivability was assigned at random, before anybody knew it would
matter, so some villagers are in padded plate and some in rags for no reason anyone chose.
**Expect this to be reported as a bug.** It is not one.

## Death and revival

A villager that loses:

1. **Drops its bag where it fell.** Everything it carried is on the ground at the death site. The
   existing removal path already drops a bag both loaded and unloaded, so the machinery is there.
2. **Is out of action for a configurable time.** This is what stops a lost fight becoming a
   respawn loop — a base being overrun runs out of defenders and *loses*, rather than feeding
   bodies into the grinder forever.
3. **Wakes at its assigned bed, or at the hearth if it has none.** `SettlementIndex.BedOf` already
   answers this, and `ResolveHome` already falls back to the spawn point.

Death must be **said loudly**. A death is the one event in this mod that a player can do nothing
about after the fact, and the whole starvation design was built around warning long before dying.
Combat cannot warn the same way — a troll is sudden — so the reporting has to carry more weight:
what died, where, and to what.
