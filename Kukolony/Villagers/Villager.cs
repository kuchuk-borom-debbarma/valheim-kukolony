using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.WorkPosts;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     A colony villager. Attached to the villager prefab at registration, so every
    ///     instance carries one.
    ///
    ///     This component does not drive itself. Valheim ticks creature AI through
    ///     MonoUpdaters at a fixed 0.05s step, and
    ///     <see cref="Patches.MonsterAiTickPatch" /> hands that tick here. Using the
    ///     game's own timestep is why the mod needs no coroutines and no async - the
    ///     predecessor's async AI loop is what used to crash the game.
    /// </summary>
    internal sealed class Villager : MonoBehaviour
    {
        /// <summary>How close to home counts as home. Avoids jittering on the boundary.</summary>
        private const float HomeStopDistance = 2f;

        /// <summary>
        ///     Every loaded villager. Mirrors WorkPost.Instances and vanilla's
        ///     BaseAI.Instances - iterating a list beats scanning the scene, and claim
        ///     checks run inside the find step.
        /// </summary>
        internal static List<Villager> Instances { get; } = new List<Villager>();

        private MonsterAI _ai;
        private Character _character;
        private ZNetView _nview;
        private VisEquipment _visEquipment;
        private Container _bag;

        private bool _pathFailureReported;
        private readonly JobRunner _jobRunner = new JobRunner();

        /// <summary>Short description of what this villager is doing, for hover text.</summary>
        internal string Activity { get; private set; } = "idle";

        /// <summary>The job it is employed on, or empty when unemployed.</summary>
        internal string CurrentJob { get; private set; } = string.Empty;

        private void Awake() => Instances.Add(this);

        /// <summary>
        ///     Must remove, or a destroyed villager keeps holding its claim forever.
        /// </summary>
        private void OnDestroy() => Instances.Remove(this);

        /// <summary>
        ///     Records what a villager is doing, logging only when it changes.
        ///
        ///     Transitions are rare and genuinely informative; the per-tick state is not.
        ///     Logging the change rather than the state is what keeps a colony of
        ///     villagers from flooding the log at 20 Hz each.
        ///
        ///     <paramref name="detail" /> is deliberately excluded from the comparison.
        ///     Anything that varies per tick - a distance, a position - would make every
        ///     tick look like a new activity and defeat the whole point.
        /// </summary>
        private void SetActivity(string activity, string detail = null)
        {
            if (Activity == activity)
            {
                return;
            }

            Activity = activity;
            Log.Info(string.IsNullOrEmpty(detail)
                ? $"Villager '{State.Name}' is now {activity}"
                : $"Villager '{State.Name}' is now {activity} ({detail})");
        }

        /// <summary>
        ///     Binds on demand. Callers outside the AI tick - hover text, diagnostics -
        ///     can read state before the villager has ever ticked.
        /// </summary>
        internal VillagerState State =>
            new VillagerState(Bind() && _nview.IsValid() ? _nview.GetZDO() : null);

        /// <summary>
        ///     Runs one AI step.
        /// </summary>
        /// <returns>
        ///     True if this villager handled its own behaviour and vanilla AI should be
        ///     skipped. False to let vanilla run - which it does correctly, since
        ///     BaseAI.UpdateAI no-ops for non-owners anyway.
        /// </returns>
        internal bool TryTakeOver(float deltaTime)
        {
            if (!Bind())
            {
                return false;
            }

            if (!_nview.IsValid())
            {
                return false;
            }

            // Every client needs the bag so it can read the contents; only the owner
            // may write to it.
            EnsureBag();

            // AI runs only on the ZDO owner (BaseAI.UpdateAI enforces the same rule).
            // Ownership can move between ticks, so this is never cached.
            if (!_nview.IsOwner())
            {
                return false;
            }

            // Identity first: everything below reports by name, and taming used to log
            // an empty one because it ran before the villager had been named.
            EnsureIdentity();
            EnsureAppearance();
            EnsureTamed();

            // Work takes priority over idling. A villager only wanders home when it has
            // nothing else to do.
            if (!TryWork(deltaTime))
            {
                CurrentJob = string.Empty;
                StayNearHome(deltaTime);
            }

            return true;
        }

        /// <summary>
        ///     Resolves components lazily rather than in Awake. A ZNetView is not
        ///     guaranteed to hold a valid ZDO by the time sibling components wake, so
        ///     binding on first use avoids depending on component order.
        /// </summary>
        private bool Bind()
        {
            if (_ai != null && _character != null && _nview != null)
            {
                return true;
            }

            // TryGetComponent, never GetComponent()?. - a destroyed Unity object passes
            // as non-null through the null-propagating operators. See docs/code-style.md.
            if (!TryGetComponent(out _ai))
            {
                return false;
            }

            if (!TryGetComponent(out _character))
            {
                return false;
            }

            if (!TryGetComponent(out _nview))
            {
                return false;
            }

            TryGetComponent(out _visEquipment);
            return true;
        }

        /// <summary>
        ///     A bag that survives reloads and ownership transfer. Attached on every
        ///     client, not just the owner, so non-owners can read its contents.
        /// </summary>
        private void EnsureBag()
        {
            if (_bag == null)
            {
                _bag = VillagerInventory.Attach(gameObject, _nview);
            }
        }

        /// <summary>
        ///     Rolls a face and outfit once. VisEquipment persists the result to the ZDO
        ///     by itself, so this never needs to run again.
        /// </summary>
        private void EnsureAppearance()
        {
            VillagerState state = State;
            if (state.HasAppearance || _visEquipment == null)
            {
                return;
            }

            VillagerAppearance.Randomise(_visEquipment);
            state.MarkAppearanceRolled();
            Log.Info($"Villager '{state.Name}' rolled its appearance");
        }

        /// <summary>
        ///     Villagers are tamed so the player and their other tame creatures never
        ///     treat them as enemies. BaseAI.IsEnemy short-circuits on tamed state before
        ///     it reaches the faction switch, which is cleaner than the predecessor's
        ///     trick of zeroing the creature's senses to fake friendliness.
        ///
        ///     Character.SetTamed persists to the ZDO, so this settles after one tick and
        ///     survives reloads.
        /// </summary>
        private void EnsureTamed()
        {
            if (_character.IsTamed())
            {
                return;
            }

            _ai.MakeTame();
            Log.Info($"Villager '{State.Name}' tamed");
        }

        /// <summary>
        ///     Gives a new villager its name and home. Runs once - afterwards both are on
        ///     the ZDO and survive save, reload and ownership transfer.
        /// </summary>
        private void EnsureIdentity()
        {
            VillagerState state = State;

            if (!state.HasName)
            {
                string chosen = VillagerNames.Random();
                state.SetName(chosen);
                Log.Info($"Villager named '{chosen}'");
            }

            if (!state.HasHome)
            {
                Vector3 home = transform.position;
                state.SetHome(home);
                Log.Info($"Villager '{state.Name}' made home at ({home.x:F1}, {home.y:F1}, {home.z:F1})");
            }
        }

        /// <summary>
        ///     Binds to a work post if unemployed, then runs one step of its job.
        /// </summary>
        /// <returns>True if the villager is working, so idling should be skipped.</returns>
        private bool TryWork(float deltaTime)
        {
            WorkPost post = ResolvePost();
            if (post == null)
            {
                return false;
            }

            post.EnsureDefaults();

            Job job = JobLibrary.Find(post.State.JobId);
            if (job == null)
            {
                return false;
            }

            string doing = _jobRunner.Tick(job, new JobContext(this, _ai, _bag, post, deltaTime));
            CurrentJob = post.State.JobId;
            SetActivity(doing);
            return true;
        }

        /// <summary>
        ///     The bound post, claiming the nearest one if unemployed. Binding is by
        ///     ZDOID so it survives the post unloading and reloading.
        /// </summary>
        private WorkPost ResolvePost()
        {
            VillagerState state = State;
            ZDOID bound = state.Post;

            if (!bound.IsNone())
            {
                GameObject postObject = ZNetScene.instance.FindInstance(bound);
                if (postObject != null && postObject.TryGetComponent(out WorkPost existing))
                {
                    return existing;
                }

                // Out of range or destroyed. Stay bound rather than stealing another
                // post - a villager whose post is merely unloaded should not resign.
                return null;
            }

            WorkPost nearest = WorkPost.FindNearest(transform.position, ModConfig.PostBindRadius.Value);
            if (nearest == null)
            {
                return null;
            }

            ZDOID id = nearest.Id;
            if (id.IsNone())
            {
                return null;
            }

            state.SetPost(id);
            Log.Info($"Villager '{state.Name}' took up work at a post");
            return nearest;
        }

        /// <summary>
        ///     Idle behaviour: stay within reach of home, otherwise walk back.
        ///
        ///     Deliberately concrete rather than hidden behind a behaviour interface.
        ///     One example is not enough to know the right abstraction, and the
        ///     predecessor's twelve near-identical AI classes are what happens when you
        ///     guess. The interface gets extracted once a second behaviour exists.
        /// </summary>
        private void StayNearHome(float deltaTime)
        {
            VillagerState state = State;
            Vector3 home = state.Home;
            float distance = Utils.DistanceXZ(home, transform.position);

            if (distance <= ModConfig.GoHomeRadius.Value)
            {
                VillagerMovement.Stop(_ai);
                SetActivity("idle");
                _pathFailureReported = false;
                return;
            }

            switch (VillagerMovement.MoveTowards(_ai, home, HomeStopDistance))
            {
                case MoveResult.Moving:
                    SetActivity("walking home", $"{distance:F0}m away");
                    _pathFailureReported = false;
                    break;

                case MoveResult.Arrived:
                    SetActivity("idle");
                    _pathFailureReported = false;
                    break;

                case MoveResult.PathFailed:
                    SetActivity("stuck");
                    // Latched: the villager retries every tick, but one log line is
                    // enough to diagnose it. Reset as soon as it moves again.
                    if (!_pathFailureReported)
                    {
                        _pathFailureReported = true;
                        Log.Warning(
                            $"Villager '{state.Name}' cannot path home, {distance:F0}m away. Retrying.");
                    }

                    break;
            }
        }

        /// <summary>
        ///     Explains why this villager is not operating. Diagnostics only - a villager
        ///     that fails to bind is silent by design, which makes it invisible when
        ///     something is wrong.
        /// </summary>
        internal string Diagnose()
        {
            if (!TryGetComponent(out MonsterAI _))
            {
                return "no MonsterAI component";
            }

            if (!TryGetComponent(out Character _))
            {
                return "no Character component";
            }

            if (!TryGetComponent(out ZNetView nview))
            {
                return "no ZNetView component";
            }

            if (nview.m_zdo == null)
            {
                // A prefab registered under Jotunn's container is a scene object too, so
                // it can be mistaken for a spawned instance. The "(Clone)" suffix and the
                // active flags tell the two apart.
                return $"ZNetView never created a ZDO - name='{gameObject.name}' "
                       + $"activeSelf={gameObject.activeSelf} "
                       + $"activeInHierarchy={gameObject.activeInHierarchy} "
                       + $"parent='{(transform.parent != null ? transform.parent.name : "<none>")}' "
                       + $"scene='{gameObject.scene.name}'";
            }

            if (!nview.m_zdo.IsValid())
            {
                return $"ZDO exists but was invalidated (uid={nview.m_zdo.m_uid}, "
                       + $"persistent={nview.m_zdo.Persistent}, owner={nview.m_zdo.HasOwner()})";
            }

            return "bound ok";
        }

        /// <summary>Hover line: who this is and what they are doing.</summary>
        internal string DescribeForHover()
        {
            VillagerState state = State;
            if (!state.IsValid)
            {
                return string.Empty;
            }

            string headline = string.IsNullOrEmpty(CurrentJob)
                ? "<color=grey>no job</color>"
                : $"<color=orange>{CurrentJob}</color>";

            return $"{state.Name}\n{headline}\n<color=grey>{Activity}</color>";
        }

        internal string DisplayName()
        {
            VillagerState state = State;
            return state.IsValid && state.HasName ? state.Name : "Villager";
        }
    }
}
