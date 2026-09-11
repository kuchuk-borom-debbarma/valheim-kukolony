# Codebase rules

Conventions for this project. Most are not style preferences — they exist because Valheim
or Unity punishes the alternative, and each one below says which.

For the general BepInEx/Harmony/Jötunn conventions this builds on, see
[modding-basics.md](modding-basics.md).

## Layout

```
Kukolony/
├── Kukolony.cs          Plugin entry. Wiring only.
├── ModConfig.cs         Every Config.Bind, in one place.
├── Core/                Cross-cutting infrastructure. Depends on nothing of ours.
├── Villagers/           The villager: prefab, component, state, movement.
├── Patches/             Harmony patches. One patch target per file.
└── Debug/               Development aids. Config-gated, never on by default.
```

- **Namespaces mirror folders.** `Kukolony/Villagers/Villager.cs` is `Kukolony.Villagers`.
- **One public type per file**, named after the file. Small tightly-coupled types
  (an enum with its only consumer, nested patch classes) may share a file.
- **`internal` by default.** Nothing here is a library; `public` is a claim we cannot
  honour.
- **Dependencies point inward.** `Core` knows nothing about villagers. `Patches` is glue
  and holds no logic of its own.

## Never use null operators on Unity objects

```csharp
// wrong - a destroyed object passes as non-null and throws inside GetComponentFastPath
if (go.GetComponent<ItemDrop>()?.m_itemData is { } data) { }

// right
if (go.TryGetComponent(out ItemDrop drop)) { }
```

Unity overloads `==` so a destroyed object compares equal to null. `?.`, `??` and `?[]`
bypass that overload. This works for a long time and then fails in the field.

Use `TryGetComponent`, or an explicit `!= null`.

## Persistent state lives on the ZDO

Anything that must survive goes on the ZDO through a `*State` wrapper — see
`Villagers/VillagerState.cs`. C# fields are for per-tick scratch only.

Two reasons, both forced by the game: the ZDO is what Valheim saves and replicates, and
**ownership can transfer mid-behaviour**, at which point field state on the previous owner
is gone. See [multiplayer.md](multiplayer.md).

Rules for state wrappers:

- Cache key hashes in `static readonly` fields. `ZDO`'s string overloads hash on every
  call and these sit on a 20 Hz path.
- Prefix keys with `kukolony.` to stay clear of vanilla and other mods.
- Expose reads as properties, writes as explicit `Set…` methods, so "this writes to the
  save file" is visible at the call site.
- Never persist a raw ZDOID as durable identity. Chunked saves reassign IDs on load. Use
  `PersistentZdoReference` and retain the runtime ID only as a validated fast-path.

## Ownership

- **Re-check `IsOwner()` every tick.** Never cache it across steps.
- **Prefer an existing vanilla RPC** over `ClaimOwnership()`. Stations register their own
  (`Smelter`: invoke names `RPC_AddOre`, `RPC_AddFuel`) and those route to the owner for free. Run
  the prefab probe and read the component's handler registration before relying on a contract.
- Container writes only persist for the owner — `Container.OnContainerChanged` checks
  `IsOwner()` before saving.

## No async, no coroutines, for behaviour

AI runs on Valheim's fixed 0.05 s tick via `MonoUpdaters` → `BaseAI.UpdateAI`. That is a
real timestep with a real `dt`; we do not need our own scheduling.

No `async void`, no `Task.Delay`, no behaviour coroutines. The predecessor drove its AI
from `async void` and it took the game down — exceptions in `async void` are unobservable.
See [predecessor-postmortem.md](predecessor-postmortem.md).

## Movement goes through `VillagerMovement`

`BaseAI.MoveTo` returns `true` for **"stopped"**, not "arrived" — two of its four
`true` branches are pathfinding failures. `VillagerMovement.MoveTowards` returns a
three-state `MoveResult` that cannot be misread.

Nothing else in the mod calls `MoveTo`.

## World queries use registries, not physics

