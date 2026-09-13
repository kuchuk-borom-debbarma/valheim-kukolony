using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Every settlement and every villager, on the map, wherever they are.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The game already does this for other players: a pin per person whose position is
    ///         rewritten as they move, and <c>m_pinUpdateRequired</c> set so the map redraws.
    ///         Villager pins are the same shape, which is why this needs no patching - only the
    ///         public pin API and a component that keeps them current.
    ///     </para>
    ///     <para>
    ///         <b>Unloaded villagers get a pin too</b>, read from their ZDO. That is precisely
    ///         when a player most wants to know where somebody is, and it costs nothing: the
    ///         colony already knows who its members are, and a ZDO answers its position without
    ///         instantiating anything.
    ///     </para>
    ///     <para>
    ///         <b>Never saved.</b> A saved pin is written into the player's own map profile and
    ///         would accumulate one per villager per session, in their save file, for ever.
    ///     </para>
    /// </remarks>
    internal sealed class SettlementPins : MonoBehaviour
    {
        /// <summary>How often to move the pins. Faster than this is redrawing for nobody.</summary>
        /// <remarks>
        ///     Once a second rather than twice, because a full pass decodes each colony's jobs,
        ///     structures and membership. That is bounded by how many settlements exist rather
        ///     than by how many villagers they hold, which is the shape that matters - but it is
        ///     not free, and a map is not worth paying for at frame rate.
        /// </remarks>
        private const float RefreshSeconds = 1f;

        /// <summary>How near a click must be, in world metres, to count as picking a villager.</summary>
        private const float ClickReach = 24f;

        private readonly Dictionary<ZDOID, Minimap.PinData> _pins = new Dictionary<ZDOID, Minimap.PinData>();

        /// <summary>Which of the pins are settlements, so a click knows what it opened.</summary>
        private readonly HashSet<ZDOID> _settlements = new HashSet<ZDOID>();
        private readonly List<ZDOID> _seen = new List<ZDOID>();
        private readonly List<ZDOID> _gone = new List<ZDOID>();

        private float _nextRefresh;
        private bool _clickHeld;

        internal static void Register(GameObject host) => host.AddComponent<SettlementPins>();

        private void Update()
        {
            if (Minimap.instance == null || Player.m_localPlayer == null)
            {
                // Between worlds. Drop everything rather than holding pins that point at a map
                // that no longer exists.
                if (_pins.Count > 0) _pins.Clear();
                return;
            }

            // A joined client's registry is not fed by the keep-alive driver; without this
            // the map showed no settlements at all until the flag screen happened to run a
            // sweep. Throttled inside the registry, so this costs nothing where the driver
            // already keeps the list fresh.
            ColonyRegistry.EnsureFresh(this);

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + RefreshSeconds;
                Refresh();
            }

            WatchForClicks();
        }

        private void OnDestroy() => Remove();

        private void Refresh()
        {
            _seen.Clear();

            // Everything is read from records rather than from loaded instances, so a settlement
            // across the map - with its outposts and its people - is on the map. That is the case
            // a map is for; one that only shows what you are standing next to is a compass.
            foreach (ZDO known in ColonyRegistry.ValidColonies())
            {
                ColonyState state = new ColonyState(known);
                string settlement = string.IsNullOrEmpty(state.Name)
                    ? Colonies.Colony.UnnamedLabel
                    : state.Name;

                // The tint hashes the ORIGINAL fallback string for a nameless Kolony, not the
                // displayed one. The hue's whole contract is stability - "a colour that
                // changed when you reloaded would be worse than none" - and renaming the
                // label already recoloured every unnamed settlement's pin family once. The
                // string is an input to a hash here, not something anybody reads.
                Color tint = Tint(string.IsNullOrEmpty(state.Name) ? "A settlement" : state.Name);

                ZDOID id = known.m_uid;
                _seen.Add(id);
                _settlements.Add(id);
                Place(id, known.GetPosition(), Settlement(state, settlement),
                    Minimap.PinType.Icon0, tint);

                PlaceWorkAreas(state, settlement, tint);
                PlaceVillagers(state, settlement, tint);
            }

            Forget();
        }

        /// <summary>
        ///     Pins the structures jobs are pointed at, named.
        /// </summary>
        /// <remarks>
        ///     A work area is a registered structure some job works around, so the ones worth
        ///     showing are exactly those a job names - not every chest in the settlement. The
        ///     label says which job sent people there, because "Outpost" on its own does not
        ///     explain why anybody is standing in it.
        /// </remarks>
        private void PlaceWorkAreas(ColonyState state, string settlement, Color tint)
        {
            List<JobDefinition> jobs = state.GetJobs();
            if (jobs.Count == 0) return;

            foreach (StructureRecord record in state.GetStructures())
            {
                string worker = null;
                foreach (JobDefinition job in jobs)
                {
                    if (job.Areas == null || !job.Areas.Contains(record.PersistentId)) continue;

                    // Named after the first job that works here. A second job on the same spot is
                    // a detail the structure's own screen can tell them.
                    worker = job.Name;
                    break;
                }

                if (worker == null) continue;

                ZDO zdo = ZDOMan.instance?.GetZDO(record.Id);
                if (zdo == null || !zdo.IsValid()) continue;

                _seen.Add(record.Id);
                Place(record.Id, zdo.GetPosition(), $"{record.Name} - {worker} ({settlement})",
                    Minimap.PinType.Icon3, tint);
            }
        }

        private void PlaceVillagers(ColonyState state, string settlement, Color tint)
        {
            foreach (ZDOID member in state.GetMembers(ColonyMemberKind.Villager))
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(member);
                if (zdo == null || !zdo.IsValid()) continue;

                _seen.Add(member);
                Place(member, zdo.GetPosition(), $"{Describe(member)} ({settlement})",
                    Minimap.PinType.Player, tint);
            }
        }

        /// <summary>
        ///     A stable colour for a settlement, from its name.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Icon says what a thing is; colour says whose it is.</b> Every pin a
        ///         settlement owns - itself, its work areas and its people - shares one colour, so
        ///         a player can see which village an outpost belongs to without reading a label.
        ///         Two signals that do not interfere.
        ///     </para>
        ///     <para>
        ///         Hashed from the name, so it is the same every session and on every machine,
        ///         with no state to keep and nothing to migrate. <c>GetStableHashCode</c> rather
        ///         than <c>string.GetHashCode</c>, which is not guaranteed to agree between
        ///         processes - a colour that changed when you reloaded would be worse than none.
        ///     </para>
        ///     <para>
        ///         Saturation and brightness are fixed rather than hashed, because the point is
        ///         telling settlements apart on a dark map: left free, a hash would eventually
        ///         choose something unreadable.
        ///     </para>
        /// </remarks>
        private static Color Tint(string settlement)
        {
            if (string.IsNullOrEmpty(settlement)) return Color.white;

            int hash = settlement.GetStableHashCode();
            float hue = Mathf.Abs(hash % 360) / 360f;

            return Color.HSVToRGB(hue, .55f, 1f);
        }

        private void Place(ZDOID id, Vector3 where, string label, Minimap.PinType kind, Color tint)
        {
            if (!_pins.TryGetValue(id, out Minimap.PinData pin))
            {
                pin = Minimap.instance.AddPin(where, kind, label, save: false, isChecked: false);
                _pins[id] = pin;
                Minimap.instance.m_pinUpdateRequired = true;
                Paint(pin, tint);
                return;
            }

            Paint(pin, tint);

            // Only say the map changed when it did. The redraw walks every pin, so claiming a
            // change every half second for a settlement standing still is work for nothing.
            if (pin.m_pos != where || pin.m_name != label)
            {
                pin.m_pos = where;
                pin.m_name = label;
                Minimap.instance.m_pinUpdateRequired = true;
            }
        }

        /// <summary>
        ///     What the pin says: who, and what they are up to.
        /// </summary>
        /// <remarks>
        ///     Both, because a name alone means opening the screen to learn anything, and what a
        ///     villager is doing is most of what anyone wanted to know. An unloaded villager has
        ///     no activity to report, so it says where it lives instead of inventing one.
        /// </remarks>
        /// <summary>
        ///     What a settlement's pin says: its name, and how many people live there.
        /// </summary>
        /// <remarks>
        ///     The population, because that is what distinguishes the settlement you are looking
        ///     for from the outpost you forgot you founded.
        /// </remarks>
        private static string Settlement(ColonyState state, string name)
        {
            int people = state.CountMembers(ColonyMemberKind.Villager);
            return people == 1 ? $"{name} (1 villager)" : $"{name} ({people} villagers)";
        }

        /// <summary>
        ///     Colours a pin's icon, once it has one.
        /// </summary>
        /// <remarks>
        ///     The image is built by the map when it first draws a pin and rebuilt when it
        ///     redraws, so the colour is reapplied every pass rather than set once - and compared
        ///     first, because assigning to a UI Image marks it dirty whether or not it changed.
        /// </remarks>
        private static void Paint(Minimap.PinData pin, Color tint)
        {
            if (pin?.m_iconElement == null || pin.m_iconElement.color == tint) return;

            pin.m_iconElement.color = tint;
        }

        private static string Describe(ZDOID id)
        {
            // The same answer the villagers list gives, from the same place, so the map and the
            // screen never disagree about what somebody is doing.
            return $"{VillagerRoster.Name(id)} - {VillagerListScreen.Doing(id)}";
        }

        /// <summary>Drops pins for villagers that are no longer anybody's member.</summary>
        private void Forget()
        {
            _gone.Clear();
            foreach (KeyValuePair<ZDOID, Minimap.PinData> pin in _pins)
            {
                if (!_seen.Contains(pin.Key)) _gone.Add(pin.Key);
            }

            foreach (ZDOID id in _gone)
            {
                Minimap.instance.RemovePin(_pins[id]);
                _pins.Remove(id);
                _settlements.Remove(id);
                Minimap.instance.m_pinUpdateRequired = true;
            }
        }

        private void Remove()
        {
            if (Minimap.instance == null)
            {
                _pins.Clear();
                return;
            }

            foreach (Minimap.PinData pin in _pins.Values) Minimap.instance.RemovePin(pin);
            _pins.Clear();
        }

        /// <summary>
        ///     Opens a villager when their pin is clicked on the large map.
        /// </summary>
        /// <remarks>
        ///     Watched here rather than patched into the map's own input, which handles clicks
        ///     inline and would need rewriting to hook. Reading the click ourselves is smaller,
        ///     and it cannot break the map for anything else.
        /// </remarks>
        private void WatchForClicks()
        {
            if (Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                _clickHeld = false;
                return;
            }

            bool down = Input.GetMouseButton(0);
            bool pressed = down && !_clickHeld;
            _clickHeld = down;
            if (!pressed || _pins.Count == 0) return;

            Vector3 clicked = Minimap.instance.ScreenToWorldPoint(Input.mousePosition);

            ZDOID nearest = ZDOID.None;
            float closest = ClickReach;

            foreach (KeyValuePair<ZDOID, Minimap.PinData> pin in _pins)
            {
                float distance = Utils.DistanceXZ(pin.Value.m_pos, clicked);
                if (distance >= closest) continue;

                closest = distance;
                nearest = pin.Key;
            }

            if (nearest.IsNone()) return;

            if (_settlements.Contains(nearest)) OpenSettlement(nearest);
            else Open(nearest);
        }

        /// <summary>Opens a settlement from its pin.</summary>
        private static void OpenSettlement(ZDOID id)
        {
            Colony colony = null;
            foreach (Colony candidate in Colony.Instances)
            {
                if (candidate == null || !candidate.TryGetComponent(out ZNetView view) ||
                    !view.IsValid() || view.GetZDO().m_uid != id)
                {
                    continue;
                }

                colony = candidate;
                break;
            }

            if (colony == null)
            {
                // Pinned from its record, so it can be shown on the map without being loaded -
                // but its screen reads live state, so there is nothing honest to open yet.
                Report.Say("That Kolony is too far away to manage from here.");
                return;
            }

            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            ColonyScreen.Instance?.Open(colony, null);
        }

        private static void Open(ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(villager);
            if (zdo == null) return;

            Colony colony = Colony.FindFor(zdo);
            if (colony == null)
            {
                Report.Say("That villager's Kolony is gone.");
                return;
            }

            // Closing the map first, or the screen opens behind it and reads as nothing having
            // happened.
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);

            ColonyScreen screen = ColonyScreen.Instance;
            if (screen == null) return;

            screen.Open(colony, null);
            screen.Push(new VillagerDetailScreen(villager));
        }
    }
}
