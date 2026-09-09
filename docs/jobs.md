# Jobs and queues

Jobs are concrete built-in typed configurations owned by a colony. There is no JSON step
loader and no generic player-facing graph editor.

Shared configuration blocks cover target mode, exact structure IDs, item filters,
source/destination containers, stock threshold, execution count, reservations, search
radius, and movement stop distance. Each concrete executor interprets only the settings it
needs.

## Queue semantics

A villager stores an ordered list of colony job IDs and independently persists:

- queue position;
- consumed attempt count for the current entry;
- active target ZDOID;
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

A portable preset clones settings and filters but clears all ZDO-specific targets,
source/destination IDs, and selected/ignored IDs. A colony-local preset retains those exact
IDs. Applying either creates a new job ID, so a preset never aliases an existing mutable
configuration.

## Runtime safety

Exclusive targets can be reserved through villagers' persisted active-target fields.
Station operations use verified vanilla RPCs after checking compatibility/capacity.
Containers are claimed, changed through Inventory, and saved through Container. Raw
internal station ZDO keys are forbidden.
