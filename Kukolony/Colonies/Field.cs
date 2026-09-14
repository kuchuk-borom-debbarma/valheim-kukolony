using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     A marked patch of ground the Kolony grows things in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>A place, with what to grow written on it.</b> Every other job carries its own
    ///         list of what to work on; a field carries the list instead, which is the arrangement
    ///         crafting stations already use and the reason this is a piece rather than a setting.
    ///         Two fields growing different things is a sentence a settlement can say without two
    ///         jobs, and where the carrots go is a fact about the ground rather than about the
    ///         work.
    ///     </para>
    ///     <para>
    ///         <b>Inside the Kolony's reach, unlike a flag.</b> A flag is exempt from the reach
    ///         gate because standing beyond reach is its whole purpose; a field is somewhere the
    ///         Kolony already is. That is not a restriction for its own sake - a <c>Plant</c> only
    ///         grows while its zone is loaded, and the keep-alive holds open what the Kolony
    ///         reaches. A field outside it would be a farm that never ripens and looks perfectly
    ///         healthy whenever anybody walks out to see it. An outfarm composes the other way:
    ///         plant a flag, then a field inside it.
    ///     </para>
    ///     <para>
    ///         The radius lives on the field's own ZDO, as a flag's does and for the same reason:
    ///         it is the field's own property, and anything that has to know how big a field is
    ///         can read it without instantiating one.
    ///     </para>
    /// </remarks>
    internal sealed class Field : MonoBehaviour, Hoverable, Interactable
    {
        /// <summary>
        ///     The radius a field may have.
        /// </summary>
        /// <remarks>
        ///     Smaller at both ends than a flag's. A field is walked across by somebody planting
        ///     one seed at a time, and its every square is a candidate the layout has to consider
        ///     - at a crop's half-metre pitch a thirty-metre field is already several thousand
        ///     squares. The bound is what keeps that arithmetic finite whatever a ZDO happens to
        ///     carry.
        /// </remarks>
        internal const float MinRadius = 4f;

        internal const float MaxRadius = 32f;

        internal const float DefaultRadius = 12f;

        private static readonly int RadiusKey = "kukolony.field.radius.v1".GetStableHashCode();

        private static readonly int NameKey = "kukolony.field.name.v1".GetStableHashCode();

        /// <summary>The prefab's own name, which is what an unnamed field is called.</summary>
        /// <remarks>
        ///     A token rather than a word, because the game translates it - the hover header and
        ///     the structures list both go through the localiser, and a hard-coded English default
        ///     would show a translated name in one place and an English one in the other for the
        ///     same unnamed field.
        /// </remarks>
        internal const string UnnamedToken = "$kukolony_field";

        internal static string UnnamedLabel =>
            Localization.instance != null
                ? Localization.instance.Localize(UnnamedToken)
                : "Kolony Field";

        private ZNetView _nview;

        private bool Bind()
        {
            if (_nview == null) _nview = GetComponent<ZNetView>();
            return _nview != null && _nview.IsValid();
        }

        internal ZDOID Id => Bind() ? _nview.GetZDO().m_uid : ZDOID.None;

        /// <summary>How far this field reaches, read from its ZDO so an unloaded field answers.</summary>
        internal float Radius => Bind() ? RadiusOf(_nview.GetZDO()) : DefaultRadius;

        /// <summary>The radius off a bare ZDO, for callers that have no instance.</summary>
        internal static float RadiusOf(ZDO zdo)
        {
            float stored = zdo?.GetFloat(RadiusKey, 0f) ?? 0f;
            return Mathf.Clamp(stored > 0f ? stored : DefaultRadius, MinRadius, MaxRadius);
        }

        internal void SetRadius(float radius)
        {
            if (!Bind()) return;

            // Claim, then write, as the flag's radius does: gating on IsOwner instead silently
            // drops the change for any peer that does not happen to own this zone, and the
            // screen's slider snaps back to the old number with nothing said.
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(RadiusKey, Mathf.Clamp(radius, MinRadius, MaxRadius));
        }

        /// <summary>The name somebody gave this field, or empty if nobody has.</summary>
        internal static string GivenNameOf(ZDO zdo) =>
            (zdo?.GetString(NameKey, string.Empty) ?? string.Empty).Trim();

        internal string GivenName => Bind() ? GivenNameOf(_nview.GetZDO()) : string.Empty;

        internal static string NameOf(ZDO zdo)
        {
            string given = GivenNameOf(zdo);
            return given.Length == 0 ? UnnamedLabel : given;
        }

        internal string Name => Bind() ? NameOf(_nview.GetZDO()) : UnnamedLabel;

        /// <summary>
        ///     Names the field, and tells its Kolony so the structures list agrees.
        /// </summary>
        /// <remarks>
        ///     Both, for the reason the flag writes both: the hover reads the ZDO while the
        ///     structures list reads the registered record, and writing one is how a thing comes
        ///     to be called two things at once.
        /// </remarks>
        internal void SetName(string name)
        {
            if (!Bind()) return;

            _nview.ClaimOwnership();
            ZDO zdo = _nview.GetZDO();
            zdo.Set(NameKey, (name ?? string.Empty).Trim());

            ColonyOperations.RenameStructure(ColonyMembership.GetColony(zdo), zdo.m_uid, NameOf(zdo));
        }

        /// <summary>Writes a name onto a field's ZDO, for a rename that came from the Kolony's side.</summary>
        internal static void WriteName(ZDOID id, string name)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsValid()) return;

            // The default is absence, not a name, so an untouched field stays distinguishable
            // from one deliberately named after the default.
            string given = (name ?? string.Empty).Trim();
            if (given == UnnamedLabel) given = string.Empty;

            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set(NameKey, given);
        }

        public string GetHoverName() => UnnamedToken;

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (!Bind()) return UnnamedLabel;

            ZDOID owner = ColonyMembership.GetColony(_nview.GetZDO());
            string whose = owner.IsNone()
                ? "<color=grey>not part of a Kolony yet</color>"
                : $"<color=orange>{WorkFlag.OwnerName(owner)}</color>";

            // Only the prompt goes through the localiser, and the name is put in afterwards. A
            // whole line through it is fed whatever the player typed, and '$' is how a token
            // starts - the flag hover already had this discipline and a field called "Odin's
            // $tash" would come out mangled without it.
            string prompt = "[<color=yellow><b>$KEY_Use</b></color>] open";
            if (Localization.instance != null) prompt = Localization.instance.Localize(prompt);

            return $"{Name}\n{whose}\n<color=grey>{Radius:0} m across</color>\n{prompt}";
        }

        /// <summary>
        ///     Walking up to a field opens its settings, as walking up to a villager opens theirs.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Through the same entry point the look-at hotkey uses, rather than a second way
        ///         in. A field the player is standing in front of and pressing Use on is by
        ///         definition the thing they are looking at, so the two cannot disagree about
        ///         which record to open - and anything that key learns to do, this learns too.
        ///     </para>
        ///     <para>
        ///         Held keys repeat, and a screen that reopens twenty times a second cannot be
        ///         used - the same refusal every other interact in this mod makes.
        ///     </para>
        /// </remarks>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;

            Gui.ColonyScreen.OpenWhatIsLookedAt();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
