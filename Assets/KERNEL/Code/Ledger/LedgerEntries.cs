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
        public ExperimentDefinition Experiment;
        public ResearchLocation Location;

        // Whether the player has researched a part that can run this experiment
        public bool IsUnlocked;

        // Discoverable regions are hidden until a vessel reaches them; normal regions are always discovered
        public bool IsDiscoverable;
        public bool IsDiscovered;

        // Null when the experiment doesn't produce that report type
        public ReportEntry Data;
        public ReportEntry Sample;

        public float Possible => (Data?.Possible ?? 0f) + (Sample?.Possible ?? 0f);
        public float Earned => (Data?.Earned ?? 0f) + (Sample?.Earned ?? 0f);
        public bool HasSubmissions => (Data?.IsSubmitted ?? false) || (Sample?.IsSubmitted ?? false);
        public bool IsComplete => (Data?.IsSubmitted ?? true) && (Sample?.IsSubmitted ?? true);
    }

    /// <summary>
    /// A place within a body's sphere of influence that has experiments: an orbit, the atmosphere as a whole,
    /// or a biome (science region). Locations that don't depend on biome live in the situation's area.
    /// </summary>
    public class AreaEntry
    {
        // Region ID, or the situation name for whole-body areas
        public string Id;
        public string DisplayName;

        // Every body has orbits, so they're never a spoiler
        public bool IsOrbit;

        // Results here are the same anywhere on the body (no biome)
        public bool IsWholeBody;

        public List<LocationEntry> Locations = new();

        public bool HasSubmissions => Locations.Any(l => l.HasSubmissions);
    }

    public class BodyEntry
    {
        public string BodyName;
        public string DisplayName;
        public bool IsStar;
        public bool IsDiscovered;

        // Empty for bodies orbiting nothing (stars); used to nest planets under stars and moons under planets
        public string ParentName;
        // Average distance from the body it orbits; orders siblings innermost first
        public double SemiMajorAxis;

        public List<AreaEntry> Areas = new();
        public List<BodyEntry> Children = new();
    }
}
