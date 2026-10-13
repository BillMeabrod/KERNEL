using System;
using System.Collections.Generic;
using System.Linq;
using KERNEL.Ledger;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// The K.E.R.N.E.L page's map of the solar system, after the last tech tier: the star on the left, its planets
    /// in a row along one orbit line, and each planet's moons (and theirs) hanging below it. Bodies are drawn as
    /// pixel art (see <see cref="BodyArt"/>) at sizes that follow their radius. Clicking one selects it for the
    /// sidebar. With more than one star, a picker along the top switches system; a system wider than the page is
    /// shrunk a little to fit, or split into pages of whole planet columns with arrows at the edges.
    ///
    /// Drawn in UI Toolkit, the Redux way: a <see cref="SystemMapPanel"/> window laid over the tech tree's view,
    /// with buttons that take UitkForKsp2's UI sounds. The component itself sits on an empty rect the prefab patch
    /// adds to the tree's view, which it keeps just after the end of the tree as the tree scrolls and slides; the
    /// window's page follows that rect.
    /// </summary>
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
        // The selection outline: dashed like the tech tree's selection outline, a little outside the body and its
        // rings
        private const float RingPadding = 7f;
        private const float RingStroke = 2f;
        private const float RingDash = 10f;
        private const float RingGap = 6f;
        private const float LineWidth = 2f;
        // How far right moons of moons sit from the moon they orbit
        private const float SubIndent = 56f;
        // The page title (the full name) and, with more than one star, the system picker take this much of the top
        private const float TitleHeight = 44f;
        private const float PickerHeight = 44f;
        // Selected when the page first opens
        private const string HomeBody = "Kerbin";
        // An arrow click slides to the next page of whole planet columns, over the stock tier scroll's time
        private const float PageTime = 0.25f;
        // A system a little too big for the page is shrunk to fit on it, down to this. One that wouldn't fit even
        // then is shown at full size, split into pages
        private const float MinFitScale = 0.85f;
        // How far an orbit line runs past the page edge when more planets follow on another page
        private const float LineOverrun = 40f;

        private static readonly Color StarColor = new(1f, 0.84f, 0.47f);
        // The deep sky of the starfield, so an edge arrow's fade reads as the edge of the page
        private static readonly Color HintFadeColor = new(0.03f, 0.035f, 0.08f, 0.9f);
        // Art is drawn a touch below full brightness, so hovering can brighten it
        private static readonly Color ArtTint = new(0.86f, 0.86f, 0.86f, 1f);

        // Set by the prefab patch
        [SerializeField] private RectTransform _treeContent;

        private SystemMapPanel _panel;
        private VisualElement _content, _picker, _leftHint, _rightHint, _lineToPrevious, _lineToNext;

        // Sliding to a page after an arrow click
        private bool _paging;
        private float _pageFrom, _pageTo, _pageTime;
        private readonly List<BodyNode> _nodes = new();
        // The star's and each planet's column, left to right, each holding its bodies and its stretch of the orbit
        // line; pages break between them so none is cut in half, and a page shows only its own
        private readonly List<(VisualElement Root, float Left, float Right)> _columns = new();
        private float _rowCentre;
        private float _contentWidth, _contentHeight, _top;
        // Index of the first column on each page, which page is shown, and where the content is on it
        private readonly List<int> _pageFirst = new();
        private int _page;
        private float _contentX, _scale = 1f;
        private Vector2 _pagedSize = new(-1f, -1f);
        private List<BodyEntry> _roots = new();
        private string _selected;
        // The star (or other top-level body) whose system is shown
        private string _system;
        private static readonly Dictionary<(int, int), Sprite> DashedRings = new();
        private static Sprite _disc, _fadeLeft, _fadeRight, _chevronLeft, _chevronRight;

        /// <summary>
        /// Raised when the selected body changes, including when a ledger rebuild replaces its entry.
        /// </summary>
        public event Action<BodyEntry> Selected;

        /// <summary>
        /// The selected body; the home planet until the player picks another.
        /// </summary>
        public BodyEntry SelectedBody { get; private set; }

        // What the current layout was built from (see Shape)
        private string _laidOutShape;

        // Counts layouts, so art arriving for one that's since been replaced is dropped
        private int _layout;

        // The largest non-star radius across every system, which body sizes are scaled from
        private double _largest;

        // The bodies whose art has been asked for ahead of their system being shown (see PrepareOtherSystems)
        private string _preparedShape;

        private void Awake()
        {
            _panel = SystemMapPanel.Attach((RectTransform)transform);
            _panel.Ready += OnPanelReady;
            BodyArt.RingColoursRead += OnRingColoursRead;
        }

        private void OnDestroy() => BodyArt.RingColoursRead -= OnRingColoursRead;

        // Ring colours are normally read while the save loads; if a shown body's arrive later, its rings were
        // drawn in the plain colours, so the map is drawn again
        private void OnRingColoursRead(string body)
        {
            if (_content == null || _nodes.All(node => node.Body.BodyName != body)) return;
            _laidOutShape = null;
            Build(_roots);
        }

        private void OnPanelReady()
        {
            VisualElement page = _panel.Page;
            _content = page.Q("content");
            _picker = page.Q("picker");
            _leftHint = SetUpHint(page.Q("hint-left"), -1, FadeLeft, ChevronLeft);
            _rightHint = SetUpHint(page.Q("hint-right"), 1, FadeRight, ChevronRight);

            // Anything built before the window was up is drawn now
            _laidOutShape = null;
            Build(_roots);
        }

        private VisualElement SetUpHint(VisualElement hint, int direction, Sprite fade, Sprite chevron)
        {
            // Set on the element, not in the stylesheet, so the theme's button look (its height, background and
            // tint) can't win: the arrow runs the page's full height below the header, and only its fade shows
            hint.style.backgroundColor = Color.clear;
            hint.style.unityBackgroundImageTintColor = HintFadeColor;
            hint.style.backgroundImage = new StyleBackground(fade);
            hint.style.minHeight = StyleKeyword.None;
            hint.style.maxHeight = StyleKeyword.None;
            hint.Q("chevron").style.backgroundImage = new StyleBackground(chevron);
            hint.pickingMode = PickingMode.Ignore;
            ((Button)hint).clicked += () => Page(direction);
            return hint;
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

            // The window follows; nothing more to do while the page is out of view
            if (_panel == null || !_panel.Place(out Vector2 pageSize) || _content == null) return;

            if (_paging)
            {
                _pageTime += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pageTime / PageTime));
                SetContentX(Mathf.Lerp(_pageFrom, _pageTo, t));
                if (t >= 1f)
                {
                    _paging = false;
                    ShowPageColumns();
                }
            }

            // How much fits depends on the page's size
            if (pageSize != _pagedSize)
            {
                FitToPage(pageSize);
                _paging = false;
                SetContentX(PageOffset(_page));
                ShowPageColumns();
            }
        }

        private void SetContentX(float x)
        {
            if (Mathf.Approximately(x, _contentX)) return;
            _contentX = x;
            _content.style.left = x;
        }

        // Shrinks the map onto one page if that's enough, and otherwise splits it into pages of whole columns
        private void FitToPage(Vector2 page)
        {
            _pagedSize = page;
            _pageFirst.Clear();
            _pageFirst.Add(0);
            if (_columns.Count == 0 || page.x <= 0f) return;

            // The arrows run from below the header to the foot of the page
            foreach (VisualElement hint in new[] { _leftHint, _rightHint }) hint.style.height = Mathf.Max(0f, page.y - _top);

            float scale = Mathf.Min(1f, page.x / _contentWidth, (page.y - _top) / _contentHeight);
            if (scale < MinFitScale) scale = 1f;
            _scale = scale;
            _content.style.scale = new Scale(new Vector3(scale, scale, 1f));

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
        private float PageOffset(int page) => -PageStart(_pageFirst[page]) * _scale;

        private int PageEnd(int page) => page + 1 < _pageFirst.Count ? _pageFirst[page + 1] : _columns.Count;

        // Shows the shown page's columns (and, while sliding, the one it's leaving), with the orbit line running
        // off whichever edges have more planets beyond, and the arrows where they do
        private void ShowPageColumns(int alsoPage = -1)
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                bool onPage = i >= _pageFirst[_page] && i < PageEnd(_page);
                bool onOther = alsoPage >= 0 && i >= _pageFirst[alsoPage] && i < PageEnd(alsoPage);
                _columns[i].Root.style.display = onPage || onOther ? DisplayStyle.Flex : DisplayStyle.None;
            }

            float start = PageStart(_pageFirst[_page]);
            float end = start + _pagedSize.x / _scale;
            bool hasPrevious = _page > 0, hasNext = _page < _pageFirst.Count - 1;

            PlaceLine(_lineToPrevious, hasPrevious, new Vector2(start - LineOverrun, _rowCentre),
                new Vector2(hasPrevious ? _columns[_pageFirst[_page]].Left : start, _rowCentre));
            PlaceLine(_lineToNext, hasNext, new Vector2(hasNext ? _columns[PageEnd(_page) - 1].Right : end, _rowCentre),
                new Vector2(end + LineOverrun, _rowCentre));

            // Arrows only where the row of planets carries on past the page
            ShowHint(_leftHint, hasPrevious);
            ShowHint(_rightHint, hasNext);
        }

        private static void ShowHint(VisualElement hint, bool shown)
        {
            hint.EnableInClassList("kernel-map__hint--shown", shown);
            hint.pickingMode = shown ? PickingMode.Position : PickingMode.Ignore;
        }

        private static void PlaceLine(VisualElement line, bool shown, Vector2 from, Vector2 to)
        {
            if (line == null) return;
            line.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (shown) SetLine(line, from, to);
        }

        // Slides the map a page left (-1) or right (+1)
        private void Page(int direction)
        {
            int page = Mathf.Clamp(_page + direction, 0, _pageFirst.Count - 1);
            if (page == _page) return;

            int leaving = _page;
            _page = page;
            ShowPageColumns(alsoPage: leaving);
            _pageFrom = _contentX;
            _pageTo = PageOffset(_page);
            _pageTime = 0f;
            _paging = true;
        }

        // The ledger is rebuilt whenever the screen opens and after every scored report, but the map only changes
        // when the bodies themselves do; otherwise the laid-out nodes just take the fresh entries
        private void Build(IEnumerable<BodyEntry> roots)
        {
            _roots = roots?.ToList() ?? new List<BodyEntry>();
            string shape = Shape(_roots);
            if (shape == _laidOutShape && _nodes.Count > 0)
            {
                Rebind();
                return;
            }

            Layout();
            if (_content != null) _laidOutShape = shape;
        }

        // Everything the layout depends on: which bodies there are, how they nest and order, their names and sizes
        private static string Shape(IEnumerable<BodyEntry> bodies)
        {
            var shape = new System.Text.StringBuilder();
            foreach (BodyEntry body in bodies)
            {
                shape.Append(body.BodyName).Append('|').Append(body.DisplayName).Append('|').Append(body.IsStar)
                    .Append('|').Append(body.Radius).Append('|');
                foreach ((double inner, double outer) in body.Rings) shape.Append(inner).Append('-').Append(outer).Append(',');
                shape.Append('(').Append(Shape(body.Children)).Append(')');
            }

            return shape.ToString();
        }

        // Points the nodes at the rebuilt entries and tells the sidebar, without laying anything out again
        private void Rebind()
        {
            Dictionary<string, BodyEntry> byName = _roots.SelectMany(b => new[] { b }.Concat(Descendants(b)))
                .ToDictionary(b => b.BodyName);
            foreach (BodyNode node in _nodes)
                if (byName.TryGetValue(node.Body.BodyName, out BodyEntry fresh)) node.Body = fresh;

            if (_selected == null || !byName.TryGetValue(_selected, out BodyEntry selected)) return;
            SelectedBody = selected;
            Selected?.Invoke(selected);
        }

        private void Layout()
        {
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

            // Keep the selection across rebuilds and system changes; start on the home planet. The sidebar needs
            // it even before the window is up
            List<BodyEntry> all = _roots.SelectMany(b => new[] { b }.Concat(Descendants(b))).ToList();
            BodyEntry selected = all.FirstOrDefault(b => b.BodyName == _selected)
                                 ?? all.FirstOrDefault(b => b.BodyName == HomeBody)
                                 ?? Orbiting(anchor).FirstOrDefault() ?? anchor;
            _selected = selected.BodyName;
            SelectedBody = selected;

            if (_content != null) Draw(systems, anchor);
            Selected?.Invoke(selected);
        }

        // Stars orbiting a body are systems of their own
        private static IEnumerable<BodyEntry> Orbiting(BodyEntry body) => body.Children.Where(child => !child.IsStar);

        private void Draw(List<BodyEntry> systems, BodyEntry anchor)
        {
            _layout++;
            _content.Clear();
            _picker.Clear();
            _nodes.Clear();
            _columns.Clear();

            float top = TitleHeight;
            _picker.style.display = systems.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (systems.Count > 1)
            {
                BuildPicker(systems, anchor);
                top += PickerHeight;
            }

            _largest = _roots.SelectMany(b => new[] { b }.Concat(Descendants(b)))
                .Where(b => !b.IsStar).Select(b => b.Radius).DefaultIfEmpty(0).Max();

            // How wide a planet's column needs to be for it, its moons and theirs
            float ColumnWidth(BodyEntry body, int depth) =>
                Orbiting(body).Select(child => ColumnWidth(child, depth + 1)).Append(Footprint(body, SizeOf(body)) + 2f * SubIndent * depth).Max();

            // Stacks a body's moons downward from y at x, then their moons further right below each. The line
            // down to each runs from just below the name above it, so it never crosses a name
            void StackMoons(VisualElement column, BodyEntry parent, float x, float lineFrom, ref float y)
            {
                foreach (BodyEntry moon in Orbiting(parent))
                {
                    float size = SizeOf(moon);
                    float half = HalfHeight(moon, size);
                    float centre = y + half;
                    AddLine(column, new Vector2(x, lineFrom), new Vector2(x, centre));
                    AddBody(column, moon, new Vector2(x, centre), size);
                    lineFrom = centre + half + LabelGap + LabelHeight;
                    y += 2f * half + LabelGap + LabelHeight + RowGap;

                    if (!Orbiting(moon).Any()) continue;
                    float subX = x + SubIndent;
                    AddLine(column, new Vector2(x, centre), new Vector2(subX, centre));
                    StackMoons(column, moon, subX, centre, ref y);
                }
            }

            List<BodyEntry> planets = Orbiting(anchor).ToList();

            // Planets line up with the star's centre; moons hang below their planet. Each column carries the
            // orbit line halfway across the gaps either side, so a page's columns join up into one line. In the
            // content's coordinates y runs down from its top
            _rowCentre = Margin + StarSize / 2f;
            float x = Margin;
            VisualElement starColumn = NewColumn();
            AddBody(starColumn, anchor, new Vector2(x + StarSize / 2f, _rowCentre), SizeOf(anchor));
            if (planets.Count > 0)
                AddLine(starColumn, new Vector2(x + StarSize / 2f, _rowCentre), new Vector2(x + StarSize + ColumnGap / 2f, _rowCentre));
            _columns.Add((starColumn, x, x + StarSize));
            x += StarSize + ColumnGap;
            float bottom = _rowCentre + StarSize / 2f + LabelGap + LabelHeight;

            for (int i = 0; i < planets.Count; i++)
            {
                BodyEntry planet = planets[i];
                float planetSize = SizeOf(planet);
                float column = Mathf.Max(MinColumnWidth, ColumnWidth(planet, 0));
                float cx = x + column / 2f;
                VisualElement root = NewColumn();
                _columns.Add((root, x, x + column));

                bool last = i == planets.Count - 1;
                AddLine(root, new Vector2(x - ColumnGap / 2f, _rowCentre),
                    new Vector2(last ? cx : x + column + ColumnGap / 2f, _rowCentre));
                AddBody(root, planet, new Vector2(cx, _rowCentre), planetSize);
                float planetHalf = HalfHeight(planet, planetSize);
                float my = _rowCentre + planetHalf + LabelGap + LabelHeight + RowGap;
                if (Orbiting(planet).Any())
                    StackMoons(root, planet, cx, _rowCentre + planetHalf + LabelGap + LabelHeight, ref my);

                bottom = Mathf.Max(bottom, my - RowGap);
                x += column + ColumnGap;
            }

            _lineToPrevious = NewLine();
            _lineToNext = NewLine();
            _content.Insert(0, _lineToPrevious);
            _content.Insert(0, _lineToNext);

            _contentWidth = x - ColumnGap + Margin;
            _contentHeight = bottom + Margin;
            _top = top;
            _content.style.top = top;
            _content.style.width = _contentWidth;
            _content.style.height = _contentHeight;
            _content.style.scale = new Scale(Vector3.one);
            _scale = 1f;
            _contentX = float.NaN;
            SetContentX(0f);
            _paging = false;
            _page = 0;
            _pagedSize = new Vector2(-1f, -1f);
            foreach (VisualElement hint in new[] { _leftHint, _rightHint })
            {
                hint.style.top = top;
                ShowHint(hint, false);
            }

            foreach (BodyNode node in _nodes) node.SetSelected(node.Body.BodyName == _selected);
            PrepareOtherSystems(systems, anchor);
        }

        // Asks for the other systems' art (and rings) right after the shown one's, so it's ready by the time the
        // picker switches to them. Sizes don't depend on which system is shown, so it's the same art they'll use.
        // Done once per set of bodies; art is made one body per frame behind the shown system's
        private void PrepareOtherSystems(List<BodyEntry> systems, BodyEntry shown)
        {
            if (_laidOutShape != null && _preparedShape == Shape(_roots)) return;
            _preparedShape = Shape(_roots);

            foreach (BodyEntry system in systems.Where(s => s != shown))
            foreach (BodyEntry body in new[] { system }.Concat(Orbiting(system).SelectMany(b => new[] { b }.Concat(Descendants(b)))))
            {
                float size = SizeOf(body);
                BodyArt.Request(body, size, _ => { });
                BodyArt.Rings(body, size);
            }
        }

        private float SizeOf(BodyEntry body)
        {
            if (body.IsStar) return StarSize;
            if (_largest <= 0) return MinBodySize;
            return Mathf.Max(MinBodySize, MaxBodySize * (float)Math.Sqrt(body.Radius / _largest));
        }

        // A row of system names along the top; the shown one is highlighted
        private void BuildPicker(List<BodyEntry> systems, BodyEntry shown)
        {
            foreach (BodyEntry system in systems)
            {
                var button = new Button { text = system.DisplayName };
                button.AddToClassList("kernel-map__system");
                if (system == shown)
                {
                    // Already showing, so it neither does nor sounds like anything
                    button.AddToClassList("kernel-map__system--shown");
                    button.AddToClassList("ui-sound-none");
                }
                else
                {
                    string name = system.BodyName;
                    button.clicked += () => ShowSystem(name);
                }

                _picker.Add(button);
            }
        }

        private void ShowSystem(string bodyName)
        {
            if (_system == bodyName) return;
            _system = bodyName;
            Layout();
        }

        private static IEnumerable<BodyEntry> Descendants(BodyEntry body) =>
            body.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

        // A group for one column, at the content's top left so its contents use the content's coordinates
        private VisualElement NewColumn()
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("kernel-map__column");
            _content.Add(column);
            return column;
        }

        private void AddBody(VisualElement parent, BodyEntry body, Vector2 centre, float size)
        {
            var button = new Button();
            button.AddToClassList("kernel-map__body");
            Place(button, centre, new Vector2(size, size));

            // The rings' back half is drawn behind the body and their front half over it
            var rings = BodyArt.Rings(body, size);
            if (rings != null) button.Add(NewSprite("kernel-map__rings", rings.Value.Back, size, rings.Value.Size));

            // A placeholder circle until the body's art has loaded
            VisualElement disc = NewSprite("kernel-map__art", Disc, size, new Vector2(size, size));
            disc.style.unityBackgroundImageTintColor = body.IsStar ? StarColor : PlaceholderColor(body.BodyName);
            button.Add(disc);

            // Hovering lays a white copy of the body's shape over it, which brightens it past what a tint can
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.AddToClassList("kernel-map__glow");
            disc.Add(glow);

            if (rings != null) button.Add(NewSprite("kernel-map__rings", rings.Value.Front, size, rings.Value.Size));

            // A circle around a body; around a ringed one, an ellipse taking in its rings too
            var outline = new Vector2(size * BodyArt.RingReach(body), HalfHeight(body, size) * 2f) + Vector2.one * (RingPadding * 2f);
            button.Add(NewSprite("kernel-map__outline", DashedRing(outline), size, outline));

            var label = new Label(body.DisplayName) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("kernel-map__label");
            float labelWidth = Mathf.Max(size, MinColumnWidth + ColumnGap * 0.5f);
            label.style.width = labelWidth;
            label.style.left = (size - labelWidth) / 2f;
            label.style.top = size / 2f + HalfHeight(body, size) + LabelGap;
            button.Add(label);

            var node = new BodyNode(body, disc, glow);
            button.RegisterCallback<PointerEnterEvent>(_ => node.SetHovered(true));
            button.RegisterCallback<PointerLeaveEvent>(_ => node.SetHovered(false));
            button.clicked += () => Select(node);
            node.Button = button;
            _nodes.Add(node);
            parent.Add(button);

            // Art can arrive before the window has first been shown (and its panel attached), so it's kept unless
            // this layout has been replaced since
            int layout = _layout;
            BodyArt.Request(body, size, art =>
            {
                if (art == null || layout != _layout) return;
                // A star's art includes its glow, so it's wider than the disc
                float scale = body.IsStar ? BodyArt.StarArtScale(BodyArt.Pixels(size)) : 1f;
                SetSprite(disc, art, size, new Vector2(size, size) * scale);
                node.SetBaseColour(ArtTint);
            });
        }

        // An element showing a sprite, centred in a body's box of the given size
        private static VisualElement NewSprite(string className, Sprite sprite, float box, Vector2 size)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            SetSprite(element, sprite, box, size);
            return element;
        }

        private static void SetSprite(VisualElement element, Sprite sprite, float box, Vector2 size)
        {
            element.style.backgroundImage = new StyleBackground(sprite);
            element.style.width = size.x;
            element.style.height = size.y;
            element.style.left = (box - size.x) / 2f;
            element.style.top = (box - size.y) / 2f;
        }

        // Places an absolutely positioned element by its centre
        private static void Place(VisualElement element, Vector2 centre, Vector2 size)
        {
            element.style.left = centre.x - size.x / 2f;
            element.style.top = centre.y - size.y / 2f;
            element.style.width = size.x;
            element.style.height = size.y;
        }

        // How far a body (or its rings) reaches above and below its centre
        private static float HalfHeight(BodyEntry body, float size) =>
            Mathf.Max(size / 2f, size / 2f * BodyArt.RingReach(body) * BodyArt.RingTilt);

        // How wide a body (and its rings) is
        private static float Footprint(BodyEntry body, float size) =>
            Mathf.Max(size, size * BodyArt.RingReach(body) + 4f * BodyArt.Unit);

        private static VisualElement NewLine()
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("kernel-map__line");
            return line;
        }

        private static void AddLine(VisualElement parent, Vector2 from, Vector2 to)
        {
            VisualElement line = NewLine();
            SetLine(line, from, to);
            // Behind the bodies
            parent.Insert(0, line);
        }

        private static void SetLine(VisualElement line, Vector2 from, Vector2 to)
        {
            Vector2 span = to - from;
            Place(line, (from + to) / 2f, new Vector2(Mathf.Max(Mathf.Abs(span.x), LineWidth), Mathf.Max(Mathf.Abs(span.y), LineWidth)));
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

        private static Sprite FadeLeft => _fadeLeft ??= FadeSprite(towardsLeft: true);
        private static Sprite FadeRight => _fadeRight ??= FadeSprite(towardsLeft: false);
        private static Sprite ChevronLeft => _chevronLeft ??= ChevronSprite(pointsLeft: true);
        private static Sprite ChevronRight => _chevronRight ??= ChevronSprite(pointsLeft: false);
        private static Sprite Disc => _disc ??= CircleSprite();

        // White, opaque at the page edge and clear towards the middle; tinted by the style
        private static Sprite FadeSprite(bool towardsLeft)
        {
            const int width = 64;
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                float t = (x + 0.5f) / width;
                pixels[x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, towardsLeft ? 1f - t : t));
            }

            return NewSprite(width, 1, pixels);
        }

        // A smooth-edged white chevron, twice as tall as it is wide
        private static Sprite ChevronSprite(bool pointsLeft)
        {
            const int w = 32, h = 64;
            const float stroke = 3.2f;
            var tip = new Vector2(pointsLeft ? 6f : w - 6f, h / 2f);
            var top = new Vector2(pointsLeft ? w - 6f : 6f, h - 6f);
            var bottom = new Vector2(top.x, 6f);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistanceToSegment(p, top, tip), DistanceToSegment(p, tip, bottom));
                pixels[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(stroke - d));
            }

            return NewSprite(w, h, pixels);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // A smooth-edged white circle, tinted by the style
        private static Sprite CircleSprite()
        {
            const int size = 128;
            float r = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
            }

            return NewSprite(size, size, pixels);
        }

        // A dashed ellipse (a circle when both sides match), one per size, so every outline has the same dash
        // length and a whole number of dashes all round
        private static Sprite DashedRing(Vector2 outline)
        {
            (int, int) key = (Mathf.RoundToInt(outline.x), Mathf.RoundToInt(outline.y));
            if (DashedRings.TryGetValue(key, out Sprite cached)) return cached;

            // Drawn at twice the canvas size for smooth edges
            const float scale = 2f;
            int width = Mathf.CeilToInt(key.Item1 * scale), height = Mathf.CeilToInt(key.Item2 * scale);
            var centre = new Vector2(width / 2f, height / 2f);
            float stroke = RingStroke * scale;
            float a = centre.x - stroke / 2f - 1f, b = centre.y - stroke / 2f - 1f;

            // Distance along the outline at each angle, for spacing the dashes evenly around an ellipse
            const int steps = 720;
            var along = new float[steps + 1];
            for (int i = 1; i <= steps; i++)
            {
                float t0 = (i - 1) * 2f * Mathf.PI / steps, t1 = i * 2f * Mathf.PI / steps;
                along[i] = along[i - 1] + Vector2.Distance(new Vector2(a * Mathf.Cos(t0), b * Mathf.Sin(t0)), new Vector2(a * Mathf.Cos(t1), b * Mathf.Sin(t1)));
            }

            float perimeter = along[steps];
            int dashes = Mathf.Max(4, Mathf.RoundToInt(perimeter / ((RingDash + RingGap) * scale)));
            float period = perimeter / dashes;
            float dash = period * RingDash / (RingDash + RingGap);

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - centre;
                // How far the pixel is from the outline, by the ellipse's slope there
                float q = Mathf.Sqrt(offset.x * offset.x / (a * a) + offset.y * offset.y / (b * b));
                if (q <= 0f) continue;
                var slope = new Vector2(offset.x / (a * a), offset.y / (b * b)) / q;
                float across = Mathf.Clamp01(stroke / 2f + 0.5f - Mathf.Abs(q - 1f) / slope.magnitude);
                if (across <= 0f) continue;

                float t = Mathf.Repeat(Mathf.Atan2(offset.y / b, offset.x / a), 2f * Mathf.PI) / (2f * Mathf.PI) * steps;
                int i0 = Mathf.Min(steps - 1, (int)t);
                float position = Mathf.Lerp(along[i0], along[i0 + 1], t - i0) % period;
                float inDash = Mathf.Clamp01(Mathf.Min(position + 0.5f, dash - position + 0.5f));
                pixels[y * width + x] = new Color(1f, 1f, 1f, across * inDash);
            }

            Sprite sprite = NewSprite(width, height, pixels);
            DashedRings[key] = sprite;
            return sprite;
        }

        private static Sprite NewSprite(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// One body on the map: brightens on hover and shows a dashed outline when selected, like the tech tree's
        /// nodes. Its button takes UitkForKsp2's UI sounds.
        /// </summary>
        private class BodyNode
        {
            private readonly VisualElement _disc, _glow;
            private Color _baseColour;
            private bool _hovered;
            // The art the glow's shape was made from; it's made on the first hover, not for every body up front
            private Sprite _glowSource;

            public BodyNode(BodyEntry body, VisualElement disc, VisualElement glow)
            {
                Body = body;
                _disc = disc;
                _glow = glow;
                _baseColour = disc.style.unityBackgroundImageTintColor.value;
            }

            public BodyEntry Body { get; set; }

            public Button Button { get; set; }

            public void SetBaseColour(Color colour)
            {
                _baseColour = colour;
                Refresh();
            }

            public void SetSelected(bool selected) => Button.EnableInClassList("kernel-map__body--selected", selected);

            public void SetHovered(bool hovered)
            {
                _hovered = hovered;
                Refresh();
            }

            // Hovering takes the tint to full and shows the white copy of the body's shape over it
            private void Refresh()
            {
                _disc.style.unityBackgroundImageTintColor = _hovered ? Color.white : _baseColour;

                Sprite art = _disc.style.backgroundImage.value.sprite;
                if (_hovered && art != null && art != _glowSource)
                {
                    _glowSource = art;
                    // The placeholder circle is already white
                    _glow.style.backgroundImage = new StyleBackground(art == Disc ? Disc : BodyArt.Silhouette(art));
                }

                _glow.style.display = _hovered ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
