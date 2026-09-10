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

**An area is a neutral place, not a kind of work.** It says *here*, nothing more. Which work
happens there is the job's business, so one forest can be logged by one villager and foraged
by another without needing two markers standing in the same trees.

**A job chooses its areas, and may choose several.** Configuring a job includes picking which
areas it works in — a hauling job might sweep the village and two outposts; a chopping job
might name three separate woods.

**The colony is always one of them.** Every job includes the settlement itself without being
told to, so a job with nothing selected still works around home. Areas are added reach, never
a replacement for it, and there is no way to configure a job into having nowhere to work.

*Open:* what happens when areas overlap.

### Which decides what: places versus things

Two questions that look alike and are answered by different halves of the design. Worth
stating plainly, because getting them the wrong way round would put every setting in the
wrong place.

- **Where does work happen?** The **job** decides, by choosing areas. Deliberate, because
  only the player knows which forest is theirs to cut and which they are saving.
- **Where does the result go?** The **structure** decides, by saying what belongs in it. A
  hauling job never names a destination; it asks the settlement where wood goes.

A pleasant consequence: because storage is chosen by what a container says rather than by
distance, putting a chest inside a distant work area is all it takes for wood to accumulate
out there instead of being walked home. The outpost becomes a real outpost, and nothing had
to be added to make that work.

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

**A villager always comes home to sleep.** Whatever it was doing and wherever it was doing
it, a tired villager walks back to its own bed. Home is a place it returns to, which is what
makes it home rather than a spawn point, and it keeps the settlement the centre of a
villager's life even when its work is somewhere else.

This gives distance a price. An outpost three hundred metres out costs a round trip every
time somebody tires, so placing one far away is a trade rather than a free win. That is the
right shape: the player chose the distance, and the cost is legible rather than hidden.

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

### How to read this

Phases, not sprints. **Each one ends with something playable** — a state you could load a
world into and enjoy, not a state where half a feature exists. Nothing is scheduled; the
order is what depends on what, and what earns the most feeling per unit of work.

Every phase carries the same bar as everything else here: the deterministic checks, a
warning-free build, an in-game run that passes twice, and screenshots looked at rather than
merely captured. A phase is not done because the code exists.

**What already exists is treated as material, not as a plan.** The engine underneath is
sound and was expensive to get right, so it gets reused; the features are whatever this
document says, and anything that disagrees loses. See *Salvage* below.

### Salvage

Kept because it is correct and hard-won, regardless of what changes above it:

- **Ownership discipline** — claim before writing, everywhere.
- **The pure decision core** — job sequencing as a function of an enum and booleans, verified
  in about a second instead of only in a four-minute game run.
- **Keep-alive** — the reason work off-screen is real, and the thing the previous attempts
  never built.
- **The benchmark harness** — two-phase run, screenshots, paired controls, fixture purge.
- **Registration and capability probing** — structures as bags of components.
- **Appearance** — villagers look like people, dressed and styled from the game's own data.
- **Persistence** — versioned ZDO records, durable references, and everything measured about
  what survives a save.

Rewritten to match this document rather than preserved: how jobs are configured, what the
screen looks like, and how work finds its destination.

---

### Phase 1 — Somewhere to live

*A place, with people in it, doing one useful thing.*

The smallest thing that is recognisably the vision rather than a demo. Everything after this
adds to a settlement that already exists; nothing after this has to invent one.

**Build**

- The colony piece, its radius, and naming.
- The colony screen: one hotkey, opens anywhere, contextual on what is being looked at.
- Registration of **storage**, with its settings: what belongs in this container.
- Spawning villagers from the screen; naming them.
- One job — **put things where they belong** — driven entirely by what containers say.
- Idling that does not look like a crash: villagers loiter around the hearth rather than
  standing rigid.

**Done when** you place a hearth, register a chest as holding wood, spawn two villagers, drop
wood on the ground, and it ends up in the chest without anyone configuring a destination —
and the villagers then behave like people waiting rather than statues.

