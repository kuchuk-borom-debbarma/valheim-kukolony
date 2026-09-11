# Job catalogue

Every job is specified here before it is built, in two terms: **the work it does**, and **what it
needs**. One at a time.

This document replaces the seven-job catalogue of the previous design, which was deleted with the
feature layer. Nothing of that survives except its lessons.

---

# Haul — keeping the settlement tidy

The first job, and deliberately the most basic: pick things up and put them where they belong.
It is the right first job because it exercises everything the foundation built — the settlement
index answers where an item goes, structures carry the settings that decide it, villagers have
bags to carry with and beds to rest in — and because it is *visible*. You watch a settlement
tidy itself.

## What it does

A villager finds an item that is in the wrong place, carries it to the right place, and puts it
down. "Wrong place" means the ground, or a registered container that should not be holding it.

## The rule that makes it safe

**The shuffle loop is the failure mode of this entire job.** Chest A says move this to B; B says
move it back; two villagers pass a stack of wood between them forever, and every individual
decision was correct. This is not a bug to fix later — it is the thing the design has to make
impossible.

So every placement is scored:

| Score | Meaning |
|---|---|
| `2` | the container names this item explicitly |
| `1` | the container accepts anything — an overflow chest |
| `0` | the container will not take it, or is at its cap for it |
| `-1` | the ground |

**A move is legal only if the destination scores strictly higher than the source.**

That single rule buys three things at once. Items cannot oscillate, because every move raises a
bounded score. Specificity beats proximity — wood sitting in an overflow chest moves to the wood
chest, which is what "organising" means. And two chests that both name wood are equal, so nothing
moves between them and villagers do not invent work.

Caps participate naturally: a container at its cap for an item scores `0` for *further* items of
that kind, so it stops attracting them, while the items already inside it still score `2` and are
left alone.

## Settings

### On the job

| Setting | Meaning |
|---|---|
| **Which items** | An allow-list; empty means everything. One villager can haul only ore while another tidies generally. |
| **Where it works** | The colony radius by default, plus any work areas registered to the colony. |
| **What it tidies** | The ground only, or the ground and containers. Lets you run a pure sweeper. |
| **Load before delivering** | Fill the bag, or set out as soon as it has something. Full loads are efficient; eager delivery looks more alive. |

### On a container — new settings this job needs

| Setting | Meaning |
|---|---|
| **Per-item caps** | "At most 200 wood here." Chosen over percentage shares deliberately: a cap is an absolute a villager checks in one look, while a share is a relationship, and satisfying it for wood can un-satisfy it for stone. Shares can come later once this is proven stable. |
| **Take unclaimed items here** | Marks a container as the settlement's dump. A fact about the chest, so it belongs on the chest rather than on every job. |

An item nothing claims goes to the dump. If there is no dump, or it is full, or it cannot be
reached, **the villager leaves the item where it is and says so** — it does not invent a home for
it.

## The state machine

```
Choosing ──► Claiming ──► Fetching ──► Collecting ──► Delivering ──► Depositing ──► Settling
    ▲                                                                                   │
    └───────────────────────────────────────────────────────────────────────────────────┘
```

- **Choosing** — pick a destination, then the best-scoring items nearby bound for it. Nothing to
  do is a *Skipped*, not a failure.
- **Claiming** — reserve the items and the space, so two villagers cannot target one stack.
- **Fetching** — walk to the first source.
- **Collecting** — take items, up to bag capacity, with the pickup animation. Several per trip,
  not one.

  **What a trip carries is recorded, not read off the bag.** A villager's clothing lives in the
  same inventory as its cargo — equipment is a mirror of the bag — so a hauler that delivered
  "everything it is holding" files its own shirt in the chest. The manifest is a *set* of prefab
  names: with a single name, taking a second kind of item erases the first and everything picked
  up before it stops being recognised as cargo, riding around undeliverable. The name is a hint
  in the usual way — it is cargo only while the bag actually holds some.
