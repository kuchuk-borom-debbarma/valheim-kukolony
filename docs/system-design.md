# System design

A living record of how the colony is *meant* to work, and why. Written as we decide, so
the reasoning survives the decision.

The other docs describe what the code does today. This one describes what it should do,
and is the place an argument gets settled once instead of every time it comes up.

---

## Already settled

These are not up for debate unless something forces them open. They are load-bearing, and
most of them were paid for with a bug.

**Only the owner writes.** Every write goes to a ZDO this peer owns; a write to one it does
not is discarded on the next sync and looks exactly like a write that worked. Ownership is
claimed before writing, never assumed.

**Facts outrank recorded state.** A villager's state says where it got to; the world says
what is still true, and the world wins. This is what repairs an interrupted cycle instead of
stranding it, and it is why a lost target is a fresh decision rather than a fault.

**Decisions are pure; doing is not.** What a villager does next is a function of an enum and
a handful of booleans, with no Unity and no colony types in it. That is what lets whole work
cycles be verified in about a second rather than only inside a four-minute game run.

**A job cannot offer a setting it ignores.** One declaration drives both the engine and the
panel. A control that changes nothing is worse than a missing one.

**Nothing fails silently.** A villager that is not working says why, in words, where a player
will see it. Silence is the failure mode every other rule here exists to avoid.

**Off-screen behaves like on-screen.** Work that only happens when someone is watching is a
lie. Zones are kept alive deliberately and selectively, and what a job needs loaded is
declared by the job.

---

## Open questions

Numbered so we can refer back. Each records the options as they stand and what each would
cost, not a recommendation dressed as a summary.

### 1. What decides what a villager does next?

Today: each villager holds an ordered **queue** of job ids, each with a repeat count. It runs
an entry until the count is spent or there is nothing to do, then moves to the next, and
wraps around.

- **Queue (today).** Predictable and explicit. The player says the order. Poor at reacting:
  a villager working through its queue cannot notice that something more urgent appeared.
- **Priorities.** Each villager ranks kinds of work; it always does the most important thing
  it *can* do right now. Familiar from colony games. Reacts well, but a player cannot say
  "do this, then that" — order is emergent.
- **Both.** Priorities decide what to pick up, a queue expresses a deliberate sequence.
  Twice the concepts to learn.

### 2. What creates work — a job, or a want?

Today: a **job** is a standing instruction ("haul wood to that chest"), and stock limits are
its brake.

- **Jobs (today).** Direct. The player configures the doing.
- **Wants.** The colony declares outcomes ("keep 200 wood in storage", "keep the smelter
  fed") and villagers work out the doing. Fewer knobs, more surprises, and much harder to
  explain when nothing happens.

### 3. How many villagers is a colony meant to hold?

Affects nearly everything: whether scheduling needs to be clever, whether per-villager scans
are affordable, whether the panel needs different navigation. Nothing today assumes a number.

### 4. Is multiplayer a constraint or an aspiration?

The ownership discipline is already there and it is the expensive part. Committing means
testing it; not committing means it quietly rots.

---

## Decisions

Filled in as we settle them.