**Risk.** The contextual screen is the piece with no precedent in the current code. Everything
else is a reshaping of something that already runs.

---

### Phase 2 — They keep themselves

*The settlement stops being a machine with names on it.*

Deliberately second, ahead of more work types. A villager that tires, walks home and sleeps
does more for "this place is inhabited" than a third kind of job ever will, and energy is the
safest need to build first because it is self-resolving.

**Build**

- Energy: spent by working, recovered by resting.
- Beds as a registered component, assigned to one villager.
- Sleeping properly — the animation, in the bed, at the right time.
- Coming home from wherever they were.
- No bed: idling by the colony piece and recovering slowly, so nothing can deadlock.

**Done when** a villager works until tired, walks home across the settlement, sleeps, wakes,
and goes back to work — and one without a bed does the same, worse, without ever getting
stuck.

**Risk.** Sleeping is an animation problem, and animation on a cloned rig is asset data. It
gets probed before it gets designed.

---

### Phase 3 — The settlement does work

*More than one kind of work, and somewhere for everything to go.*

This is where the central claim of the design gets tested: that structures saying what they
want is enough, and jobs need almost no configuration.

**Build**

- **Processing** as a registered component: smelters, kilns and relatives, configured with
  what to keep them fed.
- A job that keeps processing fed, deriving everything from the structures.
- The **junk area** component, and the three-stage answer to work with nowhere to go: refuse
  to start, fall back to junk, drop as a last resort.
- Job presets, so a settlement of a dozen is not a dozen configurations.

**Done when** registering a smelter and saying "coal" is the entire configuration required
for it to stay fed forever, and a full settlement degrades visibly instead of stalling
silently.

**Risk.** The pre-check ("can I finish this before I start?") is the part most likely to be
subtly wrong, because it has to be right without being expensive.

---

### Phase 4 — Reach

*The settlement stops being one circle.*

**Build**

- **Work areas**: placed, with their own radius, registered, belonging to a colony.
- Jobs selecting several areas; the colony always included.
- Gathering — chopping first, since it exercises discovery, ownership, tool requirements and
  the two-pass tree all at once.
- Tools: villagers need an axe, and there has to be a way to give them one.

**Done when** you place an area in a forest three hundred metres out, and wood from it ends
up in the settlement without you doing anything else — and a chest placed in that forest
keeps its wood out there instead.

**Risk.** Travel cost. If energy drains faster than a round trip, distant areas are a trap
rather than a trade, and that is a number that can only be found by playing it.

---

### Phase 5 — A place that feels lived in

*The phase that is the actual point.*

Everything before this makes a settlement that works. This makes one worth standing in.

**Build**

- Day and night meaning something: sleeping at night, working by day.
- Ambient behaviour — sitting by fires, sheltering from rain, standing about together.
- Villagers using the settlement's own furniture rather than ignoring it.
- The colony screen saying how the place is doing, in words a person would use.

**Done when** you can stand in your own village at dusk and it looks like somewhere people
live, with nobody doing anything useful.

---

### Phase 6 — Food

*The first need with a supply chain, and the first that can genuinely go wrong.*

Deliberately last of the needs. Unlike energy, it can deadlock — which is exactly why it
waits until the settlement is otherwise trustworthy and the failure is visible rather than
mysterious.

**Build**

- Villagers eating, from somewhere the settlement stocks.
- Hunger with teeth that degrade rather than kill: slower, then unwilling, and saying so.
- Cooking as work, so food is something the settlement makes rather than something you feed
  it.

**Risk.** This is where "keeps going while you are away" and "they are people who eat" collide.
It is written down in *The tension to resolve first* and it does not get built until that is
settled.

---

### Not scheduled

Wanted, but not until the above is real: multiple colonies as a first-class thing, defence,
farming, animals, crafting at stations, repair.

---

### Assumptions in this ordering

Marked because they are guesses standing in for answers, and any of them could reorder the
work. Correct them and the roadmap changes.

