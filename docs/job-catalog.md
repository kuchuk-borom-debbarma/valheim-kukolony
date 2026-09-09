# Job catalog and job pieces

The player-facing editor composes linear pipelines from these pieces: Start, End,
Stop at stock limit, Find loose item, Select source container, Select target structure,
Move to target, Pick up loose item, Take matching item, Put carried item, and Operate
verified station. Each is configured through guided customisation fields and only accepts
compatible earlier outputs. This catalog intentionally excludes generic scripting,
branches, and loops.

Every card below is implemented as a concrete executor. Common controls include name,
count, reservations, movement distance, item filters, and eligible-target mode.

## Haul loose items to storage

- **Finds:** matching loose ItemDrop objects within search radius.
- **Target:** registered destination container, automatic or exact.
- **Settings:** filters, destination, stock limit, count, reservations, movement.
- **Result:** pickup then ownership-safe deposit. Full destination fails without deleting
  the carried item; no item or limit reached skips.

## Transfer between containers

- **Finds:** a matching item in the configured/eligible source.
- **Target:** registered source and destination containers.
- **Settings:** source, destination, filters, stock limit, count, reservations, movement.
- **Result:** one ownership-safe withdrawal and deposit.

## Fuel fireplaces

- **Finds:** compatible fuel in a registered source container.
- **Target:** live registered Fireplace pieces.
- **Settings:** source, target mode/IDs, fuel filters, count, reservations, movement.
- **Result:** validates fuel and capacity, consumes one, invokes `AddFuelAmount`.

## Operate smelters and charcoal kilns

- **Finds:** allowed ore/input or the station's fuel item.
- **Target:** live registered Smelter components, including kilns.
- **Settings:** source, target mode/IDs, input/fuel filters, count, reservations, movement.
- **Result:** checks queue/fuel capacity and conversion, then invokes `AddOre` or
  `AddFuel`.

## Operate cooking stations

- **Finds:** finished slots first, otherwise allowed raw food from storage.
- **Target:** live registered CookingStation pieces.
- **Settings:** source, target mode/IDs, food filters, count, reservations, movement.
- **Result:** invokes `RemoveDoneItem` for finished food or `AddItem` for one valid
  input. A full/busy station skips.

## Operate fermenters

- **Finds:** ready output first, otherwise an allowed base from storage.
- **Target:** live registered Fermenter pieces.
- **Settings:** source, target mode/IDs, base filters, count, reservations, movement.
- **Result:** invokes `Tap` when ready or `AddItem` when empty.

## Collect beehives

- **Finds:** live registered hives with honey ready.
- **Target:** BeeHive records selected by target mode.
- **Settings:** target mode/IDs, destination/stock limit for scheduling, count,
  reservations, movement.
- **Result:** invokes `Extract`; empty hives skip.

## Future catalog

Not implemented: **farming, planting, harvesting, woodcutting, mining, repair/building,
defense, and animal work**. These remain explicit future concrete jobs, not generic graph
nodes.
