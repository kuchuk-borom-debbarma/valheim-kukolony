using System;
using System.Collections.Generic;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     What a colony understands a structure to be. The whole of it - growing this list is
    ///     how the mod grows, and nothing may be registered as something no milestone can use.
    /// </summary>
    /// <remarks>
    ///     Bit values are deliberately not consecutive. <see cref="Storage" /> and
    ///     <see cref="Processing" /> keep the bits their predecessors Container and Smelter
    ///     held, because they mean exactly the same thing and an existing record should keep
    ///     working. <see cref="Rest" /> takes a fresh bit rather than one a retired capability
    ///     used, or every old fireplace would read as a bed. The retired bits - Fireplace(2),
    ///     CookingStation(8), Fermenter(16), BeeHive(32) - are masked off when a record is
    ///     read, so a structure registered only as one of those becomes capability-less and is
    ///     shown as no longer understood rather than silently misread as something else.
    /// </remarks>
    [Flags]
    internal enum StructureCapability
    {
        None = 0,

        /// <summary>Things can be kept here. A Container.</summary>
        Storage = 1,

        /// <summary>Raw material becomes something else here. A Smelter - furnace or kiln.</summary>
        Processing = 4,

        /// <summary>One villager can sleep here. A Bed.</summary>
        Rest = 64
    }

    /// <summary>
    ///     The capability set, with no dependency on Unity so it can be verified in a second
    ///     rather than only inside a four-minute game run.
    /// </summary>
    internal static class StructureCapabilities
    {
    /// <summary>
    ///     Every capability this build understands, as a mask.
    /// </summary>
    /// <remarks>
    ///     Records written by an earlier build can carry bits for capabilities that have
    ///     since been retired. Masking on read is what stops one of those being reinterpreted
    ///     as a capability that now owns its bit.
    /// </remarks>
    internal const StructureCapability Known =
        StructureCapability.Storage | StructureCapability.Processing | StructureCapability.Rest;

    /// <summary>
    ///     What a player should read for a set of capabilities.
    /// </summary>
    /// <remarks>
    ///     Explicit, with no fallback that invents a plausible name: an unnamed capability
    ///     renders as nothing and fails loudly, because a <c>default:</c> branch returning a
    ///     real name once made a new job type display as an existing one.
    ///
    ///     <see cref="StructureCapability.None" /> is reachable on a record for the first
    ///     time now that retired bits are masked off. It means the colony once understood
    ///     this structure and no longer does - worth saying, because the alternative is a
    ///     row that matches no filter and silently does nothing.
    /// </remarks>
    internal static string Describe(StructureCapability capabilities)
    {
        capabilities &= Known;
        if (capabilities == StructureCapability.None) return "no longer understood";

        List<string> parts = new List<string>(3);
        if ((capabilities & StructureCapability.Storage) != 0) parts.Add("Storage");
        if ((capabilities & StructureCapability.Processing) != 0) parts.Add("Processing");
        if ((capabilities & StructureCapability.Rest) != 0) parts.Add("Rest");
        return string.Join(" + ", parts.ToArray());
    }
    }
}
