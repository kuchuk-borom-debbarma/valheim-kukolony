using System;
using System.Collections.Generic;
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
        private static readonly List<Piece> Pieces = new List<Piece>();

        internal static List<StructureRecord> FindRegisterable(Colony colony)
        {
            List<StructureRecord> found = new List<StructureRecord>();
            if (colony == null) return found;
            Pieces.Clear();
            Piece.GetAllPiecesInRadius(colony.transform.position, colony.EffectiveRadius, Pieces);
            foreach (Piece piece in Pieces)
            {
                if (piece == null || !piece.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;
                if (!TryCapabilities(piece.gameObject, out StructureCapability capabilities)) continue;
                found.Add(new StructureRecord { Id = view.GetZDO().m_uid, Name = DisplayName(piece.gameObject),
                    Prefab = Utils.GetPrefabName(piece.gameObject), Capabilities = capabilities });
            }
            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        internal static bool TryCapabilities(GameObject candidate, out StructureCapability capabilities)
        {
            capabilities = StructureCapability.None;
            if (candidate == null || candidate.GetComponent<Character>() != null || candidate.GetComponent<ItemDrop>() != null)
                return false;
            if (candidate.GetComponent<Container>() != null) capabilities |= StructureCapability.Container;
            if (candidate.GetComponent<Fireplace>() != null) capabilities |= StructureCapability.Fireplace;
            if (candidate.GetComponent<Smelter>() != null) capabilities |= StructureCapability.Smelter;
            if (candidate.GetComponent<CookingStation>() != null) capabilities |= StructureCapability.CookingStation;
            if (candidate.GetComponent<Fermenter>() != null) capabilities |= StructureCapability.Fermenter;
            if (candidate.GetComponent<Beehive>() != null) capabilities |= StructureCapability.BeeHive;
            return capabilities != StructureCapability.None;
        }

        internal static string DisplayName(GameObject target) => Localization.instance != null
            ? Localization.instance.Localize(target.name) : target.name;
    }
}
