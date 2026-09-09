using System.Collections.Generic;
using Kukolony.Villagers;
using Kukolony.WorkPosts;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Everything a step needs, assembled fresh each tick.
    ///
    ///     Nothing is cached between ticks on purpose. Ownership of a villager can move to
    ///     another player between one tick and the next, so a context carried in memory
    ///     would be stale or simply gone. Rebuilding from the ZDO means a villager that
    ///     changes hands resumes mid-job instead of starting over.
    /// </summary>
    internal sealed class JobContext
    {
        internal JobContext(Villager villager, MonsterAI ai, Container bag, WorkPost post, float deltaTime)
        {
            Villager = villager;
            Ai = ai;
            Bag = bag;
            Post = post;
            DeltaTime = deltaTime;
        }

        internal Villager Villager { get; }

        internal MonsterAI Ai { get; }

        /// <summary>The villager's own persistent inventory.</summary>
        internal Container Bag { get; }

        internal WorkPost Post { get; }

        internal float DeltaTime { get; }

        /// <summary>What this post works with, e.g. "Wood".</summary>
        internal string ItemFilter
        {
            get
            {
                string active = Villager.State.ActiveItem;
                List<string> filters = Post.State.ItemFilters;
                return !string.IsNullOrEmpty(active) ? active : (filters.Count > 0 ? filters[0] : string.Empty);
            }
        }

        internal List<string> ItemFilters => Post.State.ItemFilters;

        /// <summary>Everything happens relative to the post, not the villager.</summary>
        internal Vector3 Anchor => Post.transform.position;

        internal float Radius => Post.EffectiveRadius;

        /// <summary>
        ///     What the current step is acting on. Persisted, so it survives the step
        ///     boundary and an ownership change.
        /// </summary>
        internal ZDOID Target
        {
            get => Villager.State.StepTarget;
            set => Villager.State.SetStepTarget(value);
        }

        /// <summary>Resolves <see cref="Target" /> to a live object, or null if it is gone.</summary>
        internal GameObject ResolveTarget()
        {
            ZDOID target = Target;
            return target.IsNone() ? null : ZNetScene.instance.FindInstance(target);
        }
    }
}
