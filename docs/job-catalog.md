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
| **Where it works** | The colony radius by default; several work areas may be chosen and are tried in order, the Kolony among them. |
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

  **A claim ages against being stuck, not against the length of the walk.** The timeout that
  stops a stuck villager holding a resource for ever was stamped once, when the target was
  taken — so a villager on any errand longer than the timeout lost its claim halfway while
  walking perfectly well, and a second villager set off for the same thing. Both of them behaved
  correctly and the settlement double-handled the log. The stamp is now refreshed while the walk
  reports progress, which leaves the timeout doing exactly the job it was added for: a villager
  getting nowhere stops refreshing and ages out as before.

  Refreshed *rarely* — once the stamp is a third of the way to expiring — because this is called
  from the walk, and a per-tick write would mark every walking villager's ZDO dirty every frame.
  One hot field written by the whole population is the contention shape a settlement with no
  population cap cannot pay for.
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

---

# Chop — the first job that produces

The second job, and the first that *makes* something. Hauling moves what already exists, so a
settlement that only hauls is a filing system rather than an economy. Chopping is the right
second job because it shares almost nothing with the first: a target that is not a registered
structure, sustained effort instead of atomic actions, a tool that has to be held, and a target
that turns into a different object halfway through.

**This job is chopping and only chopping.** Not mining, not foraging. That narrowing is what
buys precision — every target takes an axe, every one of them keeps its health in the same ZDO
field, and the settings can talk about trees instead of about "resources".

That prediction held. Mining, when it came, needed three protocols precisely because it does not
share the health field: a `MineRock` keeps a float per hit area under a runtime-hashed key and a
`MineRock5` keeps a base64 package of them. See **Mine**, below.

## What it does

A villager with an axe walks to the nearest choppable thing inside its work area and hits it
until it stops existing. Then it looks again.

That is the whole loop, and most of the job's rules fall out of it rather than being written:

- **A tree becomes a log, and the log is then the nearest choppable thing** — so "finish what
  you started" needs no rule. Nearest-wins produces it.
- **Sub-logs and stumps are handled by the same accident.** They appear where the villager is
  already standing.
- **The target vanishing is success, not failure.** A job built on hauling's fetch-and-deliver
  shape would report every felled tree as an error.

## What it does not do

**It never learns to carry.** Wood ends on the ground where it falls, and hauling collects it.
This keeps the job to one thing and reuses everything, with one consequence worth stating
rather than discovering: **the forest is usually outside the haulers' work area**, so the player
has to connect the two. The moment chopping "just carries it back", it is two jobs.

Nor does it replant — that needs seeds and a cultivator, which is a different job with a
different tool.

## The rule that makes it honest

Hauling's hazard was the shuffle loop. Chopping's is **invisible failure**: every way this job
breaks looks, from outside, like a villager standing still — and all of them work perfectly
whenever somebody is watching.

`IDestructible` is the whole shared foundation and it is two methods. Neither says anything
about tools, `Damage` returns `void`, and tool suitability is decided privately inside the
concrete class after the RPC. **A blow too weak, a blow on the wrong channel, and a blow that
landed are indistinguishable to the caller.**

So the job does not predict. It swings, and reads the health back:

| Failure | What it looks like | What makes it visible |
|---|---|---|
| Target never owned | Villager swings forever, health never moves | Ownership claimed first, blow waits a tick |
| Axe cannot bite | Swings forever, contentedly | The give-up test: said once, target released |
| Off-screen, trees unloaded | Works under observation, idles when nobody looks | Trees on the keep-alive allowlist |
| Claim expired mid-tree | Two villagers on one trunk, both correct | The claim refreshed on every landed blow |
| Target became a log | Claim released, second villager arrives | The claim handed to the log |

**Ownership first is the single worst trap.** A world-generated tree has no owner at all, so
every peer decides the blow is somebody else's business and drops it.

**The give-up test is one health read used only as a give-up test, never as a progress model.**
A stone axe does literally zero damage to birch, forever. If a target has taken several blows
and its health has not moved at all, the axe cannot bite it: say so once, release it, and do not
choose it again this session. Several blows rather than one, because the first blow at a fresh
log is *legitimately* discarded — a `TreeLog` has 0.2 s of invulnerability after it spawns and a
`Destructible` has one frame.

The health is read defaulting to the prefab's full health. **Defaulting to zero would make a
felled tree and an untouched one report the same number**, because an undamaged object has never
written the field.

## Classifying — by component, never by name

A name list would miss every modded tree and every vanilla one nobody thought to write down,
and there are dozens. So the classifier is built from `ZNetScene.m_prefabs` by component into a
prefab-hash set, which makes finding work an integer compare per candidate rather than a
`GetComponent`. It is **cleared on world unload**, because hashes are per-session once mods can
register their own.

Three kinds, and `TreeLog` is classified **before** `TreeBase`:

