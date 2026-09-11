using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     What a processing station will actually take, read from the structure rather than
    ///     typed by the player.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read from the <em>prefab</em>, not the placed instance. A smelter's fuel and its
    ///         conversion list are asset data, identical on every instance of that prefab, so
    ///         the answer needs nothing loaded - which means an outpost's kiln can be configured
    ///         from home. The record already stores the prefab name for exactly this kind of
    ///         question.
    ///     </para>
    ///     <para>
    ///         Asking the object instead would have meant caching the answer on the record and
    ///         keeping the cache fresh, for data that cannot vary between instances.
    ///     </para>
    /// </remarks>
    internal static class ProcessingOptions
    {
        /// <summary>
        ///     The single item this station burns, or empty when it burns nothing.
        /// </summary>
        /// <remarks>
        ///     Singular because the game models it that way - <c>Smelter.m_fuelItem</c> is one
        ///     item, not a list. A charcoal kiln has none at all, which is why "what fuel does
        ///     this take" has to be allowed to answer "nothing" rather than being assumed.
        /// </remarks>
        internal static string Fuel(string prefabName)
        {
            Smelter smelter = Station(prefabName);
            return smelter != null && smelter.m_fuelItem != null
                ? smelter.m_fuelItem.gameObject.name
                : string.Empty;
        }

        /// <summary>Everything this station converts, as prefab names.</summary>
        internal static List<string> Inputs(string prefabName)
        {
            List<string> inputs = new List<string>();
            Smelter smelter = Station(prefabName);
            if (smelter == null) return inputs;

            foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
            {
                if (conversion == null || conversion.m_from == null) continue;
                string name = conversion.m_from.gameObject.name;
                if (!inputs.Contains(name)) inputs.Add(name);
            }

            return inputs;
        }

        /// <summary>How much this station holds, for turning a fraction into a count.</summary>
        internal static int MaxInput(string prefabName)
        {
            Smelter smelter = Station(prefabName);
            return smelter != null ? smelter.m_maxOre : 0;
        }

        internal static int MaxFuel(string prefabName)
        {
            Smelter smelter = Station(prefabName);
            return smelter != null ? smelter.m_maxFuel : 0;
        }

        private static Smelter Station(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null) return null;
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            return prefab != null ? prefab.GetComponentInChildren<Smelter>(true) : null;
        }
    }
}
