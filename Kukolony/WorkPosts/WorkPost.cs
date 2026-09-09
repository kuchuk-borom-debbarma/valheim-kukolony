using System.Collections.Generic;
using System.Linq;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.WorkPosts
{
    /// <summary>
    ///     A placed work post. Defines a job, and villagers bind themselves to it.
    ///
    ///     The post holds the configuration; villagers hold their own progress. Keeping
    ///     those apart means several villagers can work one post without fighting over
    ///     shared state, and a post can be reconfigured without disturbing anyone midway
    ///     through a cycle.
    /// </summary>
    internal sealed class WorkPost : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;

        internal WorkPostState State => new WorkPostState(Bind() && _nview.IsValid() ? _nview.GetZDO() : null);

        internal float EffectiveRadius
        {
            get
            {
                float configured = State.Radius;
                return configured > 0f ? configured : ModConfig.WorkPostRadius.Value;
            }
        }

        internal ZDOID Id => Bind() && _nview.IsValid() ? _nview.GetZDO().m_uid : ZDOID.None;

        /// <summary>Every loaded post. Used by villagers looking for work.</summary>
        internal static List<WorkPost> Instances { get; } = new List<WorkPost>();

        /// <summary>
        ///     Nearest post to a point, within range. Villagers bind to whatever is
        ///     closest rather than being assigned, so a newly placed post picks up idle
        ///     villagers without any UI existing yet.
        /// </summary>
        internal static WorkPost FindNearest(Vector3 point, float maxDistance)
        {
            WorkPost closest = null;
            float closestDistance = maxDistance;

            foreach (WorkPost post in Instances)
            {
                if (post == null || !post.State.IsValid)
                {
                    continue;
                }

                float distance = Utils.DistanceXZ(post.transform.position, point);
                if (distance > closestDistance)
                {
                    continue;
                }

                closest = post;
                closestDistance = distance;
            }

            return closest;
        }

        private void Awake() => Instances.Add(this);

        /// <summary>
        ///     Gives a newly placed post a working default, so it does something the
        ///     moment it is built instead of looking broken until configured.
        ///     Only the owner may write, and only once.
        /// </summary>
        internal void EnsureDefaults()
        {
            if (!Bind() || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            WorkPostState state = State;
            if (state.HasJob)
            {
                return;
            }

            state.SetJob(JobLibrary.Haul);
            state.SetItemFilter(ModConfig.WorkPostDefaultItem.Value);
            Core.Log.Info($"Work post configured: {JobLibrary.Haul} {ModConfig.WorkPostDefaultItem.Value}");
        }

        private void OnDestroy() => Instances.Remove(this);

        private bool Bind()
        {
            if (_nview != null)
            {
                return true;
            }

            return TryGetComponent(out _nview);
        }

        /// <summary>Opens the configuration panel. Hold and alt do nothing.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || alt)
            {
                return false;
            }

            Gui.WorkPostPanel.Instance?.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>
        ///     How far above the object the hover text sits. Valheim 1.0 added this to
        ///     Hoverable; zero keeps the vanilla placement.
        /// </summary>
        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "$kukolony_workpost";

        public string GetHoverText()
        {
            WorkPostState state = State;
            if (!state.IsValid)
            {
                return Localization.instance.Localize("$kukolony_workpost");
            }

            string job = state.HasJob ? state.JobId : "no job";
            string item = string.IsNullOrEmpty(state.ItemFilter) ? "nothing" : state.ItemFilter;
            int workers = CountBoundVillagers();

            return Localization.instance.Localize(
                $"$kukolony_workpost\n<color=grey>{job} {item} - {workers} villager(s)</color>"
                + "\n[<color=yellow><b>$KEY_Use</b></color>] configure");
        }

        private int CountBoundVillagers()
        {
            ZDOID id = Id;
            if (id.IsNone())
            {
                return 0;
            }

            // Registry rather than a scene scan: this runs from hover text, every frame
            // the player looks at a post.
            return Villager.Instances.Count(v => v != null && v.State.Post == id);
        }
    }
}
