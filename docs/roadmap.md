# Roadmap — foundation

Everything up to and including the machinery that runs jobs. **Not the jobs themselves**:
what work exists, what each one does and what it needs are decided in their own document, and
none of it is designed here.

Read [system-design.md](system-design.md) first. This says *how* and *in what order*; that
says *what* and *why*, and wins wherever the two disagree.

## How to read this

Milestones, not sprints. Each is a thing that either works or does not, and each ends
somewhere you could load a world and see the result.

**Done means done.** For every milestone: the deterministic checks pass, the build is
warning-free, the in-game run passes both phases, every screenshot is *looked at*, and every
positive claim has a paired control that must fail. Code existing is not done.

**Each milestone names its edge cases before it is built.** The expensive bugs in this
project were not wrong logic; they were cases nobody wrote down — an unloaded chest read as a
deleted one, a durable reference that could not be minted without ownership, a UI that
appeared without releasing the mouse.

---

## Milestone 1 — The colony — **done**

The settlement exists, is a place, and persists.

Verified 11 September 2026: 35 in-game checks in the create phase and 9 across a real save and
relaunch, a warning-free build, and the villager screenshot inspected. Each claim below that
could pass by accident has a paired control that must fail — the orphan check is the clearest,
since idling and being orphaned both look like standing still.

**One thing here is deferred and not done:** the name is generated, not yet chosen. A player
cannot rename a colony until there is a screen to do it in, which is milestone 2. Everything
the record and the hover need is in place; only the editing surface is missing.

### Build

- A buildable piece on the hammer's list. Materials are deliberately cheap: this is the
  start of the mod, not a reward.
- A **radius**, from config, the same for every colony. Not upgradeable, not per-colony — one
  fewer thing to reason about until there is a reason.
- A **name**, editable, defaulted to something readable rather than blank.
- A **colony record** on the piece's ZDO: name, and everything later milestones attach.

### Data

Versioned `ZPackage` on the hearth ZDO, as everything here already is. Version 1 starts
clean: nothing from the current format is carried, because nothing in it survives the
redesign.

### Rules and edge cases

- **Two colonies overlapping.** Allowed. Structures belong to the colony they were registered
  to, not to whichever is nearest, so overlap is a cosmetic question rather than a semantic
  one.
- **The piece is destroyed.** The colony is gone. Its villagers are orphaned rather than
  killed — they stand where they are and say they have no colony. Registered structures are
  untouched; they were never owned, only referenced.
- **Placement.** No restriction beyond the game's own. A player who wants two hearths a metre
  apart may have them and will get what they deserve.
- **Hover text** says the colony's name, its population, and how many structures it has —
  enough to tell two apart without opening anything.

### Done when

You place a hearth, name it, walk away, save, reload, and it is still there with its name.

**Result.** The hearth persists with its name, population and structure count across a
relaunch. A destroyed colony leaves its registered chest untouched in the world, and its
villagers standing where they were, reporting "no colony" rather than walking to where the
hearth used to be.

Orphaning is deliberately *behavioural and reversible*: the villager reacts to not being able
to see its colony, and nothing deletes the membership pointer. On the host a missing ZDO does
mean destroyed, but a multiplayer client is only told about part of the world, so absence
there is not evidence. This is the *never infer destruction from absence* rule in
[valheim-findings.md](valheim-findings.md) applied where it costs something to get wrong.

---

## Milestone 2 — The screen — **done**

The single surface through which the settlement is managed. Built before anything it manages,
because everything after this needs somewhere to appear.

Verified 11 September 2026. See [ui.md](ui.md) for the built result.

**One decision differs from what is written below:** the screen opens by hotkey only. Using a
hearth tells you which key rather than opening anything, so there is one route to the surface
instead of two.

### Build

**Opening.** One configurable hotkey, from anywhere. Not tied to standing at the hearth.
Escape closes. Opening releases the mouse and closing gives it back — the previous
implementation drew a panel without releasing the cursor and was invisible-but-useless.

**Context.** On opening, the screen records what the player was looking at. That is what
makes registration possible without a held tool, and what later lets pointing at a villager
or a structure mean something. Looking at nothing is not an error; it just offers less.