```csharp
Piece.GetAllPiecesInRadius(point, radius, results);
CraftingStation.FindStationsInRange(name, point, range, results);
WearNTear.GetAllInstances();
```

Never `Physics.OverlapSphere`. The predecessor scanned a 500 m sphere per worker per
second with no layer mask; it is the single biggest reason that codebase could not scale
to a colony. Registry lookups are still O(all pieces), so cache per job cycle rather than
calling per tick.

## Harmony patches

- One patch target per file, under `Patches/`, named `<Type><Method>Patch`.
- **Cheapest guard first.** A prefix on `MonsterAI.UpdateAI` runs for every creature in the
  world, 20 times a second — a `TryGetComponent` that fails fast is the whole budget.
- Patches contain no logic. They identify the case and delegate.
- **Never `UnpatchAll()` with no argument** — it unpatches every mod in the process. Do not
  unpatch on shutdown at all.

## Logging

Through `Core/Log.cs`, never `Debug.Log` or `ZLog` — those are indistinguishable from
vanilla output in `LogOutput.log`.

Levels per [modding-basics.md](modding-basics.md). Anything that can repeat on a tick must
be latched, so one occurrence logs once rather than twenty times a second.

## Configuration

All entries in `ModConfig`, bound in one batch with `SaveOnConfigSet` disabled around it.
Sections are numbered (`1 - Villagers`, `9 - Development`) because BepInEx sorts them
alphabetically.

Development aids default to off.

## Comments

Explain **why**, and cite the game behaviour that forced the decision — a reader six months
from now cannot re-derive it, and the decompiled source is not in the repo.

```csharp
// Villagers are tamed so the player never treats them as enemies. BaseAI.IsEnemy
// short-circuits on tamed state before it reaches the faction switch.
```

Not:

```csharp
// tame the villager
```

Do not comment what the code already says.

## Abstraction

Do not introduce an interface or base class until there are **two** real implementations.
The predecessor shipped twelve AI classes that each re-implemented find → walk → act,
because the shape was guessed before the second example existed.

Concrete first. Extract when the duplication is visible.

## Jobs

A job's sequencing is a pure function over an enum and booleans, and never queries Unity;
it lives in a file the deterministic project links. Performing an action is separate and
uses the shared ownership, registry, movement and inventory helpers rather than duplicating
container writes or station RPC contracts. Job records are versioned ZPackage records,
never a generic JSON bag. Adding work is a new file and a registry entry, not another case
in a switch.

## Benchmark code

Benchmark scenarios are passive and are invoked only by `ColonyBenchmarkController`.
They never auto-start, purge a world, launch processes, or terminate Valheim. Persistent
fixtures must belong to the uniquely named benchmark colony, coroutine phases must be
deadline-guarded, and new coverage follows the checklist in
[in-game-testing.md](in-game-testing.md).

## Panel layout

Nothing is placed by hand. Rows come from a `Column` at a fixed pitch; cells come from a `Row`,
allocated left to right. Two controls in a row cannot overlap and two rows cannot either, which
is why there is no longer a rule asking you to keep elements a certain distance apart - the
rule is enforced by there being no way to express the violation.

Every number lives in `Panel` (`Kukolony/Gui/ScreenLayout.cs`). Nothing outside that file may
invent a coordinate, and no widget builder takes an `x`. If a screen needs something the
widgets cannot express, add a widget rather than a coordinate.

`Panel.RowsPerPage` is derived from the geometry. Do not replace it with a chosen number: a
hand-picked row count and a hand-picked pitch disagree the moment either changes, and the
symptom is a last row drawn over the pager.

Player-facing strings come from explicit mappings; never render a raw enum value, which leaks
camelCase names like `OperateStation` into the UI. A fallback that returns a plausible name is
worse than none - an unnamed thing should render empty and fail loudly, because a `default:`
branch once made a new job display as an existing one.

Fixture names used for UI evidence must be unique across benchmark phases and short enough to
survive list truncation, so two distinct records can never render identically.

See [ui.md](ui.md) for the screen itself and for how the layout claim is checked.
