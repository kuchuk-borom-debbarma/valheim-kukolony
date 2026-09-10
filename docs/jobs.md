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
