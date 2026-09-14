using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>One thing a station can make.</summary>
    internal sealed class CraftOption
    {
        /// <summary>The product's prefab name, which is the spelling everything here counts in.</summary>
        internal string Item = string.Empty;

        /// <summary>What to show a player - the item's own name, localised by the game.</summary>
        internal string Display = string.Empty;

        /// <summary>How many come out of one craft.</summary>
        internal int Amount = 1;

        /// <summary>The station level this needs. 1 is a bare station with no extensions.</summary>
        internal int MinLevel = 1;
    }

    /// <summary>
    ///     What each kind of crafting station can make.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built from <c>ObjectDB.instance.m_recipes</c>, which is the only complete list.
    ///         Jötunn's <c>ItemManager.GetRecipe</c> returns a mod's <em>own</em> recipes and
    ///         nothing else - a trap recorded in an earlier Kuku mod's commit message, in
    ///         capitals, after it cost a day.
    ///     </para>
    ///     <para>
    ///         <b>Keyed by station name, not by prefab.</b> A recipe names the station it needs
    ///         as a string - "$piece_forge" - so a modded station calling itself a forge runs
    ///         forge recipes untouched, and one look at a prefab answers "what could be made
    ///         here" with nothing loaded. That is what lets an outpost's workbench be given
    ///         orders from home, the same property <see cref="ProcessingOptions" /> exists to
    ///         preserve.
    ///     </para>
    ///     <para>
    ///         <b>Per session</b>, because prefab hashes and the recipe list are: mods register
    ///         their own, and a new world is a new ObjectDB.
    ///     </para>
    /// </remarks>
    internal static class CraftCatalogue
    {
        private static readonly Dictionary<string, List<CraftOption>> ByStation =
            new Dictionary<string, List<CraftOption>>();

        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();

        /// <summary>
        ///     Recipes that need no station at all.
        /// </summary>
        /// <remarks>
        ///     The stone axe, the club, the hammer, the torch - the things a player makes with
        ///     bare hands. They were being dropped entirely, because a catalogue keyed by station
        ///     name has nowhere to file a recipe that names no station, and the result was a
        ///     workbench that could not be asked for a stone axe.
        ///
        ///     Vanilla shows them at a station when that station says so. <c>Player.RequiredCraftingStation</c>
        ///     accepts a recipe with no station unless <c>m_showBasicRecipies</c> is false on the
        ///     one being used, which is the rule copied here rather than invented.
        /// </remarks>
        private static readonly List<CraftOption> Basic = new List<CraftOption>();

        private static ObjectDB _owner;

        /// <summary>
        ///     Everything a station can make, in display order.
        /// </summary>
        /// <param name="stationName">
        ///     The station's own name - "$piece_workbench" - which is what a recipe names.
        /// </param>
        /// <param name="withBasic">
        ///     Whether this station also offers the recipes that need no station, as vanilla's
        ///     <c>m_showBasicRecipies</c> decides.
        /// </param>
        internal static List<CraftOption> For(string stationName, bool withBasic)
        {
            Build();

            List<CraftOption> found = new List<CraftOption>();
            if (!string.IsNullOrEmpty(stationName) && ByStation.TryGetValue(stationName, out List<CraftOption> mine))
            {
                found.AddRange(mine);
            }

            if (withBasic) found.AddRange(Basic);

            found.Sort((a, b) => string.Compare(a.Display, b.Display, System.StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// <summary>
        ///     Everything a registered structure can make, read from its prefab.
        /// </summary>
        /// <remarks>
        ///     Both halves of the question - which station is this, and does it show the basic
        ///     recipes - come off the prefab, so an outpost's forge answers with nothing loaded.
        /// </remarks>
        internal static List<CraftOption> ForStructure(string prefabName)
        {
            GameObject prefab = !string.IsNullOrEmpty(prefabName) && ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(prefabName)
                : null;

            return For(Stations.CraftProbe.NameOfPrefab(prefab), Stations.CraftProbe.ShowsBasic(prefab));
        }

        /// <summary>Whether a station may be asked to make this, by the rule vanilla uses.</summary>
        internal static bool MadeAt(Recipe recipe, string stationName, bool withBasic)
        {
            if (recipe == null) return false;

            return recipe.m_craftingStation != null
                ? recipe.m_craftingStation.m_name == stationName
                : withBasic;
        }

        /// <summary>The recipe that makes an item, or null when nothing villagers may use does.</summary>
        internal static Recipe RecipeFor(string itemPrefab)
        {
            Build();
            return !string.IsNullOrEmpty(itemPrefab) && Recipes.TryGetValue(itemPrefab, out Recipe recipe)
                ? recipe
                : null;
        }

        internal static void Clear()
        {
            ByStation.Clear();
            Basic.Clear();
            Recipes.Clear();
            _owner = null;
        }

        private static void Build()
        {
            if (ObjectDB.instance == null) return;
            if (ReferenceEquals(_owner, ObjectDB.instance) && ByStation.Count > 0) return;

            ByStation.Clear();
            Basic.Clear();
            Recipes.Clear();
            _owner = ObjectDB.instance;

            if (ObjectDB.instance.m_recipes == null) return;

            int skipped = 0;
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (!Offerable(recipe, ref skipped)) continue;

                string item = recipe.m_item.gameObject.name;

                List<CraftOption> options;
                if (recipe.m_craftingStation == null)
                {
                    options = Basic;
                }
                else if (!ByStation.TryGetValue(recipe.m_craftingStation.m_name, out options))
                {
                    options = new List<CraftOption>();
                    ByStation[recipe.m_craftingStation.m_name] = options;
                }

                options.Add(new CraftOption
                {
                    Item = item,
                    Display = Name(recipe.m_item),
                    Amount = Mathf.Max(1, recipe.m_amount),

                    // Asked of the recipe rather than read off m_minStationLevel, because the
                    // two differ: the required level rises with quality, and quality 1 is the
                    // only thing villagers make.
                    MinLevel = Mathf.Max(1, recipe.GetRequiredStationLevel(1))
                });

                // First recipe wins. Two recipes producing one item is rare and means the
                // catalogue would otherwise disagree with itself about what the item costs.
                if (!Recipes.ContainsKey(item)) Recipes[item] = recipe;
            }

            foreach (KeyValuePair<string, List<CraftOption>> pair in ByStation)
            {
                pair.Value.Sort((a, b) => string.Compare(a.Display, b.Display,
                    System.StringComparison.OrdinalIgnoreCase));
            }

            Basic.Sort((a, b) => string.Compare(a.Display, b.Display, System.StringComparison.OrdinalIgnoreCase));

            Log.Info($"[craft] {Recipes.Count} recipe(s) across {ByStation.Count} kind(s) of station, " +
                     $"{Basic.Count} needing none" +
                     (skipped > 0 ? $", {skipped} left out" : string.Empty));
        }

        /// <summary>
        ///     Whether a villager may be asked to make this.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Single-ingredient recipes are excluded, and they are the interesting one.</b>
        ///         <c>Recipe.GetAmount</c> calls <c>Player.m_localPlayer.GetFirstRequiredItem(...)</c>
        ///         unconditionally when <c>m_requireOnlyOneIngredient</c> is set, so merely asking
        ///         one of those how much it yields throws on a dedicated server and crafts against
        ///         the player's own inventory everywhere else.
        ///     </para>
        ///     <para>
        ///         Upgrade-only recipes are excluded because villagers craft at quality 1 and
        ///         never upgrade; recipes with no station are excluded because a villager has
        ///         nowhere to stand to make them.
        ///     </para>
        /// </remarks>
        private static bool Offerable(Recipe recipe, ref int skipped)
        {
            if (recipe == null || !recipe.m_enabled || recipe.m_item == null) return false;

            // A recipe naming no station is kept, not dropped: those are the things made by
            // hand, and a station that shows the basic recipes can be asked for them. A recipe
            // naming an *unnamed* station is dropped, because nothing can ever match it.
            if (recipe.m_craftingStation != null && string.IsNullOrEmpty(recipe.m_craftingStation.m_name))
                return false;

            if (recipe.m_noCraftOnlyUpgrade || recipe.m_requireOnlyOneIngredient)
            {
                skipped++;
                return false;
            }

            ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData?.m_shared;
            if (shared == null) return false;

            if (shared.m_dlc != null && shared.m_dlc.Length > 0 &&
                (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(shared.m_dlc)))
            {
                skipped++;
                return false;
            }

            return true;
        }

        private static string Name(ItemDrop item)
        {
            string token = item.m_itemData?.m_shared?.m_name;
            if (string.IsNullOrEmpty(token)) return item.gameObject.name;

            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }
    }
}
