using System.Collections.Generic;
using KERNEL.Ledger;
using KERNEL.UI;
using KSP.Game;
using KSP.Messages;
using Redux.ExtraModTypes;
using SpaceWarp2.API.Loading;
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

        private const string LoadWindowStep = "K.E.R.N.E.L: Loading ledger window";

        public static KERNELPlugin Instance { get; private set; }

        public LedgerWindowController LedgerWindow { get; private set; }

        public LedgerService Ledger { get; private set; }

        // Flight and map view are the same flight, so the window stays open between them
        private static readonly HashSet<GameState> FlightStates = new() { GameState.FlightView, GameState.Map3DView };

        // Where the ledger window can be opened: flight, map view and the VAB
        private static readonly HashSet<GameState> WindowStates =
            new() { GameState.FlightView, GameState.Map3DView, GameState.VehicleAssemblyBuilder };

        private Sprite _toolbarIcon;
        private bool _assetsRequested;

        public override void OnPreInitialized()
        {
            ResearchTab.RegisterPatch(PM);
            BodyArt.Logger = SWLogger;

            // The game gives a mod its own page in the settings menu only if its settings exist when the menu is
            // set up, which is before OnInitialized
            KernelSettings.Bind(SWConfiguration);

            // Generated planet art reads the bodies' surfaces while a save loads, not when the map opens. Redux
            // only runs the step in campaigns where K.E.R.N.E.L is switched on
            SaveLoad.AddStepAfter(SWMetadata.Guid, PrepareBodyArt.StepName, StockFlow.CampaignLoad, PrepareBodyArt.After,
                (resolve, reject) => PrepareBodyArt.Run(resolve, reject, SWLogger));

            // Redux only lets a mod load its own assets in a campaign that has it switched on. Loading them in a
            // step of the campaign load, rather than OnCampaignLoaded, registers the app bar buttons before the
            // flight app bar is built
            SaveLoad.AddStepAfter(SWMetadata.Guid, LoadWindowStep, StockFlow.CampaignLoad, PrepareBodyArt.After,
                (resolve, reject) => LoadWindow(resolve));
        }

        public override void OnInitialized()
        {
            Instance = this;
            // KERNEL's views bind show and hide through these, so they're registered before any view loads
            KernelConverters.Register();
            Ledger = gameObject.AddComponent<LedgerService>();
            Ledger.Initialize(SWLogger);
            Ledger.Rebuilt += roots => LedgerWindow?.ShowLedger(roots);

            // Showing or hiding spoilers changes what every view of the ledger shows
            KernelSettings.Changed += Ledger.Invalidate;

            Messages?.PersistentSubscribe<GameStateChangedMessage>(OnGameStateChanged);
            Messages?.PersistentSubscribe<GameLoadFinishedMessage>(message => PrepareBodyArt.CheckRan(message, Enabled, SWLogger));

            // The app bar buttons can be turned off; they come and go the next time a toolbar is built
            KernelSettings.ShowInAppBarChanged += shown =>
            {
                if (shown) RegisterAppBarButtons();
                else AppBarButtons.Unregister();
            };
        }

        // Loaded once, by the first campaign with K.E.R.N.E.L switched on; both stay for later campaigns
        private void LoadWindow(System.Action done)
        {
            if (_assetsRequested)
            {
                done();
                return;
            }

            _assetsRequested = true;
            int pending = 2;
            void Loaded()
            {
                if (--pending == 0) done();
            }

            Assets.Load<VisualTreeAsset>(WindowAddress, uxml =>
            {
                CreateWindow(uxml);
                Loaded();
            });
            Assets.Load<Texture2D>(ToolbarIconAddress, texture =>
            {
                _toolbarIcon = CreateIcon(texture, ToolbarIconAddress);
                RegisterAppBarButtons();
                Loaded();
            });
        }

        // The next campaign may have K.E.R.N.E.L switched off. Redux's app bar hides the buttons itself there
        public override void OnCampaignUnloading()
        {
            LedgerWindow?.SetOpen(false);
        }

        private void RegisterAppBarButtons()
        {
            if (_toolbarIcon != null && KernelSettings.ShowInAppBar)
                AppBarButtons.Register(SWMetadata.Guid, _toolbarIcon, SetWindowOpen);
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

        private void Update()
        {
            // The keyboard shortcut opens and closes the window wherever it can be shown, as Redux's own windows
            // read theirs
            if (LedgerWindow != null && Enabled && KernelSettings.ToggleWindow.Down && Game?.GlobalGameState != null
                && WindowStates.Contains(Game.GlobalGameState.GetState()))
                SetWindowOpen(!LedgerWindow.IsOpen);

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
