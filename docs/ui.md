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
