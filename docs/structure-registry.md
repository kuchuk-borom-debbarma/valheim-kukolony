# Structure registry

Turning a thing in the world into something the settlement uses.

## What may be registered

A structure qualifies if it carries at least one component the colony understands. That list
is the whole of what a colony can do, and growing it is how the mod grows.

| Capability | Detected by | What it means |
|---|---|---|
| **Storage** | `Container` | Things can be kept here |
| **Processing** | `Smelter` | Furnace, smelter, charcoal kiln — and anything else built on `Smelter` |
| **Rest** | `Bed` | One villager can sleep here |

**Detected by component, never by prefab name.** A name list misses every modded chest and goes
stale; a component test does not. Components count wherever they sit on the object, including
on children — Valheim routinely splits an object's parts across child transforms, and this mod
does it too — but only when the child belongs to the *same* networked object, or a building
would inherit the capabilities of everything standing inside it.

A **creature is never a structure**, and neither is a loose item, whatever components they
carry.

Fireplaces, cooking stations, fermenters and beehives were registerable under the previous
design and are not now. Registering something nothing can use is a promise the settlement
cannot keep; each returns with the milestone that gives it meaning.

### Capability bits are chosen, not sequential

`Storage` and `Processing` keep the bits their predecessors `Container` and `Smelter` held,
because they mean the same thing and an existing record should keep working. `Rest` took a
fresh bit rather than a retired one, or every old fireplace record would have come back as a
bed. Retired bits are masked off on read, so such a record becomes capability-less — a state
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

## Status, and removal

A record is **ready** (resolves, in reach), **out of reach** (resolves, outside the radius —
dormant, not lost), or **not found** (does not resolve).

Falling out of radius never deregisters. Walking away from an outpost must not cost its
configuration.

Removal happens on positive evidence only. `StructureReaper` drops a record when the game's own
dead list says the object was destroyed — never because it could not be found. See
[valheim-findings.md](valheim-findings.md) for why those are different, and
[ui.md](ui.md) for the manual Remove that covers everything the reaper cannot.
