# Found by component, never by name

The rule this mod is built on, written down because four different parts of the codebase already
follow it and none of them says it is a rule.

**Nothing here is recognised by prefab name.** What may be registered, what a job may work on,
what stays loaded off-screen, what a thing is called on screen, and which protocol operates it are
all decided by the *components* an object carries.

## Why

Valheim ships hundreds of prefabs and mods ship more. A name list is incomplete the day it is
written and silently incomplete for ever after; a component test is right for everything that has
ever been built on that component, including things that do not exist yet.

The failure mode of a name list is the reason this matters. It is not an error anybody sees — it
is a modded chest the settlement politely ignores, a new oven no villager will touch, and a player
who concludes the mod is broken. Nothing logs, nothing throws, and the settlement looks like it is
working.

## Where it already holds

This describes the code, not an aspiration:

| Question | Where | Test |
|---|---|---|
| May this be registered? | `Colonies/StructureRegistry.TryCapabilities` | `Container`, `Bed`, `WorkFlag`, and a station via `StationProbe` |
| Which station is this, and how is it worked? | `Colonies/Stations/StationProbe` | `CookingStation`, `Fermenter`, `Smelter` |
| What stays loaded off-screen? | `KeepAlive/LoadAllowlist.Matters` | `Piece`, `Container`, `CraftingStation`, `Smelter`, `CookingStation`, `Fermenter`, `Fireplace`, `TerrainComp`, `ItemDrop`, and what an axe can cut |
| What can an axe cut? | `Resources/Choppable` | `TreeBase`, `TreeLog`, `Destructible` — compiled into a prefab-hash set, so the scan is an integer compare rather than a `GetComponent` per candidate |
| What is this called? | `StructureRegistry.DisplayName` | `Piece.m_name` or `Container.m_name`, localised. The cleaned prefab name is the fallback when a thing carries neither — honest, and the reason rows do not read `charcoal_kiln(Clone)` |

## The four questions, and each is a component question

| Question | Answered by |
|---|---|
| **May this be registered?** | Does it carry a component the colony understands |
| **What is it for?** | Which components it carries — a structure is a bag of capabilities, not a kind of building |
| **What will it accept?** | The component's own asset data — `m_fuelItem`, `m_conversion` — read off the **prefab**, so an unloaded station three hundred metres away still answers |
| **How is it operated?** | A protocol chosen by probing the object, because the game provides no shared interface to operate one through |

## One predicate, every surface

A capability should have exactly **one** function that decides whether an object has it, and every
surface should ask that function: registration, the *register something nearby* list, a job's
target picker, and the settlement index.

Two surfaces with two tests drift apart, and the symptom is ugly: a structure a player can register
that no job will ever touch, or one a job wants that cannot be registered.

**Processing holds to this; Storage does not yet.** Every station question goes through
`StationProbe`. A container, by contrast, is decided at registration by the ZNetView-scoped test in
`StructureRegistry`, while half a dozen readers — `StructureInventory`, `Selection`, `HaulJob`,
`TendJob` — reach for `GetComponentInChildren<Container>` directly. Those readers already hold a
registered record and are fetching the component off the object that record names, so the looser
test is harmless *there* — but the guarantee is a convention in that half of the code and an
invariant only in this half. Written down as it is rather than as it ought to be, because a
doctrine that overstates is worse than none.

## Five consequences, each already paid for once

1. **Probe order matters, because objects carry several components.** An oven is a
   `CookingStation`. A hearth is a `Fireplace`. A windmill is a `Smelter`. A fuelled cooking
   station is both. So the more specific component is asked first — `StationProbe` asks
   `CookingStation`, then `Fermenter`, then `Smelter` — and a component we have no protocol for
   is not asked at all. `Fireplace` is in that second group today: it is genuinely feedable, and
   one that burns for ever or refuses refills still accepts fuel and still reports a change, so
   feeding one destroys the fuel silently. When it gets a protocol it goes **last** in the order,
   precisely because it is the component most often present alongside something else.

2. **Children count, but only this object's children.** Valheim routinely splits an object's parts
   across child transforms — a cart's container lives there — and this mod does the same thing: a
   villager's bag is a `Container` on a child. But a child belonging to a *different* `ZNetView`
   does not count, or a longhouse inherits the capabilities of everything standing inside it.

3. **Prefab hashes are per-session.** Any set built from `ZNetScene` is rebuilt per world, because
   mods register their own prefabs and the hashes are not stable across installs.

4. **A creature is never a structure, and neither is a loose item**, whatever components they
   carry. That exclusion is explicit rather than emergent.

5. **Asset data can still lie at the instance.** The prefab says what a station accepts; the
   station is still asked — `IsItemAllowed` — before anything is handed to it. A record can outlive
   the asset data it was made from.

## What this guarantees

A structure this mod has never heard of — a modded kiln, an oven from a content pack, a station
Valheim adds next year — is registerable, configurable and workable **the day it is installed**,
with no change here, provided it is built on a component we already have a protocol for.

That is the entire return on refusing to write a name list, and it is why this is a doctrine
rather than a convention.

## What it costs, honestly

A component test can admit more than was meant. The chop classifier admits any `Destructible` an
axe is not outright immune to, which is most scenery as well as stumps and bushes — so a kept zone
instantiates its undergrowth, and a chopping job could flatten decorations nobody asked it to.

Breadth is the price of not maintaining a name list. **The mitigation is a setting** — *chop
undergrowth* is opt-in — never a name list smuggled back in through the side door.

## Adding a capability

A new capability is three things: a component probe, a settings block, and a protocol for
operating it.

Never a `switch` on prefab name. Never a `default:` branch that guesses — a fallback returning a
plausible name once made a newly added job display on screen as an existing one, which is worse
than rendering nothing, because nothing fails loudly.

See [structure-registry.md](structure-registry.md) for what is registerable today and
[job-catalog.md](job-catalog.md) for the jobs built on it.
