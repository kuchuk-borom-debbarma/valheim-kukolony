# Valheim findings

Things measured against the running game, and traps found the hard way. Kept because **the
knowledge is the asset and the code that carried it is disposable** — most of what follows was
learned inside features that have since been deleted, and rediscovering any of it would cost
another few hours of four-minute test runs.

Every claim here was observed, not reasoned about. Where something is inferred rather than
measured, it says so.

---

## ZDO lifetime — destroyed versus not loaded

**Measured** in one run, three cases:

```
alive      GetZDO resolves      not in dead list
destroyed  GetZDO fails         in dead list
unloaded   GetZDO resolves      not in dead list   (a real chest 900m away, zone unloaded)
```

**An unloaded structure stays known.** `ZDOMan.GetZDO` still answers for an object whose zone
the game has stopped instantiating, so **a lookup that fails already means destroyed**.
`ZDOMan.m_deadZDOs` confirms it independently but no bookkeeping of our own is required.

Consequence: a colony may safely delete a record when its lookup fails — but the rule adopted
is stricter, *never infer destruction from absence*, because the cost of being wrong is deleting
an outpost's configuration.

**Two corrections to the above, found while building the reaper.**

*The dead list is server-only.* `m_deadZDOs` is written inside an `IsServer()` branch, so on a
joining client it is always empty. Anything reading it must be server-gated or it silently does
nothing — which is the safe direction, but it means the feature does not exist for clients.

*It is cleared on world load, not pruned by time.* The earlier note that it "is pruned over
time" was asserted rather than measured, and is wrong for this build. The consequence is the
opposite of what that implied: within a session the list is reliable, but destruction is never
evidence across a reload, so a structure smashed while nobody was logged in is never reaped and
must be removed by hand.

*The id you check matters.* A record loaded from disk carries last session's address. It
resolves through its token while the object lives, and the moment the object dies that lookup
fails and resolution falls back to the stale address — which is not the id the game listed as
dead. Anything reaping must remember where each record was last seen alive this session, or it
never fires for records it did not create, while looking correct because new ones reap fine.

Note that vanilla does not fully trust this either: `CreatureSpawner` pairs the check with an
`alive_time` heartbeat so a wrong answer only delays a respawn.

Neither earlier Kuku mod made this distinction; both treated a missing *instance* as "not
loaded" and teleported to it, and the newer one's answer to the whole problem was a comment
requiring players to install a third-party chunk loader.

## A durable reference must outrank the address beside it

`Resolve` returned the raw runtime id whenever that id resolved to *anything*, without checking
the object it found carried the token being resolved. Handed a chest's token and another live
object's address, it answered with the other object — one session, both loaded, no reload
involved. Demonstrated by removing the guard and watching the paired check fail while its three
controls passed.

Stale addresses are reachable because **loading rewrites ZDOIDs**: the benchmark's villager is
`597515522:9013` before a save and `1:2605` after it, and records persist whatever they last
resolved to. How often a stale address lands on a live object was not measured; the guard is one
string compare and does not depend on knowing.

## Durable references need ownership to mint

A cross-session reference is a token written **on the target's ZDO**. `PersistentZdoReference.Ensure`
returns empty for a ZDO this peer does not own, because a token written by a non-owner is
discarded on the next sync.

Which makes *where* you mint a design decision, not a detail. Minting while merely listing
candidates takes ownership of everything listed — every chest, cart, smelter and ship in the
radius, each time a player opens the list — and minting inside a whole-list writer claims every
record at once on any edit. Mint once, at the deliberate act, on the one object.

A reference minted without ownership **looks fine for the rest of the session** — the raw
runtime ZDOID still resolves — and fails only after a reload, when loading renumbers it. This
cost four consecutive benchmark runs to find.

Also: resolving a reference needs the target **loaded**. An unloaded object is
indistinguishable from a deleted one at that layer, which is correct for "can a villager work
on this" and misleading if read as "the save lost it".

## Appearance: villagers built from a creature rig

