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

        public ScienceLedger Ledger { get; private set; }

        // Flight and map view are the same flight, so the window stays open between them
        private static readonly HashSet<GameState> FlightStates = new() { GameState.FlightView, GameState.Map3DView };

        // Batches bursts of events (several reports scored at once) into one rebuild
        private const float RebuildDelay = 0.5f;

        private GalaxyOrbits _orbits;

        private Sprite _toolbarIcon;

        // Set by events that change the ledger; rebuilt while the window is open, otherwise when it next opens
        private bool _ledgerStale;
        private float _rebuildAt;

        public override void OnInitialized()
        {
            Instance = this;
            Ledger = new ScienceLedger(SWLogger);

            if (Messages != null)
            {
                Messages.PersistentSubscribe<GameLoadFinishedMessage>(OnGameLoadFinished);
                // Scoring changes earned science and can reveal a "?????" biome
                Messages.PersistentSubscribe<ResearchReportScoredMessage>(MarkLedgerStale);
                // New tech can unlock experiments. Only whole tiers send a message, but nodes can only be unlocked
                // on the R&D screen, so the scene change when leaving it covers single nodes
                Messages.PersistentSubscribe<TechTierUnlockedMessage>(MarkLedgerStale);
                Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameStateChanged);
                // Possible science scales with the difficulty's science rewards
                Messages.PersistentSubscribe<DifficultyOptionStatesChangedMessage>(MarkLedgerStale);
            }
            else
            {
                SWLogger.LogError("Message center isn't available; the ledger won't update");
            }

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

        private void OnGameStateChanged(MessageCenterMessage message)
        {
            if (message is GameStateChangedMessage stateChanged)
            {
                // Like stock windows, close when changing scene (building), including changes without a curtain
                if (!(FlightStates.Contains(stateChanged.PreviousState) && FlightStates.Contains(stateChanged.CurrentState)))
                    LedgerWindow?.SetOpen(false);
            }

            MarkLedgerStale(message);
        }

        private void AddVabButton()
        {
            if (_toolbarIcon != null) ToolbarButtons.AddToVab(_toolbarIcon, SetWindowOpen, SWLogger);
        }

        private void AddFlightButton()
        {
            if (_toolbarIcon != null) ToolbarButtons.AddToFlight(_toolbarIcon, SetWindowOpen, SWLogger);
        }

        private void MarkLedgerStale(MessageCenterMessage message)
        {
            // Nothing to refresh before the first game load
            if (_orbits == null) return;

            if (!_ledgerStale) _rebuildAt = Time.unscaledTime + RebuildDelay;
            _ledgerStale = true;
        }

        private void Update()
        {
            // Like Redux's tracking station panel, get out of the way as soon as the loading curtain starts
            // closing; the scene change itself is only announced once loading is done
            if (LedgerWindow != null && LedgerWindow.IsOpen && Game != null && Game.UI != null && Game.UI.GetCurtainVisibility())
                LedgerWindow.SetOpen(false);

            if (_ledgerStale && LedgerWindow != null && LedgerWindow.IsOpen && Time.unscaledTime >= _rebuildAt)
                RebuildLedger();
        }

        private void RebuildLedger()
        {
            _ledgerStale = false;
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
            LedgerWindow.Initialize(renderer);
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

            if (isOpen && _ledgerStale) RebuildLedger();
            LedgerWindow.SetOpen(isOpen);
        }
    }
}
