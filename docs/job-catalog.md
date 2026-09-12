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

**A move is legal only if the destination scores strictly higher than the source** — *and* the
destination must score above `0`. Ordering alone is not enough, and the decision table caught
why: with the ground at `-1`, a chest that refuses the item scores higher than where the item is
now, so "strictly higher" on its own makes moving wood into a chest that wants no wood a legal
improvement. A refusal is a floor for destinations, not a rung on the ladder.

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

**"Load before delivering" was offered and ignored.** The setting was persisted, shown on the job
screen as a toggle, and read by nothing, so every villager delivered after a single item whichever
way it was set. *A job must not offer a setting it ignores* was written down as a trap to design
around and then walked into anyway — which is worth recording, because the setting looked like it
worked: the villager hauled, the chest filled, and only counting what it held at once said
otherwise (4 with the setting on, never more than 1 with it off).

Topping up is bound to **the trip's existing destination**, not to whatever is nearest. Picking up
the nearest thing would quietly turn one destination per trip into several, and one destination
per trip is what gives every claim an obvious owner.

### On a container — new settings this job needs

| Setting | Meaning |
|---|---|
| **Per-item caps** | "At most 200 wood here." Chosen over percentage shares deliberately: a cap is an absolute a villager checks in one look, while a share is a relationship, and satisfying it for wood can un-satisfy it for stone. Shares can come later once this is proven stable. |
| **Take unclaimed items here** | Marks a container as the settlement's dump. A fact about the chest, so it belongs on the chest rather than on every job. |

An item nothing claims goes to the dump. If there is no dump, or it is full, or it cannot be
reached, **the villager leaves the item where it is and says so** — it does not invent a home for
it. If it is already carrying the thing, it puts it down: left in the bag it rides around
forever, and a villager whose bag has filled with oddments cannot haul at all.

**The head of the load must not be able to block the rest of it.** The trip is built around the
first item the settlement will actually take, not simply the first item held. Choosing on
`carried[0]` meant one flint among the firewood parked itself at the front and the villager
reported *"nowhere to put what I am carrying"* forever, with a full load of perfectly deliverable
wood behind it.

## The state machine

```
Choosing ──► Claiming ──► Fetching ──► Collecting ──► Delivering ──► Depositing ──► Settling
    ▲                                                                                   │
    └───────────────────────────────────────────────────────────────────────────────────┘
```

- **Choosing** — pick a destination, then the best-scoring items nearby bound for it. Nothing to
  do is a *Skipped*, not a failure.
- **Claiming** — reserve the items and the space, so two villagers cannot target one stack.

  **A claim has to be visible the instant it is taken.** The claim index is a cache of the
  villagers' own recorded targets, and it was trusted for half a second on the reasoning that a
  claim taken this instant is not something another villager could have known anyway. That
  reasoning is wrong in the one case the whole mechanism exists for: villagers decide in the
  same frame, so two of them reaching for one log both read an index in which neither holds
  anything, and both walk to it. Measured at **eleven collisions in twenty-three samples**; the
  index is now invalidated by every target write, which took it to **zero**. The timer survives
  only as a backstop for a villager that stops existing without clearing its target.

  The invalidation lives inside `VillagerState.SetTarget` rather than in its callers, so no
  route to a target can forget it — and every clearing path runs through that one method.
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

  **Room is counted, not asked about.** `Inventory.CanAddItem` answers for the whole stack at
  once — free stack space plus empty slots, measured against `item.m_stack` — so a villager
  carrying fifty wood to a chest with room for twenty was told no and put down *nothing*, then
  reported the chest full and went looking for another. The taking half had always worked the
  room out properly; this half asked the yes-or-no question and believed it. Both halves now
  share one `RoomFor`, so a chest with room for part of a load is treated the same way on the
  way in as on the way out.
  The head of the load is re-checked against the destination before each deposit, because the
  rest of a load can be bound somewhere else entirely — and that same check is how a chest that
  filled up mid-trip is noticed.
- **Settling** — release claims and report.

**A chest a villager finishes with is left in order**: split stacks packed together and
contents laid out in a stable order. **Packing groups on the item's name, and an item with no
name is never grouped at all.** Anything without a drop prefab answers the empty string, so
grouping on a composed `name#quality` key put every one of them in the same group — two
unrelated items merged into one stack and the surplus deleted, silently. The guard meant to
catch this tested the composed key for length one, which it can never be: an empty name still
composes to `"#0"`. Items reach that state in ordinary play, because the field is set when an
item passes through an inventory and anything that arrived another way arrives without it. Moving the wrong things out is only half of organising,
and it costs nothing — the villager is already standing at a chest it has already claimed.
Skipped when there is nothing to gain, because rewriting a container makes it save itself and
tells every watcher it changed; a settlement that rewrote every chest it looked at would pay
for tidiness twenty times a second.

**One destination per trip.** The destination is chosen first and the trip is built around it, so
every claim has an obvious owner and a destination that vanishes invalidates exactly one trip
rather than a tangle of half-committed deliveries.

**Any state falls back to Choosing when its target stops being valid**, which is most of the edge
cases below rather than a special case for each.

## Getting there

**A destination the pathfinder refuses is not a pathfinding failure.** `BaseAI.FindPath`
requires a *complete* path, and the centre of a chest is not a point anything can stand on —
so it returns false, `MoveTo` reports "stopped" with no waypoints, and the villager never
takes a step. This reads exactly like an AI that walks into walls, and is the opposite.

Every destination is snapped onto the navmesh before anyone walks to it, and arrival is judged
against the *thing wanted* rather than the point walked to. The full measurements and the API
that does it are in [valheim-findings.md](valheim-findings.md); it applies to every job, not
just this one.

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

**The sleep animation was measured, not assumed** — and the measurement is now a log line. The
rig reports every animator parameter it has at startup, and it turns out to carry the player's
own set: `attach_bed`, `lying_down`, `attach_chair`, `emote_sit` and the rest. So a villager
lies in its bed with the game's real in-bed animation, and is placed on the bed facing the way
the bed faces rather than standing politely beside it.

A rig without one rests upright. That is a cosmetic shortfall, not a broken feature: the energy,
the walking and the recovery are identical either way, and the check says which happened.

**Recovery is counted in in-game hours**, not real seconds — four hours of sleep in a bed, ten at
the hearth, twenty on the ground. A night's rest means a night of the world's time, so a server
running long days gets long nights to match rather than villagers who wake at dawn regardless.

## Saying things without shouting

A villager that cannot place an item says so once, then stays quiet until the situation changes —
the way path failure is already latched.

Repeats are **coalesced rather than repeated**: identical messages collapse into one line
carrying a count and a span, so a settlement with no space reports *"nowhere to put Wood (47
times in the last 2 minutes)"* rather than the same sentence at 20Hz. Built as a shared utility
(`Core/Chatter`), because every job after this one wants it.

The first occurrence always goes out immediately — the moment something starts going wrong is
when a player most wants to know. Callers say the situation has changed with `Forget`, so a
problem that is fixed and recurs is news again rather than being folded into a tally that started
minutes ago. Keys are chosen by the caller: one that names the villager keeps two villagers'
complaints apart, one that does not deliberately merges them into *"the settlement has nowhere
to put Wood"*.

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
