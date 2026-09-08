using Kukolony.Core;
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

        private MonsterAI _ai;
        private Character _character;
        private ZNetView _nview;

        private bool _pathFailureReported;

        /// <summary>Short description of what this villager is doing, for hover text.</summary>
        internal string Activity { get; private set; } = "idle";

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

        internal VillagerState State => new VillagerState(_nview != null && _nview.IsValid() ? _nview.GetZDO() : null);

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

            // AI runs only on the ZDO owner (BaseAI.UpdateAI enforces the same rule).
            // Ownership can move between ticks, so this is never cached.
            if (!_nview.IsOwner())
            {
                return false;
            }

            // Identity first: everything below reports by name, and taming used to log
            // an empty one because it ran before the villager had been named.
            EnsureIdentity();
            EnsureTamed();
            StayNearHome(deltaTime);
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

            return TryGetComponent(out _nview);
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
        ///     The only behaviour for now: stay within reach of home, otherwise walk back.
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

        /// <summary>Hover line: who this is and what they are doing.</summary>
        internal string DescribeForHover()
        {
            VillagerState state = State;
            if (!state.IsValid)
            {
                return string.Empty;
            }

            float distance = Utils.DistanceXZ(state.Home, transform.position);
            return $"{state.Name}\n<color=grey>{Activity}, {distance:F0}m from home</color>";
        }

        internal string DisplayName()
        {
            VillagerState state = State;
            return state.IsValid && state.HasName ? state.Name : "Villager";
        }
    }
}
