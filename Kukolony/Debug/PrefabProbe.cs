using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     One-shot diagnostic that answers questions the decompiled assembly cannot:
    ///     what prefabs actually exist and what components they carry. Prefab contents
    ///     are asset data, not code, so this is the only way to know for certain.
    ///
    ///     Run it once, read the log, delete the guesswork. Not part of the mod.
    /// </summary>
    internal sealed class PrefabProbe : MonoBehaviour
    {
        private bool _done;

        private void Update()
        {
            if (_done || !ModConfig.DebugProbeEnabled.Value)
            {
                return;
            }

            if (ZNetScene.instance == null || ObjectDB.instance == null || Player.m_localPlayer == null)
            {
                return;
            }

            _done = true;
            Probe();

            if (ModConfig.BenchmarkAutoExit.Value)
            {
                Log.Info("[Probe] done - quitting.");
                Application.Quit();
            }
        }

        private static void Probe()
        {
            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            Log.Info($"[Probe] ZNetScene holds {prefabs.Count} prefabs");

            ReportPlayerPrefab(prefabs);
            ReportVisEquipmentRigs(prefabs);
            ReportRandomisedHumanoids(prefabs);
            ReportComponents(prefabs, "Dverger");
            ReportComponents(prefabs, "FallenWarrior");
            ReportComponents(prefabs, "ShadowPerson");
            ReportSmallPieces(prefabs);
            ReportStationContracts(prefabs);
            ReportPlantables(prefabs);
            ReportCultivators(prefabs);
            ReportRepairables(prefabs);
            ReportBuildTools();
            ReportFireplaces(prefabs);
        }

        /// <summary>
        ///     What burns, what it burns, and which of them refuse to be fed.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The registry excluded fireplaces because "one that burns for ever or refuses
        ///         refills still accepts fuel and still reports a change". Both are prefab fields,
        ///         so this prints how many of each this game actually ships - if every fireplace
        ///         turned out to be infinite, the whole protocol would be dead code that looked
        ///         alive.
        ///     </para>
        ///     <para>
        ///         <b>And which value of the state key means off</b>, which is the one thing the
        ///         rule you asked for hangs on. A fire pit is spawned lit, read, switched off,
        ///         and read again - because guessing this would silently invert "off means off"
        ///         into "never feed anything that can be switched off", and both look like a job
        ///         quietly deciding not to work.
        ///     </para>
        /// </remarks>
        private static void ReportFireplaces(List<GameObject> prefabs)
        {
            int total = 0, infinite = 0, noRefill = 0, canTurnOff = 0, feedable = 0;
            List<string> lines = new List<string>();

            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Fireplace fire)) continue;

                total++;
                if (fire.m_infiniteFuel) infinite++;
                if (!fire.m_canRefill) noRefill++;
                if (fire.m_canTurnOff) canTurnOff++;

                bool takes = !fire.m_infiniteFuel && fire.m_canRefill && fire.m_fuelItem != null;
                if (takes) feedable++;

                // Also a cooking station or a smelter? Those are claimed by the component that
                // does the work, and only what falls through is a fire this job should feed.
                string also = prefab.GetComponent<CookingStation>() != null ? " ALSO-COOKS"
                    : prefab.GetComponent<Smelter>() != null ? " ALSO-SMELTS" : string.Empty;

                if (lines.Count < 14)
                {
                    lines.Add($"{prefab.name}: fuel={(fire.m_fuelItem == null ? "none" : fire.m_fuelItem.name)} " +
                              $"max={fire.m_maxFuel:0} infinite={fire.m_infiniteFuel} " +
                              $"canRefill={fire.m_canRefill} canTurnOff={fire.m_canTurnOff}{also}");
                }
            }

            Log.Info($"[Probe:fire] {total} fireplace(s): {feedable} can be fed, {infinite} burn for ever, " +
                     $"{noRefill} refuse refills, {canTurnOff} can be switched off");

            foreach (string line in lines) Log.Info($"[Probe:fire] {line}");

            ReportFireState(prefabs);
        }

        /// <summary>Which value of the state key means a fire is switched off.</summary>
        private static void ReportFireState(List<GameObject> prefabs)
        {
            GameObject named = null;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Fireplace candidate)) continue;
                if (!candidate.m_canTurnOff || candidate.m_infiniteFuel) continue;

                named = prefab;
                break;
            }

            if (named == null || Player.m_localPlayer == null || ZNetScene.instance == null)
            {
                Log.Info("[Probe:fire] no fire that can be switched off - the state rule is untestable here");
                return;
            }

            Vector3 at = Player.m_localPlayer.transform.position + new Vector3(3f, 0f, 3f);
            GameObject lit = Object.Instantiate(named, at, Quaternion.identity);

            if (lit == null || !lit.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Log.Warning("[Probe:fire] could not place a fire to read its state");
                return;
            }

            ZDO zdo = view.GetZDO();
            Fireplace fire = lit.GetComponent<Fireplace>();

            Log.Info($"[Probe:fire] {named.name} as placed: state={zdo.GetInt(ZDOVars.s_state, -99)} " +
                     $"fuel={zdo.GetFloat(ZDOVars.s_fuel, -1f):0.##} burning={fire.IsBurning()}");

            // Switched off the way the game does it, then read again. The pair is the answer.
            view.ClaimOwnership();
            view.InvokeRPC("RPC_ToggleOn");

            Log.Info($"[Probe:fire] {named.name} after toggling: state={zdo.GetInt(ZDOVars.s_state, -99)} " +
                     $"fuel={zdo.GetFloat(ZDOVars.s_fuel, -1f):0.##} burning={fire.IsBurning()}");

            ZNetScene.instance.Destroy(lit);
        }

        /// <summary>
        ///     What decays, what it is worth, and what station it needs to be mended.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Two questions the assembly cannot answer. <c>Piece.m_craftingStation</c> is a
        ///         scene reference, and a reference on a <em>prefab</em> may well be null even
        ///         though the piece plainly needs a workbench - if it is, the station rule has to
        ///         be read some other way and finding that out after building the job would be a
        ///         rule that silently never fires.
        ///     </para>
        ///     <para>
        ///         And how wide the net is. <c>WearNTear</c> is on walls and roofs, but also on
        ///         carts, ships and a good deal of world scenery, and a job pointed at all of it
        ///         is a villager maintaining the countryside. Counted by whether the thing is a
        ///         Piece as well, because that is the difference between what somebody built and
        ///         what was always there.
        ///     </para>
        /// </remarks>
        private static void ReportRepairables(List<GameObject> prefabs)
        {
            int total = 0, built = 0, loose = 0, needingStation = 0;
            List<string> sample = new List<string>();
            List<string> unbuilt = new List<string>();

            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out WearNTear wear)) continue;

                total++;

                bool isPiece = prefab.TryGetComponent(out Piece piece);
                if (isPiece) built++;
                else loose++;

                string station = isPiece && piece.m_craftingStation != null
                    ? piece.m_craftingStation.m_name
                    : string.Empty;

                if (station.Length > 0) needingStation++;

                if (!isPiece && unbuilt.Count < 12) unbuilt.Add(prefab.name);

                if (sample.Count < 12 && isPiece)
                {
                    sample.Add($"{prefab.name} hp={wear.m_health:0} " +
                               $"station='{(station.Length == 0 ? "none" : station)}' " +
                               $"noRoofWear={wear.m_noRoofWear} noSupportWear={wear.m_noSupportWear}");
                }
            }

            Log.Info($"[Probe:wear] {total} prefab(s) carry WearNTear: {built} are built pieces, " +
                     $"{loose} are not, {needingStation} name a crafting station");

            foreach (string line in sample) Log.Info($"[Probe:wear] {line}");
            Log.Info($"[Probe:wear] not pieces: {Join(unbuilt)}");
        }

        /// <summary>
        ///     Which items carry a build menu, and which of those can take a piece down.
        /// </summary>
        /// <remarks>
        ///     The hammer has to be told from the hoe and the cultivator, which carry piece tables
        ///     too. <c>PieceTable.m_canRemovePieces</c> is the difference in the assembly; whether
        ///     it is the difference in the assets is what this prints. Naming the hammer would
        ///     work until somebody added a modded one.
        /// </remarks>
        private static void ReportBuildTools()
        {
            if (ObjectDB.instance?.m_items == null)
            {
                Log.Warning("[Probe:tool] no ObjectDB - cannot look at the build tools");
                return;
            }

            List<string> found = new List<string>();

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || !item.TryGetComponent(out ItemDrop drop)) continue;

                PieceTable table = drop.m_itemData?.m_shared?.m_buildPieces;
                if (table == null) continue;

                found.Add($"{item.name}: canRemovePieces={table.m_canRemovePieces} " +
                          $"pieces={(table.m_pieces == null ? 0 : table.m_pieces.Count)}");
            }

            Log.Info($"[Probe:tool] {found.Count} item(s) carry a build menu");
            foreach (string line in found) Log.Info($"[Probe:tool] {line}");
        }

        /// <summary>
        ///     Everything that can be planted, and everything a sowing job needs to know about it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The whole of the Farm design rests on two things the assembly cannot say.</b>
        ///         First, whether a <c>Plant</c> prefab carries a <c>Piece</c> - because that is
        ///         where the seed cost lives, and a plan that assumed one would be building on a
        ///         guess. Second, what the spacing numbers actually are: a birch and a carrot are
        ///         both Plants, and if their grow radii turned out to be similar the whole
        ///         per-crop layout argument would be wasted effort.
        ///     </para>
        ///     <para>
        ///         Prefab contents are asset data. This mod does not ship the assets, cannot read
        ///         them from the managed assembly, and has been wrong about a prefab before - so
        ///         the answer is printed from a running game before anything is built on it.
        ///     </para>
        /// </remarks>
        private static void ReportPlantables(List<GameObject> prefabs)
        {
            int total = 0, withPiece = 0, withoutPiece = 0, cultivated = 0;
            List<string> lines = new List<string>();

            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Plant plant)) continue;

                total++;

                // The seed and its cost, which is the fact the design hangs on.
                string cost = "NO PIECE";
                if (prefab.TryGetComponent(out Piece piece))
                {
                    withPiece++;
                    List<string> parts = new List<string>();
                    if (piece.m_resources != null)
                    {
                        foreach (Piece.Requirement need in piece.m_resources)
                        {
                            if (need?.m_resItem == null) continue;
                            parts.Add($"{need.m_resItem.name}x{need.m_amount}");
                        }
                    }

                    cost = parts.Count > 0 ? Join(parts) : "free";
                }
                else
                {
                    withoutPiece++;
                }

                if (plant.m_needCultivatedGround) cultivated++;

                // What it turns into, and through that what it eventually gives. The picker is
                // meant to read "grow carrots" off this chain, so a break anywhere in it is
                // worth seeing now rather than as an empty list on a screen.
                List<string> grows = new List<string>();
                if (plant.m_grownPrefabs != null)
                {
                    foreach (GameObject grown in plant.m_grownPrefabs)
                    {
                        if (grown == null) continue;

                        string gives = grown.TryGetComponent(out Pickable pick) && pick.m_itemPrefab != null
                            ? pick.m_itemPrefab.name
                            : (grown.GetComponent<TreeBase>() != null ? "(tree)" : "(nothing pickable)");

                        grows.Add($"{grown.name}->{gives}");
                    }
                }

                lines.Add($"{prefab.name}: seed={cost} space={plant.m_growRadius:0.##}" +
                          $"/vines={plant.m_growRadiusVines:0.##} biome={plant.m_biome} " +
                          $"cultivated={plant.m_needCultivatedGround} " +
                          $"grow={plant.m_growTime:0}-{plant.m_growTimeMax:0}s " +
                          $"destroyIfCantGrow={plant.m_destroyIfCantGrow} => {Join(grows)}");
            }

            Log.Info($"[Probe:plant] {total} plantable prefab(s); {withPiece} carry a Piece, " +
                     $"{withoutPiece} do NOT, {cultivated} need cultivated ground");

            foreach (string line in lines) Log.Info($"[Probe:plant] {line}");
        }

        /// <summary>
        ///     Which terrain pieces cultivate, so the job can find one by component rather than
        ///     by name.
        /// </summary>
        /// <remarks>
        ///     <c>TerrainOp.Awake</c> applies its operation and then destroys its own GameObject,
        ///     so cultivating is one instantiate - but only if the right piece can be picked out
        ///     of the several that edit terrain. Printed with the paint type so the predicate
        ///     that picks it is written against what the game actually has.
        /// </remarks>
        private static void ReportCultivators(List<GameObject> prefabs)
        {
            Report("ZNetScene", prefabs);

            // And the build menus, because the first pass looked only in ZNetScene and found
            // nothing at all. A terrain op destroys its own GameObject during Awake, so it never
            // needs a ZNetView and is never registered as a networked prefab - it exists only as
            // an entry in the piece table of whatever tool places it. Looking in the wrong list
            // returns "none", which reads exactly like "this game has no cultivator".
            if (ObjectDB.instance?.m_items == null)
            {
                Log.Warning("[Probe:terrain] no ObjectDB - cannot look through the build menus");
                return;
            }

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || !item.TryGetComponent(out ItemDrop drop)) continue;

                PieceTable table = drop.m_itemData?.m_shared?.m_buildPieces;
                if (table?.m_pieces == null || table.m_pieces.Count == 0) continue;

                Report($"{item.name} piece table", table.m_pieces);
            }
        }

        /// <summary>Which of these carry a terrain operation, and what it paints.</summary>
        private static void Report(string where, List<GameObject> candidates)
        {
            List<string> found = new List<string>();

            foreach (GameObject prefab in candidates)
            {
                if (prefab == null || !prefab.TryGetComponent(out TerrainOp op) || op.m_settings == null) continue;

                found.Add($"{prefab.name}: paint={op.m_settings.m_paintType} " +
                          $"cleared={op.m_settings.m_paintCleared} radius={op.m_settings.m_paintRadius:0.##} " +
                          $"level={op.m_settings.m_level} raise={op.m_settings.m_raise} " +
                          $"znetview={(prefab.GetComponent<ZNetView>() != null)} " +
                          $"piece={(prefab.GetComponent<Piece>() != null)}");
            }

            if (found.Count == 0) return;

            Log.Info($"[Probe:terrain] {where}: {found.Count} terrain op(s)");
            foreach (string line in found) Log.Info($"[Probe:terrain] {line}");
        }

        private static void ReportStationContracts(List<GameObject> prefabs)
        {
            string[] names = { "fire_pit", "smelter", "charcoal_kiln", "piece_cookingstation",
                "fermenter", "piece_beehive" };
            foreach (string name in names)
            {
                GameObject prefab = prefabs.FirstOrDefault(p => p != null && p.name == name);
                if (prefab == null)
                {
                    Log.Warning($"[Probe:station] {name}: missing");
                    continue;
                }
                IEnumerable<Component> stations = prefab.GetComponents<Component>().Where(c =>
                    c is Fireplace || c is Smelter || c is CookingStation || c is Fermenter ||
                    c is Beehive || c is Container);
                foreach (Component component in stations)
                {
                    IEnumerable<string> rpcs = component.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method => method.Name.StartsWith("RPC_"))
                        .Select(method => method.Name + "(" + string.Join(",",
                            method.GetParameters().Select(parameter => parameter.ParameterType.Name).ToArray()) + ")");
                    Log.Info($"[Probe:station] {name}: {component.GetType().Name}; RPCs={Join(rpcs)}");
                }
            }
        }

        /// <summary>Can we clone the player body rig at all?</summary>
        private static void ReportPlayerPrefab(List<GameObject> prefabs)
        {
            GameObject player = prefabs.FirstOrDefault(p => p != null && p.name == "Player");
            if (player == null)
            {
                Log.Warning("[Probe] no 'Player' prefab in ZNetScene - cloning the player rig is not possible this way");
                return;
            }

            Log.Info("[Probe] 'Player' prefab IS present in ZNetScene");
            LogComponents("Player", player);

            if (player.TryGetComponent(out VisEquipment vis))
            {
                Log.Info($"[Probe] Player VisEquipment: models={vis.m_models.Length} " +
                         $"isPlayer={vis.m_isPlayer} bodyModel={(vis.m_bodyModel != null ? "set" : "NULL")}");
            }
        }

        /// <summary>Which prefabs carry a swappable body model - i.e. a player-like rig.</summary>
        private static void ReportVisEquipmentRigs(List<GameObject> prefabs)
        {
            List<string> rigs = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out VisEquipment vis))
                {
                    continue;
                }

                if (vis.m_models != null && vis.m_models.Length > 0)
                {
                    rigs.Add($"{prefab.name}(models={vis.m_models.Length},isPlayer={vis.m_isPlayer})");
                }
            }

            Log.Info($"[Probe] prefabs with a swappable body model ({rigs.Count}): {Join(rigs)}");
        }

        /// <summary>Vanilla humanoids that already randomise their own gear.</summary>
        private static void ReportRandomisedHumanoids(List<GameObject> prefabs)
        {
            List<string> found = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Humanoid humanoid))
                {
                    continue;
                }

                int sets = humanoid.m_randomSets?.Length ?? 0;
                int armor = humanoid.m_randomArmor?.Length ?? 0;
                int weapon = humanoid.m_randomWeapon?.Length ?? 0;
                if (sets + armor + weapon > 0)
                {
                    found.Add($"{prefab.name}(sets={sets},armor={armor},weapon={weapon})");
                }
            }

            Log.Info($"[Probe] humanoids with randomised gear ({found.Count}): {Join(found)}");
        }

        /// <summary>
        ///     Placeable pieces suitable for a colony hearth. Filtered to buildable ones
        ///     with an icon, because CustomPiece.IsValid demands an icon and cloning is
        ///     the only way to get one without Unity.
        /// </summary>
        private static void ReportSmallPieces(List<GameObject> prefabs)
        {
            List<string> candidates = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Piece piece))
                {
                    continue;
                }

                if (piece.m_icon == null || !piece.m_enabled)
                {
                    continue;
                }

                bool hasContainer = prefab.GetComponent<Container>() != null;
                bool hasCraftingStation = prefab.GetComponent<CraftingStation>() != null;
                candidates.Add($"{prefab.name}{(hasContainer ? "[chest]" : string.Empty)}" +
                               $"{(hasCraftingStation ? "[station]" : string.Empty)}");
            }

            Log.Info($"[Probe] buildable pieces with icons ({candidates.Count}): {Join(candidates)}");
        }

        private static void ReportComponents(List<GameObject> prefabs, string prefabName)
        {
            GameObject prefab = prefabs.FirstOrDefault(p => p != null && p.name == prefabName);
            if (prefab == null)
            {
                Log.Warning($"[Probe] prefab '{prefabName}' not found");
                return;
            }

            LogComponents(prefabName, prefab);
        }

        private static void LogComponents(string label, GameObject prefab)
        {
            IEnumerable<string> names = prefab.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name);
            Log.Info($"[Probe] {label} components: {Join(names)}");
        }

        private static string Join(IEnumerable<string> values)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string value in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(value);
            }

            return builder.Length == 0 ? "(none)" : builder.ToString();
        }
    }
}
