# Structure registry

The Structures tab discovers only placed network pieces in the colony live radius. Loose
items, NPCs and non-network objects are rejected. Each record stores ZDOID, editable display
name, prefab name and cached capabilities.

Capabilities are Container, Fireplace, Smelter, Cooking Station, Fermenter and Beehive.
The picker supports text search, capability/type ordering, pagination and multi-selection.
Target modes are all eligible, selected structures, and ignore selected structures.
Missing/out-of-radius entries remain inspectable but are ineligible for execution.
