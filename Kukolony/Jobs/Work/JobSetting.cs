using System;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Settings that only some jobs read, so the ones that ignore a setting can stop
    ///     offering it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Most settings are universal - what to work with, how many times, how close to
    ///         stand, whether to reserve a target - and are always shown. These four are not:
    ///         hauling never reads a source because it looks on the ground, a station job never
    ///         reads a destination because it hands its load to the station, and only work that
    ///         searches the ground reads a radius.
    ///     </para>
    ///     <para>
    ///         Declared by the job rather than inferred by the panel. A setting that is shown
    ///         but ignored is worse than one that is missing: the player changes it, nothing
    ///         happens, and nothing says why.
    ///     </para>
    /// </remarks>
    [Flags]
    internal enum JobSetting
    {
        None = 0,
        /// <summary>A specific container to take from.</summary>
        Source = 1,
        /// <summary>A specific container to put the result in.</summary>
        Destination = 2,
        /// <summary>How far from the hearth to look for something on the ground.</summary>
        SearchRadius = 4,
        /// <summary>Whether the result goes on the ground instead of into a container.</summary>
        DropOnGround = 8
    }
}
