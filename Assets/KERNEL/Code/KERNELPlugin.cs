using Redux.ExtraModTypes;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace KERNEL
{
    public class KERNELPlugin : KerbalMod
    {
        // Addressable key of the App Bar icon, set on the icon asset in the KERNEL Addressables group
        private const string IconAddress = "KERNEL/icon.png";

        // App Bar button IDs, one per scene the button appears in
        private const string FlightButtonId = "BTN-KERNEL-Flight";
        private const string OabButtonId = "BTN-KERNEL-OAB";
        private const string KscButtonId = "BTN-KERNEL-KSC";

        public static KERNELPlugin Instance { get; private set; }

        // Placeholder until the ledger window exists
        public bool IsWindowOpen { get; private set; }

        public override void OnInitialized()
        {
            Instance = this;
            Assets.Load<Texture2D>(IconAddress, RegisterAppBarButtons);
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
            // KSC: plain click, so toggle ourselves
            Appbar.RegisterKSCAppButton(name, KscButtonId, icon, () => SetWindowOpen(!IsWindowOpen));

            SWLogger.LogInfo("Registered App Bar buttons");
        }

        private void SetWindowOpen(bool isOpen)
        {
            IsWindowOpen = isOpen;
            SWLogger.LogInfo($"Ledger window {(isOpen ? "opened" : "closed")}");
        }
    }
}
