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
