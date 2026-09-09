using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Containers within a work post's radius, for the destination picker.
    ///
    ///     Reuses the same registry lookup the job's ResolveDestinationStep uses, so what
    ///     the panel offers is exactly what a villager can actually reach.
    /// </summary>
    internal static class NearbyContainers
    {
        internal readonly struct Entry
        {
            internal Entry(Container container, float distance, int itemCount)
            {
                Container = container;
                Distance = distance;
                ItemCount = itemCount;
            }

            internal Container Container { get; }

            internal float Distance { get; }

            internal int ItemCount { get; }

            internal string Describe()
            {
                string contents = ItemCount == 0 ? "empty" : $"{ItemCount} stack(s)";
                return $"{Container.m_name} - {Distance:F0}m, {contents}";
            }
        }

        private static readonly List<Piece> PieceBuffer = new List<Piece>();

        internal static List<Entry> Find(Vector3 anchor, float radius)
        {
            List<Entry> results = new List<Entry>();

            PieceBuffer.Clear();
            Piece.GetAllPiecesInRadius(anchor, radius, PieceBuffer);

            foreach (Piece piece in PieceBuffer)
            {
                if (piece == null || !piece.TryGetComponent(out Container container))
                {
                    continue;
                }

                Inventory inventory = container.GetInventory();
                if (inventory == null)
                {
                    continue;
                }

                results.Add(new Entry(
                    container,
                    Utils.DistanceXZ(piece.transform.position, anchor),
                    inventory.NrOfItems()));
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }
    }
}
