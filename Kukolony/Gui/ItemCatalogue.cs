using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Searchable index of every item in the game for concrete job filter editors.
    ///
    ///     Matching covers both the prefab name and the localised display name, because a
    ///     player thinking "Wood" and a player thinking "$item_wood" should both find it.
    /// </summary>
    internal static class ItemCatalogue
    {
        internal readonly struct Entry
        {
            internal Entry(string prefabName, string displayName)
            {
                PrefabName = prefabName;
                DisplayName = displayName;
            }

            /// <summary>What gets stored in a job's item filter; executors match on this.</summary>
            internal string PrefabName { get; }

            /// <summary>What the player reads.</summary>
            internal string DisplayName { get; }
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>
        ///     Built once from ObjectDB. Rebuilt if the game reloads it, since a stale
        ///     catalogue would offer items that no longer resolve.
        /// </summary>
        internal static void Rebuild()
        {
            Entries.Clear();

            if (ObjectDB.instance == null)
            {
                return;
            }

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop))
                {
                    continue;
                }

                string token = drop.m_itemData.m_shared.m_name;
                string display = string.IsNullOrEmpty(token)
                    ? prefab.name
                    : Localization.instance.Localize(token);

                Entries.Add(new Entry(prefab.name, display));
            }
        }

        internal static int Count => Entries.Count;

        /// <summary>
        ///     Items matching a filter, best first. An empty filter returns nothing rather
        ///     than everything - a wall of results is not a useful default.
        ///
        ///     Every match is scored and then sorted, rather than returning the first N in
        ///     catalogue order. Taking them in ObjectDB order meant searching "wood" could
        ///     fill all six rows with WoodArrow, WoodBridge and friends and never show
        ///     Wood itself, which is exactly the item the player meant.
        /// </summary>
        internal static List<Entry> Search(string filter, int limit)
        {
            List<Entry> results = new List<Entry>();
            if (string.IsNullOrEmpty(filter))
            {
                return results;
            }

            string needle = filter.ToLowerInvariant();
            List<KeyValuePair<int, Entry>> scored = new List<KeyValuePair<int, Entry>>();

            foreach (Entry entry in Entries)
            {
                int score = Score(entry, needle);
                if (score > 0)
                {
                    scored.Add(new KeyValuePair<int, Entry>(score, entry));
                }
            }

            // Higher score first; ties broken by the shorter name, so "Wood" beats
            // "WoodArrow" when both are prefix matches.
            scored.Sort((a, b) =>
            {
                int byScore = b.Key.CompareTo(a.Key);
                return byScore != 0
                    ? byScore
                    : a.Value.PrefabName.Length.CompareTo(b.Value.PrefabName.Length);
            });

            for (int i = 0; i < scored.Count && results.Count < limit; i++)
            {
                results.Add(scored[i].Value);
            }

            return results;
        }

        /// <summary>0 means no match. Higher is a better match.</summary>
        private static int Score(Entry entry, string needle)
        {
            string prefab = entry.PrefabName.ToLowerInvariant();
            string display = entry.DisplayName.ToLowerInvariant();

            if (prefab == needle || display == needle)
            {
                return 4;
            }

            if (prefab.StartsWith(needle) || display.StartsWith(needle))
            {
                return 3;
            }

            if (prefab.Contains(needle) || display.Contains(needle))
            {
                return 1;
            }

            return 0;
        }
    }
}
