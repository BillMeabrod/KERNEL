using I2.Loc;

namespace KERNEL
{
    /// <summary>
    /// Text shown to the player. KERNEL's own strings live in Copied/localizations/kernel_localization.csv,
    /// which SpaceWarp loads into the game's localization at startup.
    /// </summary>
    public static class Localization
    {
        public const string Title = "KERNEL/UI/Title";
        public const string UnknownArea = "KERNEL/UI/UnknownArea";
        public const string FullTitle = "KERNEL/UI/FullTitle";
        public const string BiomesExplored = "KERNEL/UI/BiomesExplored";
        public const string SciencePointsGained = "KERNEL/UI/SciencePointsGained";
        public const string BiomeColumn = "KERNEL/UI/BiomeColumn";
        public const string ScienceGainedColumn = "KERNEL/UI/ScienceGainedColumn";
        public const string FieldReportHint = "KERNEL/UI/FieldReportHint";
        public const string NoFieldReport = "KERNEL/UI/NoFieldReport";
        public const string Indecipherable = "KERNEL/UI/Indecipherable";
        public const string PreparingPlanetArt = "KERNEL/Loading/PreparingPlanetArt";

        /// <summary>
        /// Translates one of KERNEL's keys, showing the key itself if it's missing (as the game does).
        /// </summary>
        public static string Translate(string key) => Translate(key, key);

        public static string Translate(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            string text = LocalizationManager.GetTranslation(key);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}
