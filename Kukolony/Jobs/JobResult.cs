namespace Kukolony.Jobs
{
    /// <summary>
    ///     How one tick of work ended, and the only input the queue needs to schedule.
    /// </summary>
    /// <remarks>
    ///     The distinction between <see cref="Failed" /> and <see cref="Skipped" /> is the whole
    ///     of the retry policy, and it is worth being precise about because getting it wrong
    ///     produces two opposite bugs.
    ///
    ///     <see cref="Skipped" /> means *there is nothing useful to do right now* - no item
    ///     worth moving, nowhere to put one, too tired. It consumes nothing and yields to the
    ///     next entry, so an idle job cannot starve a villager of the work it could be doing.
    ///
    ///     <see cref="Failed" /> means *this could not be done*. It consumes a repetition, so
    ///     impossible work runs out rather than looping forever.
    ///
    ///     Collapse the two and you get either a villager that stands still because one job has
    ///     nothing to do, or one that retries something unachievable until the world ends.
    /// </remarks>
    internal enum JobResult
    {
        /// <summary>Progress was made. Keep this entry and consume nothing.</summary>
        Running,

        /// <summary>One repetition finished.</summary>
        Completed,

        /// <summary>It could not be done. Consumes a repetition.</summary>
        Failed,

        /// <summary>Nothing useful to do. Consumes nothing and yields.</summary>
        Skipped
    }
}
