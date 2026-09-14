# Structure registry

Turning a thing in the world into something the settlement uses.

## What may be registered

A structure qualifies if it carries at least one component the colony understands. That list
is the whole of what a colony can do, and growing it is how the mod grows.

| Capability | Detected by | What it means |
|---|---|---|
| **Storage** | `Container` | Things can be kept here |
| **Processing** | `Smelter`, `CookingStation`, `Fermenter` | Material goes in and a product comes out — furnace, kiln, oven, fermenter, and anything else built on those |
| **Crafting** | `CraftingStation` | Things are made here by hand — workbench, forge, stonecutter, artisan and galdr tables, black forge, and the cauldron, which is why food recipes are craftable |
| **Rest** | `Bed` | One villager can sleep here |
| **Work area** | `WorkFlag` | Ground far from the hearth that the Kolony works |

**Detected by component, never by prefab name** — the rule, and the reasoning behind it, is
[components.md](components.md). A name list misses every modded chest and goes stale; a component
test does not. Components count wherever they sit on the object, including on children — Valheim
routinely splits an object's parts across child transforms, and this mod does it too — but only
when the child belongs to the *same* networked object, or a building would inherit the
capabilities of everything standing inside it.

**One capability can be several components**, and Processing is the first: a smelter, an oven and
a fermenter are unrelated classes with unrelated protocols, so the capability says *this converts
material* and the protocol to operate it is chosen by probing. Probe order matters, because an
oven is also a fireplace and a fuelled cooking station is both.

A **creature is never a structure**, and neither is a loose item, whatever components they
carry.

Cooking stations and fermenters were registerable under the previous design, were withdrawn
because nothing could use them, and return here with the Tend job that gives them meaning.
**Fireplaces and beehives are still out**, and for reasons rather than by omission: a fireplace
that burns for ever or refuses refills still accepts fuel and still reports a change, so feeding
one destroys the fuel silently; a beehive produces a world drop rather than changing what it
holds, so the work is not finished when the call returns. Each returns with the protocol that
handles it honestly.

### Capability bits are chosen, not sequential

`Storage` and `Processing` keep the bits their predecessors `Container` and `Smelter` held,
because they mean the same thing and an existing record should keep working. `Rest` took a
fresh bit rather than a retired one, or every old fireplace record would have come back as a
bed. `Crafting` took a fresh bit (256) for the same reason — a record carrying a retired bit
would otherwise come back as a crafting station, and a settlement would try to forge nails at an
old fireplace. Retired bits are masked off on read, so such a record becomes capability-less — a state
that could not previously exist and now reads as *no longer understood* rather than as a row
that matches no filter and silently does nothing.

Capabilities are cached at registration and never re-probed, so a structure registered before a
capability existed does not gain it. Re-register to pick it up.

The masking and the labels are verified in `Kukolony.DeterministicTests`, over the exact ints a
save holds, in about a second.

## Registering

`ColonyOperations.Register` is the only way a structure becomes a colony's. Both screen routes —
*register what you are looking at*, and *register something nearby* — call it, so neither can
accept what the other refuses. It returns a decision, and the words a player reads are mapped
separately, so a refusal can be reworded without touching the logic.

It refuses, and says which: out of reach, not something the colony understands, already
registered here, held by a colony that is not loaded, or unclaimable just now.

**Ownership is claimed here and nowhere else.** A durable reference can only be minted by the
peer that owns the object, so registering claims it first. Listing candidates deliberately mints
nothing: doing it there would take ownership of, and write a token onto, every chest, cart,
smelter and ship within the radius each time a player opened the list — and rewriting the record
list, which renaming one structure does, would claim all of them at once.

## Identity

A record holds a durable token, the runtime address it last resolved to, an editable name, the
prefab name, and its capability flags.

**The token outranks the address.** Loading rewrites every ZDOID, so a persisted address is only
valid for the session that wrote it. Resolution checks that the object an address finds actually
carries the token; without that check it returned live, valid, entirely unrelated objects.

Names default to what the game calls the thing — `Piece.m_name` or `Container.m_name`, localised
— never the prefab. Rows reading `charcoal_kiln(Clone)` are a bug, not a detail.

## One colony at a time

Structures carry the same back-pointer villagers do (`ColonyMembership`), so "whose is this?" is
a read rather than a search. Registering something another colony holds **moves it, and says
so**.

