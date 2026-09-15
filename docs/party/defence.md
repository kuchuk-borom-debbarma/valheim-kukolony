# Settlement defence

The same combat, at home. A Kolony defends itself — **including when nobody is watching.**

## Why this is the sharp edge

Everything else in this mod that can lose you something was built to warn you first. Starvation
takes a configurable grace, says so through `Chatter`, stops the villager working, puts a red row
on the Kolony screen, and **ships with killing switched off by default** so that a settlement
cannot lose people before its owner has watched it feed itself once.

This decision goes the other way, deliberately and with the consequence stated: **villagers can
die at home while you are asleep.** A raid on an unattended base is fought by whoever is there, and
some of them lose.

Three things keep that from being unfair rather than merely harsh:

- **Death is not permanent.** They drop their load and wake at their bed after a while. The cost is
  the scattered bag and the downtime, not the villager.
- **The revive timer bounds the disaster.** A base that cannot win stops producing defenders
  instead of producing them forever.
- **It must be reported properly.** Coming back to a settlement and having to work out what
  happened from a strewn field is the failure mode. What died, where, to what, and when — a
  standing record, not a line in a log that has already scrolled.

That last one is not decoration. It is the difference between a feature and a mystery.

## What "off-screen" actually means here

The keep-alive holds zones open around villagers and registered structures, so an unattended base
*is* simulated — that is the mod's whole premise and it already works for jobs.

Combat asks more of it than work does:

- **Hostiles must be spawned and ticking.** Work only needs the trees to exist. A raid needs the
  spawn system to run in a zone with no player in it, and that is Valheim's behaviour to discover,
  not the mod's to assume. **This is the single largest unknown in the feature** — see
  [`unknowns.md`](unknowns.md).
- **Damage must apply to things nobody is looking at.** Health lives on the ZDO, which is
  promising, but the path from "a greyling swings" to "the ZDO's health drops" runs through code
  that normally has an observer.
- **It must not cost anything when there is no raid.** A settlement at peace must not pay for
  combat, because the no-population-cap rule means whatever this costs is multiplied by everybody.

## Model

**A reflex, not a job.** There is no Guard to assign. Any villager fights when its stance says to,
whatever it was doing — drops the work, fights, returns to it. A base is defended by whoever is
home.

This was chosen over a Guard job and it is the right call for a first version: a Guard job means
deciding patrol routes, posts, and what happens when the guard is the only one who can fight. A
reflex means the answer to "who defends the base" is "everyone who is in it", which needs no setup
and cannot be got wrong.

**Stance still applies.** A settlement of Passive villagers does not defend itself. That is a
legitimate thing to configure and a very easy thing to configure by accident, so the Kolony screen
should say when nobody at home will fight.

## What this does not include

- **No walls, no towers, no siege.** Defence is villagers with weapons.
- **No patrols.** A reflex engages what comes; it does not go looking.
- **No retreat to safety.** A losing villager fights until it loses. Fleeing is a later feature and
  interacts badly with the work system, which assumes a villager goes where it is sent.
