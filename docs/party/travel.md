# Boats and portals

The hardest part of the feature, and not for the reason it looks like.

## Boats

### The mechanism exists and is public

Read off the shipped assembly, not inferred:

```
Character.AttachStart(Transform, GameObject, bool, bool, bool, string, Vector3, Transform)
Character.AttachStop()
Character.IsAttached()
Character.IsAttachedToShip()
Character.GetStandingOnShip()  -> Ship
Character.m_lastGroundBody     -> Rigidbody   (moving-platform support)
```

`AttachStart` is how the game glues a body to a moving hull — it is what happens when a player sits
at a helm. A villager does not need to *sail*; it needs to be attached to a point on the deck and
to stop being a pedestrian.

`m_lastGroundBody` gives plain standing-on-a-moving-thing support as well, which is why players do
not slide off a deck. That is the fallback if attaching proves too rigid.

### The hard part is that our navigation is entirely ground-based

`Journey` works by walking straight at a destination while holding navmesh tiles open ahead of it.
The docstring is explicit that this is what Valheim's own creatures do and why they can cross the
world.

**There is no navmesh over water.** So:

- A villager aboard that still believes it is travelling **walks off the side.**
- The off-screen reckoning that moves distant villagers by arithmetic will happily reckon one
  straight through a hull, or to a point in open sea.
- A villager whose destination is a player standing on a moving boat is being asked to path to a
  point that is both moving fast and unreachable by ground.

**Boarding therefore suspends navigation entirely rather than redirecting it.** That is the whole
trick. Attaching is not decoration; it is the thing that stops the sea eating your party.

### Behaviour

- **It boards when the player boards.** No new gesture. It walks aboard while the boat is moored
  and attaches to a free spot on deck. It steps off when the player does.
- **Attached, but it fights.** Fixed in place, and it will engage a serpent that comes alongside if
  its stance says so. This makes ranged weapons matter in a way they do not on land, and it means
  the weapon classifier cannot be melee-only.
- **In deep water it swims if it can, and is recovered to the deck if it cannot.** Which applies is
  a **measurement** — `Character.m_canSwim` is per-prefab asset data and `FallenWarrior`'s value
  has not been read. If it cannot swim, a villager that goes over the side sinks, and the recovery
  net is not optional.
- **Villagers at sea keep holding zones open.** Chosen against my advice, and the cost is real: the
  player is already streaming the water ahead, so each aboard villager's halo is streaming ocean
  nothing is using. **The halo will be configurable** so a stuttering crossing has something to
  turn down. If crossings stutter, look here first.

### Known limitation

`Ship.m_players` is a `List<Player>` — strongly typed. A villager can **never** register as a boat
occupant, so anything in the game that counts who is aboard will not see them. Riding works;
being counted does not. Worth knowing before something is built on top of it.

## Portals

**Villagers follow the player through portals.**

Two things make this harder than it sounds:

1. **Vanilla refuses tamed creatures at a portal**, and villagers are tamed. This needs real
   patching, not a call to an existing API.
2. **A villager's bag is 24 slots, and the game deliberately forbids moving metal through a
   portal.** Letting a villager carry ore through is an exploit created directly by this decision.

The second was stated before the choice was made and accepted. **A config switch will restore the
vanilla restriction — applying the portal's own rules to a villager's bag as they apply to a
player's inventory — and it will not be the default.** Somebody who wants the game's economy back
can have it in one setting.

## Overland travel

Unchanged. `Journey` already crosses the world by walking at the destination and letting the engine
stream the navmesh, and a player is a destination like any other. The `travel` slice already covers
long journeys and flag-to-flag movement.

The one new case is **a party villager left far behind** — the leash closes the gap, but a villager
that falls far enough behind is doing a long solo journey through terrain the player has already
left, which is exactly the situation the reckoning system exists for and exactly where it has been
hardest to get right.
