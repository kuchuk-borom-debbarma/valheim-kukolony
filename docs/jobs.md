# Jobs

A job is a cycle of small fragments. Each fragment knows nothing about what runs before or
after it — it reads inputs from a shared context, writes outputs back, and reports whether
it is done. That ignorance is what lets one fragment appear twice in a job and be reused
unchanged by the next job.

The shape is adapted from RagnarsRokare's `IBehaviour` (see `npc-design.md` §3), with one
deliberate departure recorded below.

## The contract

```csharp
internal enum StepStatus { Running, Succeeded, Failed }

internal interface IJobStep
{
    string Name { get; }
    StepStatus Tick(JobContext context);
}
```

`JobRunner` advances on `Succeeded`, restarts the cycle after a cooldown on `Failed`, and
leaves the index alone on `Running`.

## Haul, the first job

```
find_ground_item -> move_to_target -> pick_up_item
                 -> resolve_destination -> move_to_target -> deposit_item -> (repeat)
```

`move_to_target` appears **twice**, unchanged — once to reach an item, once to reach a
chest. It works for both precisely because it takes its destination from the context
rather than knowing what it is walking to. That is the composition claim, demonstrated.

Verified in game: a villager bound itself to a post, found dropped wood, carried it to the
bound chest and deposited it, in about seven seconds.

## Where state lives

| Owner | Key | Meaning |
|---|---|---|
| Post | `kukolony.job` | job id |
| Post | `kukolony.job.item` | item prefab to work with |
| Post | `kukolony.job.target` | bound destination container (ZDOID) |
| Post | `kukolony.job.radius` | working radius, 0 means use the config default |
| Villager | `kukolony.post` | the post it works (ZDOID) |
| Villager | `kukolony.step` | index into the job's steps |
| Villager | `kukolony.step.target` | what the current step is acting on (ZDOID) |

**The post holds configuration; the villager holds progress.** Several villagers can work
one post without contending over shared state, and reconfiguring a post does not disturb
anyone mid-cycle.

`JobContext` is rebuilt every tick from the ZDO rather than cached. RRR keep AI state in
C# objects held by a manager; we do not, because ownership of a villager can move to
another player between ticks and anything in memory would be lost. Reading through to the
ZDO means a villager that changes hands resumes mid-job.

A ZDOID occupies **two** ZDO slots (user id and object id), so its cached key is a
`KeyValuePair<int,int>` from `ZDO.GetHashZDOID`, not a single hash.

## Customisation

Everything a player can change lives on the post's ZDO, so the job engine reads
configuration from one place no matter what sets it.

**Jobs themselves are data.** Definitions live in `BepInEx/config/Kukolony/jobs/*.json`,
and `haul.json` is written on first run so the folder documents its own schema:

```json
{
  "id": "haul",
  "steps": [
    { "type": "find_ground_item" },
    { "type": "move_to_target", "stopDistance": 2.0 },
    { "type": "pick_up_item" },
    { "type": "resolve_destination" },
    { "type": "move_to_target", "stopDistance": 2.0 },
    { "type": "deposit_item" }
  ]
}
```

Note what is *not* in there: no item, no radius. Those are configuration and belong to the
post. A definition describes the **shape** of the work, which is what lets one definition
serve every post that uses it.

### Step types are public API

`find_ground_item`, `move_to_target`, `pick_up_item`, `resolve_destination`,
`deposit_item`. These names appear in every file a player writes, so renaming one breaks
their content. Small and deliberate.

### Failure handling

A definition is accepted whole or rejected whole — half-loading would give a villager work
that silently skips a step. Errors name the file and the step index, and list what was
valid:

```
[kukolony_test_bad.json] step 0 has unknown type 'no_such_step'.
Known types: find_ground_item, move_to_target, pick_up_item, resolve_destination, deposit_item
```

The bad file is dropped, the good ones still load, and a built-in haul job remains as a
last-resort fallback so a broken folder never leaves a world with no jobs at all. The test
deliberately writes a malformed file to prove this.

## Registration order matters

Register pieces on `PrefabManager.OnVanillaPrefabsAvailable`, **not**
`PieceManager.OnPiecesRegistered`. The latter fires after `PrefabManager` has already
pushed custom prefabs into `ZNetScene`, so a piece created there appears in the build
table but cannot be resolved by name — it can be built by hand and never spawned by code.

Cloned prefabs can also come back **inactive**, and an inactive instance never runs `Awake`,
so its `ZNetView` never creates a ZDO. `SetActive(true)` after cloning. This caught both
the villager and the work post.

## Target claims

A villager's current step target **is** its reservation. `TargetClaims.IsClaimedByOther`
walks the villager registry and skips anything another villager is already working on.
No separate claim store, and it cleans itself up, because the target is already cleared on
pickup, on deposit and on failure.

The claim is deliberately **not** written onto the target. `ZDO.Set` ignores its
`okForNotOwner` argument, so a write to a ZDO we do not own lands locally and is clobbered
on the next sync from its owner — vanilla's own `Container.SetInUse` gates on `IsOwner()`
for the same reason. Claiming a loose item that way would mean an ownership round trip
before the villager had even started walking.

Exclusivity is decided by **who asks**, not by tagging the target. Find steps ask, so two
villagers never walk to the same log. Deposit steps do not, so any number of villagers can
share one chest.

`SetStepTarget` stamps `kukolony.step.since` with net time, and a claim older than
`ClaimTtlSeconds` is ignored — otherwise a stuck villager would lock a resource forever.

### Measured

Three villagers, two logs, one post. `ClaimsEnabled` exists so the difference can be
demonstrated rather than asserted:

| | colliding samples | cycle restarts | time |
|---|---|---|---|
| Claims on | 0 | 5 | 16s |
| Claims off | 795 | 10 | 27s |

## A misdiagnosis worth recording

The waste that prompted all this — villagers cycling `0:find → 1:move → 0:find` — was
**not** contention. It was `MoveToTargetStep` treating a cold-start path failure as fatal.

`BaseAI.FindPath` is throttled and returns false until it has actually run, so the first
tick after taking a target reports failure even for a perfectly reachable target.
`VillagerMovement` correctly reports that as `PathFailed`, and the step correctly gave up —
so a villager restarted its cycle the instant it set off.

The fix is a three second grace period: a missing path is retried, a missing *target* still
fails immediately, because that will not fix itself. Restarts fell from 11-12 to 5.

This was only caught by running the control with claims disabled and finding it **also
passed** — the assertion was measuring the chest, which is shared on purpose. A test that
has never failed proves nothing.

## Known gaps

- **A failed cycle keeps whatever is in the bag.** Items are not dropped, and the next
  successful cycle deposits them, but a villager can accumulate if a post is misconfigured.
- **Claims are best-effort across clients.** A villager simulated on another peer is
  visible with its target, but two clients can still race inside one sync interval. That
  degrades to fail-and-retry, not corruption.
