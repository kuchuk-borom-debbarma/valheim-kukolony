using System;
using System.Collections.Generic;
using System.IO;
using Kukolony.Colonies;
using Kukolony.Core;
using SimpleJson;

namespace Kukolony.Jobs
{
    /// <summary>Player-authored job settings, saved as portable named JSON presets.</summary>
    internal static class JobPresetStore
    {
        private const string FolderName = "Kukolony";
        private const string PresetsFolderName = "presets";

        internal static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, FolderName, PresetsFolderName);

        internal static bool Save(string name, List<string> items, List<ZDOID> destinations)
        {
            string id = Id(name);
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                Directory.CreateDirectory(Folder);
                JsonArray itemArray = new JsonArray();
                foreach (string item in items) itemArray.Add(item);
                JsonObject root = new JsonObject
                {
                    ["name"] = name.Trim(),
                    ["items"] = itemArray,
                    ["destinations"] = ColonyMembers.Encode(destinations)
                };
                File.WriteAllText(Path.Combine(Folder, id + ".json"), SimpleJson.SimpleJson.SerializeObject(root));
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Could not save job preset '{name}': {e.Message}");
                return false;
            }
        }

        internal static bool TryLoad(string name, out List<string> items, out List<ZDOID> destinations)
        {
            items = new List<string>();
            destinations = new List<ZDOID>();
            string id = Id(name);
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                string path = Path.Combine(Folder, id + ".json");
                if (!File.Exists(path)) return false;
                JsonObject root = SimpleJson.SimpleJson.DeserializeObject(File.ReadAllText(path)) as JsonObject;
                if (root == null) return false;
                if (root.TryGetValue("items", out object raw) && raw is JsonArray array)
                    foreach (object value in array) if (value != null) items.Add(value.ToString());
                if (root.TryGetValue("destinations", out object encoded) && encoded != null)
                    destinations = ColonyMembers.Decode(encoded.ToString());
                return items.Count > 0;
            }
            catch (Exception e)
            {
                Log.Error($"Could not load job preset '{name}': {e.Message}");
                return false;
            }
        }

        private static string Id(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = name.Trim().ToLowerInvariant().Replace(' ', '-');
            foreach (char c in invalid) result = result.Replace(c.ToString(), string.Empty);
            return result;
        }
    }
}
