# Colony UI

Two screens: a small colony picker and the colony management panel. Both are Jotunn wood
panels parented to `CustomGUIFront`, rebuilt from scratch whenever Jotunn signals that
custom GUI is available.

## Opening

Interacting with a placed Colony Hearth opens that colony directly; this is the primary
route. `ColonyPickerHotkey` (default `C`) opens a searchable list of loaded colonies as a
shortcut, and is ignored while chat or the console has focus. Opening the panel claims the
hearth ZDO and blocks game input until it closes.

## Panel structure

The panel is 860x680 with a title, an editable colony name, three tabs — Structures,
Members, Jobs — and Close. Switching tabs resets pagination and clears any drilled-in
selection, so a tab never reopens showing a stale member or job.

Every list paginates at four rows. Lists are built by destroying and rebuilding the content
subtree on each refresh rather than diffing, which keeps rendering honest about current
state at the cost of rebuilding a handful of rows.

## Structures tab

Search matches display name or prefab, so a renamed structure is still findable by what it
is. Sort cycles Name, Type, Capability, Status; the capability filter narrows to one kind.
Rows show the editable name, prefab, capabilities, live status, and Remove. Register nearby
adds every eligible structure currently inside the colony radius.

Records that are out of range, unloaded, or destroyed stay listed and read as unavailable
instead of disappearing. Removal is the player's decision, and a silently vanishing
registration would look like data loss.

## Members tab

Villagers only. Each row has a selection toggle, name, current activity, and Details.
Activity comes from the loaded villager when it exists and falls back to the persisted
runtime phase when it does not, so the roster stays meaningful outside loaded zones. A job
selector plus "Assign selected" applies one job to every checked villager at once.
**+ New villager** places a villager on snapped ground beside the hearth and enrols it
immediately — this is how a colony is populated, and it is not gated behind any debug flag.

Details shows the villager's queue in order with the active entry highlighted, one add
button per configured job, Clear queue, and **Remove villager**.

Removal is a two-step inline button — it re-labels to "Confirm remove?" and only acts on a
second click. It is deliberately not a modal dialog: a Valheim modal covers the panel, and
the benchmark's UI capture cannot photograph the panel behind one. The armed state is
cleared whenever the selected member changes or the tab switches, so a stale confirm can
never delete the wrong villager.

Removing a villager drops whatever it was carrying rather than destroying it. A villager
outside loaded range has no live bag to read, so its contents are decoded from its ZDO and
dropped at the hearth instead of at its own position — items move, but none are lost. A queue entry whose job no longer exists renders
as `(missing job)` rather than being hidden — the engine bypasses it, and hiding it would
make a partly broken queue look correct.

## Jobs tab

Lists configured jobs with which work each does, execution count, and target mode. New job
creates one; Configure opens the job card; Saved presets toggles to the preset list, where
Apply materialises a preset as a new job.

The job card edits everything a job reads: which work it does, name, item filters, target
mode and exact target selection, source and destination containers, execution count, stock
limit, reservations, search radius, stop distance, and whether the result goes into a
container or on the ground. It says in a sentence what the chosen work actually does and
which structures it needs, because a player can no longer read the steps off the screen.
Choose targets opens a picker filtered to the capability the job requires, showing each
candidate's live status.

Job labels come from explicit mappings. Raw enum values must never reach the UI —
`OperateSmelters` is not a player-facing string.

## Layout rules

Positioning constraints — the content column, the ±400 bound, and same-row spacing — are in
[code-style.md](code-style.md). They are enforceable by inspection rather than by eye, and
were added after unbounded labels rendered outside the panel onto the world behind it.

## Item filters

`ItemCatalogue` indexes every item in `ObjectDB` for filter editing, matching both prefab
name and localised display name so players can search by what they read. It rebuilds if the
game reloads `ObjectDB`, since a stale catalogue would offer items that no longer resolve.

## Test seams

`ShowTabForTest`, `ShowPageForTest`, `ShowMemberDetailForTest`, and `ShowJobForTest` let the
benchmark drive the panel into a deterministic state for screenshot evidence. They set the
same fields the buttons do, so captured UI is real panel output and not a mock. Prefer the
overload that selects an exact member or job; index-based selection depends on member
ordering and can photograph the wrong subject.

Screenshot review is part of verification and is described in
[in-game-testing.md](in-game-testing.md).
