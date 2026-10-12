using KERNEL.Ledger;
using KERNEL.UI;
using KSP.Messages;
using Redux.ExtraModTypes;
using SpaceWarp2.UI.API.Appbar;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL
{
    public class KERNELPlugin : KerbalMod
    {
        // Addressable keys, set on the assets in the KERNEL Addressables group
        private const string IconAddress = "KERNEL/icon.png";
        private const string WindowAddress = "KERNEL/LedgerWindow.uxml";

        // App Bar button IDs, one per scene the button appears in. The KSC gets an R&D page button instead.
        private const string FlightButtonId = "BTN-KERNEL-Flight";
        private const string OabButtonId = "BTN-KERNEL-OAB";

        public static KERNELPlugin Instance { get; private set; }

        public LedgerWindowController LedgerWindow { get; private set; }

        public ScienceLedger Ledger { get; private set; }

        private GalaxyOrbits _orbits;

        public override void OnInitialized()
        {
            Instance = this;
            Ledger = new ScienceLedger(SWLogger);

            if (Messages != null)
                Messages.PersistentSubscribe<GameLoadFinishedMessage>(OnGameLoadFinished);
            else
                SWLogger.LogError("Message center isn't available; the ledger won't update");

            Assets.Load<VisualTreeAsset>(WindowAddress, CreateWindow);
            Assets.Load<Texture2D>(IconAddress, RegisterAppBarButtons);
        }

        private void OnGameLoadFinished(MessageCenterMessage message)
        {
            // Orbit parents and distances come from the save's galaxy definition, which only changes with the galaxy
            string galaxyKey = Game.SessionManager.ActiveGalaxyDefinitionKey;
            if (_orbits != null && _orbits.Key == galaxyKey)
            {
                RebuildLedger();
                return;
            }

            Assets.Load<TextAsset>(galaxyKey, asset =>
            {
                if (asset == null)
                {
                    SWLogger.LogError($"Could not load galaxy definition '{galaxyKey}'; bodies won't be nested or ordered");
                    _orbits = new GalaxyOrbits(galaxyKey, null);
                }
                else
                {
                    _orbits = new GalaxyOrbits(galaxyKey, asset.text);
                    Assets.ReleaseAsset(asset);
                }

                RebuildLedger();
            });
        }

        private void RebuildLedger()
        {
            if (!Ledger.Rebuild(Game, _orbits)) return;

            SWLogger.LogInfo($"Ledger built: {Ledger.Bodies.Count} bodies");
            LedgerWindow?.ShowLedger(Ledger.Roots);
        }

        private void CreateWindow(VisualTreeAsset uxml)
        {
            if (uxml == null)
            {
                SWLogger.LogError($"Could not load ledger window '{WindowAddress}'");
                return;
            }

            var options = new WindowOptions
            {
                WindowId = "KERNEL_LedgerWindow",
                // Created under the main canvas
                Parent = null,
                // F2 hides it with the rest of the UI
                IsHidingEnabled = true,
                // Follows the game's UI scale setting
                UseStockScale = true,
                DisableGameInputForTextFields = true,
                BringToFrontOnPointerDown = true,
                MoveOptions = new MoveOptions { IsMovingEnabled = true, CheckScreenBounds = true }
            };

            PanelRenderer renderer = Window.Create(options, uxml).EnableUiSounds();
            LedgerWindow = renderer.gameObject.AddComponent<LedgerWindowController>();
            LedgerWindow.Initialize(renderer, FlightButtonId, OabButtonId);
        }

        private void RegisterAppBarButtons(Texture2D icon)
        {
            if (icon == null)
            {
                SWLogger.LogError($"Could not load App Bar icon '{IconAddress}'");
                return;
            }

            string name = SWMetadata.Name;

            // Flight and Map view
            Appbar.RegisterAppButton(name, FlightButtonId, icon, SetWindowOpen);
            // VAB
            Appbar.RegisterOABAppButton(name, OabButtonId, icon, SetWindowOpen);

            SWLogger.LogInfo("Registered App Bar buttons");
        }

        private void SetWindowOpen(bool isOpen)
        {
            if (LedgerWindow == null)
            {
                SWLogger.LogWarning("Ledger window isn't loaded yet");
                return;
            }

            LedgerWindow.SetOpen(isOpen);
        }
    }
}
