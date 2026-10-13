using System;
using ReduxLib.Configuration;

namespace KERNEL
{
    /// <summary>
    /// KERNEL's settings, on its own K.E.R.N.E.L page in the game's settings menu (the game gives each mod with
    /// settings a page of its own). Declared the way Redux declares its own: entries in a localized section, each
    /// wrapped in a <see cref="ConfigValue{T}"/> with localized names and descriptions. Bound in OnPreInitialized,
    /// since the page is only made for mods whose settings exist when the menu is set up.
    /// </summary>
    public static class KernelSettings
    {
        private const string SpoilersSection = "Spoilers";

        private static ConfigValue<bool> _showSpoilers;

        /// <summary>
        /// Whether the ledger shows biomes not yet visited by name (instead of "?????") and lists experiments
        /// not yet unlocked. Off by default.
        /// </summary>
        public static bool ShowSpoilers => _showSpoilers?.Value ?? false;

        /// <summary>
        /// Raised when a setting that changes what the ledger shows is changed.
        /// </summary>
        public static event Action Changed;

        public static void Bind(IConfigFile config)
        {
            if (config == null || _showSpoilers != null) return;

            IConfigSection spoilers = config.GetOrCreateSection(SpoilersSection, "KERNEL/Settings/Spoilers");
            _showSpoilers = new ConfigValue<bool>(spoilers.BindEntry("Show Spoilers", false,
                "Show biomes you haven't visited by name, and list experiments you haven't unlocked yet.", null,
                "KERNEL/Settings/ShowSpoilers", "KERNEL/Settings/Description/ShowSpoilers"));
            _showSpoilers.RegisterCallback((_, _) => Changed?.Invoke());
        }
    }
}
