using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Console commands for watching a villager travel, by hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The automated run cannot check the half of travelling that matters most to a
    ///         player: what it looks like when you are standing next to it. The benchmark's
    ///         player stands still at the hearth for the whole run, so every journey it can
    ///         observe is a short one across ground it has already loaded.
    ///     </para>
    ///     <para>
    ///         So the observed half is verified by a person, and these exist to make that a
    ///         thirty second job rather than a project: walk somewhere, type a word, watch.
    ///     </para>
    /// </remarks>
    internal static class TravelCommands
    {
        private static readonly List<Villager> Nearby = new List<Villager>();

        internal static void Register()
        {
            new Terminal.ConsoleCommand("kukolony_come",
                "sends the nearest villager to where you are standing, and reports what it does",
                args => Send(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_chest",
                "puts a chest on the ground in front of you and registers it to the nearest Kolony",
                args => Chest(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_unstick",
                "says whether this mod is holding your input, and lets go if it is",
                args => Unstick(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_buildmenu",
                "opens the hammer's build menu directly, bypassing the key entirely",
                args => BuildMenu(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_input",
                "reports what is blocking player input a few seconds from now, once the console is shut",
                args => InputLater(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_where",
                "says what every loaded villager is doing and how far away it is",
                args => Where(args.Context), isCheat: false, isNetwork: false, onlyServer: false);
        }

        /// <summary>
        ///     Places a chest on the ground and registers it, in one step.
        /// </summary>
        /// <remarks>
        ///     Spawning a piece with the game's own command drops it wherever the player is
        ///     standing, which is usually mid-air - and a piece with WearNTear and nothing under
        ///     it collapses, debugmode or not, because debugmode only relaxes placement rules for
        ///     the hammer. Snapping to the terrain first is the whole difference.
        ///
        ///     Registering it too, because an unregistered chest is invisible to the settlement
        ///     and "the villagers ignore it" is a confusing way to discover that.
        /// </remarks>
        private static void Chest(Terminal console)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) return;

            GameObject prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (prefab == null)
            {
                Tell(console, "no wooden chest prefab in this build");
                return;
            }

            Vector3 where = player.transform.position + player.transform.forward * 2f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(where, out float ground))
            {
                where.y = ground;
            }

            GameObject chest = Object.Instantiate(prefab, where, Quaternion.identity);
            if (chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Tell(console, "the chest could not be placed here");
                return;
            }

            Colony colony = NearestColony(player.transform.position);
            if (colony == null)
            {
                Tell(console, "chest placed, but there is no Kolony nearby to register it to");
                return;
            }

            RegisterOutcome outcome = ColonyOperations.Register(colony, chest);
            Tell(console, outcome == RegisterOutcome.Registered || outcome == RegisterOutcome.Moved
                ? $"chest placed and registered to {colony.State.Name} - set what it holds on its screen"
                : $"chest placed, but registering it said: {outcome}");
        }

        private static Colony NearestColony(Vector3 position)
        {
            Colony best = null;
            float closest = float.MaxValue;

            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null) continue;

                float distance = Utils.DistanceXZ(colony.transform.position, position);
                if (distance >= closest) continue;

                closest = distance;
                best = colony;
            }

            return best;
        }

        /// <summary>
        ///     Reports whether the mod is suppressing player input, and stops if it is.
        /// </summary>
        /// <remarks>
        ///     The colony screen releases the mouse while it is open and gives it back on close,
        ///     paired with OnDestroy as well. If that ever fails to pair, the symptom is a player
        ///     who can walk around while the hammer's build menu, and everything else that reads
        ///     a button, quietly does nothing - which is indistinguishable from a game bug.
        ///
        ///     So this says what is actually true rather than guessing, and releases the block
        ///     either way: being told "the mod is not holding anything" is the useful half of the
        ///     answer when it is not.
        /// </remarks>
        private static void Unstick(Terminal console)
        {
            Player player = Player.m_localPlayer;
            bool screenOpen = ColonyScreen.Instance != null && ColonyScreen.Instance.IsOpen;

            // Deliberately not reporting TakeInput here any more. Having the console open is
            // itself one of the things that stops a player taking input, and the console is
            // the only way to run this - so it read False every single time and looked like a
            // finding. kukolony_input takes that reading after the console shuts, which is the
            // only moment the answer means anything.
            Tell(console, $"Kolony screen open: {screenOpen}");
            Tell(console, $"build menu visible: {Hud.IsPieceSelectionVisible()}");
            Tell(console, $"hammer equipped: {(player != null && player.GetRightItem() != null ? player.GetRightItem().m_shared.m_name : "nothing")}");

            if (screenOpen) ColonyScreen.Instance.Close();
            Jotunn.Managers.GUIManager.BlockInput(false);

            // The build menu is a toggle, and the game decides which way to toggle it by
            // asking whether its window is already active - so a window left active while
            // nothing is drawn turns every press into a close. The symptom is a build menu
            // that never opens no matter which key is tried, on a player who can otherwise
            // move and select pieces normally, which is nothing like what a held input looks
            // like. Putting it back to closed costs nothing when it was already closed.
            bool pieceWindowOpen = Hud.IsPieceSelectionVisible();
            if (pieceWindowOpen) Hud.HidePieceSelection();

            Tell(console, pieceWindowOpen
                ? "the build menu was flagged open while showing nothing - closed it, press the key again"
                : "the build menu is flagged closed, so the next press should open it");

            Tell(console, "run kukolony_input to see what is actually blocking input, if anything");
        }

        /// <summary>
        ///     Opens the build menu without going through the key, which splits one question
        ///     into two answerable ones.
        /// </summary>
        /// <remarks>
        ///     If the menu appears, the window and every gate in front of it are fine and the
        ///     fault is that the key press never became a "BuildMenu" button — a binding, not a
        ///     state. If it does not appear, the window itself is the problem and the key was
        ///     never the question. Either answer rules out half of everything, which is worth
        ///     more than another reading of a flag.
        /// </remarks>
        private static void BuildMenu(Terminal console)
        {
            if (Hud.instance == null)
            {
                Tell(console, "no hud yet");
                return;
            }

            bool before = Hud.IsPieceSelectionVisible();
            Hud.instance.TogglePieceSelection();
            bool after = Hud.IsPieceSelectionVisible();

            Tell(console, $"build menu {(before ? "was open" : "was closed")}, now {(after ? "open" : "closed")}");
            Tell(console, after
                ? "if you can SEE it, the window works and the key is the problem"
                : "the window refused to open, so the key was never the question");
        }

        /// <summary>
        ///     Reports the input gates a few seconds from now, rather than right now.
        /// </summary>
        /// <remarks>
        ///     <b>Reading them immediately is worthless, and the first version of this did.</b>
        ///     Having the console open is itself one of the things that stops a player taking
        ///     input, and the console is the only way to ask — so "player accepting input:
        ///     False" was a check that could never say anything else. It looked like a finding
        ///     and was an artefact of the question.
        ///
        ///     So this schedules the reading for after the console is shut, and names each gate
        ///     separately: "something is blocking input" is not an answer, and the eleven things
        ///     that can be are fixed in eleven different ways.
        /// </remarks>
        private static void InputLater(Terminal console)
        {
            Tell(console, "close the console - the reading is taken in three seconds, and logged");

            GameObject host = new GameObject("KukolonyInputProbe");
            host.AddComponent<InputProbe>();
            Object.DontDestroyOnLoad(host);
        }

        /// <summary>Takes one reading of the input gates, a moment after being created.</summary>
        private sealed class InputProbe : MonoBehaviour
        {
            private float _at;

            private void Awake() => _at = Time.time + 3f;

            private void Update()
            {
                if (Time.time < _at) return;

                Player player = Player.m_localPlayer;
                Log.Info("[input] " +
                         $"takeInput={(player != null && player.TakeInput())} " +
                         $"console={Console.IsVisible()} " +
                         $"chat={(Chat.instance != null && Chat.instance.HasFocus())} " +
                         $"inventory={InventoryGui.IsVisible()} " +
                         $"menu={Menu.IsVisible()} " +
                         $"textInput={TextInput.IsVisible()} " +
                         $"store={StoreGui.IsVisible()} " +
                         $"map={Minimap.IsOpen()} " +
                         $"freeFly={GameCamera.InFreeFly()} " +
                         $"radial={Hud.InRadial()} " +
                         $"pieceWindow={Hud.IsPieceSelectionVisible()} " +
                         $"colonyScreen={(ColonyScreen.Instance != null && ColonyScreen.Instance.IsOpen)}");

                Report.Say("input reading written to the log");
                Destroy(gameObject);
            }
        }

        /// <summary>Sends the nearest villager to the player, however far that is.</summary>
        private static void Send(Terminal console)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            Villager nearest = Nearest(player.transform.position);
            if (nearest == null)
            {
                Tell(console, "no villager is loaded to send for");
                return;
            }

            Vector3 target = player.transform.position;
            nearest.SendOnErrand(target);

            float distance = Utils.DistanceXZ(nearest.transform.position, target);
            Tell(console, $"{nearest.State.Name} is coming - {distance:0}m away. Walks while you can " +
                          $"see it, covers ground unseen beyond {ModConfig.TravelObservedRange.Value:0}m.");
        }

        /// <summary>Says what every loaded villager is doing, so a journey can be followed.</summary>
        private static void Where(Terminal console)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            int found = 0;
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                found++;
                float distance = Utils.DistanceXZ(villager.transform.position, player.transform.position);
                Tell(console, $"{villager.State.Name}: {distance:0}m away, '{villager.Activity}'" +
                              (villager.IsTravelling ? " - travelling" : string.Empty));
            }

            if (found == 0) Tell(console, "no villagers are loaded");
        }

        /// <summary>
        ///     Answers where the question was asked.
        /// </summary>
        /// <remarks>
        ///     Into the console, not the log. The first version of these printed the numbers to
        ///     the log file and a bare count to the screen, which is no use at all to somebody
        ///     standing in the world watching a villager walk - the answer arrived somewhere
        ///     they would have to stop and go and read. The log line is kept as well, because
        ///     afterwards the file is exactly where the answer should be.
        /// </remarks>
        private static void Tell(Terminal console, string what)
        {
            if (console != null) console.AddString(what);
            Log.Info("[travel] " + what);
        }

        private static Villager Nearest(Vector3 to)
        {
            Nearby.Clear();
            Villager best = null;
            float closest = float.MaxValue;

            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                float distance = Utils.DistanceXZ(villager.transform.position, to);
                if (distance >= closest) continue;

                closest = distance;
                best = villager;
            }

            return best;
        }
    }
}
