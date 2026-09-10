# Jobs and queues

Jobs are editable, ordered **pipelines** owned by a colony. A pipeline is built from
guided job pieces, not JSON and not an arbitrary graph editor. Players call a piece's
typed inputs and outputs **customisation**: item filters, registered targets, source and
destination containers, stock limits, movement, and reservations.

Each piece advertises the customisation it requires and provides. Compatible adjacent
pieces connect automatically; the editor explains every connection and blocks saving or
assignment when a requirement is missing. Starter jobs are ordinary editable pipelines:
haul loose items, transfer containers, fuel fireplaces, operate smelters/kilns, operate
cooking stations, operate fermenters, and collect beehives. A player may create a blank
Start → End pipeline or duplicate a starter, then add/reorder guided pieces.

The first catalog is deliberately linear: Start, Stop-at-stock-limit, loose-item/source/
target selection, movement, pick up/take/put inventory, verified station operation, and
End. It has no player-authored loops, variables, async work, or branches; queue semantics
remain the safe retry and scheduling mechanism.

Shared configuration blocks cover target mode, exact structure IDs, item filters,
source/destination containers, stock threshold, execution count, reservations, search
radius, and movement stop distance. Each concrete executor interprets only the settings it
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
