using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>Ordering offered by the Structures tab and the target picker.</summary>
    internal enum StructureSort { Name, Type, Capability, Status }

    /// <summary>
    ///     Every way registering can end. A value here is a decision, not a message - what the
    ///     player reads is mapped separately, so the refusal reason can be worded for a person
    ///     without the decision logic depending on that wording.
    /// </summary>
    internal enum RegisterOutcome
    {
        Registered,

        /// <summary>Taken from the colony that held it.</summary>
        Moved,

        /// <summary>Already this colony's. Refused, so an edited name is not overwritten.</summary>
        AlreadyHere,

        /// <summary>Outside the colony's reach. Reach is what a settlement can use.</summary>
        OutOfReach,

        /// <summary>A creature, a loose item, or nothing the colony understands.</summary>
        NotUsable,

        /// <summary>Could not be claimed, so no durable reference could be minted.</summary>
        NotOwnable,

        /// <summary>Held by a colony this peer cannot reach to take it from.</summary>
        HolderUnreachable,

        NoColony
    }

    /// <summary>
    ///     Colony-level operations shared by the panel and the benchmark scenarios, kept out
    ///     of the UI so both drive the same code paths.
    /// </summary>
    internal static class ColonyOperations
    {
        /// <summary>
        ///     Search, capability filter, and sort over a colony's structure records. Matches
        ///     display name or prefab, so a renamed structure is still findable by what it is.
        ///     <c>Status</c> sorts live records first; ineligible ones remain listed rather
        ///     than hidden, because the player decides whether to remove them.
        /// </summary>
        internal static List<StructureRecord> FilterStructures(Colony colony, string search,
            StructureCapability capability, StructureSort sort)
        {
            string query = (search ?? string.Empty).Trim();
            IEnumerable<StructureRecord> records = colony.State.GetStructures()
                .Where(record => capability == StructureCapability.None ||
                                 (record.Capabilities & capability) != 0)
                .Where(record => query.Length == 0 ||
                                 (record.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (record.Prefab ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            switch (sort)
            {
                case StructureSort.Type: records = records.OrderBy(r => r.Prefab).ThenBy(r => r.Name); break;
                case StructureSort.Capability: records = records.OrderBy(r => (int)r.Capabilities).ThenBy(r => r.Name); break;
                case StructureSort.Status: records = records.OrderByDescending(r => r.IsLiveIn(colony)).ThenBy(r => r.Name); break;
                default: records = records.OrderBy(r => r.Name); break;
            }
            return records.ToList();
        }

        /// <summary>Renames a registered structure. The record keeps its identity and targets.</summary>
        internal static bool RenameStructure(Colony colony, ZDOID id, string name)
        {
            if (colony == null || string.IsNullOrWhiteSpace(name)) return false;
            List<StructureRecord> records = colony.State.GetStructures();
            StructureRecord record = records.Find(r => r.Id == id);
            if (record == null) return false;
            record.Name = name.Trim();
            // The list lives on the hearth, so the hearth must be ours to write. Registering
            // and removing both claim it; renaming did not, which made a non-owner's rename
            // land locally and vanish on the next sync.
            if (colony.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            colony.State.SetStructures(records);
            return true;
        }

        /// <summary>
        ///     Edits a registered structure's settings.
        /// </summary>
        /// <remarks>
        ///     Takes a mutation rather than a settings object, so a caller cannot hand back a
        ///     stale copy and silently undo an edit made between reading and writing. The whole
        ///     record list is rewritten either way - that is how it is stored - so the hearth is
        ///     claimed first, as renaming and registering both do.
        /// </remarks>
        internal static bool EditSettings(Colony colony, ZDOID id, System.Action<StructureSettings> edit)
        {
            if (colony == null || edit == null) return false;

            List<StructureRecord> records = colony.State.GetStructures();
            StructureRecord record = records.Find(r => r.Id == id);
            if (record == null) return false;

            if (record.Settings == null) record.Settings = new StructureSettings();
            edit(record.Settings);

            if (colony.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            colony.State.SetStructures(records);
            return true;
        }

        /// <summary>
        ///     The one way a structure becomes a colony's. Both screen routes call this, so
        ///     neither can accept something the other refuses.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Ownership is claimed here and nowhere else. A durable reference can only be
        ///         minted by the peer that owns the object, and minting while merely listing
        ///         candidates would claim every chest, cart and ship in the radius. Registering
        ///         is the deliberate act, so it is the one that takes the object.
        ///     </para>
        ///     <para>
        ///         Writes are ordered release-then-take. A structure in two colonies' lists is
        ///         strictly worse than one in neither: both would hold its zone loaded, both
        ///         would index it, both would send villagers to it, and nothing would ever
        ///         notice. In neither is visible and one registration away from correct.
        ///     </para>
        /// </remarks>
        internal static RegisterOutcome Register(Colony colony, GameObject target)
        {
            if (colony == null) return RegisterOutcome.NoColony;
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
                return RegisterOutcome.NotUsable;
            if (!StructureRegistry.TryCapabilities(target, out StructureCapability capabilities))
                return RegisterOutcome.NotUsable;
            if (Utils.DistanceXZ(target.transform.position, colony.transform.position) > colony.EffectiveRadius)
                return RegisterOutcome.OutOfReach;

            ZDOID colonyId = colony.Id;
            if (colonyId.IsNone()) return RegisterOutcome.NoColony;

            ZDO targetZdo = view.GetZDO();
            ZDOID holder = ColonyMembership.GetColony(targetZdo);
            bool alreadyHere = holder == colonyId &&
                               colony.State.GetStructures().Exists(r => r.Id == targetZdo.m_uid);
            if (alreadyHere) return RegisterOutcome.AlreadyHere;

            view.ClaimOwnership();
            string token = Core.PersistentZdoReference.Ensure(targetZdo);
            if (string.IsNullOrEmpty(token)) return RegisterOutcome.NotOwnable;

            bool moved = false;
            if (!holder.IsNone() && holder != colonyId)
            {
                if (!TryRelease(holder, targetZdo.m_uid)) return RegisterOutcome.HolderUnreachable;
                moved = true;
            }

            StructureRecord record = new StructureRecord
            {
                Id = targetZdo.m_uid,
                PersistentId = token,
                Name = StructureRegistry.DisplayName(target),
                Prefab = Utils.GetPrefabName(target),
                Capabilities = capabilities
            };

            if (!colony.RegisterStructure(record)) return RegisterOutcome.OutOfReach;
            ColonyMembership.SetColony(targetZdo, colonyId);
            return moved ? RegisterOutcome.Moved : RegisterOutcome.Registered;
        }

        /// <summary>
        ///     Takes a structure out of the colony that holds it, loaded or not.
        /// </summary>
        /// <remarks>
        ///     An outpost's hearth is routinely not loaded, so this works from the ZDO rather
        ///     than from a <see cref="Colony" /> instance - which only exists for loaded
        ///     hearths. Returning false rather than writing blind is what stops a half-move: on
        ///     a client a hearth that was never replicated cannot be read at all, and the move
        ///     must fail whole.
        /// </remarks>
        private static bool TryRelease(ZDOID holderId, ZDOID structureId)
        {
            ZDO holder = ZDOMan.instance?.GetZDO(holderId);
            if (holder == null || !holder.IsValid()) return false;

            holder.SetOwner(ZDOMan.GetSessionID());
            ColonyState state = new ColonyState(holder);
            List<StructureRecord> records = state.GetStructures();
            if (records.RemoveAll(r => r.Id == structureId) > 0) state.SetStructures(records);
            return true;
        }

        /// <summary>What to tell the player. Explicit, with no branch that invents a reason.</summary>
        internal static string Explain(RegisterOutcome outcome, string what, string colonyName)
        {
            switch (outcome)
            {
                case RegisterOutcome.Registered: return $"Registered {what} to {colonyName}.";
                case RegisterOutcome.Moved: return $"Moved {what} to {colonyName}.";
                case RegisterOutcome.AlreadyHere: return $"{what} already belongs to {colonyName}.";
                case RegisterOutcome.OutOfReach: return $"{what} is outside {colonyName}'s reach.";
                case RegisterOutcome.NotUsable: return $"{what} is not something a Kolony can use.";
                case RegisterOutcome.NotOwnable: return $"{what} could not be claimed just now - try again.";
                case RegisterOutcome.HolderUnreachable: return $"{what} belongs to a Kolony that is not loaded here.";
                case RegisterOutcome.NoColony: return "No Kolony to register that to.";
                default: return string.Empty;
            }
        }


    }
}
