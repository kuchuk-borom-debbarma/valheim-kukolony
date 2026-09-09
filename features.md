# Kukolony — current features

Kukolony's pre-release colony system is centered on a buildable Colony Hearth.

## Colony management

Press the configurable colony hotkey (default `C`) for a searchable picker, or interact
with a hearth directly. The tabbed panel provides:

- **Structures:** live-radius discovery, registration, editable names, search, capability
  filters, sorting, status, pagination, and removal.
- **Members:** paginated villagers, activity and queue detail, multi-selection, and
  multi-member job assignment. Debug spawning exists only when explicitly enabled.
- **Jobs:** seven typed defaults, editable settings and exact-target selection, plus
  portable and colony-local presets.

Only placed ZNet-backed structures can register. Loose items and NPCs cannot. A registered
record remains visible if its object is missing or moves outside the radius, but jobs will
not target it.

## Villagers and queues

Villagers use a persistent player-model NPC, name, appearance, inventory, membership,
queue position, attempt count, active target, and runtime progress. Queues are ordered and
loop after the final entry. Completed and failed attempts consume configured count;
skipped attempts consume nothing and yield to the next job. A stock limit is a destination
threshold, so work resumes after stock falls below it.

## Concrete jobs

The built-in jobs are:

1. Haul loose items to registered storage.
2. Transfer items between registered containers.
3. Fuel fireplaces.
4. Operate smelters and charcoal kilns.
5. Operate cooking stations.
6. Operate fermenters.
7. Collect beehives.

Station mutations use verified vanilla RPCs. Container changes claim ownership, use the
Inventory API, and save through Container. No executor edits raw station ZDO keys.

## Off-screen and multiplayer behavior

The server follows villager and registered-structure ZDO positions to keep a bounded set of
zones loaded. All players need the mod because AI follows ZDO ownership. Claims prevent
concurrent villagers selecting the same exclusive target; destinations remain shareable.

This is test-backed pre-release software, not a finished progression/economy mod. Farming,
planting, harvesting, woodcutting, mining, building/repair, defense, and animal work are
future catalog items.