- **`TreeBase`** — a standing tree.
- **`TreeLog`** — a fallen trunk or a sub-log.
- **`Destructible`** — stumps and bushes.

## Settings

Every setting here reaches the engine and gets a check proving it changes behaviour. That is not
a nicety: `FillBagFirst` was persisted, shown on the job screen as a toggle, and read by
nothing — *a job must not offer a setting it ignores* was written down as a trap to design
around and then walked into anyway.

| Setting | Meaning |
|---|---|
| **What to chop** — trees / logs / undergrowth, multi-select | A crew that only clears fallen logs is a genuinely different villager from one that fells. It is also how a player says *leave my stumps alone*. |
| **Which trees** — species allow-list, empty means all | Mirrors hauling's item list exactly, including that empty means everything. *Leave the birches* is a real thing people want. |
| **Leave standing** — a count, default 0 | The anti-clear-cut rule. The scan already counts what it found, so below the threshold there is simply no work. This is what makes a woodcutter a forester. |
| **Stop when we have** — an item and a count, 0 meaning never | The terminus the job otherwise lacks. Above the line the job returns **Skipped**, consuming no repetition and yielding to the next queue entry. |
| **Where it works** | Not new — `Areas` and `WorkRadius` on `JobDefinition`, reused unchanged. Several areas are tried in order: the first wood with anything left in it is the one that gets cut, and *leave standing* is counted per area so one copse is not stripped because another is thick. |

**The order picks where to start, not where to be.** A villager stays in the wood it is already
working while that wood has anything left, and only then takes the list from the top again. Asking
the order afresh every time instead is how a villager working a far flag saw the Kolony — first in
the list — gain a single fallen branch, walked the whole way home for it, and walked back: every
decision correct, the settlement spending its day in transit.
| **Repeat** | Not new — the queue already counts targets before yielding. |

**Two stopping rules, deliberately, because they answer different questions.** *Leave standing*
is about the forest: do not strip this place. *Stop when we have* is about the settlement: we do
not need more. Neither substitutes for the other — a full woodshed beside a bare hillside is the
failure the first one prevents.

### What is deliberately not offered

- **A tool-tier cap.** The give-up test already handles a tree the axe cannot bite, and a
  setting that duplicates an automatic behaviour is a setting that will disagree with it.
- **Fall direction, or whether to protect buildings.** Safety is not a preference. The log is
  always pushed away from whoever felled it, and there is no switch.
- **Carrying the wood home.** That is hauling.

## The state machine

```
Choosing ──► Approaching ──► Chopping ──┐
    ▲                                    │
    └────────────────────────────────────┘
```

`Choosing = 0`, because an unwritten ZDO int reads as zero and so does the state left behind by
a villager that was doing another job yesterday. Landing either in "decide what to do" is always
safe.

The table is pure and lives in the Unity-free project, so all thirty-two fact combinations are
checked in about a second rather than only inside a four-minute game run. **Facts outrank the
recorded state**: a villager that reloads mid-tree re-reads the world and carries on, and one
whose tree the player felled goes back to choosing rather than obeying a target that is gone.

Having enough is checked when *choosing*, not mid-trunk — a villager that abandoned a
half-chopped tree the moment the store filled would leave it standing at half health for the
next one to start again from.

## Finding work

**There is no registry for scenery** — the game keeps instance lists for items and creatures,
not trees. Finding one means walking `ZNetScene.instance.m_instances`, which is affordable once
every few seconds *for a colony* and never once per villager per tick. The predecessor of this
whole mod died partly of `Physics.OverlapSphere(500f)` per villager per second. Never that.

The scan is cached per colony on a short refresh — long enough to be cheap, short enough that a
tree felled by hand stops being offered before a villager has walked to it. It is bounded by
`ModConfig.ResourceScanRadius` as the outer ceiling, which a job's own `WorkRadius` narrows and
cannot reach past.

Chosen **nearest to the villager**, which is what makes the fallen log the next target.

## Claims — reused, not rebuilt

`TargetClaims` already works and is proven by a paired control: zero collisions with claims on,
eleven with them off. Chop calls the same `IsClaimedByOther` and takes a target with the same
`SetTarget`. Handing the claim from a felled tree to the log it left behind is one `SetTarget`,
not a system.

**But reuse surfaces one real gap.** The claim TTL is thirty seconds and is kept alive by the
*walk* reporting progress. A villager standing still chopping is not walking, so on any tree
taking longer than the TTL the claim ages out underneath it and a second villager joins in —
both behaving correctly, the settlement double-handling one tree. So chopping refreshes the
claim on **every blow that lands**, which is the same signal the give-up test reads: a blow that
moved the health is progress, and a run of blows that moved nothing is neither progress nor a
claim worth holding.

## Edge cases, named before building

- **The target changes identity.** Tree → log → sub-logs, and separately a stump. Not every tree
  leaves a log. One tree is up to four pieces of work.
