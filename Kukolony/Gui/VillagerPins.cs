using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Every villager, on the map, wherever they are.
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
    internal sealed class VillagerPins : MonoBehaviour
    {
        /// <summary>How often to move the pins. Faster than this is redrawing for nobody.</summary>
        private const float RefreshSeconds = .5f;

        /// <summary>How near a click must be, in world metres, to count as picking a villager.</summary>
        private const float ClickReach = 24f;

        private readonly Dictionary<ZDOID, Minimap.PinData> _pins = new Dictionary<ZDOID, Minimap.PinData>();
        private readonly List<ZDOID> _seen = new List<ZDOID>();
        private readonly List<ZDOID> _gone = new List<ZDOID>();

        private float _nextRefresh;
        private bool _clickHeld;

        internal static void Register(GameObject host) => host.AddComponent<VillagerPins>();

        private void Update()
        {
            if (Minimap.instance == null || Player.m_localPlayer == null)
            {
                // Between worlds. Drop everything rather than holding pins that point at a map
                // that no longer exists.
                if (_pins.Count > 0) _pins.Clear();
                return;
            }

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

            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null) continue;

                // The colony's own member list, so a villager nobody has loaded is still on the
                // map. Decoded once per colony rather than once per villager: unpacking it per
                // row is the shape a settlement with no population cap cannot afford.
                foreach (ZDOID member in colony.State.GetMembers(ColonyMemberKind.Villager))
                {
                    ZDO zdo = ZDOMan.instance?.GetZDO(member);
                    if (zdo == null || !zdo.IsValid()) continue;

                    _seen.Add(member);
                    Place(member, zdo);
                }
            }

            Forget();
        }

        private void Place(ZDOID id, ZDO zdo)
        {
            Vector3 where = zdo.GetPosition();
            string label = Describe(id, zdo);

            if (!_pins.TryGetValue(id, out Minimap.PinData pin))
            {
                pin = Minimap.instance.AddPin(where, Minimap.PinType.Player, label,
                    save: false, isChecked: false);
                _pins[id] = pin;
                Minimap.instance.m_pinUpdateRequired = true;
                return;
            }

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
        private static string Describe(ZDOID id, ZDO zdo)
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

            Open(nearest);
        }

        private static void Open(ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(villager);
            if (zdo == null) return;

            Colony colony = Colony.FindFor(zdo);
            if (colony == null)
            {
                Report.Say("That villager's settlement is gone.");
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
