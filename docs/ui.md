# The colony screen

One surface, opened by a hotkey, through which the settlement is managed. It is a Jotunn wood
panel parented to `CustomGUIFront`, rebuilt from scratch whenever Jotunn signals that custom
GUI is available.

## Opening

`ColonyScreenHotkey` (default `C`) opens the screen on the nearest colony, from anywhere. It is
ignored while chat or the console has focus. Escape goes back one screen, and closes from the
top one.

The hotkey is read by its own component on the plugin object, **not** by the screen. Its
predecessor read the key inside the panel's own `Update`, which meant no key worked until
Jotunn's GUI event had fired at least once — the screen could not be opened until something
else had caused it to exist.

Using a placed hearth does not open the screen; it says which key does. Reach is not what
decides whether you may manage a settlement, so there is one route rather than two.

**Opening releases the mouse** (`GUIManager.BlockInput`) and closing gives it back, paired with
`OnDestroy` as well as `Close`. Valheim keeps the cursor captured for looking around, so a panel
drawn without this is visible and completely unusable — which is exactly what the old colony
picker shipped as.

## Context

On opening, the screen records what the player was looking at, through
`Core.PlayerLook.Target`. That is what will make registration possible without a held tool.
It is captured **once**, at open: the player is looking at the panel from the moment it
appears, so a live raycast would answer "nothing" by the time anything asked.

Looking at nothing is an ordinary answer, not an error. The screen still opens; it just offers
less.

## Layout is a system

Nothing is hand-placed. `ScreenLayout.cs` holds every number the screen is built from, and
there are two pieces:

- A **`Column`** hands out rows at a fixed pitch and owns paging. A screen adds every row it
  has, unconditionally, and never counts anything; the column decides which rows fall inside
  the current page and reports how many pages there turned out to be.
- A **`Row`** hands out horizontal cells left to right from a cursor.

So two controls in a row **cannot** overlap, and two rows cannot either. Not "are checked for
overlap" — cannot. The predecessor placed every control at an absolute `(x, y)`, and controls
overlapping each other and spilling off the panel onto the world behind it is what that cost.

`Panel.RowsPerPage` is derived from the geometry rather than chosen. A hand-picked row count
and a hand-picked pitch disagree the moment either changes, and the symptom is a last row drawn
over the pager.

## Widgets

One builder per kind of value, in `Widgets.cs`, so no screen invents its own control:

| Kind | Control |
|---|---|
| A flag | a button reading Yes or No |
| A number | value with − and +, clamped to its own range, formatted with its unit |
| One of a few | a button that cycles, wrapping |
| One of many | a picker screen with search and paging |
| Several of many | the same picker, multi-select, **order preserved** |
| Free text | an input field, committed on end-of-edit |

No builder takes an x coordinate, and none should ever be given one.

Multi-select keeping its order is not decoration: a fuel list is a preference order, and
re-sorting it on save would silently change what a structure burns first.

The content size fitter is deliberately off on every text element. With it on, a `Text` resizes
its own rect to fit the string, which undoes the cell it was allocated and makes the layout
audit meaningless — the rect would always fit, having been grown to.

## Navigation

A back stack. Every sub-screen returns where it came from, and each entry remembers the page
the player had turned to. `Root` replaces the whole stack, so switching top-level screens
clears sub-screens rather than stranding the player inside one whose parent is gone.

Rendering is destroy-and-rebuild, never diffing. A colony changes underneath the screen from
several directions — other players, villagers, the world — and a diff that is wrong shows stale
state convincingly.

## The structure screens

Three, reached from the colony screen:

- **Structures** — everything registered: name, what it is, status, and a way in. Paged.
- **A structure** — its name (editable), what it registered as, its status, the prefab behind
  it, and Remove. Keyed on the durable token rather than the runtime address, because the
  address is only valid while the object stays loaded and this screen outlives that.
- **Register nearby** — everything within reach that could be registered, searchable, each row
  saying what it *would become*.

Registering what the player was looking at is a row on the colony screen itself, offered only
when there is something to offer. Both routes call `ColonyOperations.Register`, so neither can
accept what the other refuses, and both report the outcome — including refusal and why.

**Remove is a footgun on a client and is worded as one.** A structure that was never replicated
to this peer reads "not found" there, identically to one that was destroyed. The button says
"Remove anyway" and the row says the structure may still exist, because a player hand-deleting a
live outpost's records is the same loss the *never infer destruction from absence* rule exists
to prevent, just routed through a person.

