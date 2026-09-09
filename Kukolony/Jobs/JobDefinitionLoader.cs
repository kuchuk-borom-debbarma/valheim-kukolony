using System;
using System.Collections.Generic;
using System.IO;
using Kukolony.Core;
using SimpleJson;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Reads job definitions from disk.
    ///
    ///     A definition is accepted whole or rejected whole. Half-loading a job would give
    ///     a villager work that silently skips a step, which is far worse than a job that
    ///     refuses to load and says why.
    /// </summary>
    internal static class JobDefinitionLoader
    {
        private const string FolderName = "Kukolony";
        private const string JobsFolderName = "jobs";

        internal static string JobsFolder =>
            Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, FolderName), JobsFolderName);

        /// <summary>
        ///     Ensures the folder exists with the built-in definitions in it, then loads
        ///     everything there. Never throws: a broken folder must not stop the mod.
        /// </summary>
        internal static List<JobDefinition> LoadAll()
        {
            List<JobDefinition> definitions = new List<JobDefinition>();

            try
            {
                EnsureDefaults();
            }
            catch (Exception e)
            {
                Log.Error($"Could not create the jobs folder at '{JobsFolder}': {e.Message}");
                return definitions;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(JobsFolder, "*.json");
            }
            catch (Exception e)
            {
                Log.Error($"Could not read the jobs folder: {e.Message}");
                return definitions;
            }

            foreach (string file in files)
            {
                JobDefinition definition = TryLoad(file);
                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            Log.Info($"Loaded {definitions.Count} job definition(s) from {JobsFolder}");
            return definitions;
        }

        private static void EnsureDefaults()
        {
            if (!Directory.Exists(JobsFolder))
            {
                Directory.CreateDirectory(JobsFolder);
            }

            string haul = Path.Combine(JobsFolder, DefaultJobDefinitions.HaulFileName);
            if (!File.Exists(haul))
            {
                File.WriteAllText(haul, DefaultJobDefinitions.Haul);
                Log.Info($"Wrote default job definition '{DefaultJobDefinitions.HaulFileName}'");
            }
        }

        private static JobDefinition TryLoad(string path)
        {
            string name = Path.GetFileName(path);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Error($"[{name}] could not be read: {e.Message}");
                return null;
            }

            if (!(SimpleJson.SimpleJson.DeserializeObject(text) is JsonObject root))
            {
                Log.Error($"[{name}] is not a JSON object.");
                return null;
            }

            string id = ReadString(root, "id");
            if (string.IsNullOrEmpty(id))
            {
                Log.Error($"[{name}] has no 'id'.");
                return null;
            }

            if (!(root.TryGetValue("steps", out object rawSteps) && rawSteps is JsonArray stepArray)
                || stepArray.Count == 0)
            {
                Log.Error($"[{name}] has no 'steps'.");
                return null;
            }

            List<JobStepSpec> steps = new List<JobStepSpec>();
            for (int i = 0; i < stepArray.Count; i++)
            {
                if (!(stepArray[i] is JsonObject stepObject))
                {
                    Log.Error($"[{name}] step {i} is not an object.");
                    return null;
                }

                string type = ReadString(stepObject, "type");
                if (string.IsNullOrEmpty(type))
                {
                    Log.Error($"[{name}] step {i} has no 'type'.");
                    return null;
                }

                JobStepSpec spec = new JobStepSpec(type, stepObject);
                if (JobStepFactory.TryBuild(spec) == null)
                {
                    Log.Error($"[{name}] step {i} has unknown type '{type}'. " +
                              $"Known types: {string.Join(", ", new List<string>(JobStepFactory.KnownTypes).ToArray())}");
                    return null;
                }

                steps.Add(spec);
            }

            string displayName = ReadString(root, "displayName");
            return new JobDefinition(id, string.IsNullOrEmpty(displayName) ? id : displayName, steps, name);
        }

        private static string ReadString(JsonObject source, string key)
        {
            return source.TryGetValue(key, out object value) && value != null ? value.ToString() : null;
        }
    }
}