- **A felled log damages villagers unconditionally.** `ImpactEffect.m_damagePlayers` only guards
  `IsPlayer()`; there is no NPC opt-out, and *death by tree* is a real vanilla death cause. The
  chopper is safe by construction because the log is pushed away from it — but **whatever stands
  on the far side is not.**
- **A falling tree has already destroyed a benchmark chest in this repo**, surfacing two phases
  later as a persistence failure. Destructive fixtures go in their own cleared site.
- **Every blow makes the nearest player noisy** — `AddNoise(100f)` within 10 m, every hit.
- **The axe lives in the bag, and the bag is also the wardrobe.** A hauler has already once
  picked up a villager's own chestpiece and reported that nothing wanted it. The axe must be
  excluded from haulable cargo or the same bug returns in different clothes.
- The player fells the tree mid-walk; a second villager claims it first; chopping inside a ward;
  the 0.2 s log and one-frame `Destructible` invulnerabilities; log drops scattered along the
  trunk axis rather than clustered, which is a hauling reach question.

## Done when

You queue a Chop job, and a villager with an axe walks into the trees, fells one, cuts the log
it left into wood, and moves to the next — and a hauler collects the wood if you have connected
the two work areas. A stone axe against a tree it cannot cut says so once and moves on instead
of swinging forever. Two villagers never end up on the same trunk. *Leave standing* stops with
exactly that many left, and *stop when we have* stops when the woodshed is full.

And it does all of that **with nobody watching** — which is the one claim worth proving by
walking away and coming back, because every failure in the table above passes inspection.

---

# Tend — keeping the fires fed

The third job, and the first that operates something the game owns. Hauling moves what exists and
chopping makes more of it; neither *transforms* anything. A settlement that can fell a tree, file
the wood and then turn it into coal is an economy rather than a tidy woodpile.

It is also the job the foundation was already built for and never ran. Phase 1's own *done when*
says "the smelter stays fed", and it never has: `StructureCapability.Processing` registers every
`Smelter` in the game, `ProcessingOptions` reads a station's fuel, its conversions and its
capacities straight off the prefab, and the structure screen has offered *Keep fuelled with*,
*Feed it* and *Keep it N% full* since the foundation shipped. Two index queries —
`WhatWantsFeeding` and `WhereIsItKept` — were written for this job and have **zero callers**.

## What it does

A villager asks the settlement which stations are short of something, takes that something out of
a registered container, carries it to the station, and puts it in. Where a station holds its
finished product rather than dropping it, the villager takes that off too.

**A station is anything carrying a component we have a probe-verified protocol for** — today
`Smelter`, `CookingStation` and `Fermenter`. Not a list of prefabs, so a modded kiln or an oven
from a content pack works the day it is installed. The rule is [components.md](components.md); the
consequence is that adding a fourth kind of station is one adapter rather than a fourth job.

There is no shared "material in, product out" interface in the game to build this on. Those three
are unrelated classes with unrelated contracts — ore by name, fermenter items by *hash*, fuel with
no arguments at all — which is exactly why the protocol is chosen by probing the object and why
probe order matters: an oven is also a fireplace, and a fuelled cooking station is both.

**The station decides what it wants; the job decides nothing about items at all.** That is the
same division the whole design rests on — structures say what things are for, jobs say what kind
of work is done — and it is why this job's settings screen is almost empty while the work is
specific: a charcoal kiln wants wood and no fuel, a smelter wants ore and coal, and a windmill
wants barley and nothing to burn. None of that is typed by a player or hard-coded here. Fuel
versus input is decided by the station's own `m_fuelItem`.

## What it does not do

**It never carries a product.** A smelter spawns what it made on the ground at its output point;
taking food off an oven and tapping a fermenter both *produce* a world drop rather than putting
something in the villager's hands. So clearing a station is one call, and hauling files what falls
— the same division chopping uses, and the same consequence worth stating rather than discovering:
the player connects the two by registering a chest that wants coal.

Clearing is not a courtesy, either. **A cooking station full of cooked food cannot accept
anything**, so taking it off is the only move that makes progress — and food left on burns.

**Not fireplaces.** A `Fireplace` *is* feedable — it takes wood and resin — so what keeps it out is
not a missing component. One that burns for ever or refuses refills still accepts fuel and still
reports a change, so a villager feeds resin into it indefinitely and the fuel is simply destroyed.
That needs `m_infiniteFuel` and `m_canRefill` probed, and it is its own protocol.

**Not beehives.** Extracting produces a world drop rather than changing what the station holds, so
the work is not finished when the call returns: one cycle is two journeys. That is closer to
chopping than to tending.

## The rule that makes it honest

Hauling's hazard was the shuffle loop. Chopping's was invisible failure. **Tending's is feeding a
station that did not need feeding** — every call succeeds, the station reports a change, and the
material is gone. It looks like a working settlement right up until the coal runs out.

It comes in two shapes, and each needs arithmetic rather than good intentions:

