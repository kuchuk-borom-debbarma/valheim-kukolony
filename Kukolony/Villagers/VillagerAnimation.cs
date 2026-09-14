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
        ///     Names a swing might go by when the tool itself does not say.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A fallback only. What a tool swings by is written on the tool - see
        ///         <see cref="Trigger" /> - and this is for the case where it is not.
        ///     </para>
        ///     <para>
        ///         <b>The numbered form comes first, and that ordering is the whole bug this
        ///         list once caused.</b> The rig carries the player's entire parameter set, so
        ///         "swing_axe" exists - and the controller listens to no such transition. Asked
        ///         in game, one trigger at a time, the answer was plain: swing_axe plays nothing
        ///         at all, and swing_axe0 plays "axe_swing". A parameter existing is not evidence
        ///         that anything is wired to it, which is a thing HasParameter cannot tell you.
        ///     </para>
        /// </remarks>
        private static readonly string[] SwingNames =
            { "swing_axe0", "swing_axe", "swing_pickaxe", "unarmed_attack0" };

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

        /// <summary>
        ///     The same answer again, as a float.
        /// </summary>
        /// <remarks>
        ///     Vanilla's <c>SetAnimationState</c> writes both on every equipment change, and a
        ///     controller's transitions are free to read either. Writing only the int is how a
        ///     villager comes to hold an axe, fire a swing trigger the rig demonstrably has, and
        ///     play nothing at all - the transition it needed was conditioned on the other one.
        /// </remarks>
        private const string WeaponStatef = "statef";

        /// <summary>
        ///     Which crafting animation to play, if any.
        /// </summary>
        /// <remarks>
        ///     The game drives this exactly the same way for the player - Player.UpdateCrafting
        ///     does <c>m_zanim.SetInt("crafting", m_currentStation.m_useAnimation)</c> while
        ///     standing at a station and writes zero on leaving - so the number comes from the
        ///     station rather than from anything chosen here, and a modded station's own
        ///     animation works untouched.
        /// </remarks>
        private const string CraftState = "crafting";

        private static bool _describedRig;

        private readonly ZSyncAnimation _animation;
        private readonly bool _canInteract;
        private readonly string _swing;
        private readonly string _sleep;
        private readonly bool _canHold;
        private readonly bool _canHoldFloat;
        private readonly bool _canCraft;

        /// <summary>What the rig was last told it is holding, so a swing can reassert it.</summary>
        private int _holding;

        /// <summary>The trigger the held tool swings by, composed the way the game composes it.</summary>
        private string _held;

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
            _canCraft = _animation.HasParameter(CraftState, AnimatorControllerParameterType.Int);
            if (!_canCraft)
            {
                Log.Info($"[villager] the rig has no '{CraftState}'; " +
                         "it will craft without appearing to work at the station");
            }

            _canHold = _animation.HasParameter(WeaponState, AnimatorControllerParameterType.Int);
            _canHoldFloat = _animation.HasParameter(WeaponStatef, AnimatorControllerParameterType.Float);
            if (!_canHold && !_canHoldFloat)
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
            if (_animation == null) return;

            // Never null-propagate on a Unity object; ItemData is plain, but the shared half
            // can be absent on an item that arrived without passing through an inventory.
            _holding = item == null || item.m_shared == null
                ? 0
                : (int)item.m_shared.m_animationState;

            _held = Trigger(item);
            Apply();
        }

        /// <summary>
        ///     Tells the rig what it is holding, both ways the game does.
        /// </summary>
        /// <remarks>
        ///     Re-asserted rather than set once, because it is not ours alone to set: the
        ///     Humanoid rewrites both from its <em>own</em> equipped items whenever it sets its
        ///     equipment up, and a villager's equipment is a mirror of its bag rather than
        ///     anything in the creature's hands - so that rewrite says "unarmed" and undoes this.
        ///     Both setters ignore a value the animator already has, so saying it again between
        ///     blows costs nothing.
        /// </remarks>
        private void Apply()
        {
            if (_canHold) _animation.SetInt(WeaponState, _holding);
            if (_canHoldFloat) _animation.SetFloat(WeaponStatef, _holding);
        }

        /// <summary>What the rig was last told it is holding. Zero is empty-handed.</summary>
        internal int Holding => _holding;

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
            string trigger = SwingName;
            if (_animation == null || trigger == null) return;

            // The hold first, every time. An attack state is reached from the state the weapon
            // puts the rig in, so a trigger fired while the controller thinks the hands are
            // empty has nowhere to go - and looks exactly like a trigger that never fired.
            Apply();
            _animation.SetTrigger(trigger);
        }

        /// <summary>The name this rig swings by right now: the tool's own, or the fallback.</summary>
        internal string SwingName => _held ?? _swing;

        /// <summary>
        ///     What a tool swings by, composed exactly as the game composes it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Read off the item rather than guessed, because every weapon in the game
        ///         answers this for itself and a modded one answers too. An attack names its
        ///         animation, and a weapon with a combo <b>appends the chain level</b> - so an
        ///         axe swings by "swing_axe0", never by "swing_axe", and firing the bare name
        ///         puts a trigger the controller has no transition for. One that picks at random
        ///         appends an index instead, which is why that branch exists here as well.
        ///     </para>
        ///     <para>
        ///         Level zero, always: the chain is for a player stringing blows together, and a
        ///         villager chopping a tree is starting a fresh swing every time.
        ///     </para>
        /// </remarks>
        private string Trigger(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || item.m_shared.m_attack == null) return null;

            Attack attack = item.m_shared.m_attack;
            string named = attack.m_attackAnimation;
            if (string.IsNullOrEmpty(named)) return null;

            string composed = attack.m_attackChainLevels > 1
                ? named + "0"
                : attack.m_attackRandomAnimations >= 2
                    ? named + Random.Range(0, attack.m_attackRandomAnimations)
                    : named;

            if (_animation.HasParameter(composed, AnimatorControllerParameterType.Trigger)) return composed;

            // The tool named something this rig does not have. Falls back rather than firing
            // into nothing, and says so once - a villager that swings invisibly is the failure
            // this whole class exists to make visible.
            Log.Info($"[villager] the rig has no '{composed}'; falling back to '{_swing}'");
            return null;
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

        /// <summary>
        ///     Works at a station, or stops. Zero is "not crafting", as it is for the player.
        /// </summary>
        internal void Crafting(int animation)
        {
            if (_animation == null || !_canCraft) return;
            _animation.SetInt(CraftState, animation);
        }

        internal void SetFlag(string parameter, bool value)
        {
            if (_animation == null) return;
            _animation.SetBool(parameter, value);
        }
    }
}
