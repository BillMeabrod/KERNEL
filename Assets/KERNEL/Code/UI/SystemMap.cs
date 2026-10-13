using System;
using System.Collections.Generic;
using System.Linq;
using KERNEL.Ledger;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KERNEL.UI
{
    /// <summary>
    /// The system map on K.E.R.N.E.L's R&D page: a star on the left with its planets in a row to the right,
    /// innermost first, each planet's moons stacked below it, and moons of moons stacked below those. With more
    /// than one star, a row of system names picks which one is shown; a star orbiting another star is a system
    /// of its own. Bodies can be clicked to select them. A prefab
    /// patch adds this inside the tech tree's view, and it keeps itself just past the end of the tree, so it
    /// slides in with ResearchTab and is clipped like the tree.
    /// </summary>
    // After ResearchTab has slid the tree for the frame
    [DefaultExecutionOrder(100)]
    public class SystemMap : MonoBehaviour
    {
        // Layout, in canvas units
        private const float Margin = 60f;
        private const float StarSize = 150f;
        // The largest planet's size; the rest scale by the square root of their radius from it, which keeps
        // the giants clearly bigger without shrinking small moons out of sight
        private const float MaxBodySize = 130f;
        private const float MinBodySize = 18f;
        private const float ColumnGap = 48f;
        private const float MinColumnWidth = 120f;
        private const float RowGap = 44f;
        private const float LabelHeight = 28f;
        private const float LabelGap = 6f;
        // The selection ring: dashed like the tech tree's selection outline, a little outside the body
        private const float RingPadding = 7f;
        private const float RingStroke = 2f;
        private const float RingDash = 10f;
        private const float RingGap = 6f;
        private const float LineWidth = 2f;
        // How far right moons of moons sit from the moon they orbit
        private const float SubIndent = 56f;
        private const float PickerHeight = 44f;
        // The page title (the full name) along the top, above the system picker
        private const float TitleHeight = 44f;
        private const float TitleGap = 16f;
        private const float FullNameFontSize = 26f;
        // Selected when the page first opens
        private const string HomeBody = "Kerbin";
        private const float PickerGap = 28f;
        private const float PickerFontSize = 20f;
        // Edge arrows shown when part of the system is off the page
        private const float HintWidth = 96f;
        private const float ChevronSize = 40f;
        private const float HintFadeTime = 0.2f;
        // An arrow click slides to the next page of whole planet columns, over the stock tier scroll's time
        private const float PageTime = 0.25f;
        // A system a little too big for the page is shrunk to fit on it, down to this. One that wouldn't fit even
        // then is shown at full size, split into pages
        private const float MinFitScale = 0.85f;
        // How far an orbit line runs past the page edge when more planets follow on another page
        private const float LineOverrun = 40f;

        private static readonly Color StarColor = new(1f, 0.84f, 0.47f);
        private static readonly Color LineColor = new(1f, 1f, 1f, 0.18f);
        private static readonly Color LabelColor = new(0.85f, 0.88f, 0.95f);
        // Art is drawn a touch below full brightness, so hovering can brighten it
        private static readonly Color ArtTint = new(0.86f, 0.86f, 0.86f, 1f);
        private static readonly Color PickerColor = new(0.6f, 0.64f, 0.72f);
        private static readonly Color PickerHoverColor = new(0.85f, 0.88f, 0.95f);
        private static readonly Color PickerSelectedColor = new(1f, 0.84f, 0.47f);
        // The deep sky of the starfield, so the fade reads as the edge of the page
        private static readonly Color HintFadeColor = new(0.03f, 0.035f, 0.08f, 0.9f);
        private static readonly Color ChevronColor = new(0.85f, 0.88f, 0.95f, 0.7f);
        private static readonly Color ChevronHoverColor = new(1f, 1f, 1f, 1f);

        // Set by the prefab patch
        [SerializeField] private RectTransform _treeContent;

        private RectTransform _content;
        private RectTransform _picker;
        private TextMeshProUGUI _fullName;
        private EdgeHint _leftHint, _rightHint;

        // Sliding to a page after an arrow click
        private bool _paging;
        private float _pageFrom, _pageTo, _pageTime;
        private readonly List<BodyNode> _nodes = new();
        // The star's and each planet's column, left to right, each holding its bodies and its stretch of the orbit
        // line; pages break between them so none is cut in half, and a page shows only its own
        private readonly List<(RectTransform Root, float Left, float Right)> _columns = new();
        private float _rowCentre;
        // Orbit line running off the page edge where more planets follow on another page
        private Image _lineToPrevious, _lineToNext;
        private float _contentWidth, _contentHeight, _top;
        // Index of the first column on each page, and which page is shown
        private readonly List<int> _pageFirst = new();
        private int _page;
        private Vector2 _pagedSize = new(-1f, -1f);
        private List<BodyEntry> _roots = new();
        private string _selected;
        // The star (or other top-level body) whose system is shown
        private string _system;
        private static readonly Dictionary<int, Sprite> DashedRings = new();
        private static Sprite _disc, _fadeLeft, _fadeRight, _chevronLeft, _chevronRight;

        /// <summary>
        /// Raised when the selected body changes, including when a ledger rebuild replaces its entry.
        /// </summary>
        public event Action<BodyEntry> Selected;

        /// <summary>
        /// The selected body; the home planet until the player picks another.
        /// </summary>
        public BodyEntry SelectedBody { get; private set; }

        private void Awake()
        {
            // A system wider than the page is split into pages of whole planet columns, stepped through with the
            // edge arrows; the scroll wheel stays with the tier scroll
            var mask = gameObject.AddComponent<RectMask2D>();
            mask.enabled = true;
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);

            // Only shown with more than one system; stays put while the map pans
            _picker = NewRect("Systems", transform);
            _picker.anchorMin = _picker.anchorMax = new Vector2(0f, 1f);
            _picker.pivot = new Vector2(0f, 1f);
            _picker.anchoredPosition = new Vector2(Margin, -(Margin / 2f + TitleHeight));

            // The page's title, the full name (the tab already says K.E.R.N.E.L). Stays put while the map pans
            RectTransform title = NewRect("Title", transform);
            title.anchorMin = title.anchorMax = new Vector2(0f, 1f);
            title.pivot = new Vector2(0f, 1f);
            title.anchoredPosition = new Vector2(Margin, -Margin / 2f);
            title.sizeDelta = new Vector2(0f, TitleHeight);
            var titleRow = title.gameObject.AddComponent<HorizontalLayoutGroup>();
            titleRow.spacing = TitleGap;
            titleRow.childAlignment = TextAnchor.LowerLeft;
            titleRow.childControlWidth = titleRow.childControlHeight = true;
            titleRow.childForceExpandWidth = titleRow.childForceExpandHeight = false;
            var titleFit = title.gameObject.AddComponent<ContentSizeFitter>();
            titleFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _leftHint = AddEdgeHint(left: true);
            _rightHint = AddEdgeHint(left: false);
        }

        private void OnEnable()
        {
            LedgerService ledger = KERNELPlugin.Instance != null ? KERNELPlugin.Instance.Ledger : null;
            if (ledger == null) return;

            ledger.Rebuilt += Build;
            Build(ledger.Roots);
        }

        private void OnDisable()
        {
            LedgerService ledger = KERNELPlugin.Instance != null ? KERNELPlugin.Instance.Ledger : null;
            if (ledger != null) ledger.Rebuilt -= Build;
        }

        private void LateUpdate()
        {
            if (_treeContent == null) return;

            // Start where the tech tree ends, wherever it has been scrolled or slid to
            var rect = (RectTransform)transform;
            Vector2 position = rect.anchoredPosition;
            position.x = _treeContent.anchoredPosition.x + _treeContent.rect.width * (1f - _treeContent.pivot.x);
            rect.anchoredPosition = position;

            if (_paging)
            {
                _pageTime += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pageTime / PageTime));
                _content.anchoredPosition = new Vector2(Mathf.Lerp(_pageFrom, _pageTo, t), _content.anchoredPosition.y);
                if (t >= 1f)
                {
                    _paging = false;
                    ShowPageColumns();
                }
            }

            // How much fits depends on the page's size
            if (rect.rect.size != _pagedSize)
            {
                FitToPage(rect.rect.size);
                _paging = false;
                _content.anchoredPosition = new Vector2(PageOffset(_page), _content.anchoredPosition.y);
                ShowPageColumns();
            }

            // Arrows only where the row of planets carries on past the page
            _leftHint.SetShown(_page > 0);
            _rightHint.SetShown(_page < _pageFirst.Count - 1);
        }

        // Shrinks the map onto one page if that's enough, and otherwise splits it into pages of whole columns
        private void FitToPage(Vector2 page)
        {
            _pagedSize = page;
            _pageFirst.Clear();
            _pageFirst.Add(0);
            if (_columns.Count == 0 || page.x <= 0f) return;

            float scale = Mathf.Min(1f, page.x / _contentWidth, (page.y - _top) / _contentHeight);
            if (scale < MinFitScale) scale = 1f;
            _content.localScale = new Vector3(scale, scale, 1f);

            // In the map's own (unscaled) units: each page starts a margin before its first column
            float width = page.x / scale;
            for (int i = 1; i < _columns.Count; i++)
            {
                float start = PageStart(_pageFirst[_pageFirst.Count - 1]);
                if (_columns[i].Right + Margin - start > width) _pageFirst.Add(i);
            }

            _page = Mathf.Clamp(_page, 0, _pageFirst.Count - 1);
        }

        private float PageStart(int firstColumn) => firstColumn == 0 ? 0f : _columns[firstColumn].Left - Margin;

        // Where the map sits to show a page
        private float PageOffset(int page) => -PageStart(_pageFirst[page]) * _content.localScale.x;

        private int PageEnd(int page) => page + 1 < _pageFirst.Count ? _pageFirst[page + 1] : _columns.Count;

        // Shows the shown page's columns (and, while sliding, the one it's leaving), with the orbit line running
        // off whichever edges have more planets beyond
        private void ShowPageColumns(int alsoPage = -1)
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                bool onPage = i >= _pageFirst[_page] && i < PageEnd(_page);
                bool onOther = alsoPage >= 0 && i >= _pageFirst[alsoPage] && i < PageEnd(alsoPage);
                _columns[i].Root.gameObject.SetActive(onPage || onOther);
            }

            float scale = _content.localScale.x;
            float start = PageStart(_pageFirst[_page]);
            float end = start + _pagedSize.x / scale;
            bool hasPrevious = _page > 0, hasNext = _page < _pageFirst.Count - 1;

            PlaceLine(_lineToPrevious, hasPrevious, new Vector2(start - LineOverrun, _rowCentre),
                new Vector2(hasPrevious ? _columns[_pageFirst[_page]].Left : start, _rowCentre));
            PlaceLine(_lineToNext, hasNext, new Vector2(hasNext ? _columns[PageEnd(_page) - 1].Right : end, _rowCentre),
                new Vector2(end + LineOverrun, _rowCentre));
        }

        private static void PlaceLine(Image line, bool shown, Vector2 from, Vector2 to)
        {
            if (line == null) return;
            line.gameObject.SetActive(shown);
            if (!shown) return;

            var rect = (RectTransform)line.transform;
            rect.anchoredPosition = (from + to) / 2f;
            rect.sizeDelta = new Vector2(Mathf.Max(Mathf.Abs(to.x - from.x), LineWidth), LineWidth);
        }

        // Slides the map a page left (-1) or right (+1)
        private void Page(int direction)
        {
            int page = Mathf.Clamp(_page + direction, 0, _pageFirst.Count - 1);
            if (page == _page) return;

            int leaving = _page;
            _page = page;
            ShowPageColumns(alsoPage: leaving);
            _pageFrom = _content.anchoredPosition.x;
            _pageTo = PageOffset(_page);
            _pageTime = 0f;
            _paging = true;
        }

        private EdgeHint AddEdgeHint(bool left)
        {
            RectTransform hint = NewRect(left ? "Left Arrow" : "Right Arrow", transform);
            hint.anchorMin = new Vector2(left ? 0f : 1f, 0f);
            hint.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            hint.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            hint.sizeDelta = new Vector2(HintWidth, 0f);

            Image fade = NewImage("Fade", hint, left ? FadeLeft : FadeRight, HintFadeColor);
            var fadeRect = (RectTransform)fade.transform;
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.sizeDelta = Vector2.zero;

            Image chevron = NewImage("Chevron", hint, left ? ChevronLeft : ChevronRight, ChevronColor);
            ((RectTransform)chevron.transform).sizeDelta = new Vector2(ChevronSize * 0.5f, ChevronSize);
            chevron.raycastTarget = false;

            var group = hint.gameObject.AddComponent<CanvasGroup>();
            EdgeHint edge = hint.gameObject.AddComponent<EdgeHint>();
            edge.Initialize(this, left ? -1 : 1, group, chevron);
            return edge;
        }

        private void Build(IEnumerable<BodyEntry> roots)
        {
            _roots = roots?.ToList() ?? new List<BodyEntry>();
            Layout();
        }

        private void Layout()
        {
            foreach (Transform child in _content) Destroy(child.gameObject);
            foreach (Transform child in _picker) Destroy(child.gameObject);
            _nodes.Clear();
            _columns.Clear();

            // Every star is a system, including one orbiting another star; so is a top-level body that isn't one
            var systems = new List<BodyEntry>();
            void CollectStars(BodyEntry body)
            {
                if (body.IsStar) systems.Add(body);
                foreach (BodyEntry child in body.Children) CollectStars(child);
            }

            foreach (BodyEntry root in _roots)
            {
                if (!root.IsStar) systems.Add(root);
                CollectStars(root);
            }

            if (systems.Count == 0) return;

            BodyEntry anchor = systems.FirstOrDefault(b => b.BodyName == _system) ?? systems[0];
            _system = anchor.BodyName;
            BuildTitle();
            float top = TitleHeight;
            if (systems.Count > 1)
            {
                BuildPicker(systems, anchor);
                top += PickerHeight;
            }

            double largest = _roots.SelectMany(b => new[] { b }.Concat(Descendants(b)))
                .Where(b => !b.IsStar).Select(b => b.Radius).DefaultIfEmpty(0).Max();
            float SizeOf(BodyEntry body)
            {
                if (body.IsStar) return StarSize;
                if (largest <= 0) return MinBodySize;
                return Mathf.Max(MinBodySize, MaxBodySize * (float)Math.Sqrt(body.Radius / largest));
            }

            // Stars orbiting this body are systems of their own
            IEnumerable<BodyEntry> Orbiting(BodyEntry body) => body.Children.Where(child => !child.IsStar);

            // How wide a planet's column needs to be for it, its moons and theirs
            float ColumnWidth(BodyEntry body, int depth) =>
                Orbiting(body).Select(child => ColumnWidth(child, depth + 1)).Append(Footprint(body, SizeOf(body)) + 2f * SubIndent * depth).Max();

            // Stacks a body's moons downward from y at x, then their moons further right below each. The line
            // down to each runs from just below the name above it, so it never crosses a name
            void StackMoons(Transform column, BodyEntry parent, float x, float lineFrom, ref float y)
            {
                foreach (BodyEntry moon in Orbiting(parent))
                {
                    float size = SizeOf(moon);
                    float half = HalfHeight(moon, size);
                    float centre = y - half;
                    AddLine(column, new Vector2(x, lineFrom), new Vector2(x, centre));
                    AddBody(column, moon, new Vector2(x, centre), size);
                    lineFrom = centre - half - LabelGap - LabelHeight;
                    y -= 2f * half + LabelGap + LabelHeight + RowGap;

                    if (!Orbiting(moon).Any()) continue;
                    float subX = x + SubIndent;
                    AddLine(column, new Vector2(x, centre), new Vector2(subX, centre));
                    StackMoons(column, moon, subX, centre, ref y);
                }
            }

            List<BodyEntry> planets = Orbiting(anchor).ToList();

            // Planets line up with the star's centre; moons hang below their planet. Each column carries the
            // orbit line halfway across the gaps either side, so a page's columns join up into one line
            _rowCentre = -(Margin + StarSize / 2f);
            float x = Margin;
            RectTransform starColumn = NewColumn();
            AddBody(starColumn, anchor, new Vector2(x + StarSize / 2f, _rowCentre), SizeOf(anchor));
            if (planets.Count > 0)
                AddLine(starColumn, new Vector2(x + StarSize / 2f, _rowCentre), new Vector2(x + StarSize + ColumnGap / 2f, _rowCentre));
            _columns.Add((starColumn, x, x + StarSize));
            x += StarSize + ColumnGap;
            float bottom = _rowCentre - StarSize / 2f - LabelGap - LabelHeight;

            for (int i = 0; i < planets.Count; i++)
            {
                BodyEntry planet = planets[i];
                float planetSize = SizeOf(planet);
                float column = Mathf.Max(MinColumnWidth, ColumnWidth(planet, 0));
                float cx = x + column / 2f;
                RectTransform root = NewColumn();
                _columns.Add((root, x, x + column));

                bool last = i == planets.Count - 1;
                AddLine(root, new Vector2(x - ColumnGap / 2f, _rowCentre),
                    new Vector2(last ? cx : x + column + ColumnGap / 2f, _rowCentre));
                AddBody(root, planet, new Vector2(cx, _rowCentre), planetSize);
                float planetHalf = HalfHeight(planet, planetSize);
                float my = _rowCentre - planetHalf - LabelGap - LabelHeight - RowGap;
                if (Orbiting(planet).Any())
                {
                    StackMoons(root, planet, cx, _rowCentre - planetHalf - LabelGap - LabelHeight, ref my);
                }

                bottom = Mathf.Min(bottom, my + RowGap);
                x += column + ColumnGap;
            }

            _lineToPrevious = NewImage("Line To Previous Page", _content, null, LineColor);
            _lineToNext = NewImage("Line To Next Page", _content, null, LineColor);
            foreach (Image line in new[] { _lineToPrevious, _lineToNext })
            {
                line.raycastTarget = false;
                var lineRect = (RectTransform)line.transform;
                lineRect.anchorMin = lineRect.anchorMax = new Vector2(0f, 1f);
                lineRect.SetAsFirstSibling();
            }

            _contentWidth = x - ColumnGap + Margin;
            _contentHeight = -(bottom - Margin);
            _top = top;
            _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);
            _content.localScale = Vector3.one;
            _content.anchoredPosition = new Vector2(0f, -top);
            _paging = false;
            _page = 0;
            _pagedSize = new Vector2(-1f, -1f);
            foreach (EdgeHint hint in new[] { _leftHint, _rightHint })
            {
                var hintRect = (RectTransform)hint.transform;
                hintRect.offsetMax = new Vector2(hintRect.offsetMax.x, -top);
            }

            // Keep the selection across rebuilds and system changes; start on the home planet
            List<BodyEntry> all = _roots.SelectMany(b => new[] { b }.Concat(Descendants(b))).ToList();
            BodyEntry body = all.FirstOrDefault(b => b.BodyName == _selected)
                             ?? all.FirstOrDefault(b => b.BodyName == HomeBody)
                             ?? Orbiting(anchor).FirstOrDefault() ?? anchor;
            _selected = body.BodyName;
            SelectedBody = body;
            foreach (BodyNode node in _nodes) node.SetSelected(node.Body.BodyName == _selected);
            Selected?.Invoke(body);
        }

        private void BuildTitle()
        {
            Transform title = transform.Find("Title");
            if (_fullName == null)
            {
                _fullName = NewLabel("Full Name", title, Localization.Translate(Localization.FullTitle), FullNameFontSize);
                _fullName.color = Color.white;
                _fullName.overflowMode = TextOverflowModes.Overflow;
            }

            // The sidebar's font may only have become available since the last layout
            TextMeshProUGUI font = FontSource();
            if (font != null)
            {
                _fullName.font = font.font;
                _fullName.fontSharedMaterial = font.fontSharedMaterial;
            }
        }

        // A row of system names along the top; the shown one is highlighted
        private void BuildPicker(List<BodyEntry> systems, BodyEntry shown)
        {
            float x = 0f;
            foreach (BodyEntry system in systems)
            {
                TextMeshProUGUI label = NewLabel(system.BodyName, _picker, system.DisplayName, PickerFontSize);
                label.alignment = TextAlignmentOptions.Left;
                label.raycastTarget = true;
                float width = label.GetPreferredValues(system.DisplayName).x;
                var rect = (RectTransform)label.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, PickerHeight - 8f);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += width + PickerGap;

                label.gameObject.AddComponent<SystemButton>().Initialize(this, system.BodyName, label, system == shown);
            }
        }

        private void ShowSystem(string bodyName)
        {
            if (_system == bodyName) return;
            _system = bodyName;
            Layout();
        }

        private TextMeshProUGUI FontSource() => ResearchUi.FontSource(this);

        private void PlayHoverSound(Transform at) => ResearchUi.PlayHover(this, at);

        private void PlayPressSound(Transform at) => ResearchUi.PlayPress(this, at);

        private static IEnumerable<BodyEntry> Descendants(BodyEntry body) =>
            body.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

        // A group for one column, placed at the map's top left so its contents use the map's coordinates
        private RectTransform NewColumn()
        {
            RectTransform column = NewRect("Column", _content);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.sizeDelta = Vector2.zero;
            column.anchoredPosition = Vector2.zero;
            return column;
        }

        private void AddBody(Transform parent, BodyEntry body, Vector2 centre, float size)
        {
            RectTransform root = NewRect(body.BodyName, parent);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(size, size);
            root.anchoredPosition = centre;

            float ringSize = size + RingPadding * 2f;
            Image ring = NewImage("Ring", root, DashedRing(ringSize), Color.clear);
            ((RectTransform)ring.transform).sizeDelta = new Vector2(ringSize, ringSize);
            ring.raycastTarget = false;

            // The rings' back half is drawn behind the body and their front half over it
            var rings = BodyArt.Rings(body, size);
            if (rings != null)
            {
                Image back = NewImage("Rings Back", root, rings.Value.Back, Color.white);
                ((RectTransform)back.transform).sizeDelta = rings.Value.Size;
                back.raycastTarget = false;
            }

            // A placeholder circle until the body's art has loaded
            Image disc = NewImage("Body", root, Disc, body.IsStar ? StarColor : PlaceholderColor(body.BodyName));
            ((RectTransform)disc.transform).sizeDelta = new Vector2(size, size);

            if (rings != null)
            {
                Image front = NewImage("Rings Front", root, rings.Value.Front, Color.white);
                ((RectTransform)front.transform).sizeDelta = rings.Value.Size;
                front.raycastTarget = false;
            }

            TextMeshProUGUI label = NewLabel("Label", root, body.DisplayName, 18f);
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(Mathf.Max(size, MinColumnWidth + ColumnGap * 0.5f), LabelHeight);
            labelRect.anchoredPosition = new Vector2(0f, -(HalfHeight(body, size) + LabelGap + LabelHeight / 2f));

            BodyNode node = disc.gameObject.AddComponent<BodyNode>();
            node.Initialize(this, body, disc, ring);
            _nodes.Add(node);

            BodyArt.Request(body, size, art =>
            {
                if (art == null || disc == null) return;
                disc.sprite = art;
                // A star's art includes its glow, so it's wider than the disc
                float scale = body.IsStar ? BodyArt.StarArtScale(BodyArt.Pixels(size)) : 1f;
                ((RectTransform)disc.transform).sizeDelta = new Vector2(size, size) * scale;
                node.SetBaseColour(ArtTint);
            });
        }

        // How far a body (or its rings) reaches above and below its centre
        private static float HalfHeight(BodyEntry body, float size) =>
            Mathf.Max(size / 2f, size / 2f * BodyArt.RingReach(body) * BodyArt.RingTilt);

        // How wide a body (and its rings) is
        private static float Footprint(BodyEntry body, float size) =>
            Mathf.Max(size, size * BodyArt.RingReach(body) + 4f * BodyArt.Unit);

        private TextMeshProUGUI NewLabel(string name, Transform parent, string text, float fontSize)
        {
            var label = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = LabelColor;
            label.raycastTarget = false;
            TextMeshProUGUI font = FontSource();
            if (font != null)
            {
                label.font = font.font;
                label.fontSharedMaterial = font.fontSharedMaterial;
            }

            label.fontSize = fontSize;
            return label;
        }

        private void AddLine(Transform parent, Vector2 from, Vector2 to)
        {
            Image line = NewImage("Line", parent, null, LineColor);
            line.raycastTarget = false;
            var rect = (RectTransform)line.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = (from + to) / 2f;
            Vector2 span = to - from;
            rect.sizeDelta = new Vector2(Mathf.Max(Mathf.Abs(span.x), LineWidth), Mathf.Max(Mathf.Abs(span.y), LineWidth));
            // Behind the bodies
            rect.SetAsFirstSibling();
        }

        private void Select(BodyNode node)
        {
            _selected = node.Body.BodyName;
            SelectedBody = node.Body;
            foreach (BodyNode other in _nodes) other.SetSelected(other == node);
            Selected?.Invoke(node.Body);
        }

        // Stand-in until the body art: a steady colour per body
        private static Color PlaceholderColor(string bodyName)
        {
            int hash = bodyName.Aggregate(17, (h, c) => h * 31 + c);
            float hue = (hash & 0xffff) / 65535f;
            return Color.HSVToRGB(hue, 0.35f, 0.75f);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        private static Sprite FadeLeft => _fadeLeft ??= FadeSprite(towardsLeft: true);
        private static Sprite FadeRight => _fadeRight ??= FadeSprite(towardsLeft: false);
        private static Sprite ChevronLeft => _chevronLeft ??= ChevronSprite(pointsLeft: true);
        private static Sprite ChevronRight => _chevronRight ??= ChevronSprite(pointsLeft: false);

        // White, opaque at the page edge and clear towards the middle; tinted by the Image
        private static Sprite FadeSprite(bool towardsLeft)
        {
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < width; x++)
            {
                float t = (x + 0.5f) / width;
                float a = Mathf.SmoothStep(0f, 1f, towardsLeft ? 1f - t : t);
                texture.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f));
        }

        // A smooth-edged white chevron, twice as tall as it is wide
        private static Sprite ChevronSprite(bool pointsLeft)
        {
            const int w = 32, h = 64;
            const float stroke = 3.2f;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var tip = new Vector2(pointsLeft ? 6f : w - 6f, h / 2f);
            var top = new Vector2(pointsLeft ? w - 6f : 6f, h - 6f);
            var bottom = new Vector2(top.x, 6f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistanceToSegment(p, top, tip), DistanceToSegment(p, tip, bottom));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(stroke - d)));
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Sprite Disc => _disc ??= CircleSprite(0f);

        // One per ring size, so every ring has the same dash length and an even number of dashes all round
        private static Sprite DashedRing(float diameter)
        {
            int key = Mathf.RoundToInt(diameter);
            if (DashedRings.TryGetValue(key, out Sprite cached)) return cached;

            // Drawn at twice the canvas size for smooth edges
            const float scale = 2f;
            int size = Mathf.CeilToInt(key * scale);
            float centre = size / 2f;
            float stroke = RingStroke * scale;
            float radius = centre - stroke / 2f - 1f;
            float circumference = 2f * Mathf.PI * radius;
            int dashes = Mathf.Max(4, Mathf.RoundToInt(circumference / ((RingDash + RingGap) * scale)));
            float period = circumference / dashes;
            float dash = period * RingDash / (RingDash + RingGap);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var offset = new Vector2(x + 0.5f - centre, y + 0.5f - centre);
                float across = Mathf.Clamp01(stroke / 2f + 0.5f - Mathf.Abs(offset.magnitude - radius));
                float along = (Mathf.Atan2(offset.y, offset.x) + Mathf.PI) * radius % period;
                float inDash = Mathf.Clamp01(Mathf.Min(along + 0.5f, dash - along + 0.5f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, across * inDash));
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * scale);
            DashedRings[key] = sprite;
            return sprite;
        }

        // A smooth-edged white circle (or ring, inner radius as a fraction of the outer), tinted by the Image
        private static Sprite CircleSprite(float inner)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d) * (inner > 0f ? Mathf.Clamp01(d - r * inner) : 1f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// A fade and arrow at one edge of the map, shown while bodies are off the page on that side. Clicking
        /// it slides the map over.
        /// </summary>
        private class EdgeHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            private SystemMap _map;
            private int _direction;
            private CanvasGroup _group;
            private Image _chevron;
            private bool _shown;

            public void Initialize(SystemMap map, int direction, CanvasGroup group, Image chevron)
            {
                _map = map;
                _direction = direction;
                _group = group;
                _chevron = chevron;
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
            }

            public void SetShown(bool shown)
            {
                _shown = shown;
                _group.blocksRaycasts = shown;
            }

            private void Update()
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, _shown ? 1f : 0f, Time.unscaledDeltaTime / HintFadeTime);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _chevron.color = ChevronHoverColor;
                _map.PlayHoverSound(transform);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _chevron.color = ChevronColor;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left) return;
                _map.PlayPressSound(transform);
                _map.Page(_direction);
            }
        }

        /// <summary>
        /// A system name in the picker: brightens on hover, and shows that system when clicked.
        /// </summary>
        private class SystemButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            private SystemMap _map;
            private string _system;
            private TextMeshProUGUI _label;
            private bool _shown;

            public void Initialize(SystemMap map, string system, TextMeshProUGUI label, bool shown)
            {
                _map = map;
                _system = system;
                _label = label;
                _shown = shown;
                _label.color = shown ? PickerSelectedColor : PickerColor;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_shown) return;
                _label.color = PickerHoverColor;
                _map.PlayHoverSound(transform);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (!_shown) _label.color = PickerColor;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_shown || eventData.button != PointerEventData.InputButton.Left) return;
                _map.PlayPressSound(transform);
                _map.ShowSystem(_system);
            }
        }

        /// <summary>
        /// One clickable body: brightens on hover and shows a dashed ring when selected, like the tech tree's
        /// nodes, with the stock UI sounds.
        /// </summary>
        private class BodyNode : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            private static readonly Color SelectedRing = Color.white;
            // Hovering brightens the body, as it does a tech node; the ring is only for the selection
            private const float HoverBrighten = 0.3f;

            private SystemMap _map;
            private Image _body, _ring;
            private Color _bodyColour;
            private bool _hovered, _selected;

            public BodyEntry Body { get; private set; }

            public void Initialize(SystemMap map, BodyEntry body, Image disc, Image ring)
            {
                _map = map;
                Body = body;
                _body = disc;
                _bodyColour = disc.color;
                _ring = ring;
                Refresh();
            }

            public void SetBaseColour(Color colour)
            {
                _bodyColour = colour;
                Refresh();
            }

            public void SetSelected(bool selected)
            {
                _selected = selected;
                Refresh();
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _hovered = true;
                Refresh();
                _map.PlayHoverSound(transform);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _hovered = false;
                Refresh();
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left) return;

                _map.PlayPressSound(transform);
                _map.Select(this);
            }

            private void Refresh()
            {
                if (_ring != null) _ring.color = _selected ? SelectedRing : Color.clear;
                if (_body != null)
                {
                    Color colour = _hovered ? Color.Lerp(_bodyColour, Color.white, HoverBrighten) : _bodyColour;
                    colour.a = _bodyColour.a;
                    _body.color = colour;
                }
            }
        }
    }
}
