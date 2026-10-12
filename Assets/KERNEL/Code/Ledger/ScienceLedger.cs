using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSP.Game;
using KSP.Game.Science;
using KSP.Modules;
using KSP.Sim;
using KSP.Sim.Definitions;
using ReduxLib.Logging;

namespace KERNEL.Ledger
{
    /// <summary>
    /// Builds the science ledger (every experiment at every location of every body, with earned and possible
    /// science) from the game's science data. Has no UI dependencies, so any view can display it.
    /// </summary>
    public class ScienceLedger
    {
        // Retired experiment that is still registered in the game data
        private const string OldCrewReportExperimentId = "CrewReport";

        // EVA experiment every kerbal can do, so it never needs a part unlock
        private const string SurfaceSurveyExperimentId = "SurfaceSurvey";

        // Discoverables present in the data that can't actually be reached
        private static readonly HashSet<string> HiddenRegionIds = new() { "KerbinGlacier" };

        // Private game state with no public accessor
        private static readonly FieldInfo BodyRegionsField = typeof(ScienceRegionsDataProvider)
            .GetField("_cbToScienceRegions", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TravelFirstsField = typeof(TravelLogManager)
            .GetField("_firsts", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly ILogger _logger;

        public List<BodyEntry> Bodies { get; } = new();

        public ScienceLedger(ILogger logger)
        {
            _logger = logger;
        }

        public bool Rebuild(GameInstance game)
        {
            Bodies.Clear();

            ScienceRegionsDataProvider regionsProvider = game.ScienceManager?.ScienceRegionsDataProvider;
            ScienceExperimentsDataStore experimentsStore = game.ScienceManager?.ScienceExperimentsDataStore;
            if (regionsProvider == null || experimentsStore == null)
            {
                _logger.LogError("Science data isn't available");
                return false;
            }

            if (BodyRegionsField?.GetValue(regionsProvider) is not Dictionary<string, CelestialBodyScienceRegionsData> bodyRegions)
            {
                _logger.LogError("Couldn't read the science regions; the game's science data has changed");
                return false;
            }

            var firsts = TravelFirstsField?.GetValue(game.TravelLogManager) as TravelFirsts;
            if (firsts == null)
                _logger.LogWarning("Couldn't read the travel log; discovery state will be incomplete");

            var context = new BuildContext
            {
                RegionsProvider = regionsProvider,
                Firsts = firsts,
                Reports = GetSubmittedReports(game),
                ScienceMultiplier = GetScienceMultiplier(game)
            };

            HashSet<string> unlockedIds = GetUnlockedExperimentIds(game);
            List<ExperimentDefinition> experiments = experimentsStore.GetAllExperimentIDs()
                .Where(id => id != OldCrewReportExperimentId)
                .Select(experimentsStore.GetExperimentDefinition)
                .Where(definition => definition != null)
                .ToList();

            foreach (KeyValuePair<string, CelestialBodyScienceRegionsData> pair in bodyRegions)
            {
                CelestialBodyData body = game.CelestialBodies.Get(pair.Key)?.data;
                if (body == null)
                {
                    _logger.LogWarning($"Science regions exist for unknown body '{pair.Key}'");
                    continue;
                }

                var bodyEntry = new BodyEntry
                {
                    BodyName = pair.Key,
                    IsDiscovered = firsts?.SOIReached.ContainsKey(pair.Key) ?? false,
                    RegionCount = pair.Value.Regions?.Length ?? 0
                };

                bool isStar = game.CelestialBodies.GetStarStatus(pair.Key);
                foreach (ExperimentDefinition experiment in experiments)
                {
                    ExperimentEntry experimentEntry = BuildExperiment(context, body, isStar, pair.Value, experiment);
                    if (experimentEntry.Locations.Count == 0) continue;

                    experimentEntry.IsUnlocked = unlockedIds.Contains(experiment.ExperimentID);
                    bodyEntry.Experiments.Add(experimentEntry);
                }

                Bodies.Add(bodyEntry);
            }

            return true;
        }

        private class BuildContext
        {
            public ScienceRegionsDataProvider RegionsProvider;
            public TravelFirsts Firsts;
            public Dictionary<string, CompletedResearchReport> Reports;
            public float ScienceMultiplier;
        }

        private ExperimentEntry BuildExperiment(BuildContext context, CelestialBodyData body, bool isStar,
            CelestialBodyScienceRegionsData scienceData, ExperimentDefinition experiment)
        {
            var entry = new ExperimentEntry { Definition = experiment };
            ScienceRegionDefinition[] regions = scienceData.Regions ?? Array.Empty<ScienceRegionDefinition>();

            foreach (ScienceSitutation situation in Enum.GetValues(typeof(ScienceSitutation)))
            {
                if (!SituationExists(body, situation)) continue;

                var bodyWide = new ResearchLocation(false, body.bodyName, situation, "");
                if (!IsLocationValid(experiment, bodyWide, isStar, out bool regionRequired))
                {
                    // Experiments valid only in specific regions report the body-wide location as invalid
                    regionRequired = regions.Any(region =>
                        IsLocationValid(experiment, new ResearchLocation(true, body.bodyName, situation, region.Id), isStar, out _));
                    if (!regionRequired) continue;
                }

                if (!regionRequired)
                {
                    entry.Locations.Add(BuildLocation(context, scienceData, experiment, bodyWide));
                    continue;
                }

                foreach (ScienceRegionDefinition region in regions)
                {
                    var location = new ResearchLocation(true, body.bodyName, situation, region.Id);
                    if (!IsLocationValid(experiment, location, isStar, out _)) continue;
                    if (IsUnreachable(context, location)) continue;
                    // Some locations have negative scalars (e.g. Kerbin beaches while splashed); the game never offers them
                    if (GetLocationScalar(scienceData, location) < 0f) continue;

                    entry.Locations.Add(BuildLocation(context, scienceData, experiment, location));
                }
            }

            return entry;
        }

        private LocationEntry BuildLocation(BuildContext context, CelestialBodyScienceRegionsData scienceData,
            ExperimentDefinition experiment, ResearchLocation location)
        {
            var entry = new LocationEntry { Location = location, IsDiscovered = true };

            if (location.RequiresRegion)
            {
                entry.IsDiscoverable = context.RegionsProvider.IsRegionADiscoverable(location.BodyName, location.ScienceRegion);
                entry.IsDiscovered = !entry.IsDiscoverable
                    || (context.Firsts?.DiscoverableReached.ContainsKey($"{location.BodyName}_{location.ScienceRegion}") ?? false);
            }

            float valueScalar = GetLocationScalar(scienceData, location) * context.ScienceMultiplier;

            if (experiment.ExperimentType is ScienceExperimentType.DataType or ScienceExperimentType.Both)
                entry.Data = BuildReport(context, experiment, location, ScienceReportType.DataType, experiment.DataValue * valueScalar);

            if (experiment.ExperimentType is ScienceExperimentType.SampleType or ScienceExperimentType.Both)
                entry.Sample = BuildReport(context, experiment, location, ScienceReportType.SampleType, experiment.SampleValue * valueScalar);

            return entry;
        }

        private static ReportEntry BuildReport(BuildContext context, ExperimentDefinition experiment,
            ResearchLocation location, ScienceReportType type, float possible)
        {
            var entry = new ReportEntry { Possible = possible };

            // Same key format as CompletedResearchReport.ResearchReportKey
            string key = $"{experiment.ExperimentID}_{type}_{location.ResearchLocationId}";
            if (context.Reports.TryGetValue(key, out CompletedResearchReport report))
            {
                entry.IsSubmitted = true;
                // Stored values exclude the difficulty multiplier; the game applies it when awarding points
                entry.Earned = report.FinalScienceValue * context.ScienceMultiplier;
            }

            return entry;
        }

        private static bool IsLocationValid(ExperimentDefinition experiment, ResearchLocation location, bool isStar,
            out bool regionRequired)
        {
            // The Orbital Survey mod's mapping experiments don't list ValidLocations: they work in high orbit
            // around every body except stars
            if (experiment.ExperimentID.StartsWith("orbital_survey_visual_mapping")
                || experiment.ExperimentID.StartsWith("orbital_survey_biome_mapping"))
            {
                regionRequired = false;
                return !isStar && location.ScienceSituation == ScienceSitutation.HighOrbit;
            }

            return experiment.IsLocationValid(location, out regionRequired);
        }

        private static bool IsUnreachable(BuildContext context, ResearchLocation location)
        {
            if (HiddenRegionIds.Contains(location.ScienceRegion)) return true;

            // Discoverables are surface features, so they can't be studied from low orbit
            return location.ScienceSituation == ScienceSitutation.LowOrbit
                && context.RegionsProvider.IsRegionADiscoverable(location.BodyName, location.ScienceRegion);
        }

        private static bool SituationExists(CelestialBodyData body, ScienceSitutation situation)
        {
            return situation switch
            {
                ScienceSitutation.HighOrbit or ScienceSitutation.LowOrbit => true,
                ScienceSitutation.Atmosphere => body.hasAtmosphere,
                ScienceSitutation.Splashed => body.hasOcean,
                ScienceSitutation.Landed => body.hasSolidSurface,
                _ => false
            };
        }

        /// <summary>
        /// Body, situation and region scalars, matching how the game values a report.
        /// </summary>
        private static float GetLocationScalar(CelestialBodyScienceRegionsData scienceData, ResearchLocation location)
        {
            CBSituationData situationData = scienceData.SituationData;
            float situationScalar = location.ScienceSituation switch
            {
                ScienceSitutation.HighOrbit => situationData.HighOrbitScalar,
                ScienceSitutation.LowOrbit => situationData.LowOrbitScalar,
                ScienceSitutation.Atmosphere => situationData.AtmosphereScalar,
                ScienceSitutation.Splashed => situationData.SplashedScalar,
                ScienceSitutation.Landed => situationData.LandedScalar,
                _ => 1f
            };

            float regionScalar = 1f;
            if (location.RequiresRegion)
            {
                ScienceRegionDefinition region = scienceData.Regions?.FirstOrDefault(r => r.Id == location.ScienceRegion);
                if (region != null)
                {
                    // Orbits have no per-region scalar
                    regionScalar = location.ScienceSituation switch
                    {
                        ScienceSitutation.Atmosphere => region.AtmosphereScalar,
                        ScienceSitutation.Splashed => region.SplashedScalar,
                        ScienceSitutation.Landed => region.LandedScalar,
                        _ => 1f
                    };
                }
            }

            return situationData.CelestialBodyScalar * situationScalar * regionScalar;
        }

        private static Dictionary<string, CompletedResearchReport> GetSubmittedReports(GameInstance game)
        {
            var reports = new Dictionary<string, CompletedResearchReport>();
            if (!game.SessionManager.TryGetMyAgencySubmittedResearchReports(out List<CompletedResearchReport> submitted))
                return reports;

            // Keep the best submission if the same report was stored more than once
            foreach (CompletedResearchReport report in submitted)
            {
                if (!reports.TryGetValue(report.ResearchReportKey, out CompletedResearchReport existing)
                    || report.FinalScienceValue > existing.FinalScienceValue)
                    reports[report.ResearchReportKey] = report;
            }

            return reports;
        }

        private static float GetScienceMultiplier(GameInstance game)
        {
            return game.SessionManager.TryGetDifficultyOptionState("ScienceRewards", out float multiplier) ? multiplier : 1f;
        }

        /// <summary>
        /// Experiments hosted by parts the player has unlocked in the tech tree.
        /// </summary>
        private static HashSet<string> GetUnlockedExperimentIds(GameInstance game)
        {
            var unlocked = new HashSet<string> { SurfaceSurveyExperimentId };

            bool everythingUnlocked = !GameManager.Instance.GameModeManager.IsGameModeFeatureEnabled("SciencePartUnlock")
                || game.CheatSystem.GetUnlockAllParts();
            IReadOnlyDictionary<string, string> partToTechNode = game.ScienceManager.TechNodeDataStore?.PartIDLookup;

            foreach (PartCore part in game.Parts.AllParts())
            {
                if (!everythingUnlocked && !IsPartUnlocked(game, partToTechNode, part.data.partName)) continue;
                if (part.data.serializedPartModules == null) continue;

                foreach (SerializedPartModule module in part.data.serializedPartModules)
                {
                    if (module.ModuleData == null) continue;

                    foreach (SerializedModuleData moduleData in module.ModuleData)
                    {
                        if (moduleData.DataObject is not Data_ScienceExperiment science || science.Experiments == null) continue;

                        foreach (ExperimentConfiguration configuration in science.Experiments)
                        {
                            if (!string.IsNullOrEmpty(configuration.ExperimentDefinitionID))
                                unlocked.Add(configuration.ExperimentDefinitionID);
                        }
                    }
                }
            }

            return unlocked;
        }

        private static bool IsPartUnlocked(GameInstance game, IReadOnlyDictionary<string, string> partToTechNode, string partName)
        {
            // Redux tracks unlocked tech nodes per agency; ScienceManager.IsNodeUnlocked checks the player's agency
            return partToTechNode != null
                && partToTechNode.TryGetValue(partName, out string techNode)
                && !string.IsNullOrEmpty(techNode)
                && game.ScienceManager.IsNodeUnlocked(techNode);
        }
    }
}