## The structure settings

A registered structure's screen grows a section per capability it has:

- **Storage** — what belongs here (empty means *anything*, which is what an overflow chest is)
  and whether the settlement may take from it.
- **Processing** — what to keep it fuelled with, what to feed it, and how full to keep it, shown
  as a percentage and the count it works out to.
- **Rest** — who sleeps here.

Both processing lists are read from the **prefab**, so a station that is nowhere near the player
can still be configured, and a station with no fuel item shows no fuel row at all rather than an
empty one. A screen must not offer a setting the structure ignores.

## The job screens

Jobs belong to the **colony** — named, shared, and changed only when a player edits them. The
queue lives on the **villager**, because that changes per person and must not rewrite the
settlement's record every time somebody is reassigned.

- **Jobs** — every job the settlement knows, what it does, and where it happens.
- **A job** — its name, how many times it repeats before the queue advances, which items it
  handles, whether it tidies containers, whether it fills the bag before setting out, and
  **where it works**.

"Where it works" offers the whole settlement first, then the colony's work-area flags. A work
area is still a registered structure used as a centre plus a radius — any registered thing can
serve, and older jobs pointed at a chest keep working — but the picker lists only the Kolony and
the flags, because burying the outposts among every chest, kiln and bed made the list unusable.

**Several may be chosen, and the order is kept.** The job works the first place that has anything
to do and only moves on when that place is done, so *"the near copse, then the far one"* is a
thing you can say. The whole Kolony is one of the choices and is what an unpointed job shows as
chosen, so a haul job starts out working the settlement and you add outposts to it. The picker is
where the order is visible, because it has room to list it; a summary row names the first place
and counts the rest — *"North copse +2"*.

A place that has since been destroyed is said rather than silently fallen back from: the named one
reads *"(gone)"* and the ones behind the count are tallied — *"North c… +3 (1 gone)"*. The marker
is terse, and names give up room before counts do, because the cell is nineteen characters wide
and a warning that does not fit is a warning nobody gets.

The reach row belongs to the named places. It is one number applied to each of them, and it is
hidden for a job that only works the Kolony — that reach is the hearth's, set on the hearth.

Deleting a job leaves villagers' queues alone. `QueueRunner` already bypasses a job that is no
longer defined, so a deleted job strands nobody, and rewriting every villager to remove one entry
would be a great many ZDO writes to achieve what the runner does for nothing.

A villager's **Works at** row is a multi-select whose order is kept, because a queue *is* an
order. It reads as the first job plus a count — "Haul +4" — rather than a list that would not fit
and would be truncated somewhere arbitrary.

## Presets, and handing work out

A **job** says what work is. A **preset** is a named queue — "Hauler" is *these jobs in this
order* — which is the unit you actually want to copy to many villagers. Without one, a settlement
of a hundred is assigned a hundred times by hand.

A preset holds job *identities*, not copies of their settings, so editing a job changes it for
everyone doing it. Copying settings in would make a preset a snapshot, and a settlement would
drift back into a hundred configurations by a slower route.

**Applying is a one-way copy.** A villager assigned from a preset is simply a villager with that
queue; editing the preset afterwards does not reach back, and deleting it leaves everyone's
orders alone. The alternative is a villager whose orders change because somebody edited a
template they no longer remember applying.

Assignment reaches **villagers that are not loaded**, which is most of the point — a settlement
worth assigning in bulk is spread over enough ground that some of it is always out of memory.
Orders are written to the villager's own ZDO after claiming ownership, because `ZDO.Set` ignores
its `okForNotOwner` argument and a non-owner's write is discarded on the next sync. The count
reported back is how many *actually* took it, not how many were asked: "assigned to 12" when it
was 11 is the kind of small lie that makes a player distrust the screen.

New orders start at the beginning of the queue rather than resuming at whatever position the old
ones had reached.

## The villager screens

A villagers list off the colony screen, and a screen per villager: name, what they are doing,
their bed, what they wear, what they carry, and the actions — rename, choose a bed, wear
something, hand an item over, remove.

**Equipment is a mirror of the bag.** A slot offers only what the villager owns, because what is
shown is rebuilt from the bag rather than stored separately. The item never enters the
creature's own inventory: the routine that equips a creature's best weapon runs on load through
a path this mod does not suppress and would quietly strip it.

What a worn slot is *called* comes from the game's item table rather than from the bag. A
villager's rolled clothes were never bag items, so asking the bag named every villager born
dressed as wearing "something they no longer have".

