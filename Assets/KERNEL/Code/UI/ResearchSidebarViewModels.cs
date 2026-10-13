using System;
using Unity.Properties;
using UitkForKsp2.MVVM.Core;

namespace KERNEL.UI
{
    /// <summary>
    /// The R&D sidebar's data: the selected body and its totals. Bound in ResearchSidebar.uxml.
    /// </summary>
    internal class ResearchSidebarViewModel : ViewModelBase
    {
        private string _bodyName = string.Empty;
        private string _biomesExplored = string.Empty;
        private string _scienceGained = string.Empty;

        [CreateProperty]
        public string BodyName
        {
            get => _bodyName;
            set => SetField(ref _bodyName, value);
        }

        [CreateProperty]
        public string BiomesExplored
        {
            get => _biomesExplored;
            set => SetField(ref _biomesExplored, value);
        }

        [CreateProperty]
        public string ScienceGained
        {
            get => _scienceGained;
            set => SetField(ref _scienceGained, value);
        }

        private string _reportExperiment = string.Empty;
        private string _reportPlace = string.Empty;
        private string _reportText = string.Empty;

        // The field report section: the hovered or selected experiment, where it was run, and what it reported

        [CreateProperty]
        public string ReportExperiment
        {
            get => _reportExperiment;
            set => SetField(ref _reportExperiment, value);
        }

        [CreateProperty]
        public string ReportPlace
        {
            get => _reportPlace;
            set => SetField(ref _reportPlace, value);
        }

        [CreateProperty]
        public string ReportText
        {
            get => _reportText;
            set => SetField(ref _reportText, value);
        }
    }

    /// <summary>
    /// One biome (or orbit, or the atmosphere) in the sidebar. Bound in ResearchSidebarBiomeRow.uxml.
    /// </summary>
    internal class BiomeRowViewModel : ViewModelBase
    {
        private bool _isOpen;

        public BiomeRowViewModel(string name, string tally, bool isKnown, bool isComplete)
        {
            Name = name;
            Tally = tally;
            IsKnown = isKnown;
            IsComplete = isComplete;
        }

        [CreateProperty]
        public string Name { get; }

        [CreateProperty]
        public string Tally { get; }

        // Visited (or an orbit): it can be opened to list its experiments; otherwise it shows as "?????"
        public bool IsKnown { get; }

        public bool IsComplete { get; }

        /// <summary>
        /// Raised when the row opens or closes.
        /// </summary>
        public event Action<bool> OpenChanged;

        [CreateProperty]
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (SetField(ref _isOpen, value)) OpenChanged?.Invoke(value);
            }
        }

        public void Toggle()
        {
            if (IsKnown) IsOpen = !IsOpen;
        }
    }

    /// <summary>
    /// One experiment under an open biome. Bound in ResearchSidebarExperimentRow.uxml.
    /// </summary>
    internal class ExperimentRowViewModel : ViewModelBase
    {
        private readonly Func<string> _readFlavorText;
        private string _flavorText;

        public ExperimentRowViewModel(string key, string name, string place, string situation, string tally, bool isComplete,
            Func<string> flavorText, float received)
        {
            Key = key;
            Name = name;
            Place = place;
            Situation = situation;
            Tally = tally;
            IsComplete = isComplete;
            _readFlavorText = flavorText;
            Received = received;
        }

        // Which experiment where, so the selected row stays selected when the sidebar refreshes
        public string Key { get; }

        // The biome (or orbit, or atmosphere) it's run in, as the field report names it
        public string Place { get; }

        // The share of its science collected (0 to 1): how much of the field report came through
        public float Received { get; }

        [CreateProperty]
        public string Name { get; }

        // Landed, splashed... for biomes, where both can happen; empty for whole-body areas
        [CreateProperty]
        public string Situation { get; }

        [CreateProperty]
        public string Tally { get; }

        public bool IsComplete { get; }

        // What its submitted reports said in the field; empty until one is submitted. Looked up the first time the
        // row is hovered or selected, not for every row the sidebar builds
        public string FlavorText => _flavorText ??= _readFlavorText?.Invoke() ?? string.Empty;
    }
}