| Shape | What it looks like | What makes it safe |
|---|---|---|
| Fuel outruns work | A villager keeps a stopped smelter stoked for ever | Fuel only what is queued: `min(queued × m_fuelPerProduct, m_maxFuel) − GetFuel()` |
| A no-op that reports success | The station is "fed" and nothing changed | Read `GetFuel()` / `GetQueueSize()` before and after; a station that swallows without changing is refused for a while |

And the one that costs material rather than time: **consume the carried item before submitting the
RPC, never after.** A removal that fails after the call has already handed the station a free item.

Capacity is asked of the station, never assumed — `GetFuel() >= m_maxFuel` and
`GetQueueSize() >= m_maxOre`, with `IsItemAllowed` for the input. `CanUseItems` is useless here:
it checks the local *player's* inventory.

## Settings

### On the job

| Setting | Meaning |
|---|---|
| **Where it works** | `Areas` and `WorkRadius`. Which stations count is answered by *where they stand*, not by naming them. |
| **Repeat** | The queue counts trips before yielding. |

That is the whole of it, and the shortness is the point. Six settings used to live here — which
stations, which kinds, supply or clear, fuel or material, an item allow-list and a stock target —
and every one of them described a **station**. With them on the job, a settlement that wanted one
furnace emptied and one oven filled needed two jobs and two rosters to say so.

They now live on the structures they describe. A tend job is a verb and a place.

### On the structure — where the tending is actually set up

| Setting | Meaning |
|---|---|
| **Keep fuelled with** | Offered only when the station burns something. A charcoal kiln has no fuel item at all, which is why "what fuel does this take" has to be allowed to answer "nothing". |
| **Feed it** | Which of the station's own conversions this one should be kept loaded with. |
| **Keep it N% full** | A fraction of the station's own capacity, shown as the count it works out to. Below the line there is work, above it there is none. |
| **What villagers do here** — supply / clear / both | Supplying and clearing are different work, and which one a kiln wants is a fact about that kiln. |
| **What they carry to it** — fuel / material / both | Shown only where there is a choice: a station being cleared carries nothing, and one with no fuel item has only ever taken material. |
| **Orders** — an item, a count, and whether it stands | The terminus this job otherwise lacks. Without one a kiln is kept topped up for ever and a settlement turns every log it owns into coal nobody asked for, while every individual decision is correct. The item is what the station *produces*. |
| **Villagers may use this** | One switch. Off means invisible to every job, stock included. |

**Having enough stops supplying, never clearing.** A station holding finished work still has to be
emptied whatever the stores say: an oven left full burns what is on it and then accepts nothing
ever again, and *"we have enough"* is a poor epitaph for a kitchen that set itself alight.

**Two limits, and they answer different questions.** *Keep it N% full* is about the station — do
not overfill this kiln. An *order* is about the settlement — we do not need more coal. Neither
substitutes for the other, which is the same division chopping draws between *leave standing* and
*stop when we have*.

### On the structure — the older half



| Setting | Meaning |
|---|---|
| **Keep fuelled with** | Offered only when the station burns something. A charcoal kiln has no fuel item at all, which is why "what fuel does this take" has to be allowed to answer "nothing". |
| **Feed it** | Which of the station's own conversions this one should be kept loaded with. |
| **Keep it N% full** | A fraction of `m_maxOre`, shown as the count it works out to. This is the terminus: below the line there is work, above it there is none. |

### What is deliberately not offered

- **Which prefabs count as a station.** The components answer that, and a prefab list would be
  wrong for every modded station and every one Valheim adds later.
- **What a station accepts.** The station answers that, off its own asset data. A setting here
  could only disagree with it — which is why *which items* narrows and never widens.
- **How much fuel to add.** That is arithmetic against the queue, not a preference, and a setting
  that duplicates an automatic behaviour is a setting that will eventually contradict it.

## The state machine

```
Choosing ──► Fetching ──► Collecting ──► Delivering ──► Feeding ──┐
    │                                                             │
    └──► Clearing ────────────────────────────────────────────────┤
    ▲                                                             │
    └─────────────────────────────────────────────────────────────┘
```

Fetch-and-deliver, which hauling already is — and that is the point at which the shape has **two**
implementations rather than one, so it is the first honest opportunity to extract it. Tend is
written concretely first and the extraction judged afterwards, in that order, because the
predecessor shipped twelve classes built on a shape guessed before the second example existed.

`Choosing = 0`, as in every work-state enum here: an unwritten field reads as zero and so does the
state left by a villager that was doing another job yesterday.

**Facts outrank the recorded state.** A villager that reloads carrying coal delivers it; one whose
station was destroyed goes back to choosing rather than walking to a hole in the ground.

## Edge cases, named before building

- **The station fills while the villager walks to it** — another villager, or the player. Not a
  failure: choose again, and the load is delivered to the next station that wants it.
