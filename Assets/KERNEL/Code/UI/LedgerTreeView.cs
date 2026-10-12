using System.Collections.Generic;
using System.Linq;
using I2.Loc;
using KERNEL.Ledger;
using KSP.Game.Science;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// Shows the science ledger as a collapsible tree: body, then area (orbit, atmosphere, biome), then
    /// experiments. Applies the spoiler rules and keeps what's expanded across rebuilds. Hosts only need to
    /// provide a container, so the window and the R&D page can share it.
    /// </summary>
    public class LedgerTreeView
    {
        private const string UnknownAreaName = "?????";

        // Pixel icons that tell row types apart; each maps to a kernel-icon--<name> class in LedgerTree.uss
        private enum RowIcon
        {
            Star,
            Planet,
            Orbit,
            Cloud,
            Horizon,
            Flask
        }

        private readonly VisualElement _container;
        private readonly HashSet<string> _expanded = new();
        private bool _hasShown;

        public LedgerTreeView(VisualElement container)
        {
            _container = container;
        }

        public void Show(IEnumerable<BodyEntry> roots)
        {
            List<BodyEntry> rootList = roots.ToList();

            // Stars start expanded so their planets are visible right away
            if (!_hasShown)
            {
                foreach (BodyEntry star in rootList.Where(b => b.IsStar))
                    _expanded.Add(star.BodyName);
                _hasShown = true;
            }

            // Rebuilds happen while the window is open (e.g. after scoring a report): keep the scroll position
            var scrollView = _container as ScrollView;
            Vector2 scrollOffset = scrollView?.scrollOffset ?? Vector2.zero;

            _container.Clear();
            foreach (BodyEntry body in rootList)
                _container.Add(BuildBody(body));

            // The new content needs a layout pass before the offset can be applied
            scrollView?.schedule.Execute(() => scrollView.scrollOffset = scrollOffset);
        }

        private VisualElement BuildBody(BodyEntry body)
        {
            // Every body is listed (the tracking station shows them anyway); its own totals exclude its moons
            List<AreaEntry> areas = body.Areas.Where(area => VisibleLocations(area).Any()).ToList();
            Foldout foldout = CreateFoldout(body.BodyName, body.DisplayName, areas.SelectMany(VisibleLocations), "kernel-body");
            AddFoldoutIcon(foldout, body.IsStar ? RowIcon.Star : RowIcon.Planet);

            // Orbiting bodies first (a star's planets, a planet's moons), then orbits (high to low), then other
            // whole-body areas (atmosphere), then biomes.
            // Both of the last groups sort by real name, even where the name is still hidden as "?????".
            foreach (BodyEntry child in body.Children)
                foldout.Add(BuildBody(child));
            foreach (AreaEntry area in areas.Where(a => a.IsOrbit))
                foldout.Add(BuildAreaOrUnknown(body, area));
            foreach (AreaEntry area in areas.Where(a => a.IsWholeBody && !a.IsOrbit).OrderBy(a => a.DisplayName))
                foldout.Add(BuildAreaOrUnknown(body, area));
            foreach (AreaEntry area in areas.Where(a => !a.IsWholeBody).OrderBy(a => a.DisplayName))
                foldout.Add(BuildAreaOrUnknown(body, area));

            return foldout;
        }

        private VisualElement BuildAreaOrUnknown(BodyEntry body, AreaEntry area)
        {
            return IsKnown(area) ? BuildArea(body, area) : BuildUnknownArea(area);
        }

        // Every body has orbits; anything else stays a mystery until science has been gathered there
        private static bool IsKnown(AreaEntry area)
        {
            return area.IsOrbit || area.HasSubmissions;
        }

        // Looks and responds like the other area rows, but there's nothing to reveal yet: a "-" replaces the
        // arrow and it never opens
        private static VisualElement BuildUnknownArea(AreaEntry area)
        {
            List<LocationEntry> locations = VisibleLocations(area).ToList();
            var foldout = new Foldout { text = UnknownAreaName, value = false };
            foldout.AddToClassList("kernel-area");
            foldout.AddToClassList("kernel-area--unknown");
            AddFoldoutIcon(foldout, AreaIcon(area));
            foldout.Q<Toggle>()?.Add(CreateValueLabel(locations.Sum(l => l.Earned), locations.Sum(l => l.Possible)));

            var dash = new Label("-");
            dash.AddToClassList("kernel-area--unknown__dash");
            foldout.Q(className: "unity-foldout__checkmark")?.Add(dash);

            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout && evt.newValue) foldout.SetValueWithoutNotify(false);
            });

            return foldout;
        }

        private VisualElement BuildArea(BodyEntry body, AreaEntry area)
        {
            List<LocationEntry> locations = VisibleLocations(area).ToList();
            Foldout foldout = CreateFoldout($"{body.BodyName}/{area.Id}", area.DisplayName, locations, "kernel-area");
            AddFoldoutIcon(foldout, AreaIcon(area));

            // Whole-body areas are named after their situation, so their rows don't repeat it
            foreach (LocationEntry location in locations)
                foldout.Add(BuildRow(location, showSituation: !area.IsWholeBody));

            return foldout;
        }

        private static VisualElement BuildRow(LocationEntry location, bool showSituation)
        {
            var row = new VisualElement();
            row.AddToClassList("kernel-row");
            if (location.IsComplete) row.AddToClassList("kernel-row--complete");
            row.Add(CreateIcon(RowIcon.Flask));

            var name = new Label(Translate(location.Experiment.DisplayName, location.Experiment.ExperimentID));
            name.AddToClassList("kernel-row__name");
            row.Add(name);

            if (showSituation)
            {
                var situation = new Label(location.Location.ScienceSituation.GetTranslatedDescription());
                situation.AddToClassList("kernel-row__situation");
                row.Add(situation);
            }

            row.Add(CreateValueLabel(location.Earned, location.Possible));
            return row;
        }

        private Foldout CreateFoldout(string key, string title, IEnumerable<LocationEntry> locations, string className)
        {
            var foldout = new Foldout { text = title, value = _expanded.Contains(key) };
            foldout.AddToClassList(className);

            List<LocationEntry> list = locations.ToList();
            foldout.Q<Toggle>()?.Add(CreateValueLabel(list.Sum(l => l.Earned), list.Sum(l => l.Possible)));

            foldout.RegisterValueChangedCallback(evt =>
            {
                // Nested foldouts' changes bubble up through this one
                if (evt.target != foldout) return;
                if (evt.newValue) _expanded.Add(key);
                else _expanded.Remove(key);
            });

            return foldout;
        }

        // Orbits get the orbit icon, the atmosphere a cloud, and biomes (patches of surface) the horizon
        private static RowIcon AreaIcon(AreaEntry area)
        {
            if (area.IsOrbit) return RowIcon.Orbit;
            return area.IsWholeBody && area.Id == nameof(ScienceSitutation.Atmosphere) ? RowIcon.Cloud : RowIcon.Horizon;
        }

        private static VisualElement CreateIcon(RowIcon icon)
        {
            var element = new VisualElement();
            element.AddToClassList("kernel-icon");
            element.AddToClassList("kernel-icon--" + icon.ToString().ToLowerInvariant());
            return element;
        }

        // Places the icon just before the foldout's title, after its arrow
        private static void AddFoldoutIcon(Foldout foldout, RowIcon icon)
        {
            Label title = foldout.Q<Label>(className: "unity-foldout__text");
            VisualElement parent = title?.parent;
            parent?.Insert(parent.IndexOf(title), CreateIcon(icon));
        }

        private static Label CreateValueLabel(float earned, float possible)
        {
            var label = new Label($"{earned:#,0.#} / {possible:#,0.#}");
            label.AddToClassList("kernel-value");
            return label;
        }

        // Only experiments the player can run with their current tech
        private static IEnumerable<LocationEntry> VisibleLocations(AreaEntry area)
        {
            return area.Locations.Where(location => location.IsUnlocked);
        }

        private static string Translate(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            string text = LocalizationManager.GetTranslation(key);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}
