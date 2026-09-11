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

        private readonly ZSyncAnimation _animation;
        private readonly bool _canInteract;

        internal VillagerAnimation(GameObject villager)
        {
            _animation = villager.GetComponentInChildren<ZSyncAnimation>(true);
            if (_animation == null)
            {
                Log.Warning("[villager] no animator; villagers will work without gestures");
                return;
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

        /// <summary>Whether a lasting animator state exists, for callers that need to know.</summary>
        internal bool Has(string parameter, AnimatorControllerParameterType kind) =>
            _animation != null && _animation.HasParameter(parameter, kind);

        internal void SetFlag(string parameter, bool value)
        {
            if (_animation == null) return;
            _animation.SetBool(parameter, value);
        }
    }
}