- **Nothing in the settlement has what the station wants.** *Skipped*, and said once —
  "no coal to fetch" — not a failure that burns a repetition.
- **A load nothing wants any more.** The villager files it like a hauler would rather than carrying
  it for ever; a bag that fills with oddments cannot work at all. That failure has already been
  paid for once in this repo.
- **A station that is a `Smelter` with no fuel item** — kiln, windmill, spinning wheel. The fuel
  half must answer "nothing" rather than fetching nothing for ever.
- **Two villagers feeding one station.** `TargetClaims`, claimed on the *station* rather than on
  the chest, because the station is the scarce thing.
- **The station is in the work area and the chest is not**, or the reverse.
- **The station's zone is not loaded.** `GetFuel` and `GetQueueSize` are loaded-only questions, so
  an unreadable station is not an answer — the same rule `WhereIsItKept` already applies to chests.
- **The recorded RPC argument shape must be re-probed.** `valheim-findings.md` records
  `InvokeRPC("RPC_AddOre", prefabName, false)` as probe-verified, and the trailing bool is not
  obvious from the method it names. Verified against the running game before it is relied on,
  because a call that silently does nothing is precisely this job's failure mode.
- **Fuel and ore are cargo, and the bag is also the wardrobe.** The manifest rule from hauling
  applies unchanged.

## Done when

You register a charcoal kiln and tell it to keep itself half full of wood. A villager fetches wood
from the chest a hauler filled, feeds the kiln until it is half full, and **stops** — and does not
touch it again until it has burned some down. Coal appears on the ground and the hauler files it
in the chest that asked for coal.

And a smelter with nothing to smelt is not stoked, which is the one claim worth proving by walking
away and counting the coal afterwards.

---

# Craft — making what the settlement was told to make

Chop fells a tree, haul files the wood, tend feeds the kiln, mine breaks the ore. Craft closes
the loop: a villager
takes materials out of the settlement's chests, stands at a workbench or forge, and makes
something.

## What makes it different from every other job

**Valheim does not do any of it for us.** Smelting is a component with an RPC — a `Smelter` turns
ore into coal whether anybody is watching or not. Crafting has no component, no RPC and no
server-side path. `InventoryGui.DoCrafting` is the only implementation in the game and it is
welded to the local player: skills, DLC checks, the upgrade dialog, `Player.m_localPlayer`
throughout. So the craft itself is **ours**, and it has to reproduce vanilla's arithmetic exactly
or a villager becomes either a cheat or a thief.

The trap that shapes the code: `Inventory.RemoveItem` returns `void` and silently skips any item
whose world level is below the world's. On an NG+ world a villager would consume nothing, produce
everything, and no call would fail. So materials are counted under that same rule before anything
is removed, and measured again after — the same doctrine tending uses to prove a feed landed.

## Settings

**On the job:** where it works, and repeat. Nothing else.

**On the station:** its *orders* — what to make, how many, and whether the order stands or is a
one-off — and whether worn gear may be mended there. Which recipes a station offers is read from
the prefab, so an outpost forge can be given orders from home; recipes needing a higher station
level are listed and *marked* rather than hidden, because the level depends on extensions standing
beside it that may not be loaded.

**A station offers what a player standing at it would see.** Its own recipes, plus the ones that
need no station at all when it says it shows them (`m_showBasicRecipies`) — that second half is
what puts a stone axe on a workbench, and leaving it out meant a catalogue keyed by station name
silently dropped every hand-made thing in the game. And only recipes this player has discovered,
which is the same test their own crafting menu makes (`m_knownRecipes` against the item's shared
name). Anything already ordered stays listed whatever that says, because a setting that cannot be
unpicked is worse than one that can never be picked — and on a server the order may have been
placed by somebody who knows something this player does not. `OnlyKnownRecipes` turns the gate
off for exactly that case.

## What it does not do

**It does not upgrade, and it does not craft above quality 1.** Vanilla's upgrade path rolls a
break chance and dereferences the local player.

**It does not put what it makes away.** Finished goods stay in the crafter's bag, and hauling
collects them — first, before the ground and the chests, because a crafter with a full bag has
stopped working while a dropped item will still be there in a minute. A crafter that fills its bag
with nobody to relieve it stops and says *"bag full, waiting to be collected"*. That is the honest
end of the arrangement, not a bug, and it is why a *maintain* order only completes once a hauler
has filed the goods: a bag is not registered storage, and "the settlement holds fifty" means fifty
in chests.

**A hauler will not take what a crafter is working with.** The iron fetched for the nails is
reserved; the nails are not.

## Repair

The same job at the same station, when the station is set to allow it, and only when there is
nothing to make — crafting is what a player asked for by writing an order, while mending is what a
station offers to do with its spare time. It costs no materials, which is vanilla's rule and not a
simplification of it. The gate is vanilla's too, reproduced because `CanRepair` needs a `Player`:
the item must use durability, be repairable, and have a recipe whose repair or crafting station
shares this one's name, at a level clamped to four the way vanilla clamps it.

