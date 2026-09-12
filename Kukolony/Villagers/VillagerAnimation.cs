using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Plays an animation, but only one the rig actually has.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Animator parameters are asset data. No decompiled assembly can answer whether a
    ///         given rig has a given state, and this project has already been wrong three times
    ///         by trusting a reference about things only the shipped game knows. So every name
    ///         is checked with <c>HasParameter</c> before it is used, once, and the answer is
    ///         reported — a missing animation should be a line in the log, not a villager who
    ///         mysteriously does nothing.
    ///     </para>
    ///     <para>
    ///         Triggers are RPCs and are <em>not</em> persisted, so one fired for a peer that is
    ///         not watching is simply lost. That is right for a momentary gesture like picking
    ///         something up, and wrong for a lasting state like sleeping, which needs a synced
    ///         bool instead.
    ///     </para>
    /// </remarks>
    internal sealed class VillagerAnimation
    {
        /// <summary>The game's own gesture for picking something up or using it.</summary>
        private const string Interact = "interact";

        /// <summary>
        ///     Names a creature rig might use for lying down, best first.
        /// </summary>
        /// <remarks>
        ///     A list rather than a guess, because animator parameters are asset data and no
        ///     decompile can answer what this rig has. Every parameter it does have is logged
        ///     once at startup, so the answer comes from the rig rather than from hoping.
        /// </remarks>
        private static readonly string[] SleepNames = { "sleeping", "sleep", "attach_bed", "sitting" };

        private static bool _describedRig;

        private readonly ZSyncAnimation _animation;
        private readonly bool _canInteract;
        private readonly string _sleep;

        internal VillagerAnimation(GameObject villager)
        {
            _animation = villager.GetComponentInChildren<ZSyncAnimation>(true);
            if (_animation == null)
            {
                Log.Warning("[villager] no animator; villagers will work without gestures");
                return;
            }

            Describe();

            foreach (string name in SleepNames)
            {
                if (!_animation.HasParameter(name, AnimatorControllerParameterType.Bool)) continue;

                _sleep = name;
                break;
            }

            if (_sleep == null)
            {
                Log.Info("[villager] the rig has no sleep state; villagers will rest standing up");
            }

            _canInteract = _animation.HasParameter(Interact, AnimatorControllerParameterType.Trigger);
            if (!_canInteract)
            {
                Log.Info($"[villager] the rig has no '{Interact}' trigger; " +
                         "picking things up will be silent");
            }
        }

        /// <summary>Reaches for something. Harmless when the rig cannot do it.</summary>
        internal void Reach()
        {
            if (_animation == null || !_canInteract) return;
            _animation.SetTrigger(Interact);
        }

        /// <summary>Whether this rig can be made to lie down at all.</summary>
        internal bool CanSleep => _sleep != null;

        /// <summary>
        ///     Lists what the rig can actually do, once per session.
        /// </summary>
        /// <remarks>
        ///     Every animator parameter by name and kind. Three separate features have now been
        ///     built against an assumption about this rig that turned out to be wrong, and each
        ///     cost a run to disprove; one log line settles every future one.
        /// </remarks>
        private void Describe()
        {
            if (_describedRig || _animation == null) return;
            _describedRig = true;

            Animator animator = _animation.GetComponentInChildren<Animator>();
            if (animator == null || animator.parameters == null) return;

            System.Text.StringBuilder names = new System.Text.StringBuilder();
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                names.Append(parameter.name).Append(':').Append(parameter.type).Append(' ');
            }

            Log.Info($"[villager] the rig can do: {names}");
        }

        /// <summary>Whether a lasting animator state exists, for callers that need to know.</summary>
        internal bool Has(string parameter, AnimatorControllerParameterType kind) =>
            _animation != null && _animation.HasParameter(parameter, kind);

        /// <summary>
        ///     Lies down, or gets up. Harmless when the rig cannot do either.
        /// </summary>
        /// <remarks>
        ///     <b>Probed, never assumed.</b> Animator parameters are asset data that no decompile
        ///     can answer, and this rig has already turned out to differ from the reference in
        ///     three other places. A synced bool rather than a trigger, because triggers are RPCs
        ///     and are not persisted - a villager asleep when its zone unloads must still be
        ///     asleep when it comes back, and a trigger would have been consumed and forgotten.
        ///
        ///     A rig with no sleep animation rests standing up, which is a cosmetic shortfall
        ///     rather than a broken feature: the energy, the walking and the recovery are all the
        ///     same either way.
        /// </remarks>
        internal void Sleeping(bool value)
        {
            if (_animation == null || _sleep == null) return;
            _animation.SetBool(_sleep, value);
        }

        internal void SetFlag(string parameter, bool value)
        {
            if (_animation == null) return;
            _animation.SetBool(parameter, value);
        }
    }
}
