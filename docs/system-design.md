# Kukolony — vision, boundaries, roadmap

A living record of what this mod is for, what it refuses to be, and the order things get
built. Written as we decide, so the reasoning survives the decision.

The other docs describe what the code does today. This one decides what it should do. When
the two disagree, this one is the argument and the code is the bug.

**Status: draft.** The vision and boundaries below are a first proposal, written to be argued
with rather than agreed to. Nothing here is settled until it moves to *Decisions*.

---

## Vision (draft)

**A settlement, not a set of tools.** Kukolony turns a base into a place people live. You
build a hearth, people gather to it, and the base becomes a small community that works,
feeds itself, keeps itself in order, and carries on being a place while you are away.

The work matters — hauling, chopping, smelting, cooking — but it is not the point. The point
is that the base is **inhabited**. Someone is always doing something. The fire is fed because
somebody fed it. The wood is stacked because somebody stacked it. When you come back from a
week at sea, the village has been living.

The player is a **chieftain, not a foreman**. You decide what the settlement is for and give
it what it needs; you do not stand over anyone. Configuration is occasional, not constant.

**Villagers are people.** Names, faces, clothes, tools they had to be given. They eat. They
keep themselves in order. They walk places and take time. A villager is not a machine with a
name painted on it, and the settlement is not a throughput problem.

---

## Boundaries (draft)

What this refuses to be, so "could villagers also…" already has an answer.

**Not a replacement for playing Valheim.** Villagers live and work in and around the
settlement. They do not adventure — no sailing, no dungeons, no fetching from a biome you
have not been to, no fighting bosses. Anything that lets you skip the game is out.

**Not a cheat.** Work costs real materials, real tools, and real time. Food is eaten, tools
are carried, nothing is conjured. A villager is never strictly better than doing it yourself;
the value is that they do it while you are elsewhere.

**Not a combat mod.** *(Under review — a settlement that cannot survive a boar is a hard sell.
See question 4.)* Villagers work; they are not a garrison and the settlement is not a defence
answer.

**Not a psychology.** They eat and keep themselves in order, but they have no mood, no
opinions, no relationships, no grudges. A villager that will not work says so in plain words.
It is never sulking, and there is never a hidden number to guess at.

**Not an economy or a story.** No currency, no trade, no production chains beyond Valheim's
own, no dialogue trees, no quests. Names and appearance are flavour.

**Not a framework.** One opinionated mod, not a platform for other people's NPCs.

---

## The tension to resolve first

Two things in the vision pull against each other, and most of the open questions below are
really this same question wearing different clothes:

> **"It keeps going while you are away"** wants a settlement that cannot get into a state you
> have to come home and fix.
>
> **"They are people who eat and keep themselves in order"** wants needs that can go unmet —
> and a need that cannot go unmet is not a need, it is decoration.

A settlement that quietly starves while you are at sea is a betrayal of the first. A
settlement whose needs never bite is a betrayal of the second. Where the line sits decides
what this mod actually is, so it is question 1.

## Core objects

The things the whole design is built out of. Defined one at a time, deliberately, before
anything is built on top of them.

### The colony piece

A buildable piece placed in the world. It **is** the colony: there is no colony without one,
and everything a colony owns is owned relative to this.

It has a **radius** — the settlement's edge, and the world its people concern themselves with.

Interacting with it opens the colony screen. From there: name the colony, and see how it is
doing. What "how it is doing" means is decided later.

Worth keeping rather than dissolving into "wherever you registered things", because the
vision is a *place*. A settlement needs a centre — somewhere people gather to, return to, and
are counted from. A colony that is only a scattering of registered chests is a spreadsheet.

*Open:* whether the radius is fixed, configurable, or grows; overlapping colonies; more than
one per player.

### The tool

An equippable item, held in the hand.

**Its job is to buy a verb space, not a hotkey.** A single global key can only ever mean one
thing. Holding a tool puts the player in a mode where pointing at things is meaningful, the
game can show a hint bar for it, and every verb the settlement needs has somewhere to live:

- point at a structure → make it part of the colony, and configure it
- point at a villager → see them, assign them
- point at the ground → mark out an area
- point at something already registered → change it, or remove it

Without the tool each of those needs its own global key, or a menu that cannot know what you
are looking at. That is the whole argument for it; if the verb list stays at one, the tool is
ceremony and should go.

**The colony screen is not the tool, and is not the piece.** It opens from anywhere. Tying it
to walking up to the hearth punishes the player for the crime of being elsewhere.

*Open:* how it is obtained; whether it works outside a colony radius.