**Layout as a system, not as coordinates.** Every screen is rows in a single column at a
fixed pitch, paged when they overflow. Nothing is hand-placed. Hand-placed coordinates are
how the old panel ended up with controls overlapping each other and spilling out of the
frame, and the layout audit that caught it only worked because a script could parse the
positions.

**A widget for each kind of value**, so no screen invents its own:

| Kind | Control |
|---|---|
| A flag | Yes/No |
| A number | value with − and +, bounded, formatted (metres, counts) |
| One of a few | a button that cycles |
| One of many | a picker screen with search and paging |
| Several of many | the same picker, multi-select, order preserved |
| Free text | an input field, committed on end-of-edit |

**Navigation.** A back stack. Every sub-screen returns to where it came from, and switching
tabs clears sub-screens rather than stranding the player inside one.

**Saying things.** A single place for "here is what just happened", used by registration and
everything after it. Every action reports its outcome, including refusal and why.

### Rules and edge cases

- The screen must survive its subject vanishing — the colony destroyed, the structure gone —
  by closing cleanly rather than throwing every frame.
- Text that does not fit is the layout's problem, not the reader's: a sentence cut off
  mid-word explains nothing, so strings are sized to their column.
- The list a picker shows is capped and paged. A list of a thousand items is not a list.

### Done when

The screen opens anywhere, releases the mouse, knows what you were looking at, renders one of
every widget kind, pages a long list, and survives its colony being destroyed while open.

**Result.** All of it, each claim paired with a control: the cursor is released on open and
captured again on close; looking at nothing still opens the screen; a screen that fits reports
one page while the gallery reports several and shows different rows on each; a sub-screen
returns where it came from while switching top-level screens clears the stack; and a colony
destroyed underneath an open screen closes it and gives the cursor back.

The layout claim is the one that needed the most care. Rows come from a column at a fixed pitch
and cells from a row allocated left to right, so overlap is not *checked for* but impossible to
express. What a layout system cannot promise is that a widget used it, or that a string fits
the cell it was handed - so `ScreenAudit` walks the built `RectTransform` tree in-game and
asserts bounds, overlap and clipping on what was actually drawn. The predecessor's audit parsed
C# source for literal coordinates; it lived outside the repository and a computed layout has no
literals to find.

`BrokenScreen` is why any of that is believable: a fixture built wrong on purpose, which the
benchmark asserts the audit rejects on all three counts. An audit nobody has watched fail is
indistinguishable from one that inspects nothing.

---

## Milestone 3 — Registration — **done**

Turning a thing in the world into something the settlement uses.

Verified 11 September 2026. See [structure-registry.md](structure-registry.md) for the built
result and [ui.md](ui.md) for the screens.

### What may be registered

A structure qualifies if it carries at least one **compatible component**. The list is the
whole of what a colony understands, and growing it is how the mod grows. For the foundation:

| Component | Detected by | What it means |
|---|---|---|
| **Storage** | `Container` | Things can be kept here |
| **Processing** | `Smelter` | Covers furnace, smelter and charcoal kiln |
| **Rest** | `Bed` | One villager can sleep here |

Deliberately excluded for now, and each is a later milestone: fireplaces, cooking stations,
fermenters, beehives, work areas, the junk area.

**Detected by component, never by prefab name.** A name list misses every modded chest and
goes stale; a component test does not. Components are found anywhere on the object, including
on children, because Valheim routinely splits an object's parts across child transforms — but
only when the child belongs to the *same* networked object, or a building would inherit the
capabilities of everything standing inside it.

**A creature is never a structure**, and neither is a loose item drop, whatever components
they carry.

### How it happens

Two ways, both from the screen:

1. **Look at it and register.** The screen knows what you were looking at and offers it.
2. **Pick it from a list.** Everything registerable nearby, searchable, with what each one
   would be registered *as*.

Both refuse with a reason: not in range, not a thing the colony understands, already
registered.

### Data

A record per structure, holding: a **durable reference** to the object, which components it
has, a player-editable name, and the settings for each component.

**Minting a durable reference requires owning the object.** Ownership is claimed first. A
reference minted without ownership is silently empty and looks fine until a reload, when it
resolves to nothing — this cost four benchmark runs to find and is not to be rediscovered.

### Rules and edge cases

- **Registering requires being inside the radius.** Reach is what a settlement can use.
- **Falling out of radius does not deregister.** The structure goes dormant, stays listed,
  and says so. It comes back when the settlement reaches it again.
