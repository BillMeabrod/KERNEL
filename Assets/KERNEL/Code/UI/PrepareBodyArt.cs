using System;
using KSP.Game;
using KSP.Messages;
using ReduxLib.GameInterfaces;
using UnityEngine;
using ILogger = ReduxLib.Logging.ILogger;

namespace KERNEL.UI
{
    /// <summary>
    /// A save-loading step that reads the surfaces the system map's generated art is made from, so the map
    /// doesn't have to load planet models when it opens. Only bodies without shipped art need it, so with stock
    /// bodies alone it finishes straight away.
    /// </summary>
    internal class PrepareBodyArt : BaseFlowAction
    {
        // The save-loading step to run after; the game's bodies are all registered by then
        public const string After = "Initializing Session Data";

        // Steps are placed by name, so this stays the same in every language for other mods to run after; the
        // loading bar shows the translated description
        private const string StepName = "K.E.R.N.E.L: Preparing planet art";

        // Whether the step has run during the current load (see CheckRan)
        private static bool _ran;

        public PrepareBodyArt() : base(StepName, StepName)
        {
        }

        public override void DoAction(Action resolve, Action<string> reject)
        {
            _ran = true;
            try
            {
                // As stock steps do, show what's happening on the loading bar
                GameManager.Instance?.Game?.UI?.SetLoadingBarText(Localization.Translate(Localization.PreparingPlanetArt, Description));
                BodyArt.Preload(resolve);
            }
            catch (Exception e)
            {
                // Art can still be made when the map opens; never hold up loading the save
                Debug.LogException(e);
                resolve();
            }
        }

        /// <summary>
        /// Warns when a save finished loading without this step running. The step is placed after another one by
        /// name when the loading steps are put together, so if that name ever changes the step silently drops
        /// out. The map still works, but has to read planet surfaces when it opens.
        /// </summary>
        public static void CheckRan(MessageCenterMessage message, ILogger logger)
        {
            if (message is GameLoadFinishedMessage { IsSuccess: true } && !_ran)
                logger.LogWarning($"The '{StepName}' loading step didn't run: no loading step is named '{After}' any more. " +
                                  "Planet art will be prepared when the system map opens instead.");
            _ran = false;
        }
    }
}
