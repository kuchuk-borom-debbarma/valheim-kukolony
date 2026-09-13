using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Stopping work, going to bed, and getting up again.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rest is triggered by energy alone, never by nightfall. A settlement works in
    ///         shifts and nobody is stranded until dusk.
    ///     </para>
    ///     <para>
    ///         <b>Recovery is measured in in-game hours.</b> A night's sleep means a night of the
    ///         world's time, so it scales with the day length rather than with how long anybody
    ///         happens to be watching - and a player who lengthens their days gets villagers who
    ///         sleep through them rather than ones who wake at dawn regardless.
    ///     </para>
    ///     <para>
    ///         A tired villager is <b>Skipped, not Failed</b>. It has not failed its job - there
    ///         is simply nothing useful it can do - so it consumes no repetition and yields to
    ///         whatever is next in the queue.
    ///     </para>
    /// </remarks>
    internal static class Resting
    {
        /// <summary>
        ///     Close enough to lie down, for the case where the walk has not said so.
        /// </summary>
        /// <remarks>
        ///     A backstop rather than the rule. The walk's own answer is what decides, because it
        ///     knows how near a villager can get to a solid object on this terrain; this covers a
        ///     villager that was already standing on its bed and never had to walk anywhere.
        /// </remarks>
        private const float CloseEnough = 3f;

        /// <summary>How far above a bed to lie, so a villager rests on it rather than in it.</summary>
        private const float LyingHeight = .4f;

        /// <summary>Energy right now, worked out on demand rather than ticked.</summary>
        internal static float Now(VillagerState state)
        {
            if (!state.Resting || state.RestRate <= 0f) return state.StoredEnergy;

            double since = ZNet.instance == null ? 0d : ZNet.instance.GetTimeSeconds() - state.EnergyAt;
            return Energy.Recovered(state.StoredEnergy, (float)since, state.RestRate);
        }

        /// <summary>
        ///     Charges a villager for something it did, successful or not.
        /// </summary>
        /// <remarks>
        ///     Failures cost the same as successes. Charging only for success leaves a villager
        ///     thrashing at an unreachable chest working forever and never tiring, which is worse
        ///     than one that gets tired: it never stops, and never gives the queue a chance to
        ///     hand it something it can actually do.
        /// </remarks>
        internal static void Spend(VillagerState state)
        {
            if (state.Resting) return;

            state.SetEnergy(Energy.Spend(state.StoredEnergy, ModConfig.EnergyPerAction.Value));
        }

        /// <summary>
        ///     Rests if it should be resting.
        /// </summary>
        /// <returns>True when the villager is resting and must not be given work.</returns>
        internal static bool Tick(Villager villager, Colony colony, VillagerState state,
            VillagerWalk walk, VillagerAnimation animation, float deltaTime, out string doing)
        {
            doing = string.Empty;

            float energy = Now(state);
            bool resting = state.Resting;

            if (!Energy.ShouldRest(energy, resting, ModConfig.TiredBelow.Value,
                    ModConfig.RestedAbove.Value))
            {
                if (!resting) return false;

                Wake(villager, state, animation, energy);
                return false;
            }

            if (!resting)
            {
                // Bank the exact energy it stopped at, so recovery is summed from a known value
                // at a known time rather than from whatever happened to be written last.
                state.SetResting(true);
                state.SetEnergy(energy);
                state.SetRestRate(RatePerSecond(ModConfig.GroundHoursToRest.Value));
            }

            Bed bed = FindBed(colony, villager);
            if (bed != null)
            {
                doing = Settle(villager, state, walk, animation, bed.transform, bed,
                    ModConfig.BedHoursToRest.Value, "sleeping", "going to bed", energy, deltaTime);
                return true;
            }

            // No bed, or its zone is not loaded. The hearth is the settlement's fallback and it
            // is slower, which is what makes building a bed worth doing.
            doing = Settle(villager, state, walk, animation, colony.transform, null,
                ModConfig.HearthHoursToRest.Value, "resting", "going to rest", energy, deltaTime);
            return true;
        }

        /// <summary>The villager's own bed, if it has one and it is loaded.</summary>
        private static Bed FindBed(Colony colony, Villager villager)
        {
            StructureRecord record = SettlementIndex.BedOf(colony, villager.Id);
            if (record == null || ZNetScene.instance == null) return null;

            GameObject instance = ZNetScene.instance.FindInstance(record.Id);
            return instance == null ? null : instance.GetComponentInChildren<Bed>(true);
        }

        /// <summary>Walks somewhere to rest, and rests once it is there.</summary>
        private static string Settle(Villager villager, VillagerState state, VillagerWalk walk,
            VillagerAnimation animation, Transform target, Bed bed, float hoursToRest,
            string resting, string walking, float energy, float deltaTime)
        {
            // The walk decides whether it got there, because it already knows how close a
            // villager can actually get to a solid thing - which varies with the terrain and has
            // been measured between four and seven metres. Measuring it again here with a number
            // of my own is how a villager comes to stand beside its bed insisting it is still on
            // its way, and it is the fourth time in this codebase that two notions of "arrived"
            // have disagreed.
            MoveResult moved = walk.MoveTowards(target.position, Approach.ToStructure,
                deltaTime: deltaTime);
            bool arrived = moved == MoveResult.Arrived ||
                           Utils.DistanceXZ(villager.transform.position, target.position) <= CloseEnough;

            if (!arrived)
            {
                // Still on its way, so it recovers at the slowest rate. Written only when the
                // rate actually changes - a villager rewriting its ZDO every tick is the one
                // shape a settlement with no population cap cannot afford.
                Restamp(state, energy, RatePerSecond(ModConfig.GroundHoursToRest.Value));
                animation.Sleeping(false);
                return walking;
            }

            walk.Stop();
            Restamp(state, energy, RatePerSecond(hoursToRest));

            if (bed != null) LieOn(villager, bed);
            animation.Sleeping(true);

            return resting;
        }

        /// <summary>
        ///     Puts a villager on its bed, facing the way the bed faces.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Without this a villager sleeps standing beside the furniture, which reads as a
        ///         bug even though the energy underneath it is working perfectly.
        ///     </para>
        ///     <para>
        ///         Moved once, on lying down, rather than every tick - and the ZDO is told, because
        ///         Valheim decides what exists by ZDO sector and a body moved without its record
        ///         is a body the world loses track of. That lesson cost a villager three hundred
        ///         metres into a journey.
        ///     </para>
        /// </remarks>
        private static void LieOn(Villager villager, Bed bed)
        {
            Vector3 lying = bed.transform.position + Vector3.up * LyingHeight;
            if (Utils.DistanceXZ(villager.transform.position, lying) < .2f) return;

            Character body = villager.GetComponent<Character>();
            if (body != null && body.m_body != null) body.m_body.position = lying;

            villager.transform.position = lying;
            villager.transform.rotation = bed.transform.rotation;

            if (villager.TryGetComponent(out ZNetView view) && view.IsValid() && view.IsOwner())
            {
                view.GetZDO().SetPosition(lying);
                view.GetZDO().SetRotation(bed.transform.rotation);
            }
        }

        /// <summary>Banks the current energy and changes the rate, but only when the rate changed.</summary>
        private static void Restamp(VillagerState state, float energy, float rate)
        {
            if (Mathf.Approximately(state.RestRate, rate)) return;

            state.SetEnergy(energy);
            state.SetRestRate(rate);
        }

        private static void Wake(Villager villager, VillagerState state, VillagerAnimation animation,
            float energy)
        {
            state.SetEnergy(energy);
            state.SetResting(false);
            state.SetRestRate(0f);
            animation.Sleeping(false);

            // Stood up off the bed, so it does not walk away lying down.
            villager.transform.rotation = Quaternion.Euler(0f, villager.transform.eulerAngles.y, 0f);
        }

        /// <summary>
        ///     Energy per second that fills an exhausted villager in this many in-game hours.
        /// </summary>
        /// <remarks>
        ///     A game hour is the world's day divided by twenty-four, read from
        ///     <c>EnvMan.m_dayLengthSec</c> so a server that runs long days gets long nights of
        ///     sleep to match. Falls back to Valheim's own default rather than to zero, because a
        ///     rate of zero is a villager that never wakes up.
        /// </remarks>
        private static float RatePerSecond(float gameHours)
        {
            float dayLength = EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0
                ? EnvMan.instance.m_dayLengthSec
                : 1800f;

            float seconds = Mathf.Max(1f, gameHours) * (dayLength / 24f);
            return Energy.Full / seconds;
        }
    }
}
