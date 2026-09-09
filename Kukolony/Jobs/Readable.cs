using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Turns prefab names and ZDO targets into words a player would recognise.
    ///
    ///     Villagers report what they are doing on hover, and "Wood" reads better than
    ///     "Wood(Clone)" while "$item_wood" reads worse than either.
    /// </summary>
    internal static class Readable
    {
        internal static string Item(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return "something";
            }

            if (ObjectDB.instance == null)
            {
                return prefabName;
            }

            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop))
            {
                return prefabName;
            }

            string token = drop.m_itemData.m_shared.m_name;
            return string.IsNullOrEmpty(token) ? prefabName : Localization.instance.Localize(token);
        }

        /// <summary>
        ///     Describes whatever a step is currently pointed at. Falls back gracefully:
        ///     a target whose zone has unloaded is still worth naming as "somewhere".
        /// </summary>
        internal static string Target(JobContext context)
        {
            GameObject target = context.ResolveTarget();
            if (target == null)
            {
                return "somewhere";
            }

            if (target.TryGetComponent(out ItemDrop drop))
            {
                string token = drop.m_itemData.m_shared.m_name;
                return string.IsNullOrEmpty(token)
                    ? Utils.GetPrefabName(target)
                    : Localization.instance.Localize(token);
            }

            if (target.TryGetComponent(out Container container))
            {
                return Localization.instance.Localize(
                    string.IsNullOrEmpty(container.m_name) ? "container" : container.m_name);
            }

            return Utils.GetPrefabName(target);
        }
    }
}