**The glow is in the material, not in the effects.** The rig used renders body and hair with
`Custom/Fallen Warrior` while its clothing already renders with `Custom/Player`. Removing the
rig's two point lights and four particle systems changed nothing visible.

Fix: take the player's own materials at runtime.

- **Not at prefab build time** — the prefab is configured before the scene has a Player to
  copy from, and the attempt silently kept the ghost skin.
- **Not by tinting** — that means editing a shared material asset, which repaints every
  instance of that creature in the world.
- **Repaint by shader, not by knowing which renderer is which.** Hair and beards are built
  later and separately; repainting only the body left villagers with normal skin and a glowing
  haircut.
- **It is a race, so it needs a budget.** Twelve ticks lost to hair; the working version keeps
  looking until several consecutive passes find nothing.

Live materials appear as `PlayerMaterial (Instance)` / `PlayerHair (Instance)` when correct.

## Dressing and styling from the game's own data

Never hardcode prefab names. Every hand-written list this project produced was wrong or went
stale — one named trees that do not exist.

- **Clothing** — items of each visible slot type, **filtered to those that have a recipe**.
  Filtering only by slot type includes monster armour, and villagers turned up in golem plate
  and fenring boots (`MountainGolem_mat`, `FenringArmor_mat`, shader `Custom/Creature`).
  Having a recipe is the game's own answer to "could a player have this".
  Yields roughly 41 chest, 19 legs, 35 helmet, 11 shoulder, 1 utility.
- **Hair and beards** — items whose type is `Customization`, the same set the player's own
  appearance screen offers: about 87 hairstyles and 27 beards, against a hand-written 12 and 10.

Wearing something is a picture, not protection: a villager in wolf armour is dressed, not
armoured, so allowing the whole range costs nothing.

## VisEquipment

- Slot setters take **stable hashes**, not names, and are ZDO-backed, so what a villager wears
  replicates and survives a reload with nothing of ours running.
- **Signatures differ from the decompiled reference**: `SetShoulderItem(hash, quality, variant)`,
  `SetRightItem(hash, quality)`, `SetLeftItem(hash, quality, variant)` while helmet, chest, leg
  and utility take a hash alone. Check against the shipped assembly, never the reference.
- Quality and variant choose which model is drawn, so they should come from the villager's own
  copy of the item rather than a constant.
- **Do not put equipment in the creature's own inventory.** The routine that equips a
  creature's best weapon runs on load through a path this mod does not suppress, and strips it.
  Truth lives in a persisted bag; the visible slot mirrors it.

## Container contents are not on the ZDO

**Measured, and it is not specific to the villager bag.** A vanilla `piece_chest_wood` holding
two Wood, owned by this peer and loaded, reported an empty `s_items` record:

- after the inventory change that should trigger `OnContainerChanged`
- after invoking `Container.Save()` explicitly
- read through the captured ZDO, through a freshly fetched one, and through `ZDOMan.GetZDO`
- using the game's own `ZDOVars.s_items` rather than a reconstructed hash

Five runs, each eliminating one explanation. The live inventory reported two Wood throughout, so
the items were genuinely there.

**Consequence: capacity is a loaded-only question** — but that turns out to bind far less than
it first appears, because a colony keeps its own registered structures loaded.

Measured, with the unregistered chest in `CheckUnloadedStaysKnown` as the control: two chests at
900m from the player, same prefab, same distance. The unregistered one unloads. The one
registered to a colony 900m away stays instantiated, and its capacity reads normally. The only
difference between them is registration, so that is what the difference can be attributed to.

`ColonyRegistry.CollectMemberPositions` is why: every registered structure's position is fed to
the keep-alive, which holds its zone open. Registration is itself bounded by the colony radius,
so a structure is always near its own hearth — meaning a whole settlement stays loaded, not
individual chests.

So the index treats an unloaded container as *unknown*, and unknown must mean "still a
candidate" rather than "empty" — but it is a fallback rather than the usual case. It is reached
when the keep-alive is off (`KeepAliveEnabled`), on a joining client (the keep-alive is
server-gated), or when a settlement exceeds `KeepAliveMaxZones`.

