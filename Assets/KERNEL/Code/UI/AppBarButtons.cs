using System;
using KSP.UI.Binding;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace KERNEL.UI
{
    /// <summary>
    /// K.E.R.N.E.L's buttons in Redux's app tray, in flight (and map view) and in the VAB, which open and close the
    /// ledger window. Registered through Redux's <see cref="Appbar"/>, which puts mods' buttons in the app tray and
    /// hides them in campaigns where K.E.R.N.E.L is switched off. They can be left out with the "Show in app tray"
    /// setting, the window then being opened with its keyboard shortcut.
    /// </summary>
    internal static class AppBarButtons
    {
        private const string FlightId = "BTN-KERNEL-Flight";
        private const string VabId = "BTN-KERNEL-OAB";

        /// <summary>
        /// Adds the buttons, which appear the next time a toolbar is built (on entering flight or the VAB).
        /// </summary>
        public static void Register(string modId, Sprite icon, Action<bool> onToggled)
        {
            // The text is a localization key, as Redux's own buttons use, so the tooltip is translated
            Appbar.RegisterAppButton(modId, Localization.Title, FlightId, icon, onToggled);
            Appbar.RegisterOABAppButton(modId, Localization.Title, VabId, icon, onToggled);
        }

        public static void Unregister()
        {
            Appbar.Unregister(FlightId);
            Appbar.Unregister(VabId);
        }

        /// <summary>
        /// Shows the buttons pressed while the window is open, as Redux's own windows keep theirs in step.
        /// </summary>
        public static void SetPressed(bool pressed)
        {
            foreach (string id in new[] { FlightId, VabId })
                GameObject.Find(id)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(pressed);
        }
    }
}
