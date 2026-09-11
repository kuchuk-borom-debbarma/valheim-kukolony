using UnityEngine;

namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     Walking towards something, with the patience and the memory that a bare move call
    ///     cannot have.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="VillagerMovement" /> is the stateless wrapper around the game's move
    ///         call, and it is right about a single tick. It cannot be right about a journey,
    ///         because two of the things a journey needs are memory: whether a path failure is
    ///         real, and whether the villager is actually getting anywhere.
    ///     </para>
    ///     <para>
    ///         <b>The grace exists because the game's own pathfinder is throttled.</b>
    ///         <c>BaseAI.FindPath</c> answers from a cache for a second at a time, so the first
    ///         tick after taking any new target reports failure whether or not a path exists.
    ///         The previous job system had a three second grace, lost it in a rewrite, and
    ///         villagers went back to abandoning work the instant they set off - a bug that read
    ///         as claim contention and took a measured run to pin down.
    ///     </para>
    ///     <para>
    ///         Progress is tracked here rather than in the job because it is the same question
    ///         for every job, and because "stuck" is not a moment but a history.
    ///     </para>
    /// </remarks>
    internal sealed class VillagerWalk
    {
        /// <summary>How long a path failure is forgiven after taking a new target.</summary>
        private const float GraceSeconds = 3f;

        /// <summary>How much closer counts as having got somewhere.</summary>
        private const float ProgressStep = .25f;

        private readonly MonsterAI _ai;

        private Vector3 _target;
        private bool _hasTarget;
        private float _graceUntil;
        private float _closest;
        private float _lastProgress;

        internal VillagerWalk(MonsterAI ai) => _ai = ai;

        /// <summary>How long the villager has been trying without getting closer.</summary>
        internal float StalledFor => _hasTarget ? Mathf.Max(0f, Time.time - _lastProgress) : 0f;

        /// <summary>The closest it has managed to get to the current target.</summary>
        internal float Closest => _closest;

        internal MoveResult MoveTowards(Vector3 target, float stopDistance, bool run = false)
        {
            Retarget(target);

            float distance = Utils.DistanceXZ(target, _ai.transform.position);
            if (distance < _closest - ProgressStep)
            {
                _closest = distance;
                _lastProgress = Time.time;
            }

            MoveResult result = VillagerMovement.MoveTowards(_ai, target, stopDistance, run);
            if (result != MoveResult.PathFailed) return result;

            // Inside the grace a failure is most likely the throttled pathfinder answering
            // from a cache that predates this target. Keep walking and ask again.
            return Time.time < _graceUntil ? MoveResult.Moving : MoveResult.PathFailed;
        }

        internal void Stop()
        {
            VillagerMovement.Stop(_ai);
            Forget();
        }

        /// <summary>Drops what was learned about the current journey.</summary>
        internal void Forget()
        {
            _hasTarget = false;
            _closest = float.MaxValue;
        }

        /// <summary>
        ///     Starts the clock again when the destination genuinely changes.
        /// </summary>
        /// <remarks>
        ///     Compared with a tolerance rather than exactly: a target that is itself moving - a
        ///     cart, a dropped item settling - would otherwise reset the grace every tick and
        ///     the villager could never be found stuck.
        /// </remarks>
        private void Retarget(Vector3 target)
        {
            if (_hasTarget && Utils.DistanceXZ(_target, target) < 1f) return;

            _target = target;
            _hasTarget = true;
            _graceUntil = Time.time + GraceSeconds;
            _closest = float.MaxValue;
            _lastProgress = Time.time;
        }
    }
}
