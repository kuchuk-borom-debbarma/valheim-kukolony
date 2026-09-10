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

### Villagers

Spawned from the screen, into the selected colony. Not by a hotkey — spawning a person is a
deliberate act, not a gesture.

Each has a name the player may change, and a **job cycle**: an ordered list of work it moves
through. Jobs carry only what they genuinely need — chopping needs an axe, and perhaps what
kind of wood — because where things go is the structures' business now.

**Presets** are named job configurations. Assigning work to a villager means assigning a job
and a preset, so a settlement of a dozen people is not a dozen separate configurations.

### Beds, and rest

A bed is a registered structure like any other, assigned to one villager.

Work costs **energy**. A tired villager goes to its bed and sleeps — properly, with the
animation — and comes back rested.

Energy is a good first need precisely because it is **self-resolving**: a villager can always
fix it by itself, given a bed. Nothing has to be supplied, so a settlement left alone for a
week cannot deadlock on it. Food, which needs a supply chain, is the harder case and is not
being taken on yet.

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

---

## Open questions

Numbered so we can refer back.

### 1. What does "invalid" mean, and what does removal cost?

Stated: a structure out of radius or destroyed becomes invalid and is removed.

**Unloaded is not destroyed, and the two look identical.** A ZDO for a chest in a zone nobody
is standing in cannot be resolved, exactly like one that was smashed. If "cannot resolve it"
means "remove the record", then walking away from an outpost silently deletes its
configuration, and it comes back empty. This has already bitten this project once, in a
persistence check that failed for four runs because an unloaded chest read as a missing one.

So: what actually justifies removal? Options are that a structure is removed only when its
ZDO is *known* destroyed, or only when the player says so, or that out-of-radius means
dormant-and-visible rather than gone.

### 2. What happens to work with nowhere to go?

If a villager picks up wood and **no** registered container claims wood: does it fall back to
any container with room, drop it, or refuse to pick it up in the first place? "Nothing
happens" is the answer the settlement must never give silently.

### 3. What happens to a villager with no bed?

Energy is self-resolving *given a bed*. Without one: never tires, tires and sleeps rough,
tires and stops working, or cannot be spawned at all. This is the "teeth" question in its
smallest and safest form.

### 4. Does a settlement have to survive being attacked?

A village of people who stand still while a boar kills them is not a village. But the
previous mod's own notes record that working villagers mostly failed to defend themselves,
because the work loop starved the combat AI. Options: no defence; flee indoors; defend
themselves but never seek a fight.

### 5. How far does the settlement extend?

Chopping sends villagers beyond the hearth. Is the radius the edge of *everything*, or the
edge of what can be *registered*, with gathering allowed to range further?
