using System;
using System.Collections.Generic;
using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Finds buildings near the player that could be added to a colony.
    ///
    ///     Deliberately keyed off the *player*, not the colony: the colony has no radius,
    ///     so "nearby" can only mean near whoever is doing the assigning. You walk to the
    ///     chest you want and add it.
    /// </summary>
    internal static class NearbyMembers
    {
        /// <summary>How far from the player to look. Not a colony radius - a reach.</summary>
        private const float SearchRadius = 32f;

        internal readonly struct Candidate
        {
            internal Candidate(ZNetView view, string label, float distance)
            {
                View = view;
                Label = label;
                Distance = distance;
            }

            internal ZNetView View { get; }

            internal string Label { get; }

            internal float Distance { get; }
        }

        private static readonly List<Piece> PieceBuffer = new List<Piece>();

        internal static List<Candidate> Find(ColonyMemberKind kind, Vector3 around)
        {
            List<Candidate> results = new List<Candidate>();

            PieceBuffer.Clear();
            Piece.GetAllPiecesInRadius(around, SearchRadius, PieceBuffer);

            foreach (Piece piece in PieceBuffer)
            {
                if (piece == null || !piece.TryGetComponent(out ZNetView view) || !view.IsValid())
                {
                    continue;
                }

                if (!Matches(kind, piece.gameObject, out string label))
                {
                    continue;
                }

                results.Add(new Candidate(view, label, Utils.DistanceXZ(piece.transform.position, around)));
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }

        private static bool Matches(ColonyMemberKind kind, GameObject candidate, out string label)
        {
            label = string.Empty;

            switch (kind)
            {
                case ColonyMemberKind.Container:
                    if (candidate.TryGetComponent(out Container container))
                    {
                        label = Localization.instance.Localize(container.m_name);
                        return true;
                    }

                    return false;

                case ColonyMemberKind.Station:
                    if (candidate.GetComponent<WorkPosts.WorkPost>() != null)
                    {
                        label = "Work post";
                        return true;
                    }

                    return false;

                case ColonyMemberKind.Home:
                    if (candidate.GetComponent<Bed>() != null)
                    {
                        label = "Bed";
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        internal static string Describe(Candidate candidate) =>
            $"{candidate.Label} - {candidate.Distance:F0}m";

        internal static string KindLabel(ColonyMemberKind kind)
        {
            switch (kind)
            {
                case ColonyMemberKind.Villager: return "Villagers";
                case ColonyMemberKind.Container: return "Storage";
                case ColonyMemberKind.Station: return "Workstations";
                default: return "Homes";
            }
        }
    }
}
