using System;
using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    [Flags]
    internal enum StructureCapability
    {
        None = 0,
        Container = 1,
        Fireplace = 2,
        Smelter = 4,
        CookingStation = 8,
        Fermenter = 16,
        BeeHive = 32
    }

    /// <summary>A named, persistent reference to a placed (ZNet-backed) structure.</summary>
    internal sealed class StructureRecord
    {
        internal ZDOID Id;
        internal string PersistentId;
        internal string Name;
        internal string Prefab;
        internal StructureCapability Capabilities;

        internal bool IsLiveIn(Colony colony)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(Id) : null;
            return zdo != null && zdo.IsValid() && colony != null
                   && Utils.DistanceXZ(zdo.GetPosition(), colony.transform.position) <= colony.EffectiveRadius;
        }
    }

    /// <summary>Discovers only placed network structures; loose drops and characters never qualify.</summary>
    internal static class StructureRegistry
    {
        /// <summary>
        ///     Everything within the colony radius that a colony could work with.
        /// </summary>
        /// <remarks>
        ///     Scans the loaded objects rather than Valheim's piece registry. The piece registry
        ///     is cheaper, but it only holds things built from the hammer's own piece list - so
        ///     a cart, which is a perfectly good container a player would expect villagers to
        ///     draw from, was invisible to a colony and nothing said why. Every candidate still
        ///     has to be a networked object with a capability; loose drops and creatures are
        ///     excluded as before.
        ///
        ///     The cost is affordable because nothing calls this on a tick: it runs when a
        ///     player asks what is registerable, which is a deliberate act.
        /// </remarks>
        internal static List<StructureRecord> FindRegisterable(Colony colony)
        {
            List<StructureRecord> found = new List<StructureRecord>();
            if (colony == null || ZNetScene.instance == null) return found;
            float radius = colony.EffectiveRadius;
            Vector3 centre = colony.transform.position;

            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;
                if (Utils.DistanceXZ(view.transform.position, centre) > radius) continue;
                if (!TryCapabilities(view.gameObject, out StructureCapability capabilities)) continue;
                string persistentId = PersistentZdoReference.Ensure(view.GetZDO());
                if (string.IsNullOrEmpty(persistentId)) continue;
                found.Add(new StructureRecord { Id = view.GetZDO().m_uid, PersistentId = persistentId,
                    Name = DisplayName(view.gameObject),
                    Prefab = Utils.GetPrefabName(view.gameObject), Capabilities = capabilities });
            }
            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// <summary>
        ///     A record for one specific object, whether or not it is in radius, so a player
        ///     can register what they are looking at rather than what a sweep happened to find.
        ///     Null when the object is not something a colony can use.
        /// </summary>
        internal static StructureRecord Describe(GameObject candidate)
        {
            if (candidate == null || !candidate.TryGetComponent(out ZNetView view) || !view.IsValid()) return null;
            if (!TryCapabilities(candidate, out StructureCapability capabilities)) return null;
            string persistentId = PersistentZdoReference.Ensure(view.GetZDO());
            if (string.IsNullOrEmpty(persistentId)) return null;
            return new StructureRecord
            {
                Id = view.GetZDO().m_uid, PersistentId = persistentId,
                Name = DisplayName(candidate), Prefab = Utils.GetPrefabName(candidate),
                Capabilities = capabilities
            };
        }

        internal static bool TryCapabilities(GameObject candidate, out StructureCapability capabilities)
        {
            capabilities = StructureCapability.None;
            if (candidate == null || candidate.GetComponent<Character>() != null || candidate.GetComponent<ItemDrop>() != null)
                return false;
            if (Has<Container>(candidate)) capabilities |= StructureCapability.Container;
            if (Has<Fireplace>(candidate)) capabilities |= StructureCapability.Fireplace;
            if (Has<Smelter>(candidate)) capabilities |= StructureCapability.Smelter;
            if (Has<CookingStation>(candidate)) capabilities |= StructureCapability.CookingStation;
            if (Has<Fermenter>(candidate)) capabilities |= StructureCapability.Fermenter;
            if (Has<Beehive>(candidate)) capabilities |= StructureCapability.BeeHive;
            return capabilities != StructureCapability.None;
        }

        /// <summary>
        ///     Whether this networked object provides a component, wherever that component
        ///     happens to sit on its transform hierarchy.
        /// </summary>
        /// <remarks>
        ///     Looking only at the root is what made a cart unusable: the parts of a Valheim
        ///     object are routinely split across child transforms, and this mod does the same
        ///     thing itself - a villager's bag is a Container on a child. A child that belongs
        ///     to a different networked object does not count, or a building would inherit the
        ///     capabilities of everything standing inside it.
        /// </remarks>
        private static bool Has<T>(GameObject candidate) where T : Component
        {
            ZNetView owner = candidate.GetComponent<ZNetView>();
            foreach (T found in candidate.GetComponentsInChildren<T>(true))
            {
                if (found == null) continue;
                if (found.GetComponentInParent<ZNetView>() == owner) return true;
            }
            return false;
        }

        /// <summary>Why a candidate was rejected, for diagnostics. Empty when it qualifies.</summary>
        internal static string Explain(GameObject candidate)
        {
            if (candidate == null) return "null";
            if (!candidate.TryGetComponent(out ZNetView view)) return "no ZNetView";
            if (!view.IsValid()) return "invalid ZNetView";
            if (candidate.GetComponent<Character>() != null) return "is a creature";
            if (candidate.GetComponent<ItemDrop>() != null) return "is a loose item";
            if (!TryCapabilities(candidate, out StructureCapability capabilities))
                return "no usable component (children: " + ChildComponents(candidate) + ")";
            if (string.IsNullOrEmpty(PersistentZdoReference.Ensure(view.GetZDO())))
                return "not owned, so no durable id could be minted";
            return string.Empty;
        }

        private static string ChildComponents(GameObject candidate)
        {
            List<string> names = new List<string>();
            foreach (Component component in candidate.GetComponentsInChildren<Component>(true))
                if (component != null && !names.Contains(component.GetType().Name))
                    names.Add(component.GetType().Name);
            return string.Join(",", names.ToArray());
        }

        /// <summary>
        ///     What to call this on screen.
        /// </summary>
        /// <remarks>
        ///     A live object's Unity name is the prefab name with "(Clone)" stuck on the end,
        ///     so localising that produced rows reading "charcoal_kiln(Clone)". The game already
        ///     knows the readable name - a piece and a container each carry their own - and the
        ///     cleaned prefab name is the honest fallback when neither does.
        /// </remarks>
        internal static string DisplayName(GameObject target)
        {
            if (target == null) return string.Empty;
            string token = string.Empty;
            if (target.TryGetComponent(out Piece piece) && !string.IsNullOrEmpty(piece.m_name)) token = piece.m_name;
            else if (target.TryGetComponent(out Container container) && !string.IsNullOrEmpty(container.m_name))
                token = container.m_name;

            if (token.Length == 0) return Utils.GetPrefabName(target);
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }
    }
}