The check asserting this is deliberately phrased as the negative, so that if a game update makes
containers flush, it fails and the index gets made smarter on purpose rather than by accident.

## The villager bag

`Container` is documented as flushing its inventory to the ZDO on every change. **Measured, it
never wrote for this component**: with the villager owned and the container reporting itself as
owner, the record stayed empty after a change and after an explicit `Save`, and everything
carried was gone after a real save and relaunch.

The bag is therefore written to its own key on the villager's ZDO, owner only, on every change.

## Tools

Classified by **the damage they do**, not by item category: axes and pickaxes are weapons in
the game's own taxonomy, and only hammers and hoes are `Tool`. Check `GetDamage().m_chop` and
`.m_pickaxe`. Tier is `m_shared.m_toolTier`, and `HitData.m_toolTier` is a `short`.

Durability needs no handling: the game exempts non-players from wear.

## Trees, and gathering generally

- **There is no registry of trees.** The game keeps instance lists for items and creatures, not
  for scenery, so finding one means looking through what is loaded. Affordable once every few
  seconds for a colony; not once per villager per tick.
- **An untouched tree is unworkable by everyone.** Damage routes to the object's owner, and a
  world-generated tree has no owner, so every peer decides the blow is somebody else's business
  and drops it. The villager swings, health does not move, nothing reports a problem.
  **Claim ownership first and let the blow land a tick later.**
- **A tree does not produce wood.** Felling leaves a *log* — a separate object — and cutting
  that up is a second pass. A colony that only fells looks busy and fills no chests.
- **Health before and after is the only honest check.** A landed blow and one the game
  discarded are otherwise identical. Read it from the ZDO, defaulting to the prefab's full
  health: an undamaged object has never written the field, and defaulting to zero makes a
  felled tree and an untouched one report the same number.
- **Tool tier is enforced.** A stone axe refuses birch and oak. Sample trees by tier from a
  prefab index rather than naming species.
- **Keep-alive excludes trees by default**, so off-screen a villager picks a tree, walks to it
  and waits forever — and works perfectly whenever anyone watches. Trees must be loaded for
  colonies that gather, and only those, because trees are the most numerous thing in the world.
- The attacker on the hit is deliberately left unset: naming the villager credits it with the
  kill and, on a world with difficulty modifiers raised, scales the damage the game thinks it
  is dealing.

## Structures and capability probing

- Detect by **component, never by prefab name** — a name list misses every modded chest.
- **Look at children, not just the root.** Valheim routinely splits an object's parts across
  child transforms, and a cart's container lives there. But only count a component whose owning
  `ZNetView` is the same object, or a building inherits the capabilities of everything standing
  inside it.
- Valheim's *piece registry* holds only what the hammer builds, so a cart is invisible to it.
  Scanning loaded objects finds it; that is affordable because it runs when a player asks.
- A live object's Unity name carries `(Clone)`. Read the readable name from the `Piece` or
  `Container` component instead.

## Stations

Every RPC name below was probe-verified against the running game, and the arguments are the
shapes that actually worked. **The names carry an `RPC_` prefix** — the method names in the
decompiled source do not, and calling those does nothing.

| Station | Call | Notes |
|---|---|---|
| Smelter, kiln | `InvokeRPC("RPC_AddFuel")` | no arguments |
| | `InvokeRPC("RPC_AddOre", prefabName, false)` | ore by **name** |
| Cooking station | `InvokeRPC("RPC_AddItem", prefabName, false)` | |
| | `InvokeRPC("RPC_RemoveDoneItem", position, 1)` | position is where the food pops out |
| Fermenter | `InvokeRPC("RPC_Tap")` | |
| | `InvokeRPC("RPC_AddItem", prefabHash, false)` | by **hash**, not name — unlike the others |
| Beehive | `InvokeRPC("RPC_Extract")` | |