- **Removal happens on positive evidence only.** A record is dropped when the game's own dead
  list says the object was destroyed, and never because a lookup failed — walking away from an
  outpost must not delete its configuration.

  This is the line that contradicted "smash it and watch the record go" below, and the
  reconciliation is that dead-listed goes and merely-absent stays. Two corrections came out of
  building it: the dead list is **server-only**, so the reaper does not exist for joining
  clients and fails closed; and it is **cleared on world load rather than pruned**, so
  destruction is evidence only within the session that saw it. A structure smashed while nobody
  was logged in is never reaped, which makes the manual Remove the common path rather than the
  corner case.
- **A structure may belong to one colony at a time.** Registering it elsewhere moves it, and
  says so.
- **Names default to what the game calls the thing**, not to its prefab: rows reading
  `charcoal_kiln(Clone)` are a bug, not a detail.

### Done when

You look at a chest, register it, see it listed as Storage; walk out of range and watch it go
dormant rather than vanish; smash it and watch the record go; and try to register a boar and
be told why not.

**Result.** All of it. A bed registers as Rest and a kiln as Processing — neither had ever been
checked, only containers had. A creature is refused *as a creature*, which needed a fix nobody
had noticed: the look-at ray did not include the character layers, so pointing at a boar
answered "nothing in reach" and the roadmap's own acceptance case was unreachable.

Two things found here outranked the milestone.

`PersistentZdoReference.Resolve` trusted the raw runtime address whenever it resolved to
anything, without checking the object carried the token being resolved — so it returned live,
valid, entirely unrelated objects. Fixed first, and demonstrated by removing the guard and
watching a paired check fail while its three controls passed.

And the plan's own ownership step was wrong: claiming before minting in the *listing* paths
would have taken ownership of every chest, cart, smelter and ship within the radius each time a
player opened the list, and claimed every record at once on any rename. Minting is now one
deliberate act at registration.

The reaper is the only thing in the mod that deletes a player's configuration, so its control
matters more than the feature: a record pointing at an id the world never issued — unresolvable
and not dead-listed, exactly how an unloaded outpost looks to a peer that cannot see it — must
survive every sweep. Making the reaper delete on absence alone fails that control, which is how
the safety argument was demonstrated rather than asserted.

---

## Milestone 4 — Component settings — **done**

What makes registration worth doing. Each component contributes its own settings, on the
structure, where the player is already looking.

### Storage

- **What belongs here** — any number of items, chosen from the game's own catalogue with
  search. Empty means *anything*, which is what an overflow chest is.
- **Whether it may be taken from** — some chests are for keeping, not for feeding the
  settlement back.

### Processing

- **What to keep it fed with** — fuel and input, chosen from what that structure will
  actually accept, read from the structure rather than typed.
- **How full to keep it** — so a settlement does not burn every log it owns keeping one kiln
  permanently brimming.

### Rest

- **Who sleeps here** — one villager, chosen from the colony. Assigning a bed that is taken
  moves it, and says whose it was.

### The settlement index

The piece everything later leans on, and the reason this is a milestone rather than a
detail.

Jobs do not search for destinations. They **ask**: *where does wood go?* — and the settlement
answers from an index it maintains as registrations and settings change. With no cap on
population, a hundred villagers each walking every chest is the difference between a
settlement and a slideshow.

- Built from the registered records, updated when they change, never rebuilt per villager.
- Answers "which structures accept this item", "which processing wants feeding", "which beds
  are free".
- Dormant structures are excluded from answers while dormant, and return without ceremony.

### Rules and edge cases

- Two chests claiming the same item is not a conflict; both are valid answers and the nearest
  usable one wins.
- A chest that claims wood and is full is not an answer. Capacity is part of the question,
  because discovering it on arrival wastes a walk.
- Settings survive the structure going dormant. They are the record's, not the object's.

### Done when

Registering a chest as holding wood and a smelter as wanting coal is the *entire*
configuration, and the settlement can answer where wood goes without anything walking
anywhere.

**Result.** All of it. Settings live on the record, so a dormant structure keeps them. What a
station accepts is read from its **prefab** — asset data identical on every instance — so an
outpost's kiln is configurable from home with nothing loaded and no cache to go stale; a
charcoal kiln shows no fuel row at all, because it burns nothing.

