# The party

## Joining and leaving

Walk up to a villager and interact. It joins the party of the player who did it. Interact again to
dismiss it. **No cap** — the limit is what you can feed and arm.

Party membership is **one player, one villager, stored on the villager's ZDO** as the player's id.
Not a list on the player: a player is not a persistent thing this mod owns, and a villager already
carries everything else about itself. It also makes the multiplayer question answer itself — two
players cannot both hold the same villager, because there is one field.

**Joining records what the villager was doing**, so that leaving can put it back. That restoration
is easy to get wrong and expensive to notice: a villager that came home and had forgotten it was a
farmer would look fine for days.

## Where following sits in the tick

```
eating  ->  following  ->  resting  ->  the job queue
```

Below eating, because a hungry villager should still eat — and it already can, since eating looks
in the bag before it looks for a chest, so a provisioned party villager feeds itself with no party
code at all. Above resting, because **resting is suspended in a party**.

## Following

A party villager stays near its player. This is not new navigation — it is the existing
`VillagerWalk` with a destination that moves.

**And a third number constrains both**: the leash can never be inside the party work radius. They
describe the same circle from opposite ends, and a leash inside the radius makes a villager fight
itself — the job sends it to the edge of its area, arriving breaches the leash, the escort drags it
back. `Following.LeashFor` opens the leash up past the work radius, with a margin, because a leash
exactly on the boundary means arriving *is* breaching.

Two distances matter and they are not the same number:

- **Leash** — beyond this it stops working and closes the gap. Work is what it does *in* the
  radius, not instead of staying in it.
- **Comfort** — inside this it does not bother re-pathing. Without a band here a party jitters,
  each villager micro-correcting every tick, which is both ugly and expensive with five of them.

**The existing travel machinery already handles the hard part.** `Journey` walks straight at a
destination and holds navmesh tiles open ahead; a player is just a destination that moves. What it
does *not* handle is a destination that is on a boat, which is why [`travel.md`](travel.md) exists.

## You are the work area

While in a party, `KolonyReach` is not what bounds the villager's work. The player's position and
a configurable radius are.

This is deliberately the **same shape as a work flag** — a centre and a radius, added to the list
of areas a job is scoped to. A flag is a place the Kolony works; a party is a place the Kolony
works that happens to be walking around. Everything downstream — `GroundSweep`, the job tables,
the claim system — already takes a list of anchors and does not care where they came from.

**Nothing is registered.** A tree inside the party radius is workable without being anybody's
property, because the party radius is not Kolony ground and a felled tree in a far meadow should
not become a structure record. This is the one place where the flag analogy stops.

## What a party villager can and cannot do

| Job | In a party | Why |
|---|---|---|
| **Chop** | Yes | Needs a tree and an axe. Both travel. |
| **Mine** | Yes | Same. |
| **Forage** | Yes | Needs nothing but ground. |
| **Haul** | Into its own bag only | Hauling means *into a registered chest*, and there are none out here. What survives is the carrying, which becomes the party abilities below. |
| **Tend** | No | Needs a registered station. |
| **Craft** | No | Needs a registered station. |
| **Farm** | No | Needs a registered field. |
| **Repair** | No | Needs a crafting station in range, which is one of the two vanilla rules the job faithfully keeps. |

Hidden jobs are **listed with the reason rather than hidden**, both in the picker that assigns
party work and by the villager itself when it gets out there. Hiding them leaves a player
wondering where tending went; a job that quietly does nothing is the failure this codebase has paid
for more than once.

**Discovery and permission are two different bounds and both had to move.** `WorkArea` decides what
a job is *allowed* to work; `GroundSweep` decides what it is ever *offered*. The player is now an
anchor in both. Changing only one would have produced a villager with a work area full of
candidates nothing ever handed it — the same silent shape as a flag planted beyond the scan radius,
which this mod has already shipped once.

## The party queue

A second job queue that applies only in a party. Its own field, its own screen section, migrating
in as empty — and an empty party queue means **"do what the party abilities say and nothing else"**,
which is a sensible default for a villager you grabbed on your way out of the gate.

Presets work on it, because presets are already a named list of jobs and there is no reason a
party queue should need a second mechanism.

## The four party abilities

These are not jobs in the queue. They are what a party villager does *as* a party member, the way
eating and resting are things it does as a villager.

### Pack mule
Takes items from a full player inventory and carries them. Hands them back on request.
**The hard part is the gesture, not the transfer** — it must be obvious which villager has your
silver, and getting it back must not require remembering which one.

### Gleaner
Picks up what is dropped near the party — loot from a fight, resources from a felled forest.
Reuses the existing hauling pickup, pointed at the party radius instead of the settlement.
**It must respect what you are trying to leave** — a gleaner that hoovers up the stack you just
dropped on purpose is worse than no gleaner.

### Supply line
When its bag fills, it leaves the party, walks home, files everything into the right chests through
the existing `SettlementIndex.WhereDoesItGo`, and walks back.
**This is the one that makes a party more than a backpack** — it turns an expedition into a supply
chain. It is also the one most likely to lose things, because it involves a long unattended journey
with a full bag, and the mod already has a travel system that has needed three rounds of work.

### Camp
Lights a fire and keeps it fuelled. Reuses the cooking work wholesale — the fireplace handling, the
fuel reserve, and the "is it actually burning" probe all exist and were measured rather than
assumed.

**Its original reason has expired and this is deliberate.** Camp was specified as "a rest point so
the party can recover away from home", and then rest was removed from parties entirely (see
[`decisions.md`](decisions.md)). What survives is a fire that *cooks* — and since satiety comes
from a food's own burn time, a camp is how a long expedition feeds itself rather than how it
sleeps. That is a smaller thing than it was, and worth re-examining before Stage 3 rather than
building to a justification that no longer holds.

A camp fire is **not registered**, for the same reason the party radius is not Kolony ground.
