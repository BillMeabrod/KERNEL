using System.Collections.Generic;
using System.Linq;
using KERNEL.Ledger;
using KSP.Game;
using KSP.Game.Science;
using KSP.Messages;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// The selected body's science, shown over the R&D sidebar while K.E.R.N.E.L's page shows: its name, how many of
    /// its biomes have been explored and how much science has been gained there, then each biome with the
    /// experiments that can be run in it. Hovering over or selecting an experiment shows what it reported in the
    /// field at the bottom, where Paige sits in the stock sidebar; with only part of its science collected, part of
    /// the report is lost. Follows the same spoiler rules as the ledger window: only unlocked experiments, and
    /// "?????" for places not yet visited.
    ///
    /// Built the way Redux adds its tracking station panel to the map: a UI Toolkit window parented to the screen
    /// (so it lives and dies with it), a view model bound by Unity's data binding, rows made from UXML templates,
    /// and UitkForKsp2's localization and UI sounds. It covers the stock sidebar rather than changing it. Its panel
    /// isn't part of the screen's canvas, so it follows the game state the way the screen does: shown only in R&D.
    /// </summary>
    public class ResearchSidebar : MonoBehaviour
    {
        private const string UxmlAddress = "KERNEL/ResearchSidebar.uxml";
        private const string BiomeRowTemplate = "ResearchSidebarBiomeRow";
        private const string ExperimentRowTemplate = "ResearchSidebarExperimentRow";

        // Redux's sidebar, swapped in when the screen starts; ours is laid exactly over it
        private const string StockSidebarPath = "Container/TechNodeInfo";

        private RDCenterUIController _screen;
        private SystemMap _map;
        private BodyEntry _body;
        private bool _shown, _dirty;

        // The R&D screen stays alive when another building is opened; only the game state says it's showing
        private bool _inResearch = true;
        private SubscriptionHandle _onGameStateChange;

        private PanelRenderer _window;
        private VisualElement _panel;
        private ScrollView _list;
        private VisualElement _report;
        private ScrollView _reportScroll;
        private VisualTreeAsset _biomeRow, _experimentRow;
        private readonly ResearchSidebarViewModel _model = new();
        private Rect _placedAt;

        // The stock sidebar we're laid over; found once Redux has swapped it in
        private readonly UguiRect _stock = new();

        // Which biomes are open, by body and area, kept across refreshes
        private readonly HashSet<string> _open = new();

        // The field report shows the hovered experiment, or else the selected one; the selection is kept across
        // refreshes by key
        private ExperimentRowViewModel _hovered, _selected;
        private VisualElement _selectedRow;
        private string _selectedKey;

        public static ResearchSidebar Attach(RDCenterUIController screen, SystemMap map)
        {
            var sidebar = screen.gameObject.AddComponent<ResearchSidebar>();
            sidebar._screen = screen;
            sidebar._map = map;
            if (map != null)
            {
                map.Selected += sidebar.ShowBody;
                sidebar._body = map.SelectedBody;
            }

            sidebar._onGameStateChange = GameManager.Instance.Game.Messages.Subscribe<GameStateChangedMessage>(sidebar.OnGameStateChanged);
            GameManager.Instance.Assets.Load<VisualTreeAsset>(UxmlAddress, sidebar.CreateWindow);
            return sidebar;
        }

        private void CreateWindow(VisualTreeAsset uxml)
        {
            if (uxml == null || this == null) return;

            _biomeRow = uxml.templateDependencies.FirstOrDefault(t => t.name == BiomeRowTemplate);
            _experimentRow = uxml.templateDependencies.FirstOrDefault(t => t.name == ExperimentRowTemplate);

            WindowOptions options = WindowOptions.Default;
            options.WindowId = "KERNEL_ResearchSidebar";
            options.Parent = _screen.transform;
            options.UseStockScale = true;
            options.BringToFrontOnPointerDown = false;
            options.MoveOptions = new MoveOptions { IsMovingEnabled = false, CheckScreenBounds = false };
            _window = Window.Create(options, uxml);

            _window.OnPanelRoot(root =>
            {
                root.dataSource = _model;
                _panel = root.Q("kernel-sidebar");
                _list = root.Q<ScrollView>("biome-list");
                _report = root.Q("report");
                _reportScroll = root.Q<ScrollView>("report-scroll");
                _dirty = true;
            });
            _window.EnableLocalization();
            _window.EnableUiSounds();
            // As Redux does: the window's container covers the screen, and the panel is placed within it
            _window.OnWindowRoot(StretchWindowContainer);

            if (!IsVisible) _window.Hide();
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
            if (_map != null) _map.Selected -= ShowBody;
            if (GameManager.Instance?.Game?.Messages != null && !GameManager.Instance.Game.ShutdownInProgress)
                GameManager.Instance.Game.Messages.Unsubscribe(ref _onGameStateChange);
        }

        /// <summary>
        /// Shows the sidebar over the stock one, or hides it again.
        /// </summary>
        public void SetShown(bool shown)
        {
            if (shown == _shown) return;
            _shown = shown;
            UpdateVisibility();
        }

        private bool IsVisible => _shown && _inResearch;

        private void OnGameStateChanged(MessageCenterMessage message)
        {
            _inResearch = ((GameStateChangedMessage)message).CurrentState == GameState.ResearchAndDevelopment;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_window == null) return;

            if (IsVisible)
            {
                _window.Show();
                _dirty = true;
            }
            else _window.Hide();
        }

        private void ShowBody(BodyEntry body)
        {
            if (body != _body) _selectedKey = null;
            _body = body;
            _dirty = true;
        }

        /// <summary>
        /// Whether the pointer is over the biome list or the field report, whose scrolling shouldn't also scroll the page.
        /// </summary>
        public bool IsPointerOverList(Vector2 screenPoint)
        {
            if (!IsVisible || _list?.panel == null) return false;
            Vector2 point = RuntimePanelUtils.ScreenToPanel(_list.panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            return _list.worldBound.Contains(point) || (_reportScroll != null && _reportScroll.worldBound.Contains(point));
        }

        private void LateUpdate()
        {
            if (!IsVisible || _panel == null) return;

            PlaceOverStockSidebar();
            if (_dirty && _body != null) Refresh();
        }

        // Matches the stock sidebar's place and size on screen, whatever the resolution and UI scale
        private void PlaceOverStockSidebar()
        {
            if (!FindStockSidebar() || !_stock.TryGetPanelRect(_panel.panel, out Rect rect) || rect == _placedAt) return;

            _placedAt = rect;
            UguiRect.Place(_panel, rect);
        }

        // Redux swaps its own sidebar in when the screen starts, so this is looked up until it's there
        private bool FindStockSidebar()
        {
            if (_stock.Target != null) return true;
            if (!(_screen.transform.Find(StockSidebarPath) is RectTransform stock)) return false;

            _stock.Target = stock;
            return true;
        }

        private void Refresh()
        {
            _dirty = false;
            List<AreaEntry> areas = _body.ShownAreas().ToList();
            List<LocationEntry> locations = areas.SelectMany(a => a.UnlockedLocations).ToList();

            _model.BodyName = _body.DisplayName;
            _model.BiomesExplored = $"{areas.Count(a => a.HasSubmissions)} / {areas.Count}";
            _model.ScienceGained = Tally(locations.Sum(l => l.Earned), locations.Sum(l => l.Possible));

            // Keep the place in the list when the same body refreshes (e.g. after scoring a report)
            Vector2 scroll = _list.scrollOffset;
            _hovered = _selected = null;
            _selectedRow = null;
            _list.Clear();
            foreach (AreaEntry area in areas) _list.Add(BiomeRow(area));
            _list.schedule.Execute(() => _list.scrollOffset = scroll);
            if (_selected == null) _selectedKey = null;
            ShowReport();
        }

        private VisualElement BiomeRow(AreaEntry area)
        {
            List<LocationEntry> locations = area.UnlockedLocations.ToList();
            float earned = locations.Sum(l => l.Earned), possible = locations.Sum(l => l.Possible);
            string name = area.IsKnown ? area.DisplayName : Localization.Translate(Localization.UnknownArea);
            string key = $"{_body.BodyName}/{area.Id}";
            var model = new BiomeRowViewModel(name, Tally(earned, possible), area.IsKnown, possible > 0f && earned >= possible);

            TemplateContainer row = _biomeRow.Instantiate();
            row.dataSource = model;
            VisualElement biome = row.Q(className: "kernel-biome");
            if (!area.IsKnown) biome.AddToClassList("kernel-biome--unknown");
            if (model.IsComplete) row.Q("tally").AddToClassList("kernel--complete");

            if (area.IsKnown)
            {
                // A biome's experiments are only built once it's opened (a big body has hundreds), or when it holds
                // the selected one, so the selection and its report survive a refresh
                VisualElement experiments = row.Q("experiments");
                bool filled = false;
                void Fill()
                {
                    if (filled) return;
                    filled = true;
                    // Whole-body areas are named after their situation, so their rows don't repeat it
                    foreach (LocationEntry location in locations)
                        experiments.Add(ExperimentRow(location, $"{key}/{location.Location.ResearchLocationId}", name, showSituation: !area.IsWholeBody));
                }

                model.OpenChanged += open =>
                {
                    if (open) Fill();
                    biome.EnableInClassList("kernel-biome--open", open);
                    if (open) _open.Add(key);
                    else _open.Remove(key);
                };
                model.IsOpen = _open.Contains(key);
                if (_selectedKey != null && _selectedKey.StartsWith(key + "/")) Fill();
                row.Q<Button>("header").clicked += model.Toggle;
            }

            return row;
        }

        private VisualElement ExperimentRow(LocationEntry location, string key, string place, bool showSituation)
        {
            string situation = location.Location.ScienceSituation.GetTranslatedDescription();
            var model = new ExperimentRowViewModel(
                $"{key}/{location.Experiment.ExperimentID}",
                Localization.Translate(location.Experiment.DisplayName, location.Experiment.ExperimentID),
                showSituation ? $"{place}, {situation}" : place,
                showSituation ? situation : string.Empty,
                Tally(location.Earned, location.Possible),
                location.IsComplete,
                () => FlavorText(location),
                location.Possible > 0f ? location.Earned / location.Possible : 0f);

            TemplateContainer row = _experimentRow.Instantiate();
            row.dataSource = model;
            if (model.IsComplete) row.Q("tally").AddToClassList("kernel--complete");
            if (!showSituation) row.Q("situation").style.display = DisplayStyle.None;

            // Hovering over the row shows its field report; clicking it keeps it there until clicked again
            var button = row.Q<Button>("experiment");
            button.RegisterCallback<PointerEnterEvent>(_ => Hover(model, true));
            button.RegisterCallback<PointerLeaveEvent>(_ => Hover(model, false));
            button.clicked += () => Select(model, button);
            if (model.Key == _selectedKey) Select(model, button);

            return row;
        }

        private void Hover(ExperimentRowViewModel row, bool over)
        {
            if (over) _hovered = row;
            else if (_hovered == row) _hovered = null;
            ShowReport();
        }

        private void Select(ExperimentRowViewModel row, VisualElement element)
        {
            _selectedRow?.RemoveFromClassList("kernel-experiment--selected");
            bool deselect = _selected == row;
            _selected = deselect ? null : row;
            _selectedRow = deselect ? null : element;
            _selectedKey = _selected?.Key;
            _selectedRow?.AddToClassList("kernel-experiment--selected");
            ShowReport();
        }

        private void ShowReport()
        {
            if (_report == null) return;

            ExperimentRowViewModel row = _hovered ?? _selected;
            bool hasText = row != null && !string.IsNullOrEmpty(row.FlavorText);
            bool partial = hasText && !row.IsComplete && row.Received < 1f;

            _model.ReportExperiment = row?.Name ?? string.Empty;
            _model.ReportPlace = row?.Place ?? string.Empty;
            _model.ReportText = row == null ? Localization.Translate(Localization.FieldReportHint)
                : !hasText ? Localization.Translate(Localization.NoFieldReport)
                : partial ? Redact(row.FlavorText, row.Received, row.Key)
                : row.FlavorText;

            _report.EnableInClassList("kernel-report--no-title", row == null);
            _report.EnableInClassList("kernel-report--empty", !hasText);
            _report.EnableInClassList("kernel-report--partial", partial);
            _reportScroll.scrollOffset = Vector2.zero;
        }

        // A partly received report loses one run of words, as long as the share of science still to collect.
        // Where it falls is picked from the key, so the same report always reads the same.
        private static string Redact(string text, float received, string key)
        {
            string[] words = text.Split(' ');
            if (words.Length < 2) return text;

            int count = Mathf.Clamp(Mathf.RoundToInt(words.Length * (1f - received)), 1, words.Length - 1);
            int start = (int)(Hash(key) % (uint)(words.Length - count + 1));
            string lost = $"<color=#6D7387><noparse>{Localization.Translate(Localization.Indecipherable)}</noparse></color>";
            return string.Join(" ", words.Take(start).Append(lost).Concat(words.Skip(start + count)));
        }

        // FNV-1a, stable across runs (unlike string.GetHashCode)
        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }

        // The text each submitted report (data, then sample) came back with, as the game words it
        private static string FlavorText(LocationEntry location)
        {
            ScienceExperimentsDataStore store = GameManager.Instance?.Game?.ScienceManager?.ScienceExperimentsDataStore;
            if (store == null) return string.Empty;

            var texts = new List<string>();
            foreach ((ReportEntry report, ScienceReportType type) in new[] { (location.Data, ScienceReportType.DataType), (location.Sample, ScienceReportType.SampleType) })
            {
                if (report == null || !report.IsSubmitted) continue;
                string key = store.GetFlavorText(location.Experiment.ExperimentID, location.Location.ResearchLocationId, type);
                string text = Localization.Translate(key, string.Empty);
                if (!string.IsNullOrEmpty(text)) texts.Add(text);
            }

            return string.Join("\n\n", texts);
        }

        private static string Tally(float earned, float possible) => $"{earned:#,0.#} / {possible:#,0.#}";
    }
}
