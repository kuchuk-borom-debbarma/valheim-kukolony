# Jobs and queues

A job is **named work** owned by a colony: haul loose items, transfer between containers,
fuel fireplaces, operate smelters and kilns, operate cooking stations, operate fermenters,
collect beehives. Each is one small state machine with one settings screen. A player picks
which work a job does, configures it, and orders villagers' queues; there is no pipeline to
assemble and no piece list to get wrong.

Jobs used to be pipelines built from pieces, and that is gone. It was genuinely modular, but
composing nine steps and satisfying an ordering contract was the wrong way to ask for "put
the wood in that chest", and every piece carried a duplicate of settings the job already
had. Saved pipelines are read past on load; a job keeps its settings and gets the sequence
its type implies.

## How a job runs

Each job is one file implementing `IColonyWork`, registered in `WorkRegistry` by type. What
the job does is split in two:

- **Deciding** — `Next(state, facts)` is pure: an enum and six booleans, no Unity and no
  colony types. This is why a whole work cycle is verified in the Unity-free project in
  about a second rather than only inside a four-minute game run.
- **Doing** — the engine performs the chosen action against the world, and records where the
  villager got to on its ZDO.

Almost every job has the same shape, `FetchAndDeliver`: choose something, walk to it, take
from it, choose where the result goes, walk there, hand it over. Hauling, transferring and
feeding a station share it and differ only in what those steps mean. The four station jobs
are one class configured four ways, because what a station does with what it is handed is
still decided by probing the station, not by the job.

Tapping a beehive is the exception, and it earns its own transitions: the honey is a
separate object that does not exist when the villager acts, so one cycle is two journeys —
out to the hive, then out to what fell from it. Both are "collecting", so a step carries the
state it was decided from and the job reads that to tell them apart.

**Facts outrank the recorded state.** A villager that reloads holding something delivers it
rather than fetching a second load; one whose target was taken by somebody else goes back to
choosing. The state says where it got to; the world says what is still true, and the world
wins. That property is what repairs an interrupted cycle, and each job's tests cover
resuming from the middle as well as running from the start.

## Settings

> **Superseded.** This file describes the predecessor design — `IColonyWork`, `WorkRegistry`,
> beehive jobs — and is kept for the reasoning, not as a description of the code. Settings about
> a *structure* now live on the structure; see `docs/structure-registry.md` and
> `docs/job-catalog.md`.

Every setting lives on the job, once: item filters, target mode and exact structure IDs,
source and destination containers, stock threshold, execution count, reservations, search
radius, movement stop distance, and whether the result goes into a container or on the
ground as a pile.

Choosing a destination checks that it can take the load. Picking the first eligible chest
and discovering it is full on arrival fails the job after a walk, which is a worse answer
than choosing another one.

Fetching an outfit is the one job whose destination is the villager itself, so it ends when
the item is in the bag rather than when it has been carried somewhere. Putting it on is not
a step: what a villager shows is a mirror of what it holds. See
[npc-design.md](npc-design.md).

## Gathering

Chopping is the first job whose targets are not registered structures, and four things shape
it.

**An untouched tree is unworkable by everyone.** Damage is routed to whoever owns the
object, and a tree the world generated has no owner at all, so every peer decides the blow
is somebody else's business and drops it. The villager swings, the health does not move, and
nothing anywhere reports a problem. Ownership is claimed first and the blow waits a tick.

**A tree does not produce wood.** Felling it leaves a log — a separate object, which has to
be cut up in a second pass before any wood exists. Logs are chosen before standing trees so
a colony finishes what it started; one that kept felling and never cut up would look busy and
fill no chests. The wood ends on the ground, where hauling picks it up: chopping never
learns to carry.

**There is no registry of trees.** The game keeps instance lists for items and creatures, not
for scenery, so finding one means looking through everything loaded — affordable once every
few seconds for a colony, not once per villager per tick. `ColonyResources` caches it, and
`ResourceIndex` classifies prefabs by component so the scan itself is an integer compare.

