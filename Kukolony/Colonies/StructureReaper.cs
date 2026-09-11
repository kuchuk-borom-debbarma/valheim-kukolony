using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Drops a colony's record for a structure once the structure is known to be destroyed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The only thing in the mod that deletes a player's configuration, so it acts on
    ///         positive evidence and nothing weaker. A ZDO that fails to resolve is <em>not</em>
    ///         evidence: measured three ways, an unloaded chest 900m away still resolves, and a
    ///         client is only told about part of the world at all. Walking away from an outpost
    ///         must never cost its settings.
    ///     </para>
    ///     <para>
    ///         The evidence used is the game's own dead list. That list is written inside a
    ///         server-only branch, so on a joining client it is always empty and this simply
    ///         never fires - it fails closed, and the feature does not exist for clients.
    ///     </para>
    ///     <para>
    ///         It is also cleared on world load rather than pruned by time, which decides the
    ///         shape of the whole thing: destruction is evidence only within the session that
    ///         saw it. A structure smashed while nobody was logged in is never reaped, and the
    ///         detail screen's Remove is how a player clears that - which makes manual removal
    ///         the common path rather than the corner case.
    ///     </para>
    /// </remarks>
    internal static class StructureReaper
    {
        /// <summary>
        ///     The last address each record was seen alive at, this session.
        /// </summary>
        /// <remarks>
        ///     Loading renumbers every ZDO, so a record read from disk carries an address from
        ///     the previous session. It resolves through its token while the object lives; the
        ///     moment the object dies the token lookup fails and resolution falls back to that
        ///     stale address - which is not the id the game put in its dead list. Without this
        ///     the reaper would silently never fire for anything not registered this session,
        ///     and would look like it worked because new registrations reap fine.
        /// </remarks>
        private static readonly Dictionary<string, ZDOID> LastSeen = new Dictionary<string, ZDOID>();

        private static ZDOMan _owner;

        /// <summary>
        ///     Removes every record of this colony's whose structure is known dead. Returns how
        ///     many went.
        /// </summary>
        internal static int Sweep(Colony colony)
        {
            if (colony == null || ZDOMan.instance == null) return 0;
            Forget();

            List<StructureRecord> records = colony.State.GetStructures();
            List<StructureRecord> dead = new List<StructureRecord>();

            foreach (StructureRecord record in records)
            {
                ZDOID address = Address(record);
                if (address.IsNone()) continue;

                if (ZDOMan.instance.GetZDO(address) != null)
                {
                    // Alive right now: remember where, so its death is recognisable later.
                    if (!string.IsNullOrEmpty(record.PersistentId)) LastSeen[record.PersistentId] = address;
                    continue;
                }

                if (IsKnownDead(address)) dead.Add(record);
            }

            foreach (StructureRecord record in dead)
            {
                colony.RemoveStructure(record.Id);
                if (!string.IsNullOrEmpty(record.PersistentId)) LastSeen.Remove(record.PersistentId);
                Report.Say($"{record.Name} was destroyed, and is no longer {colony.State.Name}'s.");
            }

            return dead.Count;
        }

        /// <summary>
        ///     Where to look for this record: where it was last seen alive if we know, else the
        ///     address it currently resolves to.
        /// </summary>
        private static ZDOID Address(StructureRecord record)
        {
            if (!string.IsNullOrEmpty(record.PersistentId) &&
                LastSeen.TryGetValue(record.PersistentId, out ZDOID seen))
            {
                // Only while it still refers to this record - a reused address would otherwise
                // let one record's death reap another's.
                ZDO zdo = ZDOMan.instance.GetZDO(seen);
                if (zdo == null || PersistentZdoReference.Get(zdo) == record.PersistentId) return seen;
            }

            return record.Id;
        }

        private static bool IsKnownDead(ZDOID id) =>
            ZDOMan.instance.m_deadZDOs != null && ZDOMan.instance.m_deadZDOs.ContainsKey(id);

        /// <summary>Drops what was learned about a world when the world goes.</summary>
        private static void Forget()
        {
            if (ReferenceEquals(_owner, ZDOMan.instance)) return;
            LastSeen.Clear();
            _owner = ZDOMan.instance;
        }

        /// <summary>Discards the session memory. For tests that need a known starting point.</summary>
        internal static void ResetForTest() => LastSeen.Clear();
    }

    /// <summary>
    ///     Runs the reaper on a timer, on the server only.
    /// </summary>
    /// <remarks>
    ///     Deliberately its own component rather than a step inside the keep-alive: that is
    ///     gated on its own config switch, and turning off-screen simulation off should not
    ///     quietly stop a colony noticing that its chests were smashed.
    /// </remarks>
    internal sealed class StructureReaperDriver : MonoBehaviour
    {
        private const float IntervalSeconds = 5f;

        private float _next;

        internal static void Register(GameObject host) => host.AddComponent<StructureReaperDriver>();

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            if (Time.time < _next) return;
            _next = Time.time + IntervalSeconds;

            foreach (Colony colony in Colony.Instances)
            {
                if (colony != null) StructureReaper.Sweep(colony);
            }
        }
    }
}
