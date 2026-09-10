# Kukolony — vision, boundaries, roadmap

A living record of what this mod is for, what it refuses to be, and the order things get
built. Written as we decide, so the reasoning survives the decision.

The other docs describe what the code does today. This one decides what it should do. When
the two disagree, this one is the argument and the code is the bug.

**Status: draft.** The vision and boundaries below are a first proposal, written to be argued
with rather than agreed to. Nothing here is settled until it moves to *Decisions*.

---

## Vision (draft)

**A Valheim base should feel inhabited, and should keep working when you are not there.**

You build a hearth. People join it. The upkeep that makes a base tedious — carrying drops to
chests, feeding fires, loading smelters, cutting firewood — happens without you doing it by
hand. You leave to explore, sail, or fight a boss. You come back and the place has carried on.

The player is a **chieftain, not a foreman**. You say what should happen and roughly how; you
do not stand over anyone. Configuration is something done occasionally and then left alone.
A colony that needs constant attention has failed at its one job.

**Villagers are people, not machinery.** They have names, faces, clothes, and tools they had
to be given. They walk places and take time. The point is not throughput.

---

## Boundaries (draft)

What this refuses to be, so the answer to "could villagers also…" is already written down.

**Not a combat mod.** Villagers work. They do not garrison, patrol, or form a militia, and
the colony is not a defence answer.

**Not a needs simulation.** No hunger, sleep, mood, illness, relationships or opinions. A
villager that will not work says so in plain words; it is never sulking.

**Not a replacement for playing Valheim.** Villagers do chores in and around the base. They
do not adventure — no sailing, no dungeons, no fetching from a biome you have not been to,
no fighting bosses. Anything that would let you skip the game is out.

**Not a cheat.** Work costs real materials, real tools, and real time. Nothing is conjured,
nothing is free, and a villager is never strictly better than doing it yourself.

**Not an economy or a story.** No currency, trade, production chains beyond Valheim's own, no
dialogue, no quests. Names and appearance are flavour.

**Not a framework.** This is one opinionated mod, not a platform for other people's NPCs.

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