**Keep-alive deliberately skips trees**, so off-screen a villager would pick a tree, walk to
it and wait forever, while working perfectly every time anyone came to look. Trees are now
loaded in kept zones — but only for colonies that actually gather, declared by the job
itself, because trees are the most numerous thing in the world and loading them everywhere is
exactly the cost that allowlist exists to avoid.

A blow that lands and a blow the game quietly discarded look identical from outside, so
health before and after is read every time. A tool that cannot bite says so and the job
stops, rather than swinging forever at something it will never cut.

A job that needs a tool says so, and refuses to start without it rather than working by
fiat. Tools are classified by the damage they can do — an axe chops, a pickaxe mines —
because axes and pickaxes are weapons in the game's own taxonomy and only hammers and hoes
are "tools".

## Queue semantics

A villager stores an ordered list of colony job IDs and independently persists:

- queue position;
- consumed attempt count for the current entry;
- active target stable identity plus its current runtime ZDOID;
- runtime phase and progress;
- active item metadata.

`Completed` and `Failed` each consume one configured count. When count is exhausted the
queue advances. `Skipped` means no work is currently useful—no eligible target, missing
input, or stock already at its limit—and consumes no count before yielding to the next
entry. The entry after the final one is the first entry. Missing job IDs are bypassed
without stalling the villager.

A limit is a destination-stock threshold, not an amount to move. Work resumes whenever the
matching destination stock drops below the configured number.

## Presets

A portable preset clones a job's settings but clears all ZDO-specific targets,
source/destination IDs, and selected/ignored IDs. A colony-local preset retains those exact
IDs. Applying either creates a new job ID, so a preset never aliases an existing mutable
configuration.

## Runtime safety

Exclusive targets can be reserved through villagers' persisted active-target fields.
Station operations use verified vanilla RPCs after checking compatibility/capacity.
Containers are claimed, changed through Inventory, and saved through Container. Raw
internal station ZDO keys are forbidden.


## Letting go of a target: the rule that stops a livelock

Every job has arms that give up on what they were doing and return `Running` — the thing was
destroyed, the station is unusable, the chest was emptied first. Returning `Running` costs no
repetition, so the queue does not advance and the villager chooses again on the very next tick.
That is correct, and it is also how a job spins for ever while reporting that it is working.

**The rule: a job that lets go of a target must either refuse it, or be certain the chooser will
not pick it straight back up.**

In practice that means one of two things, and every arm must be one of them:

- **The chooser asks the same question the actor does.** Crafting is the clean case — the chooser
  tests `station.Usable(...)` and so does the act of crafting, so a station that refuses is never
  chosen again and letting go costs one wasted tick. Tending is the same, through
  `protocol.WhatItWants(...)` on both sides.
- **Or the arm refuses the target for a while.** `Unreachable.Refuse(villager, target, seconds)`
  is the mechanism, and every chooser already consults it — including
  `Selection.TryFindCarriedWork`, which is what makes it work for a *villager* as a target and not
  only for a thing.

**Hauling has broken this twice, both times on the same pair.** The first was the chooser accepting
a chest that takes unclaimed oddments while `Wanted` demanded the chest name the item — fixed by
aligning the predicates, and recorded in that method's own docstring. The second was collecting
from a carrier: the chooser decided a crafter was worth collecting from and `TakeFromContainer`
disagreed, and the log shows the result exactly —

```
83 × "Villager 'Olaug' is now collecting from Asketill"
83 × "Villager 'Olaug' is now it was gone"
```

— a hauler alternating between two decisions until the check gave up, with the crafter's finished
goods never filed. That arm now refuses the carrier briefly, which turns an unbounded livelock into
a bounded retry: the two can disagree for reasons that stop being true, so it must not retry
instantly and must not give up for good.

**And say which arm.** Three arms of `HaulJob.Collect` answered `"it was gone"`, so a log full of it
could not say where the villager was turning round. Distinct wording is not decoration here; it is
the difference between a diagnosis and a session spent guessing.
