using System.Collections.Generic;
using System.Linq;
using KERNEL.Ledger;
using KSP.Api.CoreTypes;
using KSP.Game.Science;
using KSP.UI.Binding;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KERNEL.UI
{
    /// <summary>
    /// Turns the R&D sidebar into the selected body's science while K.E.R.N.E.L's page is showing: its name
    /// (through the screen's own TechNodeName value), how many of its biomes have been explored and how much
    /// science has been gained there, then each biome with the experiments that can be run in it. The stock
    /// sections it replaces are hidden while the page shows and restored afterwards. Follows the same spoiler
    /// rules as the ledger window: only unlocked experiments, and "?????" for places not yet visited.
    /// </summary>
    public class ResearchSidebar : MonoBehaviour
    {
        // Redux's sidebar, swapped in when the screen starts
        private const string SidebarPath = "Container/TechNodeInfo";
        private const string TitleKey = "TechNodeName";

        // The stock sections our panel takes the place of, in the sidebar's Content
        private static readonly string[] StockSections =
            { "TechNodeDescription", "PartsAcquired", "PartsListScrollView", "PartsList", "Footer", "ResearchButton" };
        private const string DividerPath = "Footer/Divider";
        // The parts list's scrollbar, copied for the biome list so it scrolls the same way
        private const string ScrollbarPath = "PartsListScrollView/PartsListScrollbar";
        private const float ScrollbarGap = 6f;

        // The sidebar's own text styles
        private static readonly Color LabelColor = new(0.729f, 0.706f, 0.659f);
        private static readonly Color BodyColor = new(0.670f, 0.700f, 0.800f);
        private static readonly Color NameColor = new(0.839f, 0.839f, 0.851f);
        private static readonly Color ScienceColor = new(0.373f, 0.749f, 0.894f);
        private static readonly Color SituationColor = new(0.906f, 0.792f, 0.463f);
        private static readonly Color CompleteColor = new(0.39f, 0.84f, 0.56f);
        private static readonly Color UnknownColor = new(0.427f, 0.451f, 0.529f);
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.06f);
        private static readonly Color ArrowColor = new(0.863f, 0.757f, 0.506f);

        private const float RowHeight = 34f;
        private const float ExperimentIndent = 30f;

        private Property<string> _title;
        private string _savedTitle;
        private readonly Dictionary<GameObject, bool> _hiddenSections = new();
        private RectTransform _panel, _rows;
        private ScrollRect _scroll;
        private TextMeshProUGUI _biomesValue, _scienceValue;
        private Sprite _divider;
        private Image.Type _dividerType;
        private Color _dividerColor;
        private static Sprite _arrow;

        private SystemMap _map;
        private BodyEntry _body;
        private bool _shown, _dirty;

        // Which biomes are open, by body and area, kept across rebuilds
        private readonly HashSet<string> _expanded = new();

        public static ResearchSidebar Attach(RDCenterUIController screen, SystemMap map)
        {
            Transform sidebar = screen.transform.Find(SidebarPath);
            if (sidebar == null) return null;

            var component = sidebar.gameObject.AddComponent<ResearchSidebar>();
            component.Initialize(screen, map);
            return component;
        }

        private void Initialize(RDCenterUIController screen, SystemMap map)
        {
            if (screen.TryGetComponent(out ContextBindRoot bindRoot)
                && bindRoot.Context.TryResolveProperty(TitleKey, out var property))
                _title = property as Property<string>;

            Transform content = transform.Find("Content");
            if (content?.Find(DividerPath)?.GetComponent<Image>() is { } divider)
            {
                _divider = divider.sprite;
                _dividerType = divider.type;
                _dividerColor = divider.color;
            }

            BuildPanel(content);

            _map = map;
            if (_map != null)
            {
                _map.Selected += ShowBody;
                _body = _map.SelectedBody;
            }
        }

        private void OnDestroy()
        {
            if (_map != null) _map.Selected -= ShowBody;
        }

        /// <summary>
        /// Shows our panel in place of the stock sections, or puts them back.
        /// </summary>
        public void SetShown(bool shown)
        {
            if (shown == _shown || _panel == null) return;
            _shown = shown;

            if (shown)
            {
                _hiddenSections.Clear();
                foreach (string name in StockSections)
                {
                    Transform section = _panel.parent.Find(name);
                    if (section == null) continue;
                    _hiddenSections[section.gameObject] = section.gameObject.activeSelf;
                    section.gameObject.SetActive(false);
                }

                _savedTitle = _title?.GetValue();
                _panel.gameObject.SetActive(true);
                _dirty = true;
            }
            else
            {
                _panel.gameObject.SetActive(false);
                foreach (KeyValuePair<GameObject, bool> section in _hiddenSections)
                    if (section.Key != null) section.Key.SetActive(section.Value);
                _hiddenSections.Clear();
                if (_title != null && _savedTitle != null) _title.SetValue(_savedTitle);
            }
        }

        private void ShowBody(BodyEntry body)
        {
            _body = body;
            _dirty = true;
        }

        /// <summary>
        /// Whether the pointer is over the biome list, whose scrolling shouldn't also scroll the page.
        /// </summary>
        public bool IsPointerOverList(Vector2 screenPoint)
        {
            if (!_shown || _scroll == null) return false;
            Canvas parent = _scroll.GetComponentInParent<Canvas>();
            Canvas canvas = parent != null ? parent.rootCanvas : null;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_scroll.transform, screenPoint, camera);
        }

        private void LateUpdate()
        {
            if (!_shown || _body == null) return;

            // The stock screen sets the title when it refreshes its node; keep ours while the page shows
            if (_title != null && _title.GetValue() != _body.DisplayName)
            {
                _savedTitle = _title.GetValue();
                _title.SetValue(_body.DisplayName);
            }

            if (_dirty) Refresh();
        }

        private void Refresh()
        {
            _dirty = false;
            List<AreaEntry> areas = _body.ShownAreas().ToList();
            List<LocationEntry> locations = areas.SelectMany(a => a.UnlockedLocations).ToList();

            _biomesValue.text = $"{areas.Count(a => a.HasSubmissions)} / {areas.Count}";
            _scienceValue.text = Tally(locations.Sum(l => l.Earned), locations.Sum(l => l.Possible));

            float scroll = _scroll.verticalNormalizedPosition;
            // Detached first: Destroy only takes effect at the end of the frame, and the layout below must not
            // count the old rows
            for (int i = _rows.childCount - 1; i >= 0; i--)
            {
                Transform row = _rows.GetChild(i);
                row.SetParent(null, false);
                Destroy(row.gameObject);
            }
            foreach (AreaEntry area in areas) AddArea(area);

            // Keep the place in the list when the same body refreshes (e.g. after scoring a report)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rows);
            _scroll.verticalNormalizedPosition = scroll;
        }

        private void BuildPanel(Transform content)
        {
            if (content == null) return;

            _panel = NewRect("KERNEL-Science", content);
            Transform description = content.Find(StockSections[0]);
            if (description != null) _panel.SetSiblingIndex(description.GetSiblingIndex());
            var panelLayout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 12f;
            panelLayout.childControlWidth = panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            Layout(_panel, flexibleHeight: 1f, minHeight: 200f);

            // Biomes explored and science gained, side by side
            RectTransform stats = Row(_panel, "Stats", 64f, forceExpandWidth: true);
            stats.GetComponent<HorizontalLayoutGroup>().spacing = 20f;
            _biomesValue = Stat(stats, Localization.BiomesExplored, Color.white);
            _scienceValue = Stat(stats, Localization.SciencePointsGained, ScienceColor);

            Divider(_panel);

            // Column headings
            RectTransform columns = Row(_panel, "Columns", 24f);
            var columnsLayout = columns.GetComponent<HorizontalLayoutGroup>();
            columnsLayout.padding = new RectOffset(8, 12, 0, 0);
            columnsLayout.spacing = 16f;
            TextMeshProUGUI biome = Label(columns, Localization.Translate(Localization.BiomeColumn), 13f, LabelColor, TextAlignmentOptions.Left);
            biome.fontStyle = FontStyles.UpperCase;
            biome.characterSpacing = 1f;
            Layout(biome.rectTransform, flexibleWidth: 1f);
            TextMeshProUGUI science = Label(columns, Localization.Translate(Localization.ScienceGainedColumn), 13f, LabelColor, TextAlignmentOptions.Right);
            science.fontStyle = FontStyles.UpperCase;
            science.characterSpacing = 1f;

            // The biome list, scrolling when it's longer than the panel
            RectTransform list = NewRect("Biomes", _panel);
            Layout(list, flexibleHeight: 1f, minHeight: 120f);
            _scroll = list.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;

            RectTransform viewport = NewRect("Viewport", list);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            _scroll.viewport = viewport;

            _rows = NewRect("Rows", viewport);
            _rows.anchorMin = new Vector2(0f, 1f);
            _rows.anchorMax = new Vector2(1f, 1f);
            _rows.pivot = new Vector2(0.5f, 1f);
            _rows.sizeDelta = Vector2.zero;
            var rowsLayout = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 2f;
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            _rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _rows;

            if (content.Find(ScrollbarPath) is { } stockScrollbar && stockScrollbar.TryGetComponent(out Scrollbar _))
            {
                GameObject copy = Instantiate(stockScrollbar.gameObject, list, false);
                copy.name = "Scrollbar";
                var scrollbar = copy.GetComponent<Scrollbar>();
                scrollbar.onValueChanged.RemoveAllListeners();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                var bar = (RectTransform)copy.transform;
                float width = Mathf.Max(bar.rect.width, 8f);
                bar.anchorMin = new Vector2(1f, 0f);
                bar.anchorMax = Vector2.one;
                bar.pivot = new Vector2(1f, 0.5f);
                bar.sizeDelta = new Vector2(width, 0f);
                bar.anchoredPosition = Vector2.zero;
                _scroll.verticalScrollbar = scrollbar;
                _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                _scroll.verticalScrollbarSpacing = ScrollbarGap;
            }

            _panel.gameObject.SetActive(false);
        }

        private void AddArea(AreaEntry area)
        {
            List<LocationEntry> locations = area.UnlockedLocations.ToList();
            float earned = locations.Sum(l => l.Earned), possible = locations.Sum(l => l.Possible);
            string key = $"{_body.BodyName}/{area.Id}";

            RectTransform row = Row(_rows, area.Id, RowHeight);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 12, 6, 6);
            rowLayout.spacing = 8f;
            var background = row.gameObject.AddComponent<Image>();
            background.color = Color.clear;

            Graphic arrow;
            if (area.IsKnown)
            {
                var image = NewRect("Arrow", row).gameObject.AddComponent<Image>();
                image.sprite = Arrow;
                image.color = ArrowColor;
                arrow = image;
            }
            else
            {
                // Nothing to open yet: a short dash in place of the arrow
                RectTransform slot = NewRect("Dash", row);
                Layout(slot, preferredWidth: 12f, preferredHeight: 12f);
                var dash = NewRect("Line", slot).gameObject.AddComponent<Image>();
                dash.color = UnknownColor;
                dash.rectTransform.sizeDelta = new Vector2(8f, 2f);
                arrow = dash;
            }

            if (area.IsKnown) Layout(arrow.rectTransform, preferredWidth: 12f, preferredHeight: 12f);
            arrow.raycastTarget = false;

            string title = area.IsKnown ? area.DisplayName : Localization.Translate(Localization.UnknownArea);
            TextMeshProUGUI name = Label(row, title, 16f, area.IsKnown ? NameColor : UnknownColor, TextAlignmentOptions.Left);
            Layout(name.rectTransform, flexibleWidth: 1f);
            bool complete = possible > 0f && earned >= possible;
            Label(row, Tally(earned, possible), 13f, complete ? CompleteColor : BodyColor, TextAlignmentOptions.Right);

            RectTransform experiments = null;
            if (area.IsKnown)
            {
                experiments = NewRect("Experiments", _rows);
                var group = experiments.gameObject.AddComponent<VerticalLayoutGroup>();
                group.childControlWidth = group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = false;
                // Whole-body areas are named after their situation, so their rows don't repeat it
                foreach (LocationEntry location in locations) AddExperiment(experiments, location, showSituation: !area.IsWholeBody);
            }

            row.gameObject.AddComponent<BiomeRow>().Initialize(this, key, background, arrow.rectTransform, experiments);
        }

        private static void AddExperiment(Transform parent, LocationEntry location, bool showSituation)
        {
            RectTransform row = Row(parent, location.Experiment.ExperimentID, 24f);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)ExperimentIndent, 12, 2, 2);
            layout.spacing = 6f;

            string experiment = Localization.Translate(location.Experiment.DisplayName, location.Experiment.ExperimentID);
            TextMeshProUGUI name = Label(row, experiment, 14f, BodyColor, TextAlignmentOptions.Left);
            if (showSituation)
                Label(row, location.Location.ScienceSituation.GetTranslatedDescription(), 12f, SituationColor, TextAlignmentOptions.Left);
            Layout(name.rectTransform, flexibleWidth: 1f);
            Label(row, Tally(location.Earned, location.Possible), 12f, location.IsComplete ? CompleteColor : BodyColor, TextAlignmentOptions.Right);
        }

        private TextMeshProUGUI Stat(Transform parent, string labelKey, Color valueColor)
        {
            RectTransform column = NewRect(labelKey, parent);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TextMeshProUGUI label = Label(column, Localization.Translate(labelKey), 13f, LabelColor, TextAlignmentOptions.BottomLeft);
            label.fontStyle = FontStyles.UpperCase;
            label.characterSpacing = 1f;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return Label(column, "", 20f, valueColor, TextAlignmentOptions.Left);
        }

        private void Divider(Transform parent)
        {
            var divider = NewRect("Divider", parent).gameObject.AddComponent<Image>();
            divider.sprite = _divider;
            divider.type = _divider != null ? _dividerType : Image.Type.Simple;
            divider.color = _divider != null ? _dividerColor : new Color(0.670f, 0.700f, 0.800f, 0.2f);
            divider.raycastTarget = false;
            Layout(divider.rectTransform, preferredHeight: 8f, minHeight: 8f);
        }

        private static RectTransform Row(Transform parent, string name, float height, bool forceExpandWidth = false)
        {
            RectTransform row = NewRect(name, parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = forceExpandWidth;
            layout.childForceExpandHeight = false;
            Layout(row, preferredHeight: height, minHeight: height);
            return row;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            var label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            TextMeshProUGUI font = ResearchUi.FontSource(parent);
            if (font != null)
            {
                label.font = font.font;
                label.fontSharedMaterial = font.fontSharedMaterial;
            }

            return label;
        }

        private static void Layout(RectTransform rect, float flexibleWidth = -1f, float flexibleHeight = -1f,
            float preferredWidth = -1f, float preferredHeight = -1f, float minHeight = -1f)
        {
            if (!rect.TryGetComponent(out LayoutElement element)) element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.minWidth = preferredWidth;
            element.minHeight = minHeight;
        }

        private static string Tally(float earned, float possible) => $"{earned:#,0.#} / {possible:#,0.#}";

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
        }

        // A small smooth-edged white triangle pointing right, turned to point down when its biome is open
        private static Sprite Arrow
        {
            get
            {
                if (_arrow != null) return _arrow;
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                // Corners counter-clockwise, so a point is inside when it's on the left of every edge
                var corners = new[] { new Vector2(6f, 4f), new Vector2(28f, 16f), new Vector2(6f, 28f) };
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    float inside = float.MaxValue;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 from = corners[i], edge = corners[(i + 1) % 3] - from;
                        inside = Mathf.Min(inside, (edge.x * (point.y - from.y) - edge.y * (point.x - from.x)) / edge.magnitude);
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(inside + 0.5f)));
                }

                texture.Apply();
                _arrow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _arrow;
            }
        }

        /// <summary>
        /// One biome: highlights on hover and opens its experiments when clicked, with the tier tabs' sounds.
        /// Biomes not yet visited don't open.
        /// </summary>
        private class BiomeRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            private ResearchSidebar _sidebar;
            private string _key;
            private Image _background;
            private RectTransform _arrow, _experiments;

            public void Initialize(ResearchSidebar sidebar, string key, Image background, RectTransform arrow, RectTransform experiments)
            {
                _sidebar = sidebar;
                _key = key;
                _background = background;
                _arrow = arrow;
                _experiments = experiments;
                SetOpen(experiments != null && sidebar._expanded.Contains(key));
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _background.color = HoverColor;
                ResearchUi.PlayHover(_sidebar, transform);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _background.color = Color.clear;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left || _experiments == null) return;

                ResearchUi.PlayPress(_sidebar, transform);
                bool open = !_experiments.gameObject.activeSelf;
                if (open) _sidebar._expanded.Add(_key);
                else _sidebar._expanded.Remove(_key);
                SetOpen(open);
            }

            private void SetOpen(bool open)
            {
                if (_experiments != null) _experiments.gameObject.SetActive(open);
                if (_experiments != null) _arrow.localRotation = Quaternion.Euler(0f, 0f, open ? -90f : 0f);
            }
        }
    }
}
