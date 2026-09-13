using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     A placed flag that makes somewhere far away part of a Kolony.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The hearth's radius is where a Kolony lives; a flag is where it <em>works</em>.
    ///         Plant one in a forest three hundred metres out and the ground inside its radius
    ///         becomes the Kolony's: structures there can be registered, jobs can be pointed at
    ///         it, and its zones stay loaded so villagers keep working with nobody watching.
    ///     </para>
    ///     <para>
    ///         <b>A flag is assigned, never inferred.</b> Several Kolonies can exist, and
    ///         proximity is exactly the signal a distant outpost does not have — so the flag
    ///         asks which Kolony it serves, and belonging is an explicit choice made through
    ///         the same registration gate as everything else. Until it is claimed it is just a
    ///         banner: it holds nothing open and belongs to nobody.
    ///     </para>
    ///     <para>
    ///         The radius lives on the flag's own ZDO rather than in the Kolony's records,
    ///         because it is the flag's own property the way a name is a villager's — and
    ///         because the keep-alive reads it straight off the ZDO without instantiating
    ///         anything, exactly as it already reads villager positions.
    ///     </para>
    /// </remarks>
    internal sealed class WorkFlag : MonoBehaviour, Hoverable, Interactable
    {
        /// <summary>
        ///     The radius a flag may have, bounding the config default, the screen's slider
        ///     and - through <see cref="RadiusOf" /> - whatever a ZDO happens to carry. The
        ///     keep-alive walks every zone a circle touches once a second, so a number from
        ///     an older build or another mod must not be allowed to make that walk unbounded.
        /// </summary>
        internal const float MinRadius = 8f;

        internal const float MaxRadius = 256f;

        private static readonly int RadiusKey = "kukolony.flag.radius.v1".GetStableHashCode();

        private static readonly int NameKey = "kukolony.flag.name.v1".GetStableHashCode();

        /// <summary>What a flag nobody has named is called.</summary>
        internal const string UnnamedLabel = "Kolony Flag";

        private ZNetView _nview;

        private bool Bind()
        {
            if (_nview == null) _nview = GetComponent<ZNetView>();
            return _nview != null && _nview.IsValid();
        }

        internal ZDOID Id => Bind() ? _nview.GetZDO().m_uid : ZDOID.None;

        /// <summary>The Kolony this flag serves, or none while it is unclaimed.</summary>
        internal ZDOID Owner => Bind() ? ColonyMembership.GetColony(_nview.GetZDO()) : ZDOID.None;

        /// <summary>
        ///     How far this flag reaches, read from its ZDO so an unloaded flag answers too.
        /// </summary>
        internal float Radius => Bind() ? RadiusOf(_nview.GetZDO()) : ModConfig.FlagRadius.Value;

        /// <summary>The radius off a bare ZDO, for callers that have no instance.</summary>
        internal static float RadiusOf(ZDO zdo)
        {
            float configured = ModConfig.FlagRadius != null ? ModConfig.FlagRadius.Value : 48f;
            float stored = zdo?.GetFloat(RadiusKey, 0f) ?? 0f;
            return Mathf.Clamp(stored > 0f ? stored : configured, MinRadius, MaxRadius);
        }

        /// <summary>
        ///     What this flag is called.
        /// </summary>
        /// <remarks>
        ///     Kept on the flag's own ZDO rather than only in the Kolony's structure record,
        ///     because a flag can be named before anybody claims it - it is planted first and
        ///     assigned afterwards, and a name that only existed once claimed would be a name
        ///     you could not give at the moment you most want to: while deciding which of
        ///     three outposts this is.
        /// </remarks>
        internal string Name => Bind() ? NameOf(_nview.GetZDO()) : UnnamedLabel;

        /// <summary>The name somebody actually gave this flag, or empty if nobody has.</summary>
        /// <remarks>
        ///     Kept apart from <see cref="NameOf" /> so a caller can tell "unnamed" from
        ///     "named the same thing the default says" - registration needs that distinction
        ///     to decide whether it may use the prefab's display name instead.
        /// </remarks>
        internal static string GivenName(ZDO zdo) =>
            (zdo?.GetString(NameKey, string.Empty) ?? string.Empty).Trim();

        /// <summary>The name off a bare ZDO, for callers with no instance.</summary>
        internal static string NameOf(ZDO zdo)
        {
            string given = GivenName(zdo);
            return given.Length == 0 ? UnnamedLabel : given;
        }

        /// <summary>
        ///     Names the flag, and tells its Kolony so the structures list agrees.
        /// </summary>
        /// <remarks>
        ///     Both, because the two are read in different places: the hover text and this
        ///     flag's own screen read the ZDO, while the Kolony screen's structures list and
        ///     the job targets read the registered record. Writing only one is how a flag
        ///     comes to be called two things at once - a disagreement this codebase has
        ///     already paid for elsewhere.
        /// </remarks>
        internal void SetName(string name)
        {
            if (!Bind()) return;

            // Claim, then write, as the radius does: a non-owner write is discarded on the
            // next sync and the screen would snap back with nothing said.
            _nview.ClaimOwnership();
            ZDO zdo = _nview.GetZDO();
            zdo.Set(NameKey, (name ?? string.Empty).Trim());

            // Best effort by design. A flag is planted three hundred metres out, where its
            // hearth is usually not loaded, so the record cannot always be reached from
            // here - and the name on the flag is the one the player is looking at. The
            // record catches up the next time the flag is assigned.
            Colony colony = Colony.FindFor(zdo);
            if (colony != null) ColonyOperations.RenameStructure(colony, zdo.m_uid, NameOf(zdo));
        }

        internal void SetRadius(float radius)
        {
            if (!Bind()) return;

            // Claim, then write - the same move AssignFlag makes on this very ZDO. Gating
            // on IsOwner instead silently dropped the change for any peer that did not
            // happen to own the flag's zone: the screen's slider snapped back to the old
            // number with nothing said, which reads as a broken control.
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(RadiusKey, Mathf.Clamp(radius, MinRadius, MaxRadius));
        }

        public string GetHoverName() => "$kukolony_flag";

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (!Bind()) return Localization.instance.Localize("$kukolony_flag");

            ZDOID owner = Owner;
            string whose = owner.IsNone()
                ? "<color=grey>claimed by nobody</color>"
                : $"<color=orange>{OwnerName(owner)}</color>";

            return Localization.instance.Localize(
                $"{Name}\n{whose}\n<color=grey>reaches {Radius:0} m</color>"
                + "\n[<color=yellow><b>$KEY_Use</b></color>] assign");
        }

        /// <summary>
        ///     Assigning is done by talking to the flag, the same way a villager is managed by
        ///     walking up to them.
        /// </summary>
        /// <remarks>
        ///     Held keys repeat, and a picker that reopens twenty times a second cannot be
        ///     used — the same refusal every other interact in this mod makes.
        /// </remarks>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;

            Gui.FlagAssignScreen.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>
        ///     The owning Kolony's name, readable whether or not that Kolony is loaded.
        ///     Shared with the assign screen, so the flag and its screen cannot come to
        ///     word the same Kolony differently.
        /// </summary>
        internal static string OwnerName(ZDOID owner)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(owner) : null;
            if (zdo == null || !zdo.IsValid()) return "a Kolony that is gone";

            string name = new ColonyState(zdo).Name;
            return string.IsNullOrEmpty(name) ? Colony.UnnamedLabel : name;
        }
    }
}