---

# Mine — breaking rock for what is in it

Chop, haul, tend and craft made a finished chain for wood and coal and a dead one for metal:
nothing in the settlement produced copper, tin, iron or silver, so a forge full of orders waited
on the player swinging a pickaxe.

## The one thing that is not chopping again

Everything else transfers — a tool in hand, a swing, a classifier compiled to a prefab-hash set,
work areas in order, a stock rule, ground drops that hauling files. **What is new is that the
target is not a thing, it is part of one.**

A tree is destroyed or it is not. A `MineRock5` builds a hit area per child collider, each with
its own health and its own drop roll, and the object survives until the last of them is gone — so
a silver vein is forty rocks wearing one name and spends nearly its whole life partly mined. The
state table therefore asks two questions chopping only ever had to ask as one: *is the deposit
still there* and *has it anything left to hit*.

**And the part being worked is never remembered.** Mining collapses: a deposit kills its own
unsupported parts with a synthetic tool-tier-100 structural hit, several at a time. Anything
holding an opinion about which rock it was hitting would be wrong within seconds, and wrong in the
way that looks exactly like working. The part is re-chosen every tick from the colliders that are
still active, which is the game's own record of what is left.

## Settings

| Setting | Meaning |
|---|---|
| **Which ore** — multi-select, empty means all | By what a deposit *yields*, not by which rock it is. A drop table is public on the prefab, so this is answerable with nothing loaded and covers a modded deposit that drops copper without the mod hearing of it |
| **Also break loose rock** — off by default | The `Destructible` tail. `DestructibleType` has no *Stone*, so the game cannot tell a boulder from a crate; this is opt-in for the reason *chop undergrowth* is |
| **Stop when we have** | Chopping's terminus, and mining needs it more — a forest grows back and a vein does not. The screen says so |
| **Where it works**, **Repeat** | Unchanged |

## What it does not do

**It does not carry what it breaks.** Each destroyed part drops where it stood, so a vein is a
stream of ore over minutes rather than a pile at the end — and hauling files it. One miner will
comfortably out-produce one hauler.

**It does not swing at what it cannot break.** Tool tier is checked before the blow, because
`Damage` returns void and refuses silently. A blow that lands and moves nothing is tolerated four
times — a `Destructible` ignores damage during its first frame, and a peer that has just taken
ownership can be working from a stale collider set — and then the deposit is refused for the long
period that matches *until the pickaxe changes*, said once, and the trip ends as skipped rather
than as a repetition spent on work it never did.

## Done when

A deposit comes apart part by part, what is left survives being unloaded, a pickaxe too weak is
refused and said, the ore filter leaves the wrong rock alone, loose rock is untouched until it is
asked for, the stock rule stops it, and two villagers never work one vein.

---

# Forage — picking what is already there

The third gathering job and the simplest, which is the point: chopping needs an axe, mining needs
a pickaxe and a tier good enough for the rock. Foraging needs a pair of hands. It is also the first
job that makes a **farm** mean anything to a settlement — a planted field is a field of `Pickable`s
the moment it is ripe, and the same job harvests it without knowing that is what it is doing.

## What it does

Finds something in its work area with anything on it, walks there, and picks it. What falls lands
on the ground where it stood, and hauling files it — the same division chopping and mining have,
for the same reason.

## The one thing that is not like the others

**Everywhere else in this mod, finishing means the thing stopped existing.** A felled tree, an
emptied vein, a delivered load. A picked bush is still a bush: it stands exactly where it was,
looks the same to anything that works by object identity, and grows its berries back an hour
later.

So the job asks two questions where the others ask one — *is it there* and *is there anything on
it* — and never collapses them. A job that collapsed them would either pick one bush and mime at
it for ever, or report every bush as destroyed. Both of those look like working from a distance,
which is the class of failure this mod guards hardest against, so the state machine carries a
`Ripe` fact of its own and the in-game check asserts that the villager **let go** after picking.

## Classifying — one component, and no ambiguous tail

`Pickable`, and nothing else. Unlike `Destructible` — which may be a stump or a wagon, and forced
both chopping and mining to gate a tail of scenery behind an opt-in switch — `Pickable` exists for
exactly one purpose: a person walking up and taking the thing. **So there is no "what to pick"
toggle, and its absence is deliberate rather than unfinished.**

Two kinds, told apart by `m_respawnTimeMinutes`:

| Kind | What it is | Comes back |
|---|---|---|
| **Regrows** | berries, mushrooms, thistle, dandelion | yes |
| **Once** | a grown crop, obsidian, a surtling core | no |

**An unripe crop is not classified at all**, because it is a `Plant` rather than a `Pickable` —
it becomes a different prefab when it grows. So "only harvest what is ready" costs nothing and
needs no rule.

## Settings

