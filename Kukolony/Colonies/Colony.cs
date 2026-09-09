using System.Collections.Generic;
using System.Linq;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     A placed persistent colony hearth. Villager membership is explicit; structure
    ///     registration and execution eligibility use its configurable live radius.
    /// </summary>
    internal sealed class Colony : MonoBehaviour, Hoverable, Interactable
    {
        internal static List<Colony> Instances { get; } = new List<Colony>();

        private ZNetView _nview;

        internal ColonyState State =>
            new ColonyState(Bind() && _nview.IsValid() ? _nview.GetZDO() : null);

        internal ZDOID Id => Bind() && _nview.IsValid() ? _nview.GetZDO().m_uid : ZDOID.None;

        internal float EffectiveRadius => ModConfig.ColonyRadius != null ? ModConfig.ColonyRadius.Value : 48f;

        private void Awake() => Instances.Add(this);

        private void OnDestroy() => Instances.Remove(this);

        private bool Bind()
        {
            if (_nview != null)
            {
                return true;
            }

            return TryGetComponent(out _nview);
        }

        /// <summary>
        ///     Gives a freshly placed hearth a name, so it is identifiable before anyone
        ///     opens the panel.
        /// </summary>
        internal void EnsureNamed()
        {
            if (!Bind() || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            ColonyState state = State;
            if (!string.IsNullOrEmpty(state.Name))
            {
                return;
            }

            state.SetName(ColonyNames.Random());
            Log.Info($"Colony '{state.Name}' founded");
        }

        /// <summary>
        ///     Registers a villager both ways: into the colony's list, and as a back-pointer
        ///     on the member itself.
        /// </summary>
        internal bool Register(ColonyMemberKind kind, ZNetView member)
        {
            if (member == null || !member.IsValid() || !Bind() || !_nview.IsValid())
            {
                return false;
            }

            // Both ZDOs are written, so both must be ours to write to.
            _nview.ClaimOwnership();
            member.ClaimOwnership();

            ZDOID colonyId = Id;
            if (colonyId.IsNone())
            {
                return false;
            }

            bool added = State.AddMember(kind, member.GetZDO().m_uid);
            ColonyMembership.SetColony(member.GetZDO(), colonyId);
            return added;
        }

        internal bool Unregister(ColonyMemberKind kind, ZDOID member)
        {
            if (!Bind() || !_nview.IsValid())
            {
                return false;
            }

            _nview.ClaimOwnership();
            bool removed = State.RemoveMember(kind, member);

            ZDO memberZdo = ZDOMan.instance?.GetZDO(member);
            if (memberZdo != null && ColonyMembership.BelongsTo(memberZdo, Id))
            {
                ColonyMembership.SetColony(memberZdo, ZDOID.None);
            }

            return removed;
        }

        internal bool RegisterStructure(StructureRecord record)
        {
            if (record == null || !record.IsLiveIn(this) || !Bind() || !_nview.IsValid()) return false;
            _nview.ClaimOwnership();
            List<StructureRecord> records = State.GetStructures();
            int index = records.FindIndex(r => r.Id == record.Id);
            if (index >= 0) records[index] = record; else records.Add(record);
            State.SetStructures(records);
            return true;
        }

        internal bool RemoveStructure(ZDOID id)
        {
            if (!Bind() || !_nview.IsValid()) return false;
            _nview.ClaimOwnership();
            List<StructureRecord> records = State.GetStructures();
            bool removed = records.RemoveAll(r => r.Id == id) > 0;
            if (removed) State.SetStructures(records);
            return removed;
        }

        /// <summary>
        ///     How far above the object the hover text sits. Valheim added this to
        ///     Hoverable; zero keeps the vanilla placement.
        /// </summary>
        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "$kukolony_colony";

        public string GetHoverText()
        {
            ColonyState state = State;
            if (!state.IsValid)
            {
                return Localization.instance.Localize("$kukolony_colony");
            }

            string name = string.IsNullOrEmpty(state.Name) ? "unnamed" : state.Name;

            return Localization.instance.Localize(
                $"$kukolony_colony\n<color=orange>{name}</color>\n"
                + $"<color=grey>{state.CountMembers(ColonyMemberKind.Villager)} villagers, "
                + $"{state.GetStructures().Count} registered structures</color>"
                + "\n[<color=yellow><b>$KEY_Use</b></color>] manage");
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || alt)
            {
                return false;
            }

            Gui.ColonyPanel.Instance?.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>The colony a ZDO belongs to, or null.</summary>
        internal static Colony FindFor(ZDO memberZdo)
        {
            ZDOID colonyId = ColonyMembership.GetColony(memberZdo);
            if (colonyId.IsNone())
            {
                return null;
            }

            return Instances.FirstOrDefault(c => c != null && c.Id == colonyId);
        }
    }

    /// <summary>Names for newly founded colonies.</summary>
    internal static class ColonyNames
    {
        private static readonly string[] Pool =
        {
            "Riverhold", "Stonemeet", "Ashfell", "Elderwatch", "Fairhaven",
            "Grimsby", "Oakrest", "Northgate", "Wolfden", "Mistvale"
        };

        internal static string Random() => Pool[UnityEngine.Random.Range(0, Pool.Length)];
    }
}
