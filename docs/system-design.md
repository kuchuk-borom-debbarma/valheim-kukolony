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

It has a **radius**. That radius is the settlement — the edge of what belongs to this colony
and what its people concern themselves with.

*Open:* whether the radius is fixed, configurable, or something that grows; what happens when
two colonies' radii overlap; whether one player may have several.

### The tool

An equippable item, held in the hand. It is how a player acts **on** the settlement rather
than through a menu — the single physical object that means "I am doing colony things now".

Its uses are listed below, one at a time, as we decide them.

*Uses:*

1. *(to be listed)*

*Open:* how it is obtained; whether it is one item or a family; whether it works outside a
colony radius.

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

### 5. How far does the settlement extend?

Chopping already sends villagers well beyond the hearth. Is the settlement a **place** with an
edge, or a radius that follows the work? This decides whether "in the village" is a meaningful
phrase.
