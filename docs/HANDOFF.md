# Kukolony — handoff

## Current architecture

The placed Colony Hearth is the persistent root. Its ZDO stores the colony name, villager
membership, versioned structure records, edited concrete jobs, and presets. System defaults
are code-owned. Each villager ZDO stores its queue, queue position, consumed count, active
target, runtime phase/progress, identity, home fallback, and persistent bag inventory.
Cross-ZDO links use stable tokens because chunked saves reassign runtime ZDOIDs on reload.

Structures are discovered through the Piece registry and register only when they are
network-backed, supported, and inside the colony's live radius. Records remain visible when
missing/out of range but cannot be selected for execution.

The retired work-post, bed assignment, generic member ledgers, and JSON job-definition
sources were deleted. This is intentionally a breaking pre-release schema.

## Implemented job catalog

- haul loose items to storage;
- transfer between registered containers;
- fuel fireplaces;
- operate smelters and charcoal kilns;
- operate cooking stations;
- operate fermenters;
- collect beehives.

Jobs use typed settings and a synchronous fixed-tick dispatcher. Station changes invoke
probe-verified vanilla RPCs. Container operations claim ownership, use Inventory APIs, and
call Container.Save. No executor uses raw internal ZDO station keys.

## UI

The configurable `ColonyPickerHotkey` defaults to `C`. It opens a searchable colony
picker; direct hearth interaction is the shortcut. The panel has Structures, Members, and
Jobs tabs with search/filter/sort/pagination, structure rename/removal, member detail and
multi-assignment, job editing/target selection, and portable/local preset save/application.
Villagers are added from the Members tab and removed from a member's detail view behind a
two-step confirm; removal drops the villager's bag rather than destroying it.

## Toolchain

Build the solution, not the project:

```sh
dotnet build Kukolony.sln -c Debug
```

`Environment.props` is intentionally ignored and pins both references and deployment to
`/Users/kuku/Downloads/denikson-BepInExPack_Valheim-5.4.2350/BepInExPack_Valheim`.
The installed Doorstop library must match that pack and must not carry macOS quarantine.

## Required verification

Run:

```sh
./scripts/in-game-test.sh
```

It verifies the pinned runtime, builds the solution, launches through Steam, performs the
two-run save/relaunch checks, then runs the in-game screenshot harness. Reports and PNGs
are copied to `~/Desktop/kukolony`. Debug settings are restored on exit.

The runner pins the dedicated local `KukolonyBenchmark` save. The acceptance report covers
registry eligibility/status/search/sorting, seven persisted
job configs, portable/local target behavior, queue result/count/loop semantics, actual
inventory/RPC executor calls, full and invalid targets, claims and keep-alive paired
controls, and villager runtime persistence. The screenshot run captures picker, structures,
member pagination/detail, jobs/configuration, target picker, and preset application.

## Invariants

- Persistent/replicated decisions live on the relevant ZDO.
- Only the current owner writes; shared station mutation uses vanilla RPCs.
- AI has no async/tasks and no behavior coroutines.
- World queries use game registries, not broad physics scans.
- Keep dependencies inward; shared job pieces earn helpers from demonstrated concrete jobs.
- Test/debug features remain off by default and operate only in the dedicated test world.
- Build and launch exactly through the documented solution/Steam path.

## Future catalog

Farming, planting, harvesting, woodcutting, mining, repair/building, defense, and animal
work are explicitly future concrete jobs.