| Setting | Meaning |
|---|---|
| **What to gather** — multi-select, empty means all | By what a thing *yields*, read off `m_itemPrefab` and `m_extraDrops`, so it covers a modded berry without the mod hearing of it |
| **Leave alone: what does not grow back** — **off** by default | The conservation switch, and the opposite default from *loose rock* and *undergrowth*. Those gate scenery the game cannot tell from work, so the safe default is not to touch it. Nothing here is ambiguous — and the commonest thing in the finite half is a field somebody planted on purpose, so refusing it by default would mean a forage job that does not harvest the farm |
| **Stop when we have** | Reused unchanged |
| **Where it works**, **Repeat** | Unchanged |

## What it does not do

**It does not go through `Interact`.** That method credits the local player's foraging skill,
increments their profile statistics and rolls a level bonus — a villager going through it steals
the player's skill-ups on a good day and throws on a dedicated server. The job claims ownership
and invokes `RPC_Pick` directly, which is what `Interact` does at the end anyway.

**It does not count its own picks.** Ripeness is read back from the world after every reach, the
same doctrine mining uses for a blow and tending uses for a feed.

**It does not know what is dangerous.** `Pickable.m_aggravateRange` is real and readable — a
Fuling totem is a `Pickable` that calls the village when it is taken — and this job does not
consult it. That is the same answer chopping gives about a tree beside a troll: **the work area is
the control**, and a settlement whose flags reach into a Fuling village has bigger problems than
its forager. Written down rather than left to be discovered, because the first person to find it
will find it as a dead villager.

**It does not pay for a stripped clearing.** A picked bush stays in the world and stays in the
sweep, so a forager that emptied a meadow would otherwise reject the same forty candidates every
tick, for as long as it stood there. Every question in the choosing loop is answered from the
ZDO — prefab hash for what it is, one bool for whether anything is on it — so choosing costs no
scene lookups at all. Mining learned this the expensive way.

## Done when

A villager chooses something itself, picks it, lets it go once it is bare rather than reaching for
ever, and leaves alone what is outside its work area; the harvest filter narrows it and the
control raises it again; *leave what does not grow back* refuses the finite half and still takes
the rest; the stock rule stops it; and a version-7 job blob still decodes with the new settings at
their defaults.

---

# Farm — putting things in the ground

Forage harvests a field. Nothing planted one. The same hole sat under chopping: villagers have
felled trees since the second job and no villager had ever planted one. This is the job that closes
both loops, and it is the first one whose settings live on a **place you build** rather than on the
work.

## What it does

Finds a field that has asked for something, fetches the seed it costs, walks to a square nothing is
standing in, breaks the ground if it is allowed to, and plants. What grows there is somebody else's
job to pick.

## The first job that consumes

Every other job takes from the world and brings it home. This takes from the settlement and puts it
in the ground — so a villager carries seed, a settlement whose only seed is already planted is a
real state, and the job says so rather than standing in a field looking busy.

Seed is fetched by the **shared errand** — the same rule that fetches axes and pickaxes. It was
written for tools and everything in it was general except one question: *what counts as the thing*.
Sowing needed the same walk for a seed, so that question became a predicate and the walk is now
written once.

## The plant is asked, never second-guessed

`Plant.GetStatus()` and `Plant.UpdateHealth(double)` are both public, and between them they are the
entire planting rulebook: biome, cultivated ground, heat, cold, roof, and room to grow. So a
villager **places one, asks it, and takes it back out** if the answer is not `Healthy`. No copy of
seven rules to go stale the day the game changes one.

**But it has to be asked as though the plant had been in the ground a while.** `UpdateHealth` takes
the seconds since planting and gives a new plant a grace period — called with zero it answers
`Healthy` to everything, evaluating nothing. That is right for the game and fatal here, and it is
invisible in the signature. A day is past any grace a game could keep for something that takes at
most eight thousand seconds to grow.

**And the seed is spent last.** A refused square costs nothing. The alternative is a settlement
quietly eating its whole seed store on the same bad square all afternoon, which is exactly the kind
of failure that looks like working.

## Spacing, and why the pitch comes from the crop

Measured across what this game ships:

| | room it wants |
|---|---|
| every crop — carrot, turnip, onion, barley, flax, kale, oat | **0.5 m** |
| magecap | 0.8 m |
| vines | 0.5 m, plus a 1.8 m vine radius |
| birch, beech, fir, pine | **2 m** |
| oak | **3 m** |

**Six to one.** One pitch for everything would either pack saplings so tight that none of them grow
— each refusing its neighbour for room, in a field that looks perfectly planted — or scatter
carrots at a sixth of the density the ground could hold.

So a field's grid takes its pitch from **the widest thing that field grows**, not from the thing
being planted at this moment: a carrot dropped into the gap between two oaks fits, and then stops
the oak that square was for from ever growing.

## A field is shared, not claimed

