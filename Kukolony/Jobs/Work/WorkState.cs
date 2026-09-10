namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Where a villager has got to in the job it is running.
    /// </summary>
    /// <remarks>
    ///     A job knows what it is doing rather than inferring it. The engine this replaced
    ///     re-derived intent every tick from whether the bag held something, which worked but
    ///     meant no step could ever be named. Naming them is the point: a villager that is
    ///     fetching is in Fetching, and a reload resumes there.
    ///
    ///     Persisted as an int on the villager, so append rather than reorder.
    /// </remarks>
    internal enum WorkState
    {
        /// <summary>Deciding what to work on.</summary>
        Choosing = 0,
        /// <summary>Walking to what was chosen.</summary>
        Travelling = 1,
        /// <summary>Taking or gathering at that place.</summary>
        Collecting = 2,
        /// <summary>Deciding where the result goes.</summary>
        ChoosingTarget = 3,
        /// <summary>Walking to that destination.</summary>
        Delivering = 4,
        /// <summary>The cycle's work is done.</summary>
        Finished = 5,

        // The states below belong to work that produces something in the world rather than
        // in a container, which is a shape only tapping a beehive has so far.

        /// <summary>Standing by for the work just started to produce something.</summary>
        Waiting = 6,
        /// <summary>Deciding which of the things that appeared to collect.</summary>
        Gathering = 7,
        /// <summary>Walking to what appeared.</summary>
        Retrieving = 8
    }

    /// <summary>What the engine should do for a villager this tick.</summary>
    internal enum WorkAction
    {
        ChooseSource,
        ChooseTarget,
        Move,
        Collect,
        Deliver,
        /// <summary>
        ///     Stand by for work already started to bear fruit. The engine decides when the
        ///     wait is over, because whether anything has appeared is a question about the
        ///     world rather than about the job.
        /// </summary>
        Wait,
        /// <summary>Nothing useful to do now. Yields without consuming an attempt.</summary>
        Yield,
        /// <summary>One full cycle of the job is done.</summary>
        Complete
    }

    /// <summary>
    ///     What a villager can observe, reduced to primitives so the decision that follows can
    ///     be tested without a world.
    /// </summary>
    internal readonly struct WorkFacts
    {
        internal WorkFacts(bool hasTarget, bool arrived, bool carrying, bool stockLimitReached,
            bool hasTool, bool deliversInPlace = false)
        {
            DeliversInPlace = deliversInPlace;
            HasTarget = hasTarget;
            Arrived = arrived;
            Carrying = carrying;
            StockLimitReached = stockLimitReached;
            HasTool = hasTool;
        }

        /// <summary>Something has been chosen and still exists.</summary>
        internal bool HasTarget { get; }

        /// <summary>The villager is standing close enough to act.</summary>
        internal bool Arrived { get; }

        /// <summary>The bag holds something this job wants.</summary>
        internal bool Carrying { get; }

        /// <summary>The destination already holds as much as the job asks for.</summary>
        internal bool StockLimitReached { get; }

        /// <summary>The villager carries the tool this job needs, or the job needs none.</summary>
        internal bool HasTool { get; }

        /// <summary>
        ///     The load goes down where the villager stands rather than into a container, so
        ///     there is nothing to choose and nowhere to walk.
        /// </summary>
        internal bool DeliversInPlace { get; }
    }

    /// <summary>An action to take, where it was decided, and the state to record once it succeeds.</summary>
    internal readonly struct WorkStep
    {
        internal WorkStep(WorkAction action, WorkState from, WorkState next)
        {
            Action = action;
            From = from;
            Next = next;
        }

        internal WorkAction Action { get; }

        /// <summary>
        ///     The state this action was decided from, which is not always the state the
        ///     villager had recorded: transitions skip through states whose outcome already
        ///     holds. A job that does the same kind of thing twice in a cycle reads this to
        ///     tell the two apart, so it must be where the decision landed rather than where
        ///     the villager set out from.
        /// </summary>
        internal WorkState From { get; }

        /// <summary>
        ///     Recorded only when the action succeeds. A walk or an ownership handshake takes
        ///     several ticks, and the villager must arrive back at the same state each time
        ///     until it finishes.
        /// </summary>
        internal WorkState Next { get; }
    }
}
