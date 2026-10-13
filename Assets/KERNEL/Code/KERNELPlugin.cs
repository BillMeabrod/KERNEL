using System.Collections.Generic;
using KERNEL.Ledger;
using KERNEL.UI;
using KSP.Game;
using KSP.Messages;
using Redux.ExtraModTypes;
using SpaceWarp2.UI.Backend.UI.Appbar;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL
{
    public class KERNELPlugin : KerbalMod
    {
        // Addressable keys, set on the assets in the KERNEL Addressables group
        // Toolbar button icon: 48px like the stock toolbar icons, which fill their whole canvas
        private const string ToolbarIconAddress = "KERNEL/toolbar.png";
        private const string WindowAddress = "KERNEL/LedgerWindow.uxml";

        public static KERNELPlugin Instance { get; private set; }

        public LedgerWindowController LedgerWindow { get; private set; }

        public LedgerService Ledger { get; private set; }

        // Flight and map view are the same flight, so the window stays open between them
        private static readonly HashSet<GameState> FlightStates = new() { GameState.FlightView, GameState.Map3DView };

        private Sprite _toolbarIcon;

        public override void OnPreInitialized()
        {
            ResearchTab.RegisterPatch(PM);

            // Generated planet art reads the bodies' surfaces while a save loads, not when the map opens
            SpaceWarp2.API.Loading.SaveLoad.AddFlowActionToCampaignLoadAfter(new PrepareBodyArt(), PrepareBodyArt.After);
        }

        public override void OnInitialized()
        {
            Instance = this;
            Ledger = gameObject.AddComponent<LedgerService>();
            Ledger.Initialize(SWLogger);
            Ledger.Rebuilt += roots => LedgerWindow?.ShowLedger(roots);

            Messages?.PersistentSubscribe<GameStateChangedMessage>(OnGameStateChanged);
            Messages?.PersistentSubscribe<GameLoadFinishedMessage>(message => PrepareBodyArt.CheckRan(message, SWLogger));

            // Redux builds each toolbar fresh on entering its scene, then raises these
            if (IAppbarBackend.Instance != null)
            {
                IAppbarBackend.Instance.AppBarOABSubscriber.AddListener(AddVabButton);
                IAppbarBackend.Instance.AppBarInFlightSubscriber.AddListener(AddFlightButton);
            }
            else
            {
                SWLogger.LogError("App bar backend isn't available; the toolbar buttons won't be shown");
            }

            Assets.Load<VisualTreeAsset>(WindowAddress, CreateWindow);
            Assets.Load<Texture2D>(ToolbarIconAddress, texture => _toolbarIcon = CreateIcon(texture, ToolbarIconAddress));
        }

        private void OnGameStateChanged(MessageCenterMessage message)
        {
            if (message is GameStateChangedMessage stateChanged)
            {
                // Like stock windows, close when changing scene (building), including changes without a curtain
                if (!(FlightStates.Contains(stateChanged.PreviousState) && FlightStates.Contains(stateChanged.CurrentState)))
                    LedgerWindow?.SetOpen(false);
            }
        }

        private void AddVabButton()
        {
            if (_toolbarIcon != null) ToolbarButtons.AddToVab(_toolbarIcon, SetWindowOpen, SWLogger);
        }

        private void AddFlightButton()
        {
            if (_toolbarIcon != null) ToolbarButtons.AddToFlight(_toolbarIcon, SetWindowOpen, SWLogger);
        }

        private void Update()
        {
            // Like Redux's tracking station panel, get out of the way as soon as the loading curtain starts
            // closing; the scene change itself is only announced once loading is done
            if (LedgerWindow != null && LedgerWindow.IsOpen && Game != null && Game.UI != null && Game.UI.GetCurtainVisibility())
                LedgerWindow.SetOpen(false);
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
            LedgerWindow.Initialize(renderer);
            LedgerWindow.OpenChanged += isOpen => Ledger.SetShown(LedgerWindow, isOpen);
            if (Ledger.Roots.Count > 0) LedgerWindow.ShowLedger(Ledger.Roots);
        }

        private Sprite CreateIcon(Texture2D texture, string address)
        {
            if (texture == null)
            {
                SWLogger.LogError($"Could not load toolbar icon '{address}'");
                return null;
            }

            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
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
