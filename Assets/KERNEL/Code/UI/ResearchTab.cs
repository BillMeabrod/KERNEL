using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSP.Game;
using KSP.Game.Science;
using KSP.Input;
using KSP.UI;
using PatchManager.CSharpPatching;
using PatchManager.PrefabPatching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KERNEL.UI
{
    /// <summary>
    /// K.E.R.N.E.L's tab on the R&D screen, after the tech tier tabs. A prefab patch adds a starfield past the end
    /// of the tier backgrounds with this component on it, so everything is created and destroyed with the screen.
    /// Redux builds the tier tabs from the tech tree at run time, so ours is a copy of one of them, in the same
    /// toggle group: it looks, sounds and switches like the stock tabs, and rejoins the row when Redux rebuilds it.
    ///
    /// Our page is one more step past the last tier. The stock scroll still runs the tiers; past the last one
    /// this component carries the tech tree and the backgrounds on out to the starfield, by a position that
    /// the tab, the scroll input and the tier scrollbar all move, just as they move the stock scroll.
    /// </summary>
    public class ResearchTab : MonoBehaviour
    {
        private const string PrefabAddress = "RDCenterUI.prefab";
        private const string PatchName = "ResearchTab";
        private const string TabBarPath = "Container/TechTree/TierScroll/TierSelectionContainer";
        private const string ScrollbarPath = "Container/TechTree/ELE-Scrollbar-Horizontal";
        private const string TabName = "TierSelection-KERNEL";
        private const string StarfieldAddress = "KERNEL/starfield.png";


        // The starfield overlaps the last tier background slightly so no hairline shows between them at
        // fractional pixel positions; its edge continues that background, so the overlap doesn't show
        private const float StarfieldOverlap = 2f;

        // Space around a tab's title, as Redux sizes the tier tabs
        private const float TabMargin = 48f;

        // Set by the prefab patch. Only stock objects are referenced: Patch Manager loses a patch's own ID when it
        // reuses a cached plan, so references to objects the patch added itself fail on later runs
        [SerializeField] private RDCenterUIController _controller;
        [SerializeField] private RectTransform _tabBar;
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _treeContent;
        [SerializeField] private Scrollbar _scrollbar;
        [SerializeField] private RectTransform _scrollArea;

        private ToggleExtended _tab;
        private ToggleExtended _lastTier;
        private ResearchSidebar _sidebar;
        // Whether this page has told the ledger it's showing it
        private bool _showingLedger;
        private RectTransform _starfieldRect;
        private RawImage _starfield;
        private RDInputDefinition _input;
        private int _tiers;
        private readonly List<ToggleExtended> _tierTabs = new();

        // The tab row's child count when ours last joined it, and the row width we last gave it
        private int _syncedChildren = -1;
        private float _barWidthWritten = float.NaN;

        // The scrollbar track is split into one section per tier plus ours; the stock handle keeps to the tiers'
        private float _tierShare = 1f;

        // How far past the last tier we are: 0 on the last tier, 1 on our page
        private float _page;

        // Scroll input, in stock scroll units per second
        private float _scrollInput;
        private float _scrollSpeed = 1f;

        // How long a tab click slides for: the stock scroll's time between tiers
        private float _slideTime = 0.25f;

        // A tab click animates to a page instead of following input
        private bool _animating;
        private float _animTime, _animFromPage, _animToPage, _animFromScroll;

        // Where on the scrollbar handle the pointer took hold of it, if dragging
        private float? _grab;

        private readonly SlidingRect _backgroundSlide = new();
        private readonly SlidingRect _treeSlide = new();

        // Made once, so the per-frame slide allocates nothing
        private Func<float, float> _backgroundEnd, _treeEnd;

        // How far right of the tier backgrounds' position their end is, as last read
        private float _backgroundEndOffset;

        /// <summary>
        /// Registers the R&D screen patch. Patch Manager only accepts prefab patches before the game starts
        /// loading, so this runs from the plugin's OnPreInitialized.
        /// </summary>
        public static void RegisterPatch(PmScope pm)
        {
            // Fills the screen like the tier backgrounds; placed and moved by the ResearchTab on it
            PrefabPatchObjectFragment starfield = new PrefabPatchObjectBuilder("starfield", "KERNEL-Starfield")
                .RectTransform()
                .Rect(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f))
                .Component(PrefabPatchComponentBuilder.For<RawImage>("starfield-image")
                    .Reference("m_Texture", PrefabPatchBuilder.Addressable(StarfieldAddress, typeof(Texture2D)))
                    .Value("m_RaycastTarget", PrefabPatchValue.FromBoolean(false))
                    .Build())
                .Component(PrefabPatchComponentBuilder.For<ResearchTab>("research-tab")
                    .Reference("_controller", Target(PrefabPatchBuilder.ComponentAt<RDCenterUIController>("")))
                    .Reference("_tabBar", Target(PrefabPatchBuilder.ComponentAt<RectTransform>(TabBarPath)))
                    .Reference("_background", Target(PrefabPatchBuilder.ComponentAt<RectTransform>("Container/BackgroundContainer")))
                    .Reference("_treeContent", Target(PrefabPatchBuilder.ComponentAt<RectTransform>("Container/TechTree/TreeContainer/TreeContent")))
                    .Reference("_scrollbar", Target(PrefabPatchBuilder.ComponentAt<Scrollbar>(ScrollbarPath)))
                    .Reference("_scrollArea", Target(PrefabPatchBuilder.ComponentAt<RectTransform>(ScrollbarPath + "/Sliding Area")))
                    .Build())
                .Build();

            // Our page's content, inside the tech tree's view so it slides in with the tree and is clipped like it
            PrefabPatchObjectFragment systemMap = new PrefabPatchObjectBuilder("system-map", "KERNEL-SystemMap")
                .RectTransform()
                .Rect(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f))
                .Component(PrefabPatchComponentBuilder.For<SystemMap>("system-map")
                    .Reference("_treeContent", Target(PrefabPatchBuilder.ComponentAt<RectTransform>("Container/TechTree/TreeContainer/TreeContent")))
                    .Build())
                .Build();

            pm.PatchPrefab(PatchName, PrefabAddress)
                .AddObject("add-starfield", PrefabPatchBuilder.GameObjectAt("Container"), starfield)
                .AddObject("add-system-map", PrefabPatchBuilder.GameObjectAt("Container/TechTree/TreeContainer"), systemMap)
                // Lets the scrollbar reach our page; connected to the tab when the screen starts
                .AddComponent(
                    "research-scrollbar",
                    PrefabPatchBuilder.GameObjectAt(ScrollbarPath),
                    PrefabPatchComponentBuilder.For<ResearchScrollbar>("research-scrollbar").Build())
                .Register();

        }

        private static FieldInfo ControllerField(string name) =>
            typeof(RDCenterUIController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static PrefabPatchObjectReference Target(PrefabPatchObjectTarget target) =>
            PrefabPatchBuilder.TargetReference(target);

        private void Awake()
        {
            // Patched-in objects are added last, which would draw it over the whole screen: put it straight
            // after the tier backgrounds instead
            _starfieldRect = (RectTransform)transform;
            _backgroundEnd = BackgroundEndX;
            _treeEnd = TreeEndX;
            if (_background != null) _starfieldRect.SetSiblingIndex(_background.GetSiblingIndex() + 1);

            // Hidden until it's placed past the last tier, which needs the tiers to exist
            _starfield = GetComponent<RawImage>();
            if (_starfield != null) _starfield.enabled = false;

            if (_scrollbar != null && _scrollbar.TryGetComponent(out ResearchScrollbar scrollbar)) scrollbar.Tab = this;
        }

        private void Start()
        {
            // The tab leans on how Redux builds this screen, which has no way for mods to add a page. If an update
            // moves what it hooks into, the page stays hidden and says why
            string missing = string.Join(", ", new (string name, UnityEngine.Object found)[]
                {
                    (nameof(_controller), _controller), (nameof(_tabBar), _tabBar), (nameof(_background), _background),
                    (nameof(_treeContent), _treeContent), (nameof(_scrollbar), _scrollbar), (nameof(_scrollArea), _scrollArea)
                }.Where(part => part.found == null).Select(part => part.name));
            if (missing.Length > 0)
            {
                KERNELPlugin.Instance?.SWLogger.LogWarning($"The R&D page is hidden: the R&D screen has changed ({missing} not found)");
                return;
            }

            // Scroll input at the same speed as the stock tier scroll, alongside the stock handler, and tab clicks
            // sliding over the stock scroll's time between tiers. Both are private, so they're read by reflection
            // (see ScienceLedger); if a game update renames them, the defaults above stand
            if (_controller != null)
            {
                if (ControllerField("_techTreeInputTravelSpeed")?.GetValue(_controller) is float speed) _scrollSpeed = speed;
                if (ControllerField("_scrollToTierTime")?.GetValue(_controller) is float slide) _slideTime = slide;
            }

            if (GameManager.Instance.Game.InputManager.TryGetInputDefinition(out _input))
                _input.BindAction<float>(GameManager.Instance.Game.Input.RD.ScrollTechTree.name, OnScrollInput);

            // The sidebar shows the body picked on the map while our page shows
            var map = _treeContent.parent.GetComponentInChildren<SystemMap>(true);
            if (_controller != null) _sidebar = ResearchSidebar.Attach(_controller, map);
        }

        // Redux builds the tier tabs from the tech tree, and builds them again whenever the tree changes, appending
        // the new ones after ours. Ours joins them, or goes back to the end, whenever the row changes. False while
        // there are no tiers yet.
        private bool SyncTab()
        {
            if (_tab != null && _tabBar.childCount == _syncedChildren) return true;

            _tierTabs.Clear();
            foreach (Transform child in _tabBar)
            {
                // Tiers being cleared are switched off before they're destroyed at the end of the frame
                if (child.gameObject.activeSelf && (_tab == null || child != _tab.transform)
                    && child.TryGetComponent(out ToggleExtended tier))
                    _tierTabs.Add(tier);
            }
            if (_tierTabs.Count == 0) return false;

            _tiers = _tierTabs.Count;
            _tierShare = _tiers / (_tiers + 1f);
            _lastTier = _tierTabs[_tiers - 1];
            if (_tab == null) CreateTab();
            _tab.transform.SetAsLastSibling();
            _syncedChildren = _tabBar.childCount;
            _barWidthWritten = float.NaN;
            return true;
        }

        private void CreateTab()
        {
            // Copy a tab that isn't selected: a selected copy would take the selection from it on joining the group
            ToggleExtended template = _tierTabs.Find(tier => !tier.isOn) ?? _lastTier;
            _tab = Instantiate(template, _tabBar);
            _tab.name = TabName;
            _tab.SetIsOnWithoutNotify(false);

            string title = Localization.Translate(Localization.Title);
            float textWidth = 0f;
            foreach (TextMeshProUGUI text in _tab.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = title;
                textWidth = Mathf.Max(textWidth, text.GetPreferredValues(title).x);
            }

            // Sized for its own title, with the margin Redux gives the tier tabs
            if (!_tab.TryGetComponent(out LayoutElement layout)) layout = _tab.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = textWidth + TabMargin;
            layout.flexibleWidth = 1f;

            // Clicking our tab goes to our page; clicking a tier tab turns ours off and the stock scroll takes over
            _tab.onValueChanged.AddListener(selected =>
            {
                AnimateTo(selected ? 1f : 0f);
                if (selected) ScrollTabIntoView();
            });
        }

        // Redux sizes the tab row to fit its own tabs only, which would leave ours cut off past the end of the row.
        // Each time it sizes the row, ours is added in.
        private void FitTabBar()
        {
            if (Mathf.Approximately(_tabBar.sizeDelta.x, _barWidthWritten) || !(_tabBar.parent is RectTransform viewport))
                return;

            float width = 0f;
            int tabs = 0;
            foreach (Transform child in _tabBar)
            {
                if (!child.gameObject.activeSelf || !child.TryGetComponent(out LayoutElement layout)) continue;
                width += layout.preferredWidth;
                tabs++;
            }
            if (_tabBar.TryGetComponent(out HorizontalLayoutGroup group))
                width += group.padding.horizontal + group.spacing * Mathf.Max(0, tabs - 1);

            _barWidthWritten = Mathf.Max(viewport.rect.width, width);
            _tabBar.sizeDelta = new Vector2(_barWidthWritten, _tabBar.sizeDelta.y);
        }

        // The tab row scrolls when it's wider than the screen; ours is at its end
        private void ScrollTabIntoView()
        {
            if (!(_tabBar.parent is RectTransform viewport)) return;
            if (viewport.TryGetComponent(out ScrollRect scroll)) scroll.StopMovement();
            float end = Mathf.Max(0f, _tabBar.rect.width - viewport.rect.width);
            _tabBar.anchoredPosition = new Vector2(-end, _tabBar.anchoredPosition.y);
        }

        private void OnDestroy()
        {
            ShowLedger(false);

            // As the stock R&D screen does: unbound whenever the game is still there, leaving for the main menu
            // included, so the input doesn't keep calling into this screen once it's gone
            GameInstance game = GameManager.Instance != null ? GameManager.Instance.Game : null;
            if (game?.InputManager == null) return;
            _input?.UnbindAction<float>(game.Input.RD.ScrollTechTree.name, OnScrollInput);
        }

        // While our page shows, the ledger keeps itself up to date (e.g. after reports scored in flight)
        private void ShowLedger(bool shown)
        {
            if (shown == _showingLedger) return;
            _showingLedger = shown;
            KERNELPlugin.Instance?.Ledger?.SetShown(this, shown);
        }

        private void OnScrollInput(float input) => _scrollInput = input * _scrollSpeed;

        // Scrolling the sidebar's biome list shouldn't also scroll the page
        private bool IsPointerOverSidebarList() =>
            _sidebar != null && UnityEngine.InputSystem.Mouse.current is { } mouse && _sidebar.IsPointerOverList(mouse.position.ReadValue());

        private void AnimateTo(float page)
        {
            _animating = true;
            _animTime = 0f;
            _animFromPage = _page;
            _animToPage = page;
            _animFromScroll = _scrollbar != null ? _scrollbar.value : 1f;
        }

        /// <summary>
        /// Called by ResearchScrollbar when the player presses on the tier scrollbar, with the pointer's x in the
        /// scrollbar's own space.
        /// </summary>
        public void OnScrollbarDown(float x)
        {
            if (_tab == null || _scrollbar == null) return;

            HandleSpan(out float handleLeft, out float handleWidth);
            if (x >= handleLeft && x <= handleLeft + handleWidth)
            {
                _grab = x - handleLeft;
                _animating = false;
            }
            else
            {
                // A press on the track jumps a page toward it, as the stock scrollbar pages through tiers
                _grab = null;
                if (x > handleLeft + handleWidth && _scrollbar.value >= 0.999f) AnimateTo(1f);
                else if (x < handleLeft && _page > 0f) AnimateTo(0f);
            }
        }

        /// <summary>
        /// Called by ResearchScrollbar while the player drags on the tier scrollbar. Positions the handle under
        /// the pointer across all the sections, ours included, after the stock scrollbar has had its turn.
        /// </summary>
        public void OnScrollbarDrag(float x)
        {
            if (_grab == null || _tab == null || _scrollbar == null || _scrollArea == null) return;

            Rect rect = ((RectTransform)_scrollbar.transform).rect;
            float areaWidth = rect.width * _tierShare + _scrollArea.sizeDelta.x;
            float handleWidth = areaWidth * _scrollbar.size;
            float travel = areaWidth - handleWidth;
            float left = x - _grab.Value;

            // Within the tiers the stock scroll position follows the handle; past them, our page does
            float areaLeftAtTiers = rect.xMin + _scrollArea.offsetMin.x;
            float scroll = travel > 0f ? (left - areaLeftAtTiers) / travel : 0f;
            if (scroll <= 1f)
            {
                _page = 0f;
                _scrollbar.value = Mathf.Clamp01(scroll);
            }
            else
            {
                float shift = (left - travel - areaLeftAtTiers) / rect.width;
                _page = Mathf.Clamp01(shift / (1f - _tierShare));
                _scrollbar.value = 1f;
            }
        }

        public void OnScrollbarUp() => _grab = null;

        // The scrollbar handle's left edge and width, in the scrollbar's space
        private void HandleSpan(out float left, out float width)
        {
            var track = (RectTransform)_scrollbar.transform;
            var corners = new Vector3[4];
            _scrollbar.handleRect.GetWorldCorners(corners);
            left = track.InverseTransformPoint(corners[0]).x;
            width = track.InverseTransformPoint(corners[3]).x - left;
        }

        // After the stock scroll has positioned the tree and backgrounds for this frame
        private void LateUpdate()
        {
            if (_tabBar == null || _scrollbar == null || _background == null || _treeContent == null || _starfieldRect == null)
                return;

            if (!SyncTab())
            {
                if (_starfield != null && _starfield.enabled) _starfield.enabled = false;
                return;
            }
            FitTabBar();

            if (_animating)
            {
                _animTime += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_animTime / _slideTime));
                _page = Mathf.Lerp(_animFromPage, _animToPage, t);
                // Going to our page from an earlier tier carries the tiers to the end on the way
                if (_animToPage > 0f) _scrollbar.value = Mathf.Lerp(_animFromScroll, 1f, t);
                if (t >= 1f) _animating = false;
            }
            else if (_grab == null && _scrollInput != 0f && !IsPointerOverSidebarList())
            {
                // Past the last tier, scroll input moves through our page at the stock speed (in tiers per second)
                if (_page > 0f || (_scrollInput > 0f && _scrollbar.value >= 0.999f))
                    _page = Mathf.Clamp01(_page + _scrollInput * (_tiers - 1) * Time.unscaledDeltaTime);
            }

            // While past the last tier, the stock scroll stays on it (it applied this frame's input already)
            bool leaving = _animating && _animToPage == 0f;
            if (_page > 0f && !leaving && _scrollbar.value < 1f) _scrollbar.value = 1f;

            // Select whichever of the last tier and our page is nearer, as the stock scroll does for tiers
            if (!_animating && _scrollbar.value >= 0.999f)
            {
                if (_page >= 0.5f && !_tab.isOn)
                {
                    _tab.SetIsOnWithoutNotify(true);
                    ScrollTabIntoView();
                }
                else if (_page < 0.5f && _tab.isOn && _lastTier != null) _lastTier.SetIsOnWithoutNotify(true);
            }

            ShowLedger(_tab.isOn);
            if (_sidebar != null) _sidebar.SetShown(_tab.isOn);

            // Carry everything left until the end of the tier backgrounds, and of the tree, leaves the screen
            _backgroundSlide.Apply(_background, _page, _backgroundEnd);
            _treeSlide.Apply(_treeContent, _page, _treeEnd);

            // The starfield starts where the tier backgrounds end. Positions are only written when they change, so
            // an idle screen doesn't dirty its canvas every frame.
            Vector2 position = _starfieldRect.anchoredPosition;
            float starfieldX = BackgroundEndX(_background.anchoredPosition.x) - StarfieldOverlap;
            if (!Mathf.Approximately(position.x, starfieldX))
                _starfieldRect.anchoredPosition = new Vector2(starfieldX, position.y);
            if (_starfield != null && !_starfield.enabled) _starfield.enabled = true;

            // The stock handle travels the tiers' share of the track, which moves over to end in ours
            if (_scrollArea != null)
            {
                float shift = (1f - _tierShare) * _page;
                if (!Mathf.Approximately(_scrollArea.anchorMin.x, shift))
                {
                    _scrollArea.anchorMin = new Vector2(shift, _scrollArea.anchorMin.y);
                    _scrollArea.anchorMax = new Vector2(_tierShare + shift, _scrollArea.anchorMax.y);
                }
            }
        }

        // Where the right end of the tier backgrounds is, from the screen's left edge, with the strip at x. Redux
        // builds and sizes the tier backgrounds as the screen runs, so it's read from the last one each time
        private float BackgroundEndX(float x)
        {
            RectTransform last = LastTierBackground();
            if (last != null)
            {
                var screen = (RectTransform)_background.parent;
                Vector3 end = screen.InverseTransformPoint(last.TransformPoint(new Vector3(last.rect.xMax, 0f, 0f)));
                _backgroundEndOffset = end.x - screen.rect.xMin - _background.anchoredPosition.x;
            }

            return x + _backgroundEndOffset;
        }

        // The last tier background still in use: those being cleared are switched off before they're destroyed
        private RectTransform LastTierBackground()
        {
            for (int i = _background.childCount - 1; i >= 0; i--)
            {
                Transform child = _background.GetChild(i);
                if (child.gameObject.activeSelf) return child as RectTransform;
            }
            return null;
        }

        // Where the right end of the tech tree is, from the left edge of its view, with the tree at x
        private float TreeEndX(float x)
        {
            return x + _treeContent.rect.width * (1f - _treeContent.pivot.x);
        }

        /// <summary>
        /// Moves a stock rect left by an offset on top of wherever the stock code puts it. Anything the stock code
        /// writes becomes the new base, so the stock tier scroll keeps working underneath.
        /// </summary>
        private class SlidingRect
        {
            private float _base;
            private float _written = float.NaN;

            // Moves it left by page times how far its end is (endFor gets the stock position); on the last tier,
            // page is 0 and the stock position stands
            public void Apply(RectTransform rect, float page, Func<float, float> endFor)
            {
                Vector2 position = rect.anchoredPosition;
                if (!Mathf.Approximately(position.x, _written)) _base = position.x;

                float x = page > 0f ? _base - page * endFor(_base) : _base;
                _written = x;
                if (Mathf.Approximately(position.x, x)) return;

                position.x = x;
                rect.anchoredPosition = position;
            }
        }
    }
}
