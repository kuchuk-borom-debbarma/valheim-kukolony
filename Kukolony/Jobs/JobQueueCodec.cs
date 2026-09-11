using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Packs a villager's job queue into one ZDO string, and back.
    /// </summary>
    /// <remarks>
    ///     The same shape the colony uses for members and structures: write into a ZPackage,
    ///     store its base64 under one key. Reusing a proven format rather than inventing one,
    ///     and it keeps a whole queue in a single ZDO entry rather than one per position.
    /// </remarks>
    internal static class JobQueueCodec
    {
        /// <summary>Guards against a malformed string claiming an absurd length.</summary>
        private const int MaxEntries = 256;

        internal static string Encode(IEnumerable<string> jobs)
        {
            List<string> list = new List<string>(jobs ?? new List<string>());
            ZPackage package = new ZPackage();
            package.Write(1);
            package.Write(list.Count);
            foreach (string job in list) package.Write(job ?? string.Empty);
            return package.GetBase64();
        }

        internal static List<string> Decode(string encoded)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(encoded)) return result;

            try
            {
                ZPackage package = new ZPackage(encoded);
                if (package.ReadInt() != 1) return result;

                int count = package.ReadInt();
                if (count < 0 || count > MaxEntries)
                {
                    Log.Warning($"[job] a queue claims {count} entries - ignoring it.");
                    return result;
                }

                for (int i = 0; i < count; i++) result.Add(package.ReadString());
            }
            catch (System.Exception e)
            {
                // A corrupt queue must not take the villager down with it: an empty queue is
                // idle, which a player can see and fix, and a thrown exception every tick is
                // neither.
                Log.Error($"[job] could not read a queue: {e.Message}");
                result.Clear();
            }

            return result;
        }
    }
}