**The list decodes its members once**, not once per row. `GetMembers` unpacks a packed string on
every call, so a per-row lookup would cost more the more villagers a settlement had — which is
the one shape a settlement with no population cap cannot afford.

## Settlements and villagers on the map

Every settlement carries a pin with its name and population, and every villager carries one
labelled with what they are doing. Clicking either opens it.

Settlements are pinned from the **registry** rather than from loaded instances, so a village
across the map is on the map — finding your own settlement is the thing a map is most obviously
for, and one that only appears once you are standing in it is no help at all. A settlement too
far away to be loaded says so when clicked rather than opening a screen built on state it cannot
read.

**Work areas are pinned too** — but only the structures a job is actually pointed at, named after
the job that sends people there. Every registered chest on the map would be noise; "Quarry —
Quarry haul" explains why anybody is standing in it. Point a job somewhere and the pin appears;
stop, and it goes.

**Icon says what a thing is; colour says whose it is.** Every pin a settlement owns — itself, its
work areas and its people — shares one colour, hashed from the settlement's name, so you can see
which village an outpost belongs to without reading a label. Two signals that do not interfere.

The colour is hashed with `GetStableHashCode` rather than `string.GetHashCode`, which is not
guaranteed to agree between processes — a colour that changed when you reloaded would be worse
than none. Saturation and brightness are fixed rather than hashed, because the point is telling
settlements apart on a dark map, and a hash left free would eventually choose something
unreadable.

There is a check that a settlement and a villager do not share an icon: two kinds of thing that
look identical on a map are one kind of thing as far as a player is concerned.

The game already does exactly this for other players — a pin whose position is rewritten as they
move, with `m_pinUpdateRequired` set so the map redraws — so villager pins need no patching, only
the public pin API and a component that keeps them current.

**Unloaded villagers get a pin too**, read from their ZDO. That is precisely when a player most
wants to know where somebody is, and it costs nothing: the colony already knows who its members
are, and a ZDO answers its position without instantiating anything.

**Pins are never saved.** A saved pin goes into the player's own map profile, so getting this
wrong would add one pin per villager per session to their save file, for ever — invisible while
playing and permanent. There is a check for it.

The label comes from the same function the villagers list uses, so the map and the screen can
never disagree about what somebody is doing. The map redraw walks every pin, so the position is
only rewritten when it actually changed — claiming a change twice a second for a settlement
standing still is work for nothing.

Clicks are read directly rather than patched into the map's own input, which handles them inline
and would need rewriting to hook. Reading the click ourselves is smaller and cannot break the map
for anything else.

## Saying things

`Core.Report.Say` is the one place the mod tells the player what happened, including refusal
and why. It draws on the screen's message line while the screen is open and as a centre message
over the world otherwise, and logs either way — a message drawn for two seconds during an
automated run is read by nobody.

## The screen survives its subject vanishing

A colony destroyed while the screen is open closes it, with a message. Every screen below
describes a colony that no longer exists, so keeping them would be showing a stale reference.
The old panel had no such check.

## Test seams

`ShowPageForTest`, `SearchForTest`, and the `Open`/`Push`/`Pop`/`Root` calls themselves drive
the screen into a deterministic state for the benchmark. They set the same fields the buttons
do, so what is captured is real screen output rather than a mock.

Subjects are chosen **exactly**, never by index: index-based selection depends on list ordering
and photographed the wrong subject before now.

## How the layout claim is checked

`ScreenAudit` walks the live `RectTransform` tree and asserts that every element is inside the
content column, that no two overlap, and that every string fits the cell it was given
(`preferredWidth` against the rect).

The predecessor's audit was a script that parsed the C# source for literal coordinates. It
caught real bugs, but it lived outside the repository, and a layout computed by a system has no
literals to find. Auditing what was built instead means it runs inside the gate and sees
dynamic content.

**`BrokenScreen` is the reason to believe any of it.** It is a fixture built wrong on purpose —
an element outside the column, two drawn on top of each other, a string far too long for its
cell — and the benchmark asserts the audit catches all three. It places by hand, deliberately
bypassing `Row`, because the layout system makes these faults impossible and the only way to
produce one is to do what the layout system exists to stop. An audit nobody has seen reject
anything is indistinguishable from one that inspects nothing.

Screenshot review remains part of verification and is described in
[in-game-testing.md](in-game-testing.md); the audit covers overlap and clipping, and says
nothing about whether the result reads as a settlement's control panel.
