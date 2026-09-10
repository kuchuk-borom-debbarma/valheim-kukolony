# Structure registry

Each record contains a stable persistent token, its resolved runtime ZDOID, editable
display name, prefab/type name, and cached capability
flags. Cached flags let search and configuration work while an object is unloaded; live
validity is always rechecked before targeting.

Supported flags are Container, Fireplace, Smelter, CookingStation, Fermenter, and BeeHive.
A single placed piece may expose more than one.

## Registration and validity

Discovery uses Valheim's Piece registry within the colony radius, then requires a valid
ZNetView and supported component. Characters, loose ItemDrop objects, and non-network
objects never qualify. Duplicate registration updates the record rather than adding a
second row.

A record is **ready** only when its ZDO still exists and its live position is inside the
colony radius. Missing or out-of-radius records remain named and inspectable but are not
eligible for a job.

## Picker behavior

The shared list behavior provides case-insensitive search across display and prefab names,
sorting by name/type/capability/status, capability filtering, pagination, and multi-select.
Exact-target selection reuses these records.

Target modes are:

- **All (`*`)** — every live record with the required capability.
- **Selected** — only IDs selected on the job.
- **Ignore** — every eligible record except the selected IDs.

Source and destination pickers show only registered containers. Selection never makes a
stale record executable.