**The decompiled reference in `.reference/` is stale for these members, and it has now misled two
reviews.** It shows `Fermenter.RPC_AddItem` registered for a `string`; the assembly this mod
compiles against declares `RPC_AddItem(long sender, int prefabHash, bool cheated)` — a parameter
name no decompiled text could invent. `Smelter.RPC_AddOre` likewise carries a third argument the
reference does not show. **Where they disagree, the shipped assembly wins**, because that is what
the game runs — and the `tend` slice now settles it by feeding a real item to each of the three
station kinds and reading the station's own numbers back, which is the only evidence that cannot
be argued with.

Behavioural facts, each of which cost a run to find:

- **Consume the carried item before submitting the RPC**, never after. A removal that fails
  after the call has already handed the station a free item.
- **Read the fermenter's prefab hash before consuming.** The item is gone by the time the call
  is made.
- **Smelters:** fuel-versus-ore is decided by the station's own `m_fuelItem`, not by the job.
  Capacity via `GetFuel() >= m_maxFuel` and `GetQueueSize() >= m_maxOre`; `IsItemAllowed` for
  input. Only fuel what is queued — `min(queued × m_fuelPerProduct, m_maxFuel) − currentFuel` —
  or an idle smelter is fed forever.
- **Fireplaces are the trap.** One that burns forever or refuses refills *still accepts*
  `AddFuel` and *still reports a change*, so without checking `m_infiniteFuel` and
  `m_canRefill` a villager feeds resin into it indefinitely and the fuel is simply destroyed.
  `AddFuel` owns the max-fuel guard. `CanUseItems` is useless here — it checks the local
  *player's* inventory. To tell a real change from a no-op, compare the ZDO's `DataRevision`
  before and after.
- **Cooking stations: clear before adding.** A station full of cooked food cannot accept
  anything, so taking it off is the only move that makes progress.
- **Beehives are unlike the rest**: extracting produces a **world drop** rather than changing
  what the station holds, so the work is not finished when the RPC returns.
- **Several prefabs carry more than one of these components** — an oven is a cooking station, a
  hearth is a fireplace, a windmill is a smelter, and fuelled cooking stations are both. Probe
  order matters, and **Fireplace must be probed last** precisely because it is the one most
  often present alongside something else.
- `DamageText.instance` is null on a dedicated server, which matters for any path that shows
  damage.

## Inventory

**`Inventory.MoveItemToThis`'s amount overload requires a real grid coordinate.** Passing
`(-1, -1)` is rejected in current Valheim *even after `CanAddItem` has succeeded* — a measured
API defect, and a silent one.

The working pattern is to find the slot yourself: prefer an existing compatible stack, then an
empty slot, and let the vanilla helper do both inventories' bookkeeping.

```
destination.MoveItemToThis(source, item, 1, x, y)   // x,y must be a real slot
```

## Claims and ownership

**`ZDO.Set` ignores its `okForNotOwner` argument.** A write to a ZDO this peer does not own
lands locally and is clobbered on the next sync from its owner. That is why a claim on a
target is *not* written onto the target: doing so would mean taking ownership first, which is
an RPC round trip with exponential backoff, before the villager has even started walking.

The claim used instead is the villager's own recorded target, read by everyone else. It needs
no new state and cleans itself up, because the target is cleared on success and on every
failure.

**Claiming is asynchronous, so the work waits a tick.** The pattern that works for containers
and for trees alike: if not the owner, call `ClaimOwnership()` and report *still running*; the
mutation lands on a later tick, once ownership has actually moved.

## Crafting

Reimplementable, and the trap is recorded in an earlier mod's commit message: *"CUSTOM RECIPE
IS NOT THE WAY. It doesn't give you the recipes in game."* Jötunn's `ItemManager.GetRecipe`
returns only **your own mod's** recipes. Scan `ObjectDB.instance.m_recipes` instead, and read
`m_resources`, `m_craftingStation`, `m_minStationLevel`.

Two further traps: item removal ignores anything below the world's level, so on an NG+ world
inputs are not consumed and crafting is free; and recipes flagged as taking only one ingredient
dereference the local player.

## Harness lessons

- **Purge everything a run spawns**, not just what a colony registered. Cleaning only
  registered structures left every chest, kiln, cart and log a scenario ever spawned standing
  where it fell; one run then purged 70 leftovers. A check that finds a leftover chest with
  room in it passes for the wrong reason — which happened.