- **Defence is not in it.** Asked three times, unanswered, so treated as out of scope. If a
  settlement must survive a boar, that is a phase of its own and it belongs after phase 4.
- **A handful of villagers**, not thirty. Scheduling stays simple, per-villager scans stay
  affordable. Designed not to preclude more, not built for it.
- **Energy degrades rather than blocks** — slower, then unwilling, always saying why.
- **Single-player first.** The ownership discipline is kept because it is already written and
  correct; it is not promised or tested until someone asks for multiplayer.
- **Overlapping work areas are a union**, because that is what a player drawing two circles
  most likely means.
- **Personal use before release.** No packaging, no compatibility work, no Thunderstore, until
  the thing is worth other people having.

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

## What we have measured

Findings from tests run against the real game, not reasoning about it. Each one closed a
question that was otherwise going to be guessed at.

### A destroyed object *can* be told from one that is merely not loaded

Measured three ways in one run:

```
alive      resolves=True   listedDead=False
destroyed  resolves=False  listedDead=True
unloaded   resolves=True   listedDead=False    (a real chest, 900m away, after its zone unloaded)
```

**An unloaded structure stays known.** `ZDOMan.GetZDO` still answers for a chest whose zone
the game has stopped keeping instantiated, so a lookup returning nothing already means
destroyed. The game's dead-ZDO list confirms it independently, but no bookkeeping of our own
is needed.

This is what makes "never delete an outpost by walking away from it" free rather than
expensive. Neither previous mod distinguished these: both treated a missing instance as "not
loaded" and teleported to it, and the newer one's answer to the whole problem was a comment
requiring players to install a third-party chunk loader so nothing was ever unloaded.

Worth remembering that vanilla does not fully trust the check either — its own creature
spawner pairs it with an `alive_time` heartbeat so a wrong answer only delays a respawn.

### Villagers were drawn with the ghost rig's own shader

Their body and hair used `Custom/Fallen Warrior`; only their clothing used `Custom/Player`.
That is why they glowed gold while the player did not, and why removing the rig's lights and
particles did not fix it: the glow was in the skin material, not in an effect.

Fixed by taking the player's own materials at runtime — not at prefab build time, which
happens before the scene has a player to copy from, and not by tinting, which would mean
editing a shared material and repainting every Fallen Warrior in the world.

Two things this taught that generalise:

- **Repaint by shader, not by knowing which renderer is which.** Hair and beards are built
  after the body and separately, so repainting the body left villagers with normal skin and a
  glowing haircut.
- **It is a race, so it needs a budget.** A twelve-tick budget lost to hair; it now keeps
  looking until several passes running find nothing.

### The game can dress and style villagers itself

Nothing needs a hand-written list of prefab names, and every such list this project has
written has been wrong or gone stale. Read from the game instead:

- **Clothing** — items of each visible slot type, **filtered to what has a recipe**. "Anything
  wearable" turned out to include monster armour, and villagers came back in golem plate and
  fenring boots. Having a recipe is the game's own answer to "could a player have this".
  Yields 41 chest, 19 legs, 35 helmet, 11 shoulder, 1 utility.
- **Hair and beards** — items typed as customisation, the same set the player's own
  appearance screen offers: 87 hairstyles and 27 beards, against the 12 and 10 a hand-written
  list had.

Wearing something is a picture, not protection, so a villager in wolf armour is dressed
rather than armoured — this costs nothing and breaks no boundary.

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
- **A villager always comes home to sleep**, from wherever it was working. Distance to an
  outpost costs a round trip: a legible price the player chose.
- **A bed is an upgrade, not a requirement.** No bed means slow recovery idling by the colony
  piece, so a settlement cannot deadlock for want of furniture.
- **Areas are neutral places; jobs point at them, and may point at several.** The colony is
  always included, so no job can be configured into having nowhere to work.
- **Jobs choose where work happens; structures choose where results go.**
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
