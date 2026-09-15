# The party system

A player can take villagers with them. Those villagers fight, carry, gather, and come home.

This is the largest single feature in the mod. It is the first that gives villagers **combat**,
the first that makes a **player** rather than a placed hearth the thing work is organised around,
and the first that lets a villager leave its Kolony's ground on purpose. It also overturns two
decisions that were settled in `docs/system-design.md`.

Because it is large, it is written down before it is built, in this folder, and the decisions
behind it are recorded so they are not relitigated halfway through.

## The one-paragraph version

You walk up to a villager and it joins you. From then on **you are its work area** — a radius that
travels with you, in place of the hearth and the work flags it normally answers to. It runs a
separate *party queue* of jobs, filtered to the ones that work anywhere: chopping, mining,
foraging, carrying. It fights what its **stance** tells it to fight, with a weapon it fetched from
a chest before you left, wearing armour that now actually protects it. It follows you onto boats
and through portals. It eats out of its own bag, and then out of yours. When its bag is full it
walks home, files everything, and comes back. If it dies it drops what it was carrying where it
fell and wakes up at its bed — or at the hearth if it hasn't got one — after a while.

And the same fighting works at home: **an unattended Kolony now defends itself.**

## The documents

| File | What it settles |
|---|---|
| [`decisions.md`](decisions.md) | Every decision taken, with its reason and its cost. **Read this first.** |
| [`party.md`](party.md) | Joining, following, the moving work area, the party queue, the four party abilities |
| [`combat.md`](combat.md) | Stances, targeting, weapons, armour, friendly fire, death and revival |
| [`defence.md`](defence.md) | A Kolony that fights its own raids, including while nobody is watching |
| [`travel.md`](travel.md) | Boats and portals — the hardest part, and why |
| [`unknowns.md`](unknowns.md) | What must be **measured in game** before any of this is built |
| [`build-order.md`](build-order.md) | The order to build it in, and what is shippable at each stop |

## What this changes elsewhere

- **`docs/system-design.md`** — "Wearing something is a picture, not protection" is **overturned**.
  Armour protects now.
- **`docs/npc-design.md`** — villagers acquire health that matters, gear that matters, and a way
  to die that is not starvation.
- **`docs/job-catalog.md`** — every job gains an answer to "does this work in a party".
- **`docs/off-screen-simulation.md`** — the keep-alive now has to carry combat, not just work.
- **`Kukolony/Patches/MonsterAiTickPatch.cs`** — the patch that suppresses vanilla AI entirely has
  to start letting some of it back in. **This is the single riskiest edit in the feature**: that
  file has already produced one fault that killed the AI tick for every creature in the world.