The index answers rather than searches, rebuilt on a revision the colony bumps on write rather
than per query.

Two measurements shaped it. **A container's contents are not on its ZDO** — a chest holding two
wood reported an empty record after the change, after an explicit `Save`, through a fresh
reference and through `ZDOMan`, using the game's own key. So capacity is a loaded-only question.
But **a colony keeps its own registered structures loaded**: a chest registered to a settlement
900m away stays instantiated with its capacity readable, while an identical unregistered chest
at the same distance unloads. So "unknown capacity" is a fallback, not the common case — and
unknown means *still a candidate*, since treating it as empty would funnel every villager to the
one chest nobody can see.

---

## Milestone 5 — Villagers — **done**

People in the settlement.

### Build

- **Spawned from the screen**, into the selected colony. A deliberate act, not a hotkey.
- **Placed on real ground** next to the hearth. The spawn point becomes the villager's home,
  so getting it wrong is permanent — the ground check must be the reporting kind, which fails
  loudly, not the silent one that returns your own height when it misses.
- **Named**, from the game's own name pool, editable.
- **Appearance**, rolled once and persisted: body, skin, hair, beard, and clothes drawn from
  what the game actually has rather than a list written by hand.
- **Belongs to a colony**, and knows it. Losing the colony is a state it reports, not a crash.
- **A bag**, persisted on the villager rather than trusted to the container component, which
  was measured not to save.
- **Removal**, which drops what they carried rather than destroying it, and works whether or
  not they are loaded.

### Rules and edge cases

- **No population cap.** Everything that touches all villagers is therefore bounded work:
  amortised scans, no per-villager sweeps over the settlement.
- A villager whose colony is destroyed says so and stands still. It is not deleted, so a
  misplaced hammer blow does not erase a settlement.
- Two villagers never occupy the same job target — claiming is part of the job system, not
  of politeness.
- Villagers do not shove the player. Collision with the player is ignored.

### Done when

You spawn five villagers, each looks different, all survive a reload with their names, and
removing one returns what it was carrying.

**Result.** All of it, from the screen rather than a debug hotkey — plus equipment a player can
change, a villagers list and a per-villager screen, and an assigned bed that is now where a
villager calls home.

**A correction belongs here.** This milestone was planned around villagers being undressed. They
never were: `VillagerAppearance` had always picked chest, legs, hair and beard from the game's
own item table. The claim came from misreading a benchmark photograph that frames the player and
a villager together without saying which is which. What the milestone actually added is the
*assertion* — five villagers, all wearing chest and legs, five distinct looks, nothing a player
could not craft — whose absence is why a wrong claim about appearance survived four milestones.

Equipment is a mirror of the persisted bag and never the creature's own inventory, because the
load-time routine that equips a creature's best weapon strips whatever is put there.

Names now come from the game's pool. `WarriorNames` is absent from the decompiled reference
while present in the shipped assembly, so its shape was probed at runtime — which caught the
obvious-looking mistake of taking every string field, 370 of them, most being prefixes and
suffixes the game combines with a name rather than uses alone. Villagers would have been called
"the Bold".

---

## Milestone 6 — The job system

The machinery that runs work. **No actual work is defined here** — that is the job document.
What this delivers is something a job can be plugged into.

### The shape

- **A job is defined on the colony**: a named configuration, shared, not per-villager.
- **A villager holds a queue** of job references, in order, each with a repeat count.
- **The queue is a ring.** After the last entry comes the first.
- **One entry runs at a time**, to completion or refusal, then the queue advances.

### Outcomes, and what each means

The whole scheduler is four answers, and the distinctions matter:

| Outcome | Meaning | Effect |
|---|---|---|
| **Running** | progress was made | keep going, consume nothing |
| **Completed** | one repetition finished | consume one of the count |
| **Failed** | it could not be done | consume one, so impossible work cannot loop |
| **Skipped** | nothing useful to do right now | consume nothing, yield to the next entry |

The difference between *Failed* and *Skipped* is what stops an idle job starving a villager,
and what stops a broken one spinning forever.

### Persistence

On the villager: the queue, its position, repetitions consumed, and enough about the current
attempt to resume. A villager reloaded mid-job resumes rather than restarting — but **facts
outrank the record**: if the world has moved on, the recorded state is discarded rather than
obeyed.