- **Keep destructive fixtures away from other checks' subjects.** Felling 200-health trees
  beside the benchmark's chests destroyed one, and the failure surfaced two phases later as a
  persistence error.
- **A screenshot must frame its subject, and say what the subject is.** Every capture framed
  the interface, so villagers glowing like ghosts survived run after run: nothing ever
  photographed one. The opposite failure followed: a capture that deliberately puts the player
  and a villager in one frame, with nothing recording which is which, had me diagnose the
  player's bare chest as a villager bug across several runs and write "villagers are undressed"
  into two documents. A photograph is evidence only if the thing it proves is identified.
- **One writer per piece of state.** A persistence snapshot had two writers that disagreed, so
  every failure named an object the check being edited had not chosen.
- **Report the value you asserted on, not the value at report time.** Several checks printed a
  field that a later phase had already changed, so passing and failing runs printed the same
  thing.
- **Never chain an unverified edit into a long run.** A failed edit script once left the source
  unchanged and the run tested the previous build.

## Screen lessons

- **Releasing the cursor is not optional.** A panel drawn without it is visible and completely
  unusable; the game keeps the mouse captured for looking around. Assert it on `Cursor.visible`,
  which is the game's state, rather than on a flag of your own recording that you set the flag.
- **Do not hand-place coordinates.** Rows in a column at a fixed pitch cannot overlap or spill;
  hand-placed ones did both. Build the layout so the violation cannot be expressed, and audit
  the built `RectTransform` tree rather than parsing the source - a computed layout has no
  literal coordinates for a script to find, and the tree is what the player actually gets.
- **Audit only what your own layout placed.** Unity controls overlap themselves on purpose: a
  button's label sits on the button, and an input field stacks its placeholder and its text in
  the same space because only one is ever shown. Comparing every drawn element reports all of
  those as faults. Depth 1 is the layout's business; deeper is the control's.
- **`preferredWidth` against the rect is how you catch a clipped string**, and it only works
  with the content size fitter *off* - with it on, the rect grows to fit and every string
  passes.
- **Two states behind one boolean will be rendered wrong.** A structure that could not be found
  and one merely out of radius both came from `IsLiveIn`, so the screen called a destroyed chest
  "out of reach". Found by looking at a screenshot, not by any assertion.
- **Size strings to their column.** A sentence cut off mid-word explains nothing.
- **A fallback that looks plausible hides a bug.** A `default:` branch returning a real name
  made a new job type display as an existing one; returning empty makes it fail loudly instead.

## Pitfalls in the two earlier Kuku mods

Worth knowing because the same shapes are tempting again:

- A Harmony postfix on `Humanoid.GetCurrentWeapon` rewrote `weapon.m_shared.m_damages`.
  `m_shared` is shared by every instance of that item — it changes the **player's** weapon too.
- `Random.Range(0, list.Count - 1)` appears repeatedly: it silently excludes the last element
  and throws on an empty list, inside a try/catch that hides it.
