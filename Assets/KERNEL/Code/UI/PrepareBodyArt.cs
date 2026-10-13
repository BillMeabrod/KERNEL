using System;
using ReduxLib.GameInterfaces;
using UnityEngine;

namespace KERNEL.UI
{
    /// <summary>
    /// A save-loading step that reads the surfaces the system map's generated art is made from, so the map
    /// doesn't have to load planet models when it opens. Only bodies without shipped art need it, so with stock
    /// bodies alone it finishes straight away.
    /// </summary>
    internal class PrepareBodyArt : IFlowAction
    {
        // The save-loading step to run after; the game's bodies are all registered by then
        public const string After = "Initializing Session Data";

        public string Name => "K.E.R.N.E.L: Preparing planet art";

        public string Description => Name;

        public void DoAction(Action resolve, Action<string> reject)
        {
            try
            {
                BodyArt.Preload(resolve);
            }
            catch (Exception e)
            {
                // Art can still be made when the map opens; never hold up loading the save
                Debug.LogException(e);
                resolve();
            }
        }
    }
}
