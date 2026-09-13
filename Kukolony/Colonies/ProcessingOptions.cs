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
        ///     item, not a list. A charcoal kiln has none at all, and neither does an oven or a
        ///     fermenter, which is why "what fuel does this take" has to be allowed to answer
        ///     "nothing" rather than being assumed.
        /// </remarks>
        internal static string Fuel(string prefabName)
        {
            Smelter smelter = Component<Smelter>(prefabName);
            return smelter != null && smelter.m_fuelItem != null
                ? smelter.m_fuelItem.gameObject.name
                : string.Empty;
        }

        /// <summary>
        ///     Everything this station converts, as prefab names.
        /// </summary>
        /// <remarks>
        ///     Asked of whichever component the prefab actually carries. The three keep their
        ///     conversion lists in three unrelated types with three unrelated element types -
        ///     which is exactly why this reads them one at a time rather than through a shared
        ///     interface the game does not provide.
        /// </remarks>
        internal static List<string> Inputs(string prefabName)
        {
            List<string> inputs = new List<string>();

            Smelter smelter = Component<Smelter>(prefabName);
            if (smelter != null && smelter.m_conversion != null)
            {
                foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                {
                    Add(inputs, conversion == null ? null : conversion.m_from);
                }
            }

            CookingStation cooking = Component<CookingStation>(prefabName);
            if (cooking != null && cooking.m_conversion != null)
            {
                foreach (CookingStation.ItemConversion conversion in cooking.m_conversion)
                {
                    Add(inputs, conversion == null ? null : conversion.m_from);
                }
            }

            Fermenter fermenter = Component<Fermenter>(prefabName);
            if (fermenter != null && fermenter.m_conversion != null)
            {
                foreach (Fermenter.ItemConversion conversion in fermenter.m_conversion)
                {
                    Add(inputs, conversion == null ? null : conversion.m_from);
                }
            }

            return inputs;
        }

        /// <summary>
        ///     How much this station holds, for turning a fraction into a count.
        /// </summary>
        /// <remarks>
        ///     A smelter counts ore, a cooking station counts slots, and a fermenter holds one
        ///     batch at a time. The unit differs; the question a player asks - "how full should
        ///     this be kept" - does not.
        /// </remarks>
        internal static int MaxInput(string prefabName)
        {
            Smelter smelter = Component<Smelter>(prefabName);
            if (smelter != null) return smelter.m_maxOre;

            CookingStation cooking = Component<CookingStation>(prefabName);
            if (cooking != null) return cooking.m_slots != null ? cooking.m_slots.Length : 0;

            return Component<Fermenter>(prefabName) != null ? 1 : 0;
        }

        internal static int MaxFuel(string prefabName)
        {
            Smelter smelter = Component<Smelter>(prefabName);
            return smelter != null ? smelter.m_maxFuel : 0;
        }

        private static void Add(List<string> inputs, ItemDrop from)
        {
            if (from == null) return;

            string name = from.gameObject.name;
            if (name.Length > 0 && !inputs.Contains(name)) inputs.Add(name);
        }

        private static T Component<T>(string prefabName) where T : UnityEngine.Component
        {
            if (string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null) return null;

            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            return prefab != null ? prefab.GetComponentInChildren<T>(true) : null;
        }
    }
}
