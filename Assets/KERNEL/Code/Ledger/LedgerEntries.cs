using System.Collections.Generic;
using System.Linq;
using KSP.Game.Science;

namespace KERNEL.Ledger
{
    /// <summary>
    /// One report (data or sample) for an experiment at a location.
    /// Science values include all location scalars and the difficulty's science multiplier.
    /// </summary>
    public class ReportEntry
    {
        public float Possible;
        public float Earned;
        public bool IsSubmitted;
    }

    /// <summary>
    /// An experiment at one research location: a situation on a body, optionally narrowed to a region.
    /// </summary>
    public class LocationEntry
    {
        public ResearchLocation Location;

        // Discoverable regions are hidden until a vessel reaches them; normal regions are always discovered
        public bool IsDiscoverable;
        public bool IsDiscovered;

        // Null when the experiment doesn't produce that report type
        public ReportEntry Data;
        public ReportEntry Sample;

        public float Possible => (Data?.Possible ?? 0f) + (Sample?.Possible ?? 0f);
        public float Earned => (Data?.Earned ?? 0f) + (Sample?.Earned ?? 0f);
    }

    public class ExperimentEntry
    {
        public ExperimentDefinition Definition;
        public bool IsUnlocked;
        public List<LocationEntry> Locations = new();

        public float Possible => Locations.Sum(l => l.Possible);
        public float Earned => Locations.Sum(l => l.Earned);
    }

    public class BodyEntry
    {
        public string BodyName;
        public bool IsDiscovered;
        public int RegionCount;
        public List<ExperimentEntry> Experiments = new();

        public float Possible => Experiments.Sum(e => e.Possible);
        public float Earned => Experiments.Sum(e => e.Earned);
    }
}
