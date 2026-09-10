using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>
    ///     Chooses the protocol for a station by what the target actually is.
    /// </summary>
    /// <remarks>
    ///     Several vanilla prefabs carry more than one of these components - an oven is a
    ///     cooking station, a hearth is a fireplace, a windmill is a smelter, and fuelled
    ///     cooking stations are both. So the job's declared capability narrows the search
    ///     first and probe order only settles the rest. Fireplace is last precisely because
    ///     it is the one most often present alongside something else.
    /// </remarks>
    internal static class StationProtocols
    {
        private static readonly IStationProtocol[] Registered =
        {
            new BeehiveProtocol(),
            new FermenterProtocol(),
            new CookingStationProtocol(),
            new SmelterProtocol(),
            new FireplaceProtocol()
        };

        /// <summary>The protocol for this target, or null when nothing can operate it.</summary>
        internal static IStationProtocol Resolve(GameObject target, StructureCapability declared)
        {
            if (target == null) return null;
            foreach (IStationProtocol protocol in Registered)
                if (Wants(declared, protocol.Capability) && protocol.Matches(target)) return protocol;
            return null;
        }

        /// <summary>An undeclared capability accepts any protocol; a declared one narrows to it.</summary>
        private static bool Wants(StructureCapability declared, StructureCapability kind) =>
            declared == StructureCapability.None || (declared & kind) != 0;
    }
}
