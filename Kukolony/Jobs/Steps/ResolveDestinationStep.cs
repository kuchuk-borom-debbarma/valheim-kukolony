using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs.Steps
{
    /// <summary>
    ///     Decides where the carried items go.
    ///
    ///     Input:  the post's bound destination, else its item filter and radius.
    ///     Output: <see cref="JobContext.Target" /> set to a container.
    ///
    ///     A bound container wins, so "put wood in *that* chest" is honoured exactly.
    ///     Falling back to the nearest chest already holding the item keeps a post working
    ///     if its chest is destroyed, rather than silently stalling.
    /// </summary>
    internal sealed class ResolveDestinationStep : IJobStep
    {
        private static readonly List<Piece> PieceBuffer = new List<Piece>();

        public string Name => "resolve_destination";

        public string Describe(JobContext context) =>
            $"finding somewhere to put {Readable.Item(context.ItemFilter)}";

        public StepStatus Tick(JobContext context)
        {
            List<ZDOID> boundDestinations = context.Post.State.Destinations;
            if (boundDestinations.Count > 0)
            {
                foreach (ZDOID bound in boundDestinations)
                {
                    GameObject boundObject = ZNetScene.instance.FindInstance(bound);
                    if (boundObject != null && boundObject.GetComponent<Container>() != null)
                    {
                        context.Target = bound;
                        return StepStatus.Succeeded;
                    }
                }

                // "Not instantiated" is a routine state off-screen and must not be read as
                // "destroyed" - doing so made a villager quietly deposit into a different
                // chest whenever the bound one was outside its loaded area, which is
                // correct while a player watches and wrong the moment they leave.
                foreach (ZDOID bound in boundDestinations)
                {
                    if (ZDOMan.instance.GetZDO(bound) != null)
                    {
                        return StepStatus.Running;
                    }
                }

                Log.Debug("[job] bound containers no longer exist, falling back to nearest match");
            }

            Container nearest = FindNearestContainerHolding(context);
            if (nearest == null)
            {
                return StepStatus.Failed;
            }

            context.Target = nearest.GetComponent<ZNetView>().GetZDO().m_uid;
            return StepStatus.Succeeded;
        }

        private static Container FindNearestContainerHolding(JobContext context)
        {
            // Piece keeps a static registry, so this is a list walk rather than a physics
            // query. See docs/code-style.md.
            PieceBuffer.Clear();
            Piece.GetAllPiecesInRadius(context.Anchor, context.Radius, PieceBuffer);

            Container closest = null;
            float closestDistance = float.MaxValue;

            foreach (Piece piece in PieceBuffer)
            {
                if (piece == null || !piece.TryGetComponent(out Container container))
                {
                    continue;
                }

                Inventory inventory = container.GetInventory();
                if (inventory == null || !inventory.HaveItem(context.ItemFilter))
                {
                    continue;
                }

                float distance = Utils.DistanceXZ(piece.transform.position, context.Anchor);
                if (distance >= closestDistance)
                {
                    continue;
                }

                closest = container;
                closestDistance = distance;
            }

            return closest;
        }
    }
}
