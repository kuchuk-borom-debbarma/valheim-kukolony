# Job catalog

| Job | Primary targets | Key settings |
|---|---|---|
| Haul loose items | loose drops to containers | filters, destination, count |
| Transfer containers | registered containers | source, destination, filters, stock limit |
| Fuel fireplaces | fireplaces | fuel filters, target mode, stock limit |
| Operate smelters and kilns | smelters | input/fuel filters, target mode, limit |
| Operate cooking stations | cooking stations | food filters, target mode, limit |
| Operate fermenters | fermenters | ingredient filters, target mode, limit |
| Collect beehives | beehives to containers | destination, target mode, count |

Station mutation contracts must be confirmed by the in-game prefab/API probe before an
executor is enabled. Use vanilla RPCs and ownership-safe container writes; do not write
internal ZDO keys.

Future catalog: farming, planting, harvesting, woodcutting, mining, repair/building,
defence and animal work.
