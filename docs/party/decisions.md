# Decisions

Settled in conversation, with the reason and — where there is one — the cost that was accepted
along with it. Recorded so they are not reopened halfway through building.

Where a decision has a **cost**, that cost was stated before the choice was made. Nothing here was
agreed to blind.

---

## The party itself

**A villager joins by being interacted with, and there is no cap.**
Walk up, press use. The limit on a party is what you can feed and arm, not a number — which is the
same answer the settlement itself gives, where there is no population cap either.
*Cost:* a large party is a real performance question, and nothing in the design bounds it. See
[`unknowns.md`](unknowns.md).

**The player is a moving work area.**
While in a party, a villager's work is bounded by a radius around the player instead of by the
hearth and the work flags. This reuses the flag machinery wholesale — jobs already scope to a list
of areas, and `KolonyReach` already answers "is this our ground". A party is a flag that walks.

**Jobs that need the settlement are hidden while in a party.**
Craft and Tend need stations, Farm needs a field, Repair needs a crafting station in range, Haul
needs registered chests. In a party those are greyed out with a reason rather than silently
failing. **The reason matters more than the hiding** — this mod has shipped silent stalls before,
and a job that quietly does nothing is the failure mode that costs the most to find.

**A party villager runs a separate party queue.**
Not a filter on its home queue: its own second queue that applies only while in a party. So a
chopper at home can be a hauler in the field, deliberately.
*Cost:* one more thing to configure per villager, and a second queue to persist, migrate and show.

**When the player logs off or dies, the party waits, then walks home — and goes back to the jobs
it had before it joined.**
A forgotten party does not stand in a field forever, and nothing is teleported. The restoration is
the part to get right: joining a party must remember what the villager was doing so that leaving
one can put it back. A villager that came home from an expedition and had forgotten it was a
farmer would be a bug nobody noticed until the crops failed.

---

## Combat

**Villagers fight for the player.** Full combat: they take targets and attack. This is the largest
part of the feature.

**The same fighting defends the Kolony**, not only a party. A base is defended by whoever is home.

**Fighting is a reflex, not a job.** Any villager fights when its stance says to, whatever it was
doing — it drops its work, fights, and goes back. There is no Guard job to assign.
*Consequence:* it belongs with eating and resting, asked on the work tick before the queue, and it
pre-empts them both. A villager being hit has nothing useful to offer any job.

**Aggression is a player-set stance, per villager.** Aggressive, Defensive, Passive.
*New villagers default to Defensive* — it never starts a fight you did not want.

**Weapons are fetched, exactly as axes and pickaxes are.**
A villager with no weapon walks to a registered chest and takes the best one it may have, through
the existing `Errand`. Arming a party is "put swords in a chest". The same rule that governs tools
governs weapons: **a chest marked *villagers may not use what is here* keeps its swords.**

**Armour is real.** This **overturns** `docs/system-design.md`, which says wearing something is a
picture rather than protection. Worn chest, legs and helmet now contribute real armour.

**Existing villagers keep the outfit they already rolled, and it now protects them.**
No reset, no reroll.
*Cost, accepted:* survivability was assigned at random, before anybody knew it would matter. Some
villagers are in padded plate and some are in rags for no reason anyone chose. This will look like
a bug and is not one — it is the price of not taking anybody's gear away.

**Neither side can hurt the other.** A villager's swing cannot hit the player and the player's
cannot hit a villager. You can fight in a crowd without losing somebody to your own axe.

---

## Death

**A dead villager drops its bag where it fell and revives at its bed, or the hearth if it has no
bed.** Death is not permanent. The cost is the load, the walk back to fetch it, and the time.

**Revival takes real time**, configurable. This is what stops a losing fight becoming an endless
respawn loop: a base being overrun runs out of defenders and loses, rather than feeding bodies in
forever.

**A Kolony fights raids with nobody watching, and villagers can die while you are asleep.**
This was chosen with the consequence stated. It is the sharpest edge in the mod: the existing
starvation feature was deliberately shipped with killing switched *off* by default for exactly
this reason, and this decision goes the other way. See the risks in [`defence.md`](defence.md).

---

## Away from home

**A party villager eats out of its own bag, and then out of the player's.**
Provisioning matters, and you are never ambushed by a starving villager while you are carrying
food. It reuses the hunger rule that already exists; only the *source* changes.

**Villagers follow through portals and onto boats.**
*Cost, accepted:* Valheim refuses tamed creatures at portals, so this needs real patching — and a
villager's 24-slot bag becomes a way to move metal through a portal, which the game deliberately
forbids. **That exploit is a direct consequence of this choice.** A config switch will exist to
restore the vanilla rule; it will not be the default.

**On a boat a villager attaches to the deck and fights from it**, rather than walking around on it.
It boards when the player boards.
*Why:* there is no navmesh over water. A villager aboard that still believes it is a pedestrian
walks off the side. Attaching is not decoration — it is the thing that stops the sea eating your
party. See [`travel.md`](travel.md).

**A villager in deep water swims if the creature can swim, and is recovered to the boat if it
cannot.** Which of those applies is a **measurement**, not a decision — `m_canSwim` is per-prefab
asset data and has not been read yet.

**Villagers at sea keep holding zones open.**
*Cost, accepted, and I advised against it:* every villager aboard keeps its keep-alive halo, so a
five-villager crossing streams five haloes' worth of ocean that nothing is using — the player is
already loading the water ahead. The halo will be configurable so it can be turned down if a
crossing stutters. If it does stutter, this is the first thing to look at.

---

## The four party abilities

All four were chosen. Detail in [`party.md`](party.md).

- **Pack mule** — takes items off a full player inventory, gives them back on request.
- **Gleaner** — picks up what is dropped near the party.
- **Supply line** — when its bag fills it leaves, walks home, files everything in the right
  chests, and comes back.
- **Camp** — lights and feeds a fire so the party can rest away from home.
