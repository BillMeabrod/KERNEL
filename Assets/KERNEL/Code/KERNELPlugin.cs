using KERNEL.UI;
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

        public override void OnInitialized()
        {
            Instance = this;
            Assets.Load<VisualTreeAsset>(WindowAddress, CreateWindow);
            Assets.Load<Texture2D>(IconAddress, RegisterAppBarButtons);
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
