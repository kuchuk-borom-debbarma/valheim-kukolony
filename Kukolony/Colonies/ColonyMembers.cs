using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Encodes a list of ZDOIDs into a single ZDO string, and back.
    ///
    ///     Same mechanism Container uses for an inventory: write into a ZPackage, take its
    ///     base64, store that under one key. Reusing a proven shape rather than inventing
    ///     a format, and it keeps a colony's whole membership in a handful of ZDO entries
    ///     rather than one per member.
    /// </summary>
    internal static class ColonyMembers
    {
        /// <summary>Guards against a malformed string claiming an absurd count.</summary>
        private const int MaxMembers = 4096;

        internal static string Encode(IEnumerable<ZDOID> ids)
        {
            List<ZDOID> list = new List<ZDOID>(ids);

            ZPackage package = new ZPackage();
            package.Write(2);
            package.Write(list.Count);
            foreach (ZDOID id in list)
            {
                package.Write(id);
                package.Write(PersistentZdoReference.Ensure(ZDOMan.instance?.GetZDO(id)));
            }

            return package.GetBase64();
        }

        internal static List<ZDOID> Decode(string encoded)
        {
            List<ZDOID> result = new List<ZDOID>();
            if (string.IsNullOrEmpty(encoded))
            {
                return result;
            }

            try
            {
                ZPackage package = new ZPackage(encoded);
                if (package.ReadInt() != 2) return result;
                int count = package.ReadInt();
                if (count < 0 || count > MaxMembers)
                {
                    Core.Log.Warning($"[colony] member list claims {count} entries - ignoring it.");
                    return result;
                }

                for (int i = 0; i < count; i++)
                {
                    ZDOID saved = package.ReadZDOID();
                    string persistentId = package.ReadString();
                    ZDOID resolved = PersistentZdoReference.Resolve(persistentId, saved);
                    if (!resolved.IsNone()) result.Add(resolved);
                }
            }
            catch (System.Exception e)
            {
                // A corrupt list must not take the colony down with it - better an empty
                // membership the player can rebuild than a colony that throws every tick.
                Core.Log.Error($"[colony] could not read a member list: {e.Message}");
                result.Clear();
            }

            return result;
        }
    }
}
