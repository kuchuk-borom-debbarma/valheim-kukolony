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

Phases, not sprints. **Each ends with something playable** — a state you could load a world
into and enjoy, not a state where half a feature exists. Nothing is scheduled; the order is
what depends on what.

Every phase carries the same bar as the rest of this project: deterministic checks, a
warning-free build, an in-game run that passes twice, and screenshots looked at rather than
merely captured. A phase is not done because the code exists.

### Restarting, and what that means

**The features are rebuilt from this document, not evolved from what runs today.** What
exists was built to a different plan; morphing it would mean carrying that plan's shape
around forever, and the shape is the thing being changed.

**The engine is salvaged, because it is right and was expensive.** Concretely:

- Ownership discipline, and everything measured about what survives a save.
- The pure decision core — sequencing as a function of an enum and booleans, so a work cycle
  is verified in a second rather than in a four-minute game run.
- Keep-alive: the reason off-screen work is real, and what both previous attempts lacked.
- The benchmark harness: two-phase run, paired controls, screenshots, fixture purge.
- Capability probing — structures as bags of components.
- Appearance: villagers who look like people, dressed and styled from the game's own data.

**Existing work that is not yet wanted stays in the code, unexposed.** Chopping, work areas,
outfits and equipment all work. None are wired into the new design until their turn comes.
Deleting them would throw away measurements — tool tiers, the two-pass tree, the ownership
claim before damage — that were expensive to obtain and are not written down anywhere else.

### Every job is defined before it is built

Jobs are not ported. Each is written down first in the same two terms:

- **The work** — what it actually does, step by step, and what makes a cycle finish.
- **The equipment** — what a villager must be carrying for it to be possible at all.

Then it is built. Going one at a time is deliberate: the last job list grew by analogy until
seven jobs shared settings none of them used.

### Building for a settlement, not a household

There is no population cap, and the target is high. That does **not** mean building a
scheduler for a hundred villagers before there is one villager — it means **not choosing
anything that will have to be thrown away**:

- No per-tick scan whose cost grows with the settlement. Anything that looks through every
  structure, every container or every loaded object is cached, indexed, or amortised across
  frames — from the first version, because retrofitting it means rewriting every job.
- **Answers, not searches.** "Which container takes wood" is a lookup the settlement
  maintains, not a walk over every chest performed by every villager.
- Work is claimed, so two hundred villagers do not converge on one tree.
- Zones kept alive are bounded and prioritised; villagers are cheap, loaded terrain is not.
- Anything the screen lists is paged and filtered, because a list of a hundred is not a list.

**Written for multiplayer, not tested at scale.** Every write claims ownership first and no
peer writes what it does not own — that discipline is free to keep and expensive to add
later. It is not promised, and it is not tested with a second player, until someone asks.

---

### Phase 1 — The core system

*A colony, things registered to it, people in it, and the jobs that matter — working.*

Everything else in the mod is an addition to this. Nothing here is a placeholder.

**Build**

- **The colony piece**: placed, named, with a radius that is the settlement.
- **The colony screen**: one hotkey, opens anywhere, contextual on what is being looked at.
  Switch colonies, name them, register what you are looking at, spawn and name villagers.
- **Registration by component**, with the settings living on the structure:
  - **Storage** — what belongs in this container.
  - **Processing** — what to keep this smelter or kiln fed with.
- **Villagers**: spawned from the screen, belonging to a colony, doing their job cycle.
- **The core jobs**, each specified before it is written, each driven by what structures say
  rather than by its own configuration.
- **The indexes that make it scale**, built in from the start rather than added later.

**Done when** you place a hearth, register a chest as holding wood and a smelter as wanting
coal, spawn villagers, and the settlement runs: wood is put away, the smelter stays fed, and
nothing anywhere was told a destination.

**Risks.** The contextual screen has no precedent in the existing code. And the settlement
index — the thing that answers "where does wood go" — is the piece that everything else
leans on, so it is the one worth getting right slowly.

---

### Phase 2 — A place that lives

*The settlement stops being a machine with names on it.*

**Build**

- Energy, beds, and sleeping properly — with the animation, in the bed, having walked home.
- Ambient life: day and night meaning something, sitting by fires, sheltering from rain.
- Food: eating, hunger that degrades rather than kills, cooking as work.

**Done when** you can stand in your village at dusk and it looks like somewhere people live,
with nobody doing anything useful.

---

### Phase 3 — Reach

*The settlement stops being one circle.*

**Build**

- Work areas: placed, with their own radius, registered, several selectable per job.
- Gathering, starting with chopping — the code exists and waits here for its turn.
- Equipment as a real requirement: an axe a villager must be given before it can chop.

**Done when** an area placed in a forest three hundred metres out feeds the settlement, and a
chest placed in that forest keeps its wood out there instead.

---

### Not scheduled

Defence — **decided against**. Farming, animals, crafting at stations, repair, and multiple
colonies as a first-class idea, all after the above is real.

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
- **Clothing and equipment stay in the code, unused for now.** So do chopping and work
  areas. Unexposed rather than deleted: they hold measurements — tool tiers, the two-pass
  tree, claiming ownership before damage — written down nowhere else.
- **Features are rebuilt from this document; the engine is salvaged.** What runs today was
  built to a different plan, and morphing it would carry that plan's shape around forever.
- **Every job is specified before it is built**, in two terms: the work it does, and the
  equipment it requires. One at a time — the last job list grew by analogy until seven jobs
  shared settings none of them used.
- **No population cap, and the target is high.** That means choosing nothing that must later
  be thrown away: no per-tick cost that grows with the settlement, and answers rather than
  searches. It does not mean building for a hundred villagers before there is one.
- **Written for multiplayer, not tested at scale.** The ownership discipline is free to keep
  and expensive to add later; it is neither promised nor tested until someone asks.
- **No defence.** A settlement that cannot survive a boar is accepted, deliberately.
- **Overlapping work areas are a union** — what a player drawing two circles most likely
  means.
- **Energy degrades rather than blocks**: slower, then unwilling, always saying why. Coming
  home to a stalled village is a story; coming home to corpses is a bug report.
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