The move releases before it takes. A structure in two colonies' lists is strictly worse than one
in neither: both would hold its zone loaded, both would index it, both would send villagers, and
nothing would ever notice. In neither is visible and one registration from correct. If the
holding colony cannot be reached — on a client it may never have been replicated — the move
fails whole rather than half.

## Settings

Settings belong to the **record**, not the object, so a structure that goes dormant keeps its
configuration. Every component's fields live in one `StructureSettings` rather than a blob per
capability — a structure can carry more than one capability, and unused fields sitting at their
defaults cost a few bytes and remove a class of "which decoder is this" mistakes.

| Component | Settings |
|---|---|
| **Storage** | what belongs here (empty means *anything*, which is what an overflow chest is), and whether the settlement may take from it |
| **Processing** | what to keep it fuelled with, what to feed it, how full to keep it, whether villagers supply it or clear it, and whether they carry fuel, material or both |
| **Crafting** | what to make and how many of it (its *orders*), and whether worn gear may be mended here |
| **Rest** | who sleeps here |
| **Any** | whether villagers may use it at all |

**One switch for the whole structure.** *Villagers may use this* sits above the capability
panels because it governs all of them. Almost every structure has a single capability, so a
switch per capability would mostly be a second click to reach the same place — and "villagers
may use this" is a sentence a person can hold in their head. Out of service means **invisible**,
not merely unusable: a switched-off chest does not count towards what the settlement holds
either, because stock nobody can reach is stock the settlement does not have. Switching off is
not unregistering; the record keeps its name, its orders and everything else it was told.

**Orders are the stopping rule, and they live on the structure.** Each line is an item, a count,
and whether it stands or is a one-off. On a crafting station the item is what to make; on a
processing station it is what the station produces, so *"make fifty nails"* and *"keep it fed
until we have a hundred coal"* are one sentence with two subjects. A one-off latches when it is
filled, because "have we made fifty" cannot be answered by looking at the settlement — fifty
arrows made and fifty arrows fired leave no trace. **No orders means no limit**, which is the
opposite of what an empty list means elsewhere here and is the right answer for a kiln that was
registered before orders existed.

**What a station accepts is read from the prefab.** A smelter's fuel and conversion list are
asset data, identical on every instance, so the answer needs nothing loaded and there is no
cache to go stale — an outpost's kiln is configurable from home. A station with no fuel item
(a charcoal kiln) shows no fuel row at all, because a job cannot offer a setting the structure
ignores.

"How full" is a **fraction**, not a count: the cap belongs to the structure, and a count would be
wrong the moment the same setting met a different station.

**Bed assignment is ours.** Valheim's own bed owner is a `long` player id used for spawn points;
writing a villager into it would stop a player claiming that bed themselves. One villager sleeps
in one bed, so assigning someone who already has one moves them and says which bed they left.

## The settlement index

Jobs do not search the settlement, they ask it: *where does wood go*, *what wants feeding*,
*which beds are free*. The index keeps one list per capability, and `Find` searches **all** of
them — a lesson learned the hard way, since a crafting station is in none of the first three and
the craft job read "I cannot find my own station" as "my station wants nothing" and looped for
ever while reporting that it was working. With no population cap, a hundred villagers each walking every chest is
the difference between a settlement and a slideshow.

Built once from the records and reused. It rebuilds when a revision the colony bumps on write
changes — a revision rather than a timer, so an edit shows immediately and a quiet settlement
costs nothing, and read from the colony's ZDO so another peer's edit invalidates it here too.

Two chests claiming the same item is not a conflict: both are valid answers and the nearest
usable one wins.

**Capacity is part of the question**, because discovering a chest is full on arrival wastes the
walk. Capacity can only be read from a loaded container — see
[valheim-findings.md](valheim-findings.md), contents are not on the ZDO — but a colony keeps its
own registered structures loaded, so in practice it is nearly always answerable. When it is not,
the container counts as *room* rather than none: refusing to answer would cost the settlement a
destination it really had. Taking *from* a container is the opposite case, and an unreadable one
is not an answer there — a fetch with nothing at the end of it is worse than no fetch.

## Status, and removal

A record is **ready** (resolves, in reach), **out of reach** (resolves, outside the radius —
dormant, not lost), or **not found** (does not resolve).

Falling out of radius never deregisters. Walking away from an outpost must not cost its
configuration.

Removal happens on positive evidence only. `StructureReaper` drops a record when the game's own
dead list says the object was destroyed — never because it could not be found. See
[valheim-findings.md](valheim-findings.md) for why those are different, and
[ui.md](ui.md) for the manual Remove that covers everything the reaper cannot.
