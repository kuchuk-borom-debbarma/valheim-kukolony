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

        /// <summary>
        ///     At most this many of an item here. Absent means no limit.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An absolute count rather than a share of the settlement's total, which is
        ///         what the first sketch of this called for. A cap is something a villager
        ///         checks in one look; a share is a relationship, and satisfying it for wood can
        ///         un-satisfy it for stone - so a settlement chasing shares never converges.
        ///         Shares can come later, once this is proven stable.
        ///     </para>
        ///     <para>
        ///         Kept as a list of pairs rather than a dictionary in the record so the encoded
        ///         order is stable: a blob that reorders itself between writes looks like a
        ///         change to everything downstream that watches the revision.
        ///     </para>
        /// </remarks>
        internal readonly List<KeyValuePair<string, int>> Caps = new List<KeyValuePair<string, int>>();

        /// <summary>
        ///     Whether items nothing else claims may be dumped here.
        /// </summary>
        /// <remarks>
        ///     A fact about the chest, so it belongs on the chest rather than being repeated on
        ///     every job that might need somewhere to put an oddment. Without one, an unclaimed
        ///     item is left where it lies and the villager says so - it does not invent a home.
        /// </remarks>
        internal bool TakeUnclaimed;

        /// <summary>The cap for an item, or -1 when it has none.</summary>
        internal int CapFor(string itemPrefab)
        {
            foreach (KeyValuePair<string, int> cap in Caps)
            {
                if (cap.Key == itemPrefab) return cap.Value;
            }

            return -1;
        }

        /// <summary>Sets or clears a cap. A negative amount removes it.</summary>
        internal void SetCap(string itemPrefab, int amount)
        {
            if (string.IsNullOrEmpty(itemPrefab)) return;

            for (int i = 0; i < Caps.Count; i++)
            {
                if (Caps[i].Key != itemPrefab) continue;
                if (amount < 0) Caps.RemoveAt(i);
                else Caps[i] = new KeyValuePair<string, int>(itemPrefab, amount);
                return;
            }

            if (amount >= 0) Caps.Add(new KeyValuePair<string, int>(itemPrefab, amount));
        }

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
            package.Write(TakeUnclaimed);
            package.Write(Caps.Count);
            foreach (KeyValuePair<string, int> cap in Caps)
            {
                package.Write(cap.Key ?? string.Empty);
                package.Write(cap.Value);
            }

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
            settings.TakeUnclaimed = package.ReadBool();

            // Thrown rather than shrugged off. Every field after this one is read from the
            // same stream, so a count this wrong means the position is already lost - and
            // returning what has been read so far would decode the next record from the middle
            // of this one. The caller discards the whole registry, which is the honest outcome.
            int caps = package.ReadInt();
            if (caps < 0 || caps > MaxEntries)
            {
                throw new System.IO.InvalidDataException($"structure settings claim {caps} caps");
            }

            for (int i = 0; i < caps; i++)
            {
                string item = package.ReadString();
                settings.SetCap(item, package.ReadInt());
            }

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
