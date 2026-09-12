using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     A named queue: the jobs one kind of villager does, in order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The answer to a settlement of a hundred being a hundred configurations. A job
    ///         already answers "what work is this"; a preset answers "who does what", which is
    ///         the part that otherwise has to be repeated per villager by hand.
    ///     </para>
    ///     <para>
    ///         It holds job <em>identities</em> rather than copies of their settings, so editing
    ///         a job changes it for everyone already doing it. Copying the settings in would make
    ///         a preset a snapshot, and a settlement would drift into a hundred configurations
    ///         again by a slower route.
    ///     </para>
    ///     <para>
    ///         Applying one writes the queue onto each villager. The queue lives on the villager
    ///         because that is what changes per person; the preset lives on the colony because it
    ///         is shared. Nothing links them afterwards - a villager assigned from a preset is
    ///         just a villager with that queue, and editing the preset later does not reach back.
    ///         That is deliberate: the alternative is a villager whose orders change because
    ///         somebody edited a template they no longer remember applying.
    ///     </para>
    /// </remarks>
    internal sealed class JobPreset
    {
        /// <summary>Stable identity, so renaming does not orphan anything.</summary>
        internal string Id = string.Empty;

        internal string Name = string.Empty;

        /// <summary>Job ids, in the order a villager should work them.</summary>
        internal List<string> Jobs = new List<string>();

        internal void Write(ZPackage package)
        {
            package.Write(Id ?? string.Empty);
            package.Write(Name ?? string.Empty);
            package.Write(Jobs.Count);
            foreach (string job in Jobs) package.Write(job ?? string.Empty);
        }

        internal static JobPreset Read(ZPackage package)
        {
            JobPreset preset = new JobPreset
            {
                Id = package.ReadString(),
                Name = package.ReadString()
            };

            int count = package.ReadInt();
            if (count < 0 || count > MaxJobs)
            {
                // Thrown rather than shrugged off: every preset after this one is read from the
                // same stream, so a count this wrong means the position is already lost and
                // continuing would decode the next record from the middle of this one.
                throw new System.IO.InvalidDataException($"preset '{preset.Name}' claims {count} jobs");
            }

            for (int i = 0; i < count; i++) preset.Jobs.Add(package.ReadString());
            return preset;
        }

        /// <summary>Guards against a malformed record claiming an absurd queue.</summary>
        private const int MaxJobs = 64;
    }
}
