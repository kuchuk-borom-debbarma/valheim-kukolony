using Kukolony.Colonies;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>
    ///     Everything a station protocol needs, and nothing more. Handing over the engine
    ///     would let a protocol reach into job dispatch; this keeps it to one station, one
    ///     villager, and the item in hand.
    /// </summary>
    internal readonly struct StationContext
    {
        internal StationContext(GameObject target, ZNetView view, Inventory bag,
            ItemDrop.ItemData item, VillagerState state)
        {
            Target = target;
            View = view;
            Bag = bag;
            Item = item;
            State = state;
        }

        internal GameObject Target { get; }
        internal ZNetView View { get; }
        internal Inventory Bag { get; }

        /// <summary>First bag item this job accepts, or null when carrying nothing usable.</summary>
        internal ItemDrop.ItemData Item { get; }

        internal VillagerState State { get; }

        /// <summary>Prefab name of the carried item, or empty when there is none.</summary>
        internal string ItemName => Item == null ? string.Empty : Utils.GetPrefabName(Item.m_dropPrefab);

        /// <summary>
        ///     Removes the carried item. Always call this before submitting the RPC that
        ///     consumes it, so a failed removal cannot hand the station a free item.
        /// </summary>
        internal bool Consume() => Item != null && Bag.RemoveItem(Item, 1);
    }

    /// <summary>
    ///     One station's operating protocol: what it accepts, when it is full, and which
    ///     verified vanilla RPC moves it forward.
    /// </summary>
    /// <remarks>
    ///     Stations were a switch over job types, which meant a new station could not be
    ///     added without editing the engine, and a job type had to exist for every station.
    ///     A protocol is selected by what the target actually is, so supporting a new one is
    ///     a new file and a registry entry.
    /// </remarks>
    internal interface IStationProtocol
    {
        /// <summary>Capability this protocol serves, used to disambiguate multi-component prefabs.</summary>
        StructureCapability Capability { get; }

        /// <summary>True when this protocol can operate the target.</summary>
        bool Matches(GameObject target);

        /// <summary>Performs one step of work against the station.</summary>
        JobResult Operate(StationContext context, out string activity);
    }
}
