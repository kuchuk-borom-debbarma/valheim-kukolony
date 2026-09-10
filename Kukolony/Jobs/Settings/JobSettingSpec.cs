using System;
using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Jobs.Settings
{
    /// <summary>What kind of control a setting needs.</summary>
    internal enum SettingKind
    {
        /// <summary>On or off.</summary>
        Toggle,
        /// <summary>A number with bounds and a step.</summary>
        Number,
        /// <summary>Exactly one of a fixed list.</summary>
        Choice,
        /// <summary>Any number of items, chosen from what the game actually has.</summary>
        Items,
        /// <summary>Any number of the colony's registered structures.</summary>
        Structures,
        /// <summary>One registered structure, or none.</summary>
        StructureRef
    }

    /// <summary>One offered value, and what to call it.</summary>
    internal readonly struct Option
    {
        internal Option(string value, string label)
        {
            Value = value;
            Label = label;
        }

        internal string Value { get; }
        internal string Label { get; }
    }

    /// <summary>
    ///     One configurable setting on a job: what it is called, what kind of control it needs,
    ///     where its value lives, and when it is worth showing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Jobs describe their settings rather than the panel knowing them. That is what
    ///         lets different jobs offer genuinely different configuration without the panel
    ///         growing a branch per job, and it is why a setting cannot be shown by a screen
    ///         that the job then ignores - the same declaration drives both.
    ///     </para>
    ///     <para>
    ///         Values are read and written through delegates rather than by key, so a setting
    ///         can bind to a real field on the job and the engine keeps reading exactly what it
    ///         always did. Nothing here needs a save-format change or a bag of loose strings.
    ///     </para>
    ///     <para>
    ///         This is C# rather than a JSON schema on purpose: the compiler checks that every
    ///         setting binds to something real, options and conditions are ordinary code, and
    ///         there is no parser and no second source of truth to drift.
    ///     </para>
    /// </remarks>
    internal sealed class JobSettingSpec
    {
        internal string Label = string.Empty;

        /// <summary>One line under the control, or empty. Say what it does, not what it is.</summary>
        internal string Help = string.Empty;

        internal SettingKind Kind = SettingKind.Toggle;

        /// <summary>Bounds and granularity for <see cref="SettingKind.Number"/>.</summary>
        internal float Minimum;
        internal float Maximum = 100f;
        internal float Step = 1f;

        /// <summary>Formats a number for display, so metres and counts do not look alike.</summary>
        internal Func<float, string> Format;

        /// <summary>
        ///     What may be chosen. Given the colony because the useful answers are usually
        ///     things it owns - its containers, its stations - rather than a fixed list.
        /// </summary>
        internal Func<Colony, List<Option>> Options;

        /// <summary>
        ///     When this setting is worth showing at all. A setting that cannot affect anything
        ///     in the job's current configuration is noise, and worse, invites a player to
        ///     change something and watch nothing happen.
        /// </summary>
        internal Func<ColonyJobConfig, bool> Visible;

        // Value access. Only the pair matching Kind is used.
        internal Func<ColonyJobConfig, bool> GetFlag;
        internal Action<ColonyJobConfig, bool> SetFlag;
        internal Func<ColonyJobConfig, float> GetNumber;
        internal Action<ColonyJobConfig, float> SetNumber;
        internal Func<ColonyJobConfig, string> GetChoice;
        internal Action<ColonyJobConfig, string> SetChoice;
        internal Func<ColonyJobConfig, List<string>> GetList;
        internal Func<ColonyJobConfig, List<ZDOID>> GetReferences;
        internal Func<ColonyJobConfig, ZDOID> GetReference;
        internal Action<ColonyJobConfig, ZDOID> SetReference;

        /// <summary>Narrows a structure picker to what the setting can actually use.</summary>
        internal StructureCapability Capability = StructureCapability.Container;

        internal bool IsVisible(ColonyJobConfig job) => Visible == null || Visible(job);

        internal string Describe(ColonyJobConfig job, Colony colony)
        {
            switch (Kind)
            {
                case SettingKind.Toggle:
                    return GetFlag(job) ? "yes" : "no";
                case SettingKind.Number:
                    float value = GetNumber(job);
                    return Format != null ? Format(value) : value.ToString("0.##");
                case SettingKind.Choice:
                    string current = GetChoice(job);
                    foreach (Option option in Options(colony))
                        if (option.Value == current) return option.Label;
                    return current.Length == 0 ? "any" : current;
                case SettingKind.Items:
                    List<string> items = GetList(job);
                    return items.Count == 0 ? "anything" : string.Join(", ", items.ToArray());
                case SettingKind.Structures:
                    return GetReferences(job).Count + " chosen";
                default:
                    return GetReference(job).IsNone() ? "automatic" : "chosen";
            }
        }
    }
}
