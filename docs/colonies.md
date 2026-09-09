# Colonies

A placed Colony Hearth is the ZDO-backed root for a settlement. It owns its name,
villager membership, structure registry, job configurations and colony-local presets.
Use the hearth directly, or press the configured colony-picker hotkey and search by name.

The hearth has a live registration radius (48m by default). Registering is intentionally
local: only placed, ZNet-backed structures inside this circle are eligible. Moving a
registered structure outside the circle does not delete its record; the UI marks it
out-of-radius and jobs will not target it. A destroyed/missing structure is likewise kept
visible until removed by the player.

Villagers belong to a colony and own ordered job queues. Beds and work posts are no longer
assigned, and villagers never auto-bind to a nearby post.

Persistent colony records are versioned ZPackage payloads on the hearth ZDO:
kukolony.colony.structures.v1 and kukolony.colony.jobs.v1. The old container,
station, home and JSON-job ledgers are retired pre-release data.
