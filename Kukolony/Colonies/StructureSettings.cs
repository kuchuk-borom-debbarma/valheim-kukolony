using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     What a registered structure is for. One object carrying every component's settings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Settings belong to the <em>record</em>, not to the object. A chest that goes
    ///         dormant keeps what it was told to hold, because the alternative is a settlement
    ///         that forgets its own configuration whenever nobody stands near it.
    ///     </para>
    ///     <para>
    ///         Every component's fields live here together rather than in a per-capability blob.
    ///         A structure can carry more than one capability - a thing that is both storage and
    ///         processing is ordinary - and the fields a capability does not use simply stay at
    ///         their defaults, which cost a few bytes and remove a whole class of "which decoder
    ///         do I use" mistakes.
    ///     </para>
    /// </remarks>
    internal sealed class StructureSettings
    {
        /// <summary>
        ///     Which items belong here. Empty means <em>anything</em>, which is what an
        ///     overflow chest is - so empty is a real answer and not an unset value.
        /// </summary>
        internal List<string> Accepts = new List<string>();

        /// <summary>
        ///     Whether the settlement may take from this, as opposed to only filling it. Some
        ///     chests are for keeping.
        /// </summary>
        internal bool MayTakeFrom = true;

        /// <summary>What to keep this fed with. Chosen from what the structure actually takes.</summary>
        internal List<string> Fuel = new List<string>();

        internal List<string> Input = new List<string>();

        /// <summary>
        ///     How full to keep it, as a fraction of its own capacity.
        /// </summary>
        /// <remarks>
        ///     A fraction rather than a count, because the cap belongs to the structure and a
        ///     count would be wrong the moment it was applied to a different kind of station.
        ///     Defaults to half, so a settlement does not burn every log it owns keeping one
        ///     kiln permanently brimming.
        /// </remarks>
        internal float KeepFull = 0.5f;

        /// <summary>Who sleeps here. None until assigned.</summary>
        internal ZDOID Sleeper = ZDOID.None;

        /// <summary>
        ///     The sleeper's durable token.
        /// </summary>
        /// <remarks>
        ///     Stored beside the address for the same reason structures are: loading rewrites
        ///     every ZDOID, so an address alone points at whatever later occupies that slot.
        /// </remarks>
        internal string SleeperToken = string.Empty;

        internal bool HasSleeper => !Sleeper.IsNone();

        internal void Write(ZPackage package)
        {
            WriteList(package, Accepts);
            package.Write(MayTakeFrom);
            WriteList(package, Fuel);
            WriteList(package, Input);
            package.Write(KeepFull);
            package.Write(Sleeper);
            package.Write(SleeperToken ?? string.Empty);
        }

        internal static StructureSettings Read(ZPackage package)
        {
            StructureSettings settings = new StructureSettings();
            settings.Accepts = ReadList(package);
            settings.MayTakeFrom = package.ReadBool();
            settings.Fuel = ReadList(package);
            settings.Input = ReadList(package);
            settings.KeepFull = UnityEngine.Mathf.Clamp01(package.ReadSingle());

            ZDOID saved = package.ReadZDOID();
            settings.SleeperToken = package.ReadString();
            settings.Sleeper = PersistentZdoReference.Resolve(settings.SleeperToken, saved);
            return settings;
        }

        /// <summary>Guards against a malformed record claiming an absurd list length.</summary>
        private const int MaxEntries = 256;

        private static void WriteList(ZPackage package, List<string> values)
        {
            List<string> list = values ?? new List<string>();
            package.Write(list.Count);
            foreach (string value in list) package.Write(value ?? string.Empty);
        }

        private static List<string> ReadList(ZPackage package)
        {
            List<string> values = new List<string>();
            int count = package.ReadInt();
            if (count < 0 || count > MaxEntries)
            {
                Log.Warning($"[colony] structure settings claim {count} entries - ignoring them.");
                return values;
            }

            for (int i = 0; i < count; i++) values.Add(package.ReadString());
            return values;
        }
    }
}
