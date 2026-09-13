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

        /// <summary>
        ///     Names an axe swing might go by, best first.
        /// </summary>
        /// <remarks>
        ///     Several, because the rig is a clone of a creature and carries the player's
        ///     parameter set, and which of these a given build ships is asset data no
        ///     decompiled assembly can answer.
        /// </remarks>
        private static readonly string[] SwingNames = { "swing_axe", "swing_axe0", "swing_pickaxe", "attack" };

        /// <summary>
        ///     The animator's own idea of what is being held.
        /// </summary>
        /// <remarks>
        ///     Valheim's attack states transition out of this rather than out of nothing, so a
        ///     rig left at unarmed has no route into an axe swing: the trigger fires, the
        ///     controller has nowhere to go, and the villager chops invisibly. The visible item
        ///     in the hand is a separate thing entirely - that is VisEquipment, which decides
        ///     what is drawn, not what can be animated with it.
        /// </remarks>
        private const string WeaponState = "statei";

        private static bool _describedRig;

        private readonly ZSyncAnimation _animation;
        private readonly bool _canInteract;
        private readonly string _swing;
        private readonly string _sleep;
        private readonly bool _canHold;

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

            // Probed, not assumed. The rig turns out to carry the player's own parameter set,
            // so an axe swing is likely - but "likely" is what this project has already been
            // wrong about three times, and the answer is one call away.
            foreach (string name in SwingNames)
            {
                if (!_animation.HasParameter(name, AnimatorControllerParameterType.Trigger)) continue;

                _swing = name;
                break;
            }

            if (_swing == null)
            {
                Log.Info("[villager] the rig has no axe swing; chopping will be silent");
            }

            // Probed like everything else here. Without it the swing trigger has no state to
            // enter, which looks exactly like the trigger not firing.
            _canHold = _animation.HasParameter(WeaponState, AnimatorControllerParameterType.Int);
            if (!_canHold)
            {
                Log.Info($"[villager] the rig has no '{WeaponState}'; " +
                         "it will swing without appearing to hold anything");
            }
        }

        /// <summary>
        ///     Tells the rig what it is holding, so a swing has somewhere to go.
        /// </summary>
        /// <remarks>
        ///     Null bares the hands. The item's own animation state is used rather than a
        ///     guess: it is what the game sets for a player holding the same tool, so an axe
        ///     swings like an axe and anything else swings like itself.
        /// </remarks>
        internal void Hold(ItemDrop.ItemData item)
        {
            if (_animation == null || !_canHold) return;

            _animation.SetInt(WeaponState,
                item?.m_shared == null ? 0 : (int)item.m_shared.m_animationState);
        }

        /// <summary>
        ///     Swings an axe. Harmless when the rig cannot do it.
        /// </summary>
        /// <remarks>
        ///     A trigger rather than a bool, deliberately: triggers are RPCs and are not
        ///     persisted, which is exactly right for a momentary gesture and exactly wrong for
        ///     a lasting state like sleeping. A blow that a peer was not watching is simply a
        ///     blow they did not see.
        /// </remarks>
        internal void Swing()
        {
            if (_animation == null || _swing == null) return;
            _animation.SetTrigger(_swing);
        }

        /// <summary>Whether this rig can be seen to swing at all.</summary>
        internal bool CanSwing => _swing != null;

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