### Assignment

- From a villager's own screen, and in bulk from the colony's.
- **Presets**: named job configurations, so a settlement of a hundred is not a hundred
  configurations.

### Saying what is happening

Every villager reports what it is doing in words a player would use, and every refusal says
why — *needs an axe*, *nowhere to put it*, *nothing to do*. A settlement that stops without
explaining itself is the failure this whole project is arranged against.

### Scale

- Work is **claimed**, so villagers do not converge on one target.
- Deciding what to do next is bounded: an index lookup, not a search.
- Nothing iterates all villagers, all structures, or all loaded objects on a tick.

### Rules and edge cases

- A queue entry whose job was deleted is skipped and shown as missing, not hidden — a
  half-broken queue must look half-broken.
- An empty queue is idle, not an error.
- A job removed from the colony while a villager is mid-cycle ends the cycle cleanly.

### Done when

A villager can be given a queue of two placeholder jobs, runs them in order with their
counts, resumes mid-cycle across a reload, reports what it is doing throughout, and yields
rather than starving when one has nothing to do.

### Where this actually got to

Delivered, and verified in the gate: the queue and its four outcomes; claims; resuming across
a reload; the **Haul** job end to end — ground to container, container to container, caps, the
dump flag and the rule that stops the shuffle; **work areas**; **energy, rest and sleeping**;
and screens for all of it.

Three things were pulled forward from *after the foundation* because the first job could not be
honest without them, and two were not planned at all:

- **Work areas** — a hauler confined to the colony radius is not the job that was asked for.
  Built out of registered structures rather than as a new thing to place.
- **Energy and rest** — the thing that finally makes beds matter.
- **Long-distance travel and a rescue ladder**, which took most of the effort and is written up
  in [off-screen-simulation.md](off-screen-simulation.md) and
  [valheim-findings.md](valheim-findings.md). A villager used to stop existing at about a
  hundred metres.

**Presets and bulk assignment** landed too: a preset is a named queue, applied to everyone or to
a chosen few, and it reaches villagers nobody has loaded — which is most of the point, since a
settlement worth assigning in bulk is spread over enough ground that some of it always is.

**Nothing is outstanding from this milestone.**

---

## After the foundation

**The job catalogue** — its own document. Every job specified before it is built, in two
terms: the work it does, and the equipment it requires. One at a time.

Then, in the order set out in [system-design.md](system-design.md): rest and energy, ambient
life, food; then reach — work areas and gathering.

---

## Milestone 7 — The party — **designed, not built**

Villagers that follow a player, fight for them, carry for them and come home; and a Kolony that
defends itself. The largest single feature so far, and the first that makes a **player** rather
than a placed hearth the thing work is organised around.

It overturns two settled decisions in [system-design.md](system-design.md) — *no defence*, and
*wearing something is a picture, not protection* — and one of its choices knowingly breaks the
game's own economy (villagers carrying metal through portals, restorable by a config switch).

Fully specified in **[docs/party/](party/README.md)**, in seven documents:

- [`decisions.md`](party/decisions.md) — every decision, its reason, and the cost accepted with it
- [`party.md`](party/party.md) — joining, following, the moving work area, the party queue, the
  four party abilities
- [`combat.md`](party/combat.md) — stances, targeting, weapons, armour, death and revival
- [`defence.md`](party/defence.md) — a base that fights its own raids unattended
- [`travel.md`](party/travel.md) — boats and portals
- [`unknowns.md`](party/unknowns.md) — **what must be measured before any of it is built**
- [`build-order.md`](party/build-order.md) — seven stops, each one shippable on its own

**Nothing starts until [`unknowns.md`](party/unknowns.md) is worked through.** Three of the eight
items there can invalidate whole parts of the design — whether hostiles spawn off-screen, whether
damage applies off-screen, and whether vanilla `MonsterAI` can be handed control cleanly. This mod's
method is to probe rather than reason from code, and it keeps being right.

**And the acceptance run comes first.** It has not been done since Mine, Forage, Farm, Repair,
Cooking and hunger landed — six job-blob versions and a structure-blob version — and it is the only
thing that checks a world survives save and reload. The party system adds ZDO fields to every
villager and a second job queue on top of all of that.
