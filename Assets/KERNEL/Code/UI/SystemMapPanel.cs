using System;
using KSP.Game;
using KSP.Game.Science;
using KSP.Messages;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// The UI Toolkit window the system map is drawn in. Built the way the R&D sidebar is (a UitkForKsp2 window
    /// parented to the R&D screen, with localization and UI sounds, following the game state), and laid over the
    /// tech tree's view, clipped to it. Inside, the map's page follows where the stock layout puts the map's
    /// anchor, an empty rect the prefab patch adds after the tech tree, so it slides in and out with the tree.
    /// </summary>
    internal class SystemMapPanel : MonoBehaviour
    {
        private const string UxmlAddress = "KERNEL/SystemMap.uxml";

        private PanelRenderer _window;
        private VisualElement _viewport;
        private readonly UguiRect _viewportRect = new();
        private readonly UguiRect _pageRect = new();
        private Rect _viewportAt, _pageAt;
        private Vector2 _pageSize;
        private bool _pageShown = true;

        // The R&D screen stays alive when another building is opened; only the game state says it's showing
        private bool _inResearch = true;
        private SubscriptionHandle _onGameStateChange;

        /// <summary>
        /// The map's page, which its content goes in; null until the window is up.
        /// </summary>
        public VisualElement Page { get; private set; }

        /// <summary>
        /// Raised once the window is up and <see cref="Page"/> can be filled.
        /// </summary>
        public event Action Ready;

        /// <summary>
        /// Creates the window over the tech tree's view, following the anchor.
        /// </summary>
        public static SystemMapPanel Attach(RectTransform anchor)
        {
            var panel = anchor.gameObject.AddComponent<SystemMapPanel>();
            panel._viewportRect.Target = anchor.parent as RectTransform;
            panel._pageRect.Target = anchor;
            panel._onGameStateChange = GameManager.Instance.Game.Messages.Subscribe<GameStateChangedMessage>(panel.OnGameStateChanged);
            GameManager.Instance.Assets.Load<VisualTreeAsset>(UxmlAddress, panel.CreateWindow);
            return panel;
        }

        private void CreateWindow(VisualTreeAsset uxml)
        {
            if (uxml == null || this == null) return;

            var screen = GetComponentInParent<RDCenterUIController>(true);
            WindowOptions options = WindowOptions.Default;
            options.WindowId = "KERNEL_SystemMap";
            options.Parent = screen != null ? screen.transform : transform;
            options.UseStockScale = true;
            options.BringToFrontOnPointerDown = false;
            options.MoveOptions = new MoveOptions { IsMovingEnabled = false, CheckScreenBounds = false };
            _window = Window.Create(options, uxml);

            _window.OnPanelRoot(root =>
            {
                _viewport = root.Q("kernel-map");
                Page = root.Q("page");
                _viewportAt = _pageAt = default;
                Ready?.Invoke();
            });
            _window.EnableLocalization();
            _window.EnableUiSounds();
            // As the sidebar does: the window's container covers the screen, and the map is placed within it
            _window.OnWindowRoot(StretchWindowContainer);

            if (!_inResearch) _window.Hide();
        }

        private static void StretchWindowContainer(VisualElement windowRoot)
        {
            VisualElement container = windowRoot?.parent;
            if (container == null) return;
            container.style.width = new StyleLength(Length.Percent(100f));
            container.style.height = new StyleLength(Length.Percent(100f));
        }

        private void OnDestroy()
        {
            if (GameManager.Instance?.Game?.Messages != null && !GameManager.Instance.Game.ShutdownInProgress)
                GameManager.Instance.Game.Messages.Unsubscribe(ref _onGameStateChange);
        }

        private void OnGameStateChanged(MessageCenterMessage message)
        {
            _inResearch = ((GameStateChangedMessage)message).CurrentState == GameState.ResearchAndDevelopment;
            if (_window == null) return;
            if (_inResearch) _window.Show();
            else _window.Hide();
        }

        /// <summary>
        /// Lays the window over the tech tree's view and the page where the anchor is now; call after the anchor
        /// has been moved for the frame. Gives the page's size, and false while the page is out of the view (on
        /// the tier pages), when it's left out entirely so nothing of it is drawn or takes the pointer.
        ///
        /// The page is laid out in the canvas's units, as the stock screen and the uGUI map were, and scaled onto
        /// the panel: its size is the anchor's own (steady while it slides, unlike its on-screen corners), so
        /// sizes, gaps and page breaks come out as they do in the canvas.
        /// </summary>
        public bool Place(out Vector2 pageSize)
        {
            pageSize = _pageSize;
            if (!_inResearch || _viewport?.panel == null) return false;

            if (_viewportRect.TryGetPanelRect(_viewport.panel, out Rect viewport) && viewport != _viewportAt)
            {
                _viewportAt = viewport;
                UguiRect.Place(_viewport, viewport);
            }

            if (!_pageRect.TryGetPanelRect(_viewport.panel, out Rect page)) return false;
            pageSize = _pageRect.Target.rect.size;

            bool shown = page.Overlaps(_viewportAt);
            if (shown != _pageShown)
            {
                _pageShown = shown;
                Page.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!shown || (page == _pageAt && pageSize == _pageSize)) return shown;

            _pageAt = page;
            _pageSize = pageSize;
            // The page sits inside the view, so it's placed relative to it, at its canvas size scaled to the panel
            UguiRect.Place(Page, new Rect(page.position - _viewportAt.position, pageSize));
            float scale = pageSize.x > 0f ? page.width / pageSize.x : 1f;
            Page.style.scale = new Scale(new Vector3(scale, scale, 1f));
            return true;
        }
    }
}