Whether something is exclusive is decided by **who asks**. Hauling asks, so two villagers never
target one stack; depositing does not, so any number share a chest. A field is the second kind —
it is hundreds of squares, and the point of marking out a big one is that several people can work
it.

They keep off each other's ground by **starting their scan of the grid at different places**, taken
from each villager's own id. No claim, no store, no release path to forget. Two can still land on
one square, and that is fine: the world is the arbiter, and the loser finds it taken next tick.

## Replanting is free

The square is re-derived from the world every tick — the same doctrine mining uses for a vein's
parts. So a crop that Forage harvested leaves an empty square, and the next pass fills it. There is
no bookkeeping, no event and nothing to go stale, and *"replant what was harvested"* is not a
feature so much as a thing that could not have failed to happen.

## Settings — on the field, not on the job

| Setting | Meaning |
|---|---|
| **Grows** — a list of crops | By plant rather than by yield, because the two are not one-to-one: a carrot and a carrot seed come from different saplings and cost each other as seed. Labelled by what the build menu calls each one |
| Each crop's mode | **Fill** — as many as fit · **Keep** — this many growing, sown again whenever one is taken · **Once** — this many and then stop for good |
| **How big** | The field's own radius, on its own ZDO, clamped |
| **May break new ground** — off by default | The only setting in this mod that lets a villager do something unregistering cannot undo. Terrain is permanent; the screen says so |
| **In service** | The existing per-structure switch, free |

The **job** carries nothing but `Repeat`. That is the crafting arrangement, and it is the point of
the field being a piece.

A field is **not** exempt from the reach gate the way a work flag is. A flag is exempt because
standing beyond reach is its whole purpose; a field is somewhere the Kolony already is — and has to
be, because a `Plant` only grows while its zone is loaded and the keep-alive holds open what the
Kolony reaches. A field outside it would be a farm that never ripens and looks perfectly healthy
whenever anybody walks out to see it. An outfarm composes the other way: plant a flag, then a field
inside it.

## What it does not do

**It does not decide where a field goes.** That is the piece, and the piece is the player's.

**It does not know what is dangerous.** Same answer foraging gives: the field's edge is the control.

**It does not count what the larder holds.** A field counts what stands *in the ground*, which is a
different question from what the settlement has stored — *"keep twenty carrots growing"* is not
*"keep twenty carrots in the chest"*. That is why a field's order is its own type rather than the
station order it resembles.

## Done when

A villager fetches seed it did not have, finds a field, breaks its ground and plants in it; no two
plants land closer than the crop needs; a crop refused by bad ground leaves nothing standing and
costs no seed; *Keep* stops at its number and sows again when one is taken; and a version-4
structure blob still decodes with the field settings at their defaults.

---

# The tool errand — a rule the tool-holding jobs share

Not a job. Nothing queues it, nothing on any screen shows it, and it has no settings of its own.
It is what anybody does before starting work: if the job needs a tool and the bag has none, go and
get one.

## Why it is not a job

Written as a job it would need a queue entry per tool, per villager — and a chopper walking to a
chest for an axe would be reporting *"no axe"* the whole way, which reads as broken. It is also
the wrong shape: every job that holds a tool needs exactly this, and none of them needs it
differently.

## Where it runs

**Before the state machine, not inside it.** Both `ChopJob.Tick` and `MineJob.Tick` ask it first,
and it answers `null` when there is nothing to fetch — at which point the table gives the answer
it always gave. That is what keeps *"the settlement has no pickaxe"* an honest report instead of a
missing feature.

Two things are asked before it:

- **The stock rule.** A villager about to stand down because the store is full does not first walk
  across the settlement for a tool it will never swing.
- **Nothing else.** In particular it is not gated on there being work, because the walk to the
  chest is long enough that work can appear during it.

## Which chest

The nearest one in `SettlementIndex.WhatMayBeTidied` that holds a tool of the right kind — which
is already the list of containers the settlement may take from. **A chest switched to *villagers
may not use what is here* keeps its tools**, and that is exactly what a player means by putting
their own axe in one.

`VillagerTool.Best` decides on both sides — the chest and the bag — so a tool the errand would
fetch is always a tool the job will then find. Asking two different questions there would be a
villager fetching an axe for ever and never seeing it.

## It keeps no state

The chest is found again every tick rather than remembered. The alternative is a third set of
target fields living beside the job's own and going stale in ways only this errand would know
about; re-deciding costs one lookup against a cached snapshot, and buys the case where somebody
else took the axe first for free.

## What it does not do

- **It does not upgrade.** A villager holding a flint axe does not cross the settlement for a
  bronze one; the errand only runs when the hand is empty.
- **It does not take from a player's inventory**, or from anything that is not a registered
  container the settlement may take from.
- **It does not haul.** A bag with no room for a tool is a bag that needs emptying, which is
  hauling's business.

## Done when

A villager with an empty bag fetches a pickaxe from a registered chest and mines with it — and
does not touch the same chest while it is marked as one villagers may not take from.
