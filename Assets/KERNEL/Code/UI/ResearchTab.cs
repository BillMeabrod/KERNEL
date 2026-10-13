using System;
using System.Collections;
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
    /// of the tier backgrounds with this component on it, so everything is created and destroyed with the screen. The tier tabs themselves are only created when the screen starts, so ours is
    /// a copy of one of them, in the same toggle group: it looks, sounds and switches like the stock tabs.
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

        // Same as the stock scroll between tiers
        private const float SlideTime = 0.25f;

        // The starfield overlaps the last tier background slightly so no hairline shows between them at
        // fractional pixel positions; its edge continues that background, so the overlap doesn't show
        private const float StarfieldOverlap = 2f;

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
        private RectTransform _starfieldRect;
        private RDInputDefinition _input;
        private int _tiers;

        // The scrollbar track is split into one section per tier plus ours; the stock handle keeps to the tiers'
        private float _tierShare = 1f;

        // How far past the last tier we are: 0 on the last tier, 1 on our page
        private float _page;

        // Scroll input, in stock scroll units per second
        private float _scrollInput;
        private float _scrollSpeed = 1f;

        // A tab click animates to a page instead of following input
        private bool _animating;
        private float _animTime, _animFromPage, _animToPage, _animFromScroll;

        // Where on the scrollbar handle the pointer took hold of it, if dragging
        private float? _grab;

        private readonly SlidingRect _backgroundSlide = new();
        private readonly SlidingRect _treeSlide = new();

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

        private static PrefabPatchObjectReference Target(PrefabPatchObjectTarget target) =>
            PrefabPatchBuilder.TargetReference(target);

        private void Awake()
        {
            // Patched-in objects are added last, which would draw it over the whole screen: put it straight
            // after the tier backgrounds instead
            _starfieldRect = (RectTransform)transform;
            if (_background != null) _starfieldRect.SetSiblingIndex(_background.GetSiblingIndex() + 1);

            if (_scrollbar != null && _scrollbar.TryGetComponent(out ResearchScrollbar scrollbar)) scrollbar.Tab = this;
        }

        private IEnumerator Start()
        {
            // The R&D controller creates the tier tabs in its own Start; they exist from the next frame
            yield return null;

            if (_tabBar == null) yield break;
            ToggleExtended[] tierTabs = _tabBar.GetComponentsInChildren<ToggleExtended>();
            if (tierTabs.Length == 0) yield break;

            _tiers = tierTabs.Length;
            _tierShare = _tiers / (_tiers + 1f);
            _lastTier = tierTabs[_tiers - 1];

            // Copy a tab that isn't selected: a selected copy would take the selection from it on joining the group
            ToggleExtended template = Array.Find(tierTabs, tier => !tier.isOn) ?? _lastTier;
            _tab = Instantiate(template, _tabBar);
            _tab.name = TabName;
            _tab.transform.SetAsLastSibling();
            _tab.SetIsOnWithoutNotify(false);

            string title = Localization.Translate(Localization.Title);
            foreach (TextMeshProUGUI text in _tab.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.text = title;

            // Clicking our tab goes to our page; clicking a tier tab turns ours off and the stock scroll takes over
            _tab.onValueChanged.AddListener(selected => AnimateTo(selected ? 1f : 0f));

            // Scroll input at the same speed as the stock tier scroll, alongside the stock handler
            FieldInfo speed = typeof(RDCenterUIController).GetField("_techTreeInputTravelSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_controller != null && speed?.GetValue(_controller) is float value) _scrollSpeed = value;

            if (GameManager.Instance.Game.InputManager.TryGetInputDefinition(out _input))
                _input.BindAction<float>(GameManager.Instance.Game.Input.RD.ScrollTechTree.name, OnScrollInput);

            // The sidebar shows the body picked on the map while our page shows
            var map = _treeContent.parent.GetComponentInChildren<SystemMap>(true);
            if (_controller != null) _sidebar = ResearchSidebar.Attach(_controller, map);
        }

        private void OnDestroy()
        {
            _input?.UnbindAction<float>(GameManager.Instance.Game.Input.RD.ScrollTechTree.name, OnScrollInput);
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
            if (_tab == null || _scrollbar == null || _background == null || _treeContent == null || _starfieldRect == null)
                return;

            if (_animating)
            {
                _animTime += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_animTime / SlideTime));
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
                if (_page >= 0.5f && !_tab.isOn) _tab.SetIsOnWithoutNotify(true);
                else if (_page < 0.5f && _tab.isOn && _lastTier != null) _lastTier.SetIsOnWithoutNotify(true);
            }

            if (_sidebar != null) _sidebar.SetShown(_tab.isOn);

            // Carry everything left until the end of the tier backgrounds, and of the tree, leaves the screen
            _backgroundSlide.Apply(_background, x => _page * BackgroundEndX(x));
            _treeSlide.Apply(_treeContent, x => _page * TreeEndX(x));

            // The starfield starts where the tier backgrounds end
            Vector2 position = _starfieldRect.anchoredPosition;
            position.x = BackgroundEndX(_background.anchoredPosition.x) - StarfieldOverlap;
            _starfieldRect.anchoredPosition = position;

            // The stock handle travels the tiers' share of the track, which moves over to end in ours
            if (_scrollArea != null)
            {
                float shift = (1f - _tierShare) * _page;
                _scrollArea.anchorMin = new Vector2(shift, _scrollArea.anchorMin.y);
                _scrollArea.anchorMax = new Vector2(_tierShare + shift, _scrollArea.anchorMax.y);
            }
        }

        // Where the right end of the tier backgrounds is, from the screen's left edge, with the strip at x
        private float BackgroundEndX(float x)
        {
            var screen = (RectTransform)_background.parent;
            Bounds strip = RectTransformUtility.CalculateRelativeRectTransformBounds(_background);
            Vector3 end = screen.InverseTransformPoint(_background.TransformPoint(new Vector3(strip.max.x, 0f, 0f)));
            return end.x - screen.rect.xMin + (x - _background.anchoredPosition.x);
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

            // offsetFor gets the stock position and returns how far left of it to move
            public void Apply(RectTransform rect, Func<float, float> offsetFor)
            {
                Vector2 position = rect.anchoredPosition;
                if (!Mathf.Approximately(position.x, _written)) _base = position.x;

                position.x = _base - offsetFor(_base);
                rect.anchoredPosition = position;
                _written = position.x;
            }
        }
    }
}
