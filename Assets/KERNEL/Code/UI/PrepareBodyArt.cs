using System;
using KSP.Game;
using KSP.Messages;
using ILogger = SpaceWarp2.API.Logging.ILogger;

namespace KERNEL.UI
{
    /// <summary>
    /// A save-loading step that reads what the system map's art is made from, so the map doesn't have to load
    /// planet models when it opens: the surfaces of bodies without shipped art, the light of stars without it, and
    /// the ring colours of every ringed body (stock Dres included). Registered with Redux's
    /// <c>SaveLoad.AddStepAfter</c>, which only runs it in campaigns where K.E.R.N.E.L is switched on.
    /// </summary>
    internal static class PrepareBodyArt
    {
        // The save-loading step to run after: it registers the game's bodies (from their JSON, with any added by
        // mods), which this step goes through. Any earlier and there are none yet, so nothing gets prepared
        public const string After = "Loading Celestial Body Data";

        // Steps are placed by name, so this stays the same in every language for other mods to run after; the
        // loading bar shows the translated text
        public const string StepName = "K.E.R.N.E.L: Preparing planet art";

        // Whether the step has run during the current load (see CheckRan)
        private static bool _ran;

        public static void Run(Action resolve, Action<string> reject, ILogger logger)
        {
            _ran = true;
            try
            {
                // As stock steps do, show what's happening on the loading bar
                GameManager.Instance?.Game?.UI?.SetLoadingBarText(Localization.Translate(Localization.PreparingPlanetArt, StepName));
                // Each campaign's art is made from its own bodies; the last campaign's is freed first
                BodyArt.FreeRetired();
                BodyArt.Preload(resolve);
            }
            catch (Exception e)
            {
                // Art can still be made when the map opens; never hold up loading the save
                logger?.LogError(e);
                resolve();
            }
        }

        /// <summary>
        /// Warns when a save finished loading without this step running though K.E.R.N.E.L is on. The step is
        /// placed after another one by name, so if that name ever changes the step silently drops out, with the
        /// window's step placed after the same one. The map still works, but has to read planet surfaces when it
        /// opens, and the plugin loads the window once the save has loaded.
        /// </summary>
        public static void CheckRan(MessageCenterMessage message, bool enabled, ILogger logger)
        {
            if (message is GameLoadFinishedMessage { IsSuccess: true } && enabled && !_ran)
                logger.LogWarning($"K.E.R.N.E.L's loading steps didn't run: no loading step is named '{After}' any more. " +
                                  "Planet art will be prepared when the system map opens instead, and the ledger window loads after the save.");
            _ran = false;
        }
    }
}
