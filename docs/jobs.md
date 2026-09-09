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
configuration from one place no matter what sets it — a GUI, an interaction, or a config
file. Adding a job today means composing steps in `JobLibrary`; the next stage moves those
definitions to JSON so new jobs need no recompile.

## Registration order matters

Register pieces on `PrefabManager.OnVanillaPrefabsAvailable`, **not**
`PieceManager.OnPiecesRegistered`. The latter fires after `PrefabManager` has already
pushed custom prefabs into `ZNetScene`, so a piece created there appears in the build
table but cannot be resolved by name — it can be built by hand and never spawned by code.

Cloned prefabs can also come back **inactive**, and an inactive instance never runs `Awake`,
so its `ZNetView` never creates a ZDO. `SetActive(true)` after cloning. This caught both
the villager and the work post.

## Known gaps

- **No target reservation.** Two villagers on one post can pick the same dropped item; the
  loser's target vanishes, its move step fails, and it restarts the cycle. Correct, but
  wasteful — observed directly in testing. A claim on the target ZDO is the fix.
- **A failed cycle keeps whatever is in the bag.** Items are not dropped, and the next
  successful cycle deposits them, but a villager can accumulate if a post is misconfigured.
