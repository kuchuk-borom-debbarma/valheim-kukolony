# Colonies

A placed Colony Hearth is the persistent ZDO-backed root for a settlement. It owns its
display name, villager IDs, structure records, configured jobs, and presets. Direct
interaction opens it; the configurable hotkey opens a searchable colony picker.

## Live radius and structures

The hearth has a configurable live radius (48m by default). Registration accepts only a
placed object with a valid ZNetView, a Piece component, at least one supported capability,
and a position inside that radius. Loose ItemDrop objects, characters/NPCs, and
non-networked scene objects are rejected.

Radius is evaluated again when a job chooses or executes a target. Moving a registered
structure out of range or unloading/deleting it does not erase the record: the Structures
tab shows it as unavailable, and execution excludes it. The player decides whether to
remove or repair the registration.

## Membership

Only villagers are members. A successful registration writes the villager ID to the
colony ZDO and the colony ID to the villager ZDO, after claiming both objects. Containers,
stations, beds, and work posts are not member kinds; structures use the registry and beds
have no assignment role.

Villagers are created from the hearth's Members tab and removed from a member's detail
view. Registration writes both directions; removal spills the villager's bag, clears both
directions, and destroys its ZDO. Because the bag persists through the villager's own ZDO,
removal cannot leave an orphaned container behind — but it must drop before destroying, or
the contents would go with it.

The colony registry can discover hearth ZDOs without instantiating them. This supports
keep-alive planning and persistence inspection while normal UI operations use a loaded
hearth instance.

## Persistence

Structure, job, and preset payloads are bounded, versioned ZPackage records stored as
base64 strings on the hearth ZDO. System defaults remain code-owned: an untouched colony
does not write copies of defaults until a user changes them. This is a breaking pre-release
schema; retired ledgers are ignored rather than migrated.