- `async void` called from `Update`, with the author's own `//TODO: Fix this crashing the whole
  game` above it. Unhandled exceptions there take the process down.
- Working villagers mostly could not defend themselves, because re-commanding the AI every tick
  starved the vanilla combat logic. Stated as a known bug in that mod's README.

---

## Pathfinding: `FindPath` requires a complete path and gives up without one

**The single most important thing to know before making anything walk anywhere.**

A villager sent to a chest stood motionless five metres away. Measured at the moment of
failure:

```
agent=Humanoid  fullPath=False  partialPath=True  waypoints=0
```

Every part of that matters. The agent type is right. The navmesh *can* route most of the
way there. But `BaseAI.FindPath` asks `Pathfinding.GetPath` for a **complete** path, and
the destination was the centre of a solid object — not a point anything can stand on. So:

1. `FindPath` returns false.
2. `MoveTo` takes its "stopped" branch — `StopMoving(); return true` — with an empty
   waypoint list.
3. The villager never takes a step, and `MoveTo` reports success.

This reads exactly like "the AI walks into obstacles and does not path around them". It is
the opposite: the AI refused a destination it was right to refuse, and the caller could not
tell that apart from arriving.

### What to do instead

**Snap the destination to the navmesh before asking anyone to walk to it.**
`Pathfinding.instance.FindValidPoint(out point, near, radius, agentType)` is the engine's
own answer to "somewhere around here an agent of this kind can be". It beats offsetting by
a guessed distance, which cannot tell a clear spot from a wall, a drop, or the inside of the
next chest along.

`Kukolony/Villagers/Navigation/Approach.cs` does this for everything the mod moves, and
`VillagerWalk` resolves it **once per journey** — `HavePath` is a real query against the
navmesh, not a field read.

### Two distances, and never the same one

- **Where you walk to** — the snapped point, approached with a tight stop distance.
- **Whether you arrived** — measured against the *thing you wanted*, generously.

Passing one tolerance to both compounds them: you stop short of a point that is already
short of the chest, and arrive exactly where you were sent, still out of reach.

### The tolerances are measurements, not preferences

A villager walks its full path, consumes every waypoint, and comes to rest **4.1 m** from a
chest with a valid complete path behind it. That is as near as the navmesh goes — Valheim's
path tiles are coarse and a placed piece blocks several metres around itself. A loose item
lying against something measured **3.4 m** for the same reason.

So: 5 m for a structure, 3.5 m for a loose item. Demanding less produces a villager standing
as close as it will ever get, reporting that it cannot get there, forever. Nothing is lost by
the distance — containers are worked through their ZDO, not with an outstretched arm.

### Useful API surface (verified to compile against publicized assemblies)

| Call | Use |
|---|---|
| `Pathfinding.instance.HavePath(from, to, agentType)` | is this destination reachable at all |
| `Pathfinding.instance.FindValidPoint(out p, near, radius, agentType)` | nearest point an agent can stand on |
| `Pathfinding.instance.GetPath(from, to, path, agentType, requireFullPath, cleanup)` | `path` may be null, so it doubles as a query |
| `ai.m_pathAgentType` | the agent type — `Humanoid` for the Dverger rig |
| `ai.m_path` | current waypoints; empty means `MoveTo` will stop |

`Pathfinding.SnapToGround` does **not** exist. Do not reach for it.

---

## Long journeys: walk when watched, reckon when not

The navmesh section above explains why a destination gets refused. This is about the harder
version of the same problem: **ground nobody has ever been near has no colliders, so it has no
navmesh, so nothing can path into it — and it may never get one.**

Valheim builds navmesh tiles only where something asks to walk, one tile per `UpdatePathfinding`
call, from colliders that are actually loaded. A villager sent across the world alone is
therefore waiting on a chain of things that may simply never happen.

Measured, on identical terrain with the same villager: **eleven metres covered in thirty seconds
on one run, two metres on the next.** That is not something to build a settlement on.

### What does not work, and is worth not trying again

**Choosing your own intermediate hops.** The idea is obvious and the failure is not: the navmesh
around a villager that has not moved reaches about *two metres*, so the hops it picks are one to
three metres long — and handing `BaseAI.MoveTo` a target one metre away makes a character
decelerate to a stop rather than walk. Measured: three centimetres of progress every three
seconds, while planning flawlessly and reporting itself as travelling.

**Demanding a full path.** `BaseAI.FindPath` calls `GetPath` with its defaults, so
`requireFullPath` is *false*. Valheim's own creatures follow **partial** paths and re-ask once a
second. Nothing in the game waits for a complete route, and code that does will wait forever.

### What works

**Walk straight at the destination when a player is near enough to see it.** Partial-path
following plus the once-a-second replan *is* the streaming mechanism; it only needs ground to
stream, which the keep-alive halo provides by anchoring on the villager's waypoint as well as its
position.

**Advance by dead reckoning when nobody is watching** (`TravelObservedRange`, default 96m). The
villager moves toward its destination at its own `m_walkSpeed`, a bounded step at a time,
snapping to ground height where ground exists. It takes exactly as long as walking would, and it
cannot fail — which is what turns arrival into a matter of time rather than of luck. Coming back
into view, the villager is put down on the navmesh with `FindValidPoint` before it walks again,
so it never resumes standing in a lake.

**A walk fails when it stops getting closer, not when it stops walking.** `MoveTo` reports
"stopped" every time a villager reaches the end of the partial path it is following, which on any
walk across unvisited ground happens over and over.

### Pass the AI's own delta, not the frame's

The AI is driven at a fixed 0.05s while a frame is nearer 0.02. A villager reckoning with
`Time.deltaTime` moved at forty per cent of its own walking speed — steady, plausible, and wrong.

---

## Moving a networked object by hand: tell the ZDO

`ZDO.SetPosition` is what calls `SetSector`. **Moving a transform never touches it.**

Valheim decides what exists by ZDO *sector*, so an object whose transform you moved yourself has
a body in one place and a record in another. Found the hard way: a villager covering ground
unseen walked 317 metres and then stopped existing, with no death, no damage and nothing in the
log. Its keep-alive was holding zones around a position nothing else agreed with.

Anything that repositions a networked object outside the physics path must do all three:

```csharp
if (body.m_body != null) body.m_body.position = position;   // or the rigidbody drags it back
body.transform.position = position;                          // or readers are a frame behind
body.m_nview.GetZDO().SetPosition(position);                 // or the world loses track of it
```

## Unloaded is not destroyed, and the difference is the whole diagnosis

`ZNetScene.FindInstance(id) == null` means **not loaded**. The object may be perfectly alive as a
ZDO and come back when something holds its zone. `ZDOMan.GetZDO(id) == null` is the one that means
gone.

A check that measured only the instance reported `DESTROYED EN ROUTE` for a villager whose record
was intact the whole time, and sent the investigation after the wrong bug twice. The project rule
*never infer destruction from absence* applies to your own diagnostics, not just to the feature
code.

## Liveness must be reported by something that ticks

A heartbeat written by the phase runner's loop is not a heartbeat. `RunPhase` writes one each
time its enumerator advances, but a check yielded as a **nested enumerator** does not return
control until that check *finishes* - so a check that watches a villager walk for three minutes
freezes the heartbeat for three minutes while everything is healthy.

That cost hours, and cost them expensively: it was read as a crashed coroutine, then as a hung
game, then as a hanging distant teleport. None of those were happening. The heartbeat is written
from `Update` now, which runs whatever the coroutines are doing.

**Corollary for reading logs:** "Valheim stopped logging for N seconds" is not evidence of a hang
either. The game logs nothing while nothing happens, and a quiet stretch looks identical to a
freeze from outside.

---

## `GetPath` snaps BOTH ends, and fails outright if either will not snap

**The single most misleading fault in this codebase so far.** It presents as "walking does not work
near the colony", and the colony has nothing to do with it.

```csharp
if (!SnapToNavMesh(ref from, extendedSearchArea: true, settings)) return false;
if (!SnapToNavMesh(ref to,   !havePath,               settings)) return false;
```

A destination in terrain nobody has loaded has no navmesh to snap to. So asking for a path to it
returns **nothing at all** — not a partial path, not a short one, nothing — regardless of how good
the ground under the villager is. `BaseAI.MoveTo` then takes its "stopped" branch with an empty
waypoint list and the villager never takes a step.

That is why the symptom looked like local terrain:

- `fullPath=False partialPath=True waypoints=0`, in every direction, at the colony.
- The same villager walks to a chest six metres away without complaint.
- It "stalled" at 70m, at 77m, and at 2m from home — all different places, one cause.

**So never ask for a path to somewhere that is not loaded.** Walk towards a point on the route
that is inside the loaded halo — ours is 45m ahead along the bearing, which is the same waypoint
that holds the ground open — and judge arrival against the real destination. The far end becomes
askable only once the villager is near enough for it to be loaded, which is exactly when it
matters.

Hours went into treating this as bad pathfinding, a bad navmesh, bad terrain, physics, and the
keep-alive. It was a question that could not be answered, asked over and over.
