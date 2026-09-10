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

Defined one at a time, deliberately, before anything is built on them.

### The colony piece

A buildable piece placed in the world. It **is** the colony: there is no colony without one,
and everything a colony owns is owned relative to this. Worth being a physical thing rather
than dissolving into "wherever you registered stuff", because the vision is a *place* — a
settlement needs a centre people gather to, return to, and are counted from.

It has a **radius**: the settlement's edge, and the world its people concern themselves with.

### The colony screen

One screen, opened by one hotkey, from anywhere. Not tied to standing at the hearth, which
would only punish a player for being elsewhere.

It is **contextual**: what it offers depends on what the player was looking at when it
opened. Looking at an unregistered structure adds the option to register it; looking at
nothing offers management only.

*There is no separate tool.* A held item was considered, to give pointing-at-things a set of
verbs. The contextual screen buys the same thing without an item to craft, lose, or explain,
so the item is not worth its weight.

From the screen:

- switch between colonies, when there is more than one
- name the colony
- register what the player is looking at
- spawn villagers into the colony currently selected
- name villagers, set their job cycle, assign presets
- assign a villager a bed

### Registration

A structure joins the colony by being **looked at** and registered from the screen.

**Only structures with a component the colony understands may be registered.** The list of
understood components is the whole of what a colony can use, and growing it is how the mod
grows. To begin with:

- **Storage** — a container. Configured with what belongs in it.
- **Processing** — a smelter, kiln and their relatives. Configured with what to keep it fed.
- **Rest** — a bed, assigned to one villager.
- **Junk** — where things go that belong nowhere else. See *Work with nowhere to go*.

A structure is a bag of capabilities, not a kind of building: whatever components it has, it
gets those settings. Nothing enumerates kinds of building, and a new capability is a new
entry rather than a new branch.

**Registration is where a structure's settings live.** That is why it is not a second radius:
a radius can say what the settlement reaches, but nothing about a radius can say what a chest
is *for*. Membership is a side effect of having settings.

### Structure settings drive the work

Decided: **structures say where things belong; jobs say what kind of work is done.**

A hauling job is not told where to put wood. It picks wood up and asks the settlement where
wood goes — and a container that was configured to hold wood answers. A processing job does
not need a list of smelters; it asks which registered smelters want feeding.

This is the simplification the whole design rests on. Configuration lives on the physical
thing the player is already looking at, and a job carries almost nothing.

### Work areas

A settlement is a place, and a place has an edge. But the work does not: trees, ore and
everything else worth gathering are wherever the world put them, which is usually not next to
the house.

Resolving this by making the colony radius enormous would be the wrong fix. The radius is
what makes the settlement *a place*; inflate it and it stops meaning anything, and "in the
village" stops being a phrase that means something.

Instead: **a work area is a placed structure with its own radius, belonging to a colony.**

- Put one down in a forest 300 metres away and that forest is where the settlement gets wood.
- It is registered like anything else, and carries the settings for what happens inside it.
- Villagers walk there and back. That takes time, which is correct: they are people.

This keeps the settlement small and the reach large, and it makes reach something a player
*places* rather than a number they raise. A colony's territory becomes a shape they drew
rather than a circle they inflated.

**Travel is already paid for.** Zones are kept alive around villagers as they move, so one
walking to a distant work area keeps its own corridor loaded and works when it arrives. The
cost is real — a villager crossing open country forces zones along the way — and is the price
of the work being genuine rather than pretend.

*Open:* whether a work area names the kind of work, or is a neutral place that jobs point at;
what happens when areas overlap; whether a villager assigned far away should sleep out there.

### Villagers

Spawned from the screen, into the selected colony. Not by a hotkey — spawning a person is a
deliberate act, not a gesture.

Each has a name the player may change, and a **job cycle**: an ordered list of work it moves
through. Jobs carry only what they genuinely need — chopping needs an axe, and perhaps what
kind of wood — because where things go is the structures' business now.

**Presets** are named job configurations. Assigning work to a villager means assigning a job
and a preset, so a settlement of a dozen people is not a dozen separate configurations.

### Work with nowhere to go

Decided, and it is a three-stage answer because the failure has three different causes.

**Refuse to start.** A job checks it has somewhere to put the result *before* it begins. A
villager that would have nowhere to put wood does not pick the wood up; it says so and does
something else. Most of the time this is the whole answer.

**Fall back to the junk area.** A check that passed can still be wrong by the time the
villager gets back — the space filled up while it was walking. Rather than stranding a
carried item, it goes to the **junk area**: a registered structure that exists to be the
answer to "nowhere else". A settlement with one has no unhandled case.

**Drop it.** No junk area and nowhere to store it: put it on the ground and say so. Ugly on
purpose. Nothing is ever destroyed and nothing is ever silently held forever.

### Beds, and rest

A bed is a registered structure like any other, assigned to one villager.

Work costs **energy**. A tired villager goes to its bed and sleeps — properly, with the
animation — and comes back rested.

**A bed is an upgrade, not a requirement.** A villager without one goes and idles by the
colony piece and recovers there — far more slowly than in a bed, but it does recover. So a
settlement can never deadlock for want of furniture; beds make it *better*, not possible.

Energy is a good first need precisely because it is **self-resolving**: a villager can always
fix it by itself. Nothing has to be supplied, so a settlement left alone for a week cannot
starve. Food, which needs a supply chain, is the harder case and is not being taken on yet.

### Clothing and equipment

The system stays; it is not used for now. Villagers keep the clothes they are born in, and
tools are given the same way they are today.

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

Settled, with the reason.

- **The colony is a placed piece with a radius.** The vision is a place; a place has a centre.
- **One screen, one hotkey, opened from anywhere, contextual on what you are looking at.**
- **No held tool.** A contextual screen buys the same verbs without an item to craft or lose.
- **Registration is per-component, and is where a structure's settings live.** Not a second
  radius: a radius cannot say what a chest is for.
- **Structure settings drive the work.** Structures say where things belong; jobs say what
  kind of work is done. A haul job asks the settlement where wood goes.
- **Villagers are spawned from the screen**, and run an ordered **job cycle** with named
  presets.
- **Energy first, food later.** Energy is self-resolving given a bed, so a settlement left
  alone cannot deadlock on it.
- **Clothing and equipment stay in the code, unused for now.**
- **A job refuses to start rather than stranding its result**, falls back to a junk area, and
  drops on the ground only as a last resort. Nothing is destroyed, nothing is held silently.
- **A bed is an upgrade, not a requirement.** No bed means slow recovery idling by the colony
  piece, so a settlement cannot deadlock for want of furniture.
- **Reach is placed, not raised.** The colony radius stays modest so the settlement remains a
  place; a **work area** is a separate placed structure with its own radius, anywhere, that
  says where a kind of work happens. Territory is a shape a player draws.
- **Never infer destruction from absence.** A record is removed only on positive evidence:
  the object was seen destroyed, or the player said so. Anything else is dormant and visible.
  A ZDO that cannot be resolved may be destroyed *or* merely not in memory, and the two are
  indistinguishable from the outside — including through the persistent token, which is
  stored on the ZDO and so needs the ZDO to read it.

---

## Open questions

Numbered so we can refer back.

### 1. *(settled — see Decisions: never infer destruction from absence)*

### 4. Does a settlement have to survive being attacked?

A village of people who stand still while a boar kills them is not a village. But the
previous mod's own notes record that working villagers mostly failed to defend themselves,
because the work loop starved the combat AI. Options: no defence; flee indoors; defend
themselves but never seek a fight.
