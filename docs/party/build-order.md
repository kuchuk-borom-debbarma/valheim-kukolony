# Build order

This is too large to build in one go, and the parts have a real dependency order. Each stop below
is **shippable** — it leaves the mod working and better than before, rather than half a feature
behind a flag.

The ordering principle: **measure first, then build the thing everything else stands on, then the
things that can fail independently.**

---

## 0. Measure

[`unknowns.md`](unknowns.md), in its listed order. Nothing here is building; all of it changes what
gets built.

**Stop and re-plan if:** hostiles do not spawn off-screen (kills off-screen defence), or damage
does not apply off-screen (kills it harder), or vanilla `MonsterAI` cannot be handed control
cleanly (roughly doubles the combat work).

## 1. Party membership and following — **done**

Join, dismiss, follow, leash. No combat, no new abilities, no boats.

A villager you can take for a walk and send home again, that remembers what it was doing and goes
back to it. Not useful yet — and it is the thing every later stop stands on, so it is the thing
whose bugs are cheapest to find now.

**Ships as:** "villagers will follow you."

**Landed.** Membership is one `long` on the villager's ZDO; `Party/Following.cs` is the pure
hysteresis, `Party/Escort.cs` the rule in the tick, `Party/PartyMembership.cs` the gestures. Plain
use joins or leaves, alternate use opens the screen — **a change**, since use used to open the
screen, and justified because the screen keeps two other ways in and recruiting had none.

Verified by a `party` slice that runs: 28 m closed to 4 m, then zero creep in two seconds, and an
exhausted villager that stays with its player in a party and lies down out of one. The old
`chop`, `travel`, `queue` and `eat` slices were re-run and pass — `eat` for the first time ever.

## 2. The moving work area and the party queue

The player as an anchor, jobs filtered to the ones that work anywhere, the party queue, and the
greyed-out reasons for the ones that do not.

**Ships as:** "take a villager out and it will chop and mine wherever you go." This is genuinely
useful on its own and it is the first stop a player would notice.

## 3. Carrying — the four party abilities

Pack mule, gleaner, supply line, camp. Independent of each other and of combat, so they can land
one at a time and each is worth having alone.

Suggested order within: **gleaner** (smallest, reuses hauling pickup), **pack mule** (the gesture
is the hard part), **camp** (reuses the cooking work wholesale), **supply line** (largest, involves
a long unattended journey with a full bag — build it when the travel system has been exercised by
everything above).

**Ships as:** "a party is a supply chain."

## 4. Combat, in a party only

Stances, targeting, weapon errand, armour, friendly-fire exemption, death and revival.

The big one. Deliberately **after** carrying, because carrying is independent and useful, and
because by this point the party machinery has been exercised for weeks before anything starts
depending on it during a fight.

Note that armour lands here even though it touches the appearance system, which is old and settled
code — `docs/system-design.md` must be corrected in the same change that overturns it.

**Ships as:** "villagers fight for you."

## 5. Settlement defence

The same combat at home, plus the off-screen question and the reporting that makes an unattended
loss legible.

After party combat because party combat is *watched* — you are standing there when it goes wrong,
which is the only way to debug a fight. Debugging combat that only happens when nobody is looking,
before ever having seen it work in front of you, is the worst possible order.

**Ships as:** "your base defends itself."

## 6. Boats

Attach, board, fight from the deck, swim or recover, the sea halo setting.

Last of the travel work because it needs combat (fighting from a deck) and needs the party to be
solid, and because it is the part most likely to need several attempts.

**Ships as:** "take your party across the sea."

## 7. Portals

Smallest and most self-contained, and it is last because it is the only piece that knowingly breaks
the game's economy. It should land when everything else is stable, with its config switch, so that
the one setting a player reaches for is not tangled up with a feature that is still moving.

**Ships as:** "your party comes through the portal with you."

---

## Testing, throughout

The house rules apply and have earned it:

- **Pure logic goes in `Kukolony.DeterministicTests`.** Stance decisions, target selection, leash
  and comfort bands, revive timing, bag-full thresholds — all of it is arithmetic and branching
  with boundaries at both ends, which is exactly what that suite is for.
- **Every new assertion gets falsified** by re-breaking the code it guards. A green check that has
  never been shown to go red proves nothing, and this codebase has shipped one of those.
- **New in-game slices** per stop: `party`, `fight`, `defend`, `boat`. The slice harness already
  takes any name.
- **Run the old slices too.** Three severe pre-existing bugs were found by running slices that had
  nothing to do with the change in hand. This feature touches the AI tick patch, the navigation,
  the inventory and the appearance system — everything is downstream of it.

## And the debt this inherits

Still outstanding from before any of this: **the full acceptance run has not been done since Mine,
Forage, Farm, Repair, Cooking and hunger landed** — six job-blob versions and a structure-blob
version — and it is the only thing that checks a world survives save and reload. The party system
adds ZDO fields to every villager and a second job queue.

**That run should happen before stop 1, not after stop 7.**
