# Jobs and queues

Jobs are editable, ordered **pipelines** owned by a colony. A pipeline is built from
guided job pieces, not JSON and not an arbitrary graph editor. Players call a piece's
typed inputs and outputs **customisation**: item filters, registered targets, source and
destination containers, stock limits, movement, and reservations.

**The pieces are what runs.** The engine walks a job's piece list and performs each step;
it does not switch on the job's type, which now only supplies a display name. So a
pipeline is a description of behaviour rather than a description of a description, and a
job that is missing a step does not do that step.

Each piece declares, in `PieceCustomisation`, which settings it reads, what it provides to
later steps, and what must already have been provided. Ordering validity is derived from
that declaration rather than written out case by case, so adding a piece kind means filling
in its contract entry.

Starter jobs are ordinary editable pipelines: haul loose items, transfer containers, fuel
fireplaces, operate smelters/kilns, operate cooking stations, operate fermenters, and
collect beehives. A player may create a blank Start → End pipeline or duplicate a starter,
then add, reorder and configure pieces from the panel.

The catalog is deliberately linear: Start, Stop-at-stock-limit, loose-item/source/target
selection, movement, pick up/take/put inventory, verified station operation, wait-for-drop,
and End. It has no player-authored loops, variables, async work, or branches; queue
semantics remain the safe retry and scheduling mechanism.

Sequencing is decided by a pure function over the piece list, a cursor, and four facts
about the world, which is why whole cycles are verified in the Unity-free project in about
a second. Facts win over the cursor: the walker skips any piece whose outcome already
holds, so a villager that reloads mid-cycle carrying something finishes the delivery
rather than fetching a second load.

Every piece carries its own item filters, container, structure scope, stock limit, amount,
search radius, stop distance, target mode and reservation flag. Each has an inherit value —
an empty list, no container, a negative number — so a piece holds only what was deliberately
changed and everything else follows the job. Without this a transfer could only move one
kind of item between two places, and a station could not tell its fuel from its input.

The job-level settings remain the defaults every piece falls back to: target mode, exact
structure IDs, item filters, source/destination containers, stock threshold, execution
count, reservations, search radius, and movement stop distance. Each concrete executor
interprets only the settings it
needs.

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

A portable preset clones the full pipeline and its settings but clears all ZDO-specific targets,
source/destination IDs, and selected/ignored IDs. A colony-local preset retains those exact
IDs. Applying either creates a new job ID, so a preset never aliases an existing mutable
configuration.

## Runtime safety

Exclusive targets can be reserved through villagers' persisted active-target fields.
Station operations use verified vanilla RPCs after checking compatibility/capacity.
Containers are claimed, changed through Inventory, and saved through Container. Raw
internal station ZDO keys are forbidden.