- **Delivering** — walk to the destination.
- **Depositing** — put them in. Partial deposits are fine; what will not fit stays in the bag.
  The head of the load is re-checked against the destination before each deposit, because the
  rest of a load can be bound somewhere else entirely — and that same check is how a chest that
  filled up mid-trip is noticed.
- **Settling** — release claims and report.

**One destination per trip.** The destination is chosen first and the trip is built around it, so
every claim has an obvious owner and a destination that vanishes invalidates exactly one trip
rather than a tangle of half-committed deliveries.

**Any state falls back to Choosing when its target stops being valid**, which is most of the edge
cases below rather than a special case for each.

## Edge cases, named before building

- The item is destroyed, or the player picks it up, while the villager walks to it.
- The destination is destroyed, unloaded, or filled by another villager mid-trip.
- The bag fills mid-collection; the rest of the claim is released.
- A stack is larger than the space left — take what fits, leave the remainder.
- A container is marked *may not be taken from*; it is never a source.
- The villager's colony is destroyed mid-job — it is orphaned, and stops.
- An item lies inside another colony's radius.
- The path to an item or a chest cannot be walked.
- Ground items need **ownership claimed before removal**, or the take is silently discarded.
- A villager already carrying hauled goods **delivers them before taking new work**, including
  across a reload. Nothing accumulates in a bag.

## Energy and rest

Work is paid for. A villager spends energy **per completed action**, declared by the job rather
than assumed by the scheduler.

**A failed cycle costs energy too.** Charging only success leaves a villager that thrashes at an
unreachable chest working forever and never tiring, which is worse than one that gets tired.

**Energy is stored with a timestamp, not ticked down.** A value and the time it was written;
current energy is `stored − spent since`. That is O(1), correct for a villager nobody has watched
in ten minutes, and it does not scale with population or tick rate — which the no-population-cap
rule requires.

**Hysteresis, for the same reason caps beat shares.** Tired below one threshold, rested above a
higher one; a single threshold makes a villager flicker between working and sleeping.

Resting:

- **With a bed** — walk to it and sleep, recovering faster. This is what makes a bed worth
  building.
- **Without one** — walk to the hearth and rest there, recovering slower.
- **With neither** — rest where it stands rather than freeze.

A tired villager reports **Skipped**, not Failed: it has not failed its job, there is simply
nothing useful it can do. It consumes no repetition and yields to the next entry.

Rest is triggered by energy alone, not by nightfall — a settlement works in shifts, and nobody is
stranded until dusk.

**The sleep animation must be measured, not assumed.** Valheim's bed sleeping is written for
players and villagers are a creature rig. The name pool, the container ZDO and `VisEquipment`
have each already turned out to differ from what the decompiled reference implied.

## Saying things without shouting

A villager that cannot place an item says so once, then stays quiet until the situation changes —
the way path failure is already latched.

Repeats are **coalesced rather than repeated**: identical messages collapse into one line
carrying a count and a span, so a settlement with no space reports *"nowhere to put Wood (47
times in the last two minutes)"* rather than the same sentence at 20Hz. Built as a shared utility,
because every job after this one will want it.

## Done when

You drop a pile of wood and stone in the settlement, and villagers put each in the chest that
asked for it. Wood sitting in an overflow chest migrates to the wood chest, and stops. A chest at
its cap stops attracting more. An item nothing wants goes to the dump, or stays where it is and
says why. A tired villager delivers what it is carrying, walks to its bed, and sleeps.

And nothing ping-pongs — which is the one claim worth proving by leaving a settlement running and
watching it *stop* moving.

---

## What this job needs that does not exist yet

This job cannot be built alone. In dependency order:

1. **The job system** (roadmap milestone 6) — the queue, the four outcomes, claims, and resuming
   a job across a reload. No work runs without it.
2. **Work areas** — a placed structure with its own radius, registered like any other, selectable
   per job. Pulled forward from *after the foundation* deliberately, because a hauler confined to
   the colony radius is not the job that was asked for.
3. **Container caps and the dump flag** — small additions to milestone 4's storage settings.
4. **Energy and rest** — also pulled forward, and the thing that finally makes beds matter.

Each is separately verifiable, and each is worth landing on its own rather than as part of one
large change.
