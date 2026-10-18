using System;
using SpaceWarp2.API.Configuration;
using SpaceWarp2.API.Input;
using UnityEngine;

namespace KERNEL
{
    /// <summary>
    /// KERNEL's settings, on its own K.E.R.N.E.L page in the game's settings menu (the game gives each mod with
    /// settings a page of its own). Declared the way Redux declares its own: entries in a localized section, each
    /// wrapped in a <see cref="ConfigValue{T}"/> with localized names and descriptions. Bound in OnPreInitialized,
    /// since the page is only made for mods whose settings exist when the menu is set up.
    ///
    /// They're kept per campaign, in SpaceWarp's campaign settings: changed from the pause menu, saved with the
    /// campaign, and back to their defaults in a new one. Loading a campaign sets each of them, which raises the
    /// same change events as changing them in the menu.
    /// </summary>
    public static class KernelSettings
    {
        private const string SpoilersSection = "Spoilers";
        private const string WindowSection = "Ledger Window";

        private static ConfigValue<bool> _showSpoilers;
        private static ConfigValue<bool> _showInAppBar;
        private static ConfigValue<KeyboardShortcut> _toggleWindow;

        /// <summary>
        /// Whether the ledger shows biomes not yet visited by name (instead of "?????") and lists experiments
        /// not yet unlocked. Off by default.
        /// </summary>
        public static bool ShowSpoilers => _showSpoilers?.Value ?? false;

        /// <summary>
        /// Whether K.E.R.N.E.L has buttons in Redux's app tray in flight and the VAB. On by default; with it off, the
        /// window is opened with <see cref="ToggleWindow"/>.
        /// </summary>
        public static bool ShowInAppBar => _showInAppBar?.Value ?? true;

        /// <summary>
        /// Opens and closes the ledger window in flight, map view and the VAB. Left Alt+K by default, rebindable
        /// on the settings page.
        /// </summary>
        public static KeyboardShortcut ToggleWindow => _toggleWindow?.Value ?? DefaultToggleWindow;

        private static readonly KeyboardShortcut DefaultToggleWindow = new(KeyCode.K, KeyCode.LeftAlt);

        /// <summary>
        /// Raised when a setting that changes what the ledger shows is changed.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when "Show in app tray" is changed.
        /// </summary>
        public static event Action<bool> ShowInAppBarChanged;

        public static void Bind(IConfigFile config)
        {
            if (config == null || _showSpoilers != null) return;

            IConfigSection spoilers = config.GetOrCreateSection(SpoilersSection, "KERNEL/Settings/Spoilers");
            _showSpoilers = new ConfigValue<bool>(spoilers.BindEntry("Show Spoilers", false,
                "Show biomes you haven't visited by name, and list experiments you haven't unlocked yet.", null,
                "KERNEL/Settings/ShowSpoilers", "KERNEL/Settings/Description/ShowSpoilers"));
            _showSpoilers.RegisterCallback((_, _) => Changed?.Invoke());

            IConfigSection window = config.GetOrCreateSection(WindowSection, "KERNEL/Settings/LedgerWindow");
            _showInAppBar = new ConfigValue<bool>(window.BindEntry("Show In App Bar", true,
                "Show K.E.R.N.E.L's button in the app tray in flight and the VAB.", null,
                "KERNEL/Settings/ShowInAppBar", "KERNEL/Settings/Description/ShowInAppBar"));
            _showInAppBar.RegisterCallback((_, shown) => ShowInAppBarChanged?.Invoke(shown));
            _toggleWindow = new ConfigValue<KeyboardShortcut>(window.BindEntry("Toggle Window", DefaultToggleWindow,
                "Opens and closes the ledger window in flight and the VAB.", null,
                "KERNEL/Settings/ToggleWindow", "KERNEL/Settings/Description/ToggleWindow"));
        }
    }
}
