using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Core
{
    /// <summary>
    ///     Says something once, then says how often it has been true since.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The shared utility the whole settlement talks through, because every job after the
    ///         first one wants it: a villager decides what to do twenty times a second, and
    ///         anything it says about a situation that has not changed is otherwise said twenty
    ///         times a second.
    ///     </para>
    ///     <para>
    ///         The first occurrence goes out immediately - the moment something starts going
    ///         wrong is when a player most wants to know - and repeats are collected into a count
    ///         and a span. <see cref="Forget" /> is how a caller says the situation has changed,
    ///         so the next occurrence is treated as new rather than as more of the same.
    ///     </para>
    ///     <para>
    ///         Keyed by whatever the caller chooses. A key that includes the villager keeps two
    ///         villagers' complaints apart; a key that does not deliberately merges them, which
    ///         is what you want for "the settlement has nowhere to put Wood".
    ///     </para>
    /// </remarks>
    internal static class Chatter
    {
        private sealed class Heard
        {
            internal int Since;
            internal float FirstAt;
            internal float LastSaidAt;
        }

        /// <summary>Entries untouched for this long are dropped, so the table cannot grow forever.</summary>
        private const float ForgottenAfterSeconds = 600f;

        private static readonly Dictionary<string, Heard> Recent = new Dictionary<string, Heard>();
        private static float _nextPrune;

        /// <summary>Tells the player, at most once every half minute per key.</summary>
        internal static void Say(string key, string message) => Emit(key, message, Report.Say);

        /// <summary>Warns the log, at most once every half minute per key.</summary>
        internal static void Warn(string key, string message) => Emit(key, message, Log.Warning);

        /// <summary>
        ///     Says the situation has changed, so the next occurrence is news again.
        /// </summary>
        /// <remarks>
        ///     Without this, a problem that is fixed and then recurs half a minute later is
        ///     reported as a continuation of the old one - "47 times in the last 2 minutes" for
        ///     something that has happened twice, with a gap in the middle nobody can see.
        /// </remarks>
        internal static void Forget(string key)
        {
            if (key != null) Recent.Remove(key);
        }

        /// <summary>Drops everything, so one world's complaints do not leak into the next.</summary>
        internal static void Clear() => Recent.Clear();

        /// <summary>
        ///     Whether anything has been said under this key.
        /// </summary>
        /// <remarks>
        ///     For checks asserting that a situation was reported rather than swallowed. A
        ///     branch whose whole purpose is to be loud needs something able to hear it, or
        ///     the check can only prove the quiet half and would pass just as well if the
        ///     branch had gone silent.
        /// </remarks>
        internal static bool Said(string key) => key != null && Recent.ContainsKey(key);

        private static void Emit(string key, string message, System.Action<string> speak)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message)) return;

            float now = Time.realtimeSinceStartup;
            Prune(now);

            if (!Recent.TryGetValue(key, out Heard heard))
            {
                Recent[key] = new Heard { Since = 0, FirstAt = now, LastSaidAt = now };
                speak(message);
                return;
            }

            heard.Since++;

            if (!Repeats.DueAgain(now - heard.LastSaidAt)) return;

            // The one just counted is included, so the number is how many times it happened
            // rather than how many times it happened after the last time it was mentioned.
            speak(Repeats.Summarise(message, heard.Since + 1, now - heard.FirstAt));

            heard.Since = 0;
            heard.FirstAt = now;
            heard.LastSaidAt = now;
        }

        private static void Prune(float now)
        {
            if (now < _nextPrune) return;
            _nextPrune = now + ForgottenAfterSeconds;

            List<string> stale = null;
            foreach (KeyValuePair<string, Heard> entry in Recent)
            {
                if (now - entry.Value.LastSaidAt < ForgottenAfterSeconds) continue;

                stale = stale ?? new List<string>();
                stale.Add(entry.Key);
            }

            if (stale == null) return;
            foreach (string key in stale) Recent.Remove(key);
        }
    }
}