### Registration, and why it is not a second radius

The objection — *"we have a radius and then we also have registration, that is two gates"* —
is right if registration only answers **is this in the colony**. Then it is a permission list
duplicating a circle, and it should go.

It is not that. Registration is **where a structure's settings live**.

Nothing about a radius can tell you *what this chest is for*. The moment a container can say
"wood goes here" and a smelter can say "keep this fed with coal", every structure needs a
record of its own, and that record is registration. Membership is a side effect of having
settings, not the point of it.

So the two answer different questions:

- **Radius** — what the settlement *reaches*. Its edge, and the villagers' world.
- **Registration** — what the settlement *uses*, and *how*.

A structure that drifts out of radius is not deregistered; it goes dormant and says so, and
comes back when the settlement reaches it again.

### Structures are configured by component

A structure is not a *kind of thing*, it is a bag of capabilities, and each capability brings
its own settings.

- has a **Container** → what is stored here
- has a **Smelter** → what it is kept fed with
- has a **Fireplace**, a **CookingStation**, a **Beehive** → whatever each of those needs

A chest that is also something else gets both sets. Nothing has to enumerate kinds of
building, and supporting a new one is a new capability rather than a new branch — the same
shape jobs and station protocols already use.

**This has a consequence worth deciding deliberately.** Once a chest can say "wood goes
here", a hauling job no longer needs to be told where to put wood; it can ask the settlement.
Structure settings could absorb most of what job configuration does today, which would leave
jobs saying *what kind of work* and structures saying *where things belong*. That is a
simpler model, and a different one. See question 6.

---

## Roadmap

To be filled in once vision and boundaries settle. Ordered by what makes the colony *work*,
not by what is easiest.

---

## Ground rules

Load-bearing and mostly paid for with a bug. Not up for debate unless something forces them.

**Only the owner writes.** A write to a ZDO this peer does not own is discarded on the next
sync and looks exactly like a write that worked.

**Facts outrank recorded state.** State says where a villager got to; the world says what is
still true, and the world wins. This is what repairs an interrupted cycle instead of
stranding it.

**Decisions are pure; doing is not.** What to do next is a function of an enum and a few
booleans, with no Unity in it, so whole work cycles are verified in about a second rather
than only inside a four-minute game run.

**A job cannot offer a setting it ignores.** One declaration drives the engine and the panel.

**Nothing fails silently.** A villager that is not working says why, where a player will see
it.

**Off-screen behaves like on-screen.** Work that only happens when someone is watching is a
lie.

---

## Decisions

Settled, with the reason. Filled in as we go.

---

## Open questions

Numbered so we can refer back.

### 1. Do needs have teeth, and how sharp?

If a villager is not fed, what happens? Options, in order of how much they can ruin a week
away: nothing but a visible complaint; works slower; stops working; leaves; dies. The answer
decides whether the settlement is something you tend or something that punishes you for
leaving, and everything else about food follows from it.

### 2. Where does food come from?

The player stocks a larder and villagers take from it; or villagers cook it themselves from
what the colony has; or villagers eventually farm it. Each is a different amount of machinery
and a different failure mode when the chain breaks.

### 3. What does "doing their things" mean, concretely?

Ambient life is what makes a place feel inhabited, and none of it is work: sitting by the
fire, standing about, sleeping at night, going indoors in the rain. Cheap to fake, and it may
matter more to the feeling of a settlement than any job does. Related: does a settlement need
**homes** — beds a villager belongs to and returns to?

### 4. Does a settlement have to survive being attacked?

A village of people who stand still while a boar kills them is not a village. But combat is a
boundary I drew on purpose, and the previous mod's own notes say working villagers mostly
failed to defend themselves because the work loop starved the combat AI. Options: no defence
at all; villagers flee indoors and the player deals with it; villagers defend themselves but
never seek a fight.

### 6. Do structure settings drive the work, or only constrain it?

Once a chest can say "wood goes here":

- **Structures drive.** A haul job is told nothing about destinations; it asks where wood
  belongs. Configuration lives on the physical thing, which is where a player is already
  looking. Fewer settings, but "why did it go *there*" is answered somewhere other than
  the job.
- **Structures constrain.** Jobs still name a destination; a structure's settings only rule
  places out. More knobs, more explicit, more duplication.

This decides how much of the current job configuration survives.

### 5. How far does the settlement extend?

Chopping already sends villagers well beyond the hearth. Is the settlement a **place** with an
edge, or a radius that follows the work? This decides whether "in the village" is a meaningful
phrase.
