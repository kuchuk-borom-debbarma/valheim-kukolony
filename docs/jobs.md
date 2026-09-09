# Jobs and queues

Jobs are concrete built-in configurations stored by a colony, not JSON-authored step
graphs. A villager holds an ordered list of job IDs plus its queue position, attempt count,
active target and runtime progress on its own ZDO.

Every job has typed settings: target mode (all, selected, ignore), selected structure IDs,
item filters, optional source/destination containers, stock limit and count. A stock limit
is a target-stock threshold: work resumes when stock drops below it.

Queue rules are deliberately small:

- Completed and failed attempts consume one configured count.
- Skipped work (no valid target, or already at limit) consumes nothing and yields.
- Exhaustion advances to the next entry; the final entry loops to the first.

Presets have two forms: portable settings-only presets omit structure IDs, while
colony-local presets retain exact targets. Both are versioned ZPackage records on the
colony, never files under the player config directory.
