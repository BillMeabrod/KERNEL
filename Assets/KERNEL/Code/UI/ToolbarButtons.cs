using System;
using System.Collections;
using KSP.Api.CoreTypes;
using KSP.OAB;
using KSP.UI;
using KSP.UI.Binding;
using KSP.UI.Binding.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ILogger = ReduxLib.Logging.ILogger;
using Object = UnityEngine.Object;

namespace KERNEL.UI
{
    /// <summary>
    /// Puts KERNEL's button directly on the stock VAB and flight toolbars, rather than in SpaceWarp's app tray.
    /// Each is a clone of the stock button whose clipboard icon ours should match (so it inherits that button's
    /// size and state visuals), with the stock behaviour stripped and the icon swapped for ours. Both toolbars
    /// size themselves to their buttons, so they grow to fit.
    /// </summary>
    public static class ToolbarButtons
    {
        public const string OabButtonId = "BTN-KERNEL-OAB";
        public const string FlightButtonId = "BTN-KERNEL-Flight";

        private const string Tooltip = "K.E.R.N.E.L";

        private const string OabBarPath = "OAB(Clone)/HUDSpawner/HUD/widget_SideBar/widget_sidebarNav";
        private const string FlightBarPath =
            "GameManager/Default Game Instance(Clone)/UI Manager(Clone)/Scaled Popup Canvas/Container/ButtonBar";

        // VAB: cloned from and placed after the Engineer's Report (design checklist)
        private const string OabTemplate = "button_design-checklist";

        // Flight: cloned from and placed after the Research Inventory, the stock science button
        private const string FlightTemplate = "BTN-Research-Inventory";
        // The visible icon is this image nested inside Content/GRP-icon
        private const string FlightIconName = "ICO-asset";

        // Scene UI is built after the scene switch; stop waiting if the toolbar never shows up
        private const float WaitTimeout = 30f;

        public static IEnumerator AddToVab(Sprite icon, Action<bool> onToggled, ILogger logger)
        {
            Transform bar = null;
            yield return WaitForChild(OabBarPath, OabTemplate, found => bar = found);
            if (bar == null)
            {
                logger.LogError("Couldn't find the VAB toolbar; the K.E.R.N.E.L button won't be shown");
                yield break;
            }

            if (bar.Find(OabButtonId) != null) yield break;

            Transform template = bar.Find(OabTemplate);
            GameObject button = CloneButton(template, OabButtonId);

            // Children are the Engineer's Report's warning badge
            for (int i = button.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(button.transform.GetChild(i).gameObject);

            var iconImage = button.GetComponent<Image>();
            SetIcon(button, iconImage, icon);
            button.GetComponent<ObjectAssemblyBuilderTooltipDisplay>()?.SetTooltipText(Tooltip);

            FinishWithToggle(button, OabButtonId, onToggled, logger);
        }

        public static IEnumerator AddToFlight(Sprite icon, Action<bool> onToggled, ILogger logger)
        {
            Transform bar = null;
            yield return WaitForChild(FlightBarPath, FlightTemplate, found => bar = found);
            if (bar == null)
            {
                logger.LogError("Couldn't find the flight toolbar; the K.E.R.N.E.L button won't be shown");
                yield break;
            }

            if (bar.Find(FlightButtonId) != null) yield break;

            Transform template = bar.Find(FlightTemplate);
            GameObject button = CloneButton(template, FlightButtonId);

            var iconImage = FindDeep(button.transform, FlightIconName)?.GetComponent<Image>();
            if (iconImage != null) SetIcon(button, iconImage, icon);

            BasicTextTooltipData tooltip = button.GetComponent<BasicTextTooltipData>();
            if (tooltip != null) tooltip.TooltipTitleKey = Tooltip;

            FinishWithProperty(button, FlightButtonId, onToggled, logger);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }

            return null;
        }

        private static IEnumerator WaitForChild(string barPath, string childName, Action<Transform> found)
        {
            float giveUpAt = Time.unscaledTime + WaitTimeout;
            while (Time.unscaledTime < giveUpAt)
            {
                GameObject bar = GameObject.Find(barPath);
                if (bar != null && bar.transform.Find(childName) != null)
                {
                    found(bar.transform);
                    yield break;
                }

                yield return null;
            }
        }

        private static GameObject CloneButton(Transform template, string id)
        {
            GameObject button = Object.Instantiate(template.gameObject, template.parent);
            // Configure while inactive so the clone's stock behaviour never runs
            button.SetActive(false);
            button.name = id;
            button.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
            Isolate(button);
            return button;
        }

        // Detaches a cloned stock button from the game: no data bindings, stock actions or tutorial tags
        private static void Isolate(GameObject button)
        {
            foreach (UIDataContextBindBase binding in button.GetComponentsInChildren<UIDataContextBindBase>(true))
                binding.BindContext(null);
            foreach (UIAction_Void_Toggle action in button.GetComponentsInChildren<UIAction_Void_Toggle>(true))
                Object.DestroyImmediate(action);
            foreach (IndicatorTag tag in button.GetComponentsInChildren<IndicatorTag>(true))
                Object.DestroyImmediate(tag);
        }

        /// <summary>
        /// Stock buttons keep per-state copies of their icon (rest, hover, press, selected...) and re-apply them:
        /// in Unity's own sprite swap on the toggle and in KSP's visualizers. Point every state that drives the
        /// icon image at ours. Each state's scale and colour stay stock.
        /// </summary>
        private static void SetIcon(GameObject button, Image iconImage, Sprite icon)
        {
            iconImage.sprite = icon;
            iconImage.overrideSprite = null;

            foreach (Selectable selectable in button.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable.targetGraphic != iconImage) continue;

                SpriteState states = selectable.spriteState;
                if (states.highlightedSprite != null) states.highlightedSprite = icon;
                if (states.pressedSprite != null) states.pressedSprite = icon;
                if (states.selectedSprite != null) states.selectedSprite = icon;
                if (states.disabledSprite != null) states.disabledSprite = icon;
                selectable.spriteState = states;
            }

            foreach (SelectableExtendedVisualizer visualizer in button.GetComponentsInChildren<SelectableExtendedVisualizer>(true))
            {
                if (visualizer.SelectableGraphic == iconImage)
                {
                    visualizer.NormalSprite = icon;
                    if (visualizer.HighlightedSprite != null) visualizer.HighlightedSprite = icon;
                    if (visualizer.PressedSprite != null) visualizer.PressedSprite = icon;
                    if (visualizer.SelectedSprite != null) visualizer.SelectedSprite = icon;
                    if (visualizer.DisabledSprite != null) visualizer.DisabledSprite = icon;
                }

                if (visualizer is ToggleExtendedEventsVisualizer eventsVisualizer)
                    ReplaceStateSprites(eventsVisualizer.TransitionVisualsToggle, iconImage, icon);
            }

            foreach (ToggleExtendedVisualizer visualizer in button.GetComponentsInChildren<ToggleExtendedVisualizer>(true))
                ReplaceStateSprites(visualizer.transitionVisuals, iconImage, icon);
        }

        private static void ReplaceStateSprites(TransitionVisualToggle[] toggles, Graphic target, Sprite icon)
        {
            if (toggles == null) return;

            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i].targetGraphics == null || Array.IndexOf(toggles[i].targetGraphics, target) < 0) continue;
                toggles[i].transitionDataOn = ReplaceStateSprites(toggles[i].transitionDataOn, icon);
                toggles[i].transitionDataOff = ReplaceStateSprites(toggles[i].transitionDataOff, icon);
            }
        }

        private static TransitionVisualData ReplaceStateSprites(TransitionVisualData data, Sprite icon)
        {
            data.spriteEntryOnRest = WithSprite(data.spriteEntryOnRest, icon);
            data.spriteEntryOnHover = WithSprite(data.spriteEntryOnHover, icon);
            data.spriteEntryOnPress = WithSprite(data.spriteEntryOnPress, icon);
            data.spriteEntryOnDisabled = WithSprite(data.spriteEntryOnDisabled, icon);
            data.spriteEntryOnSelect = WithSprite(data.spriteEntryOnSelect, icon);
            return data;
        }

        private static SpriteEntry WithSprite(SpriteEntry entry, Sprite icon)
        {
            // Only states that swap the sprite; the rest just tint or scale the current one
            if (entry.sprite != null) entry.sprite = icon;
            return entry;
        }

        /// <summary>
        /// Shows our toolbar buttons pressed or not, e.g. when the window closes from its own close button.
        /// </summary>
        public static void SetPressed(bool pressed)
        {
            foreach (string id in new[] { OabButtonId, FlightButtonId })
            {
                GameObject button = GameObject.Find(id);
                if (button == null) continue;

                var value = button.GetComponent<UIValue_WriteBool_Toggle>();
                if (value != null) value.SetValue(pressed);
                else if (button.GetComponent<ToggleExtended>() is { } toggle) toggle.isOn = pressed;
            }
        }

        // For buttons whose stock behaviour lives in the toggle's listeners and click triggers
        private static void FinishWithToggle(GameObject button, string id, Action<bool> onToggled, ILogger logger)
        {
            var toggle = button.GetComponent<ToggleExtended>();
            if (toggle == null)
            {
                logger.LogError($"Couldn't set up the K.E.R.N.E.L toolbar button '{id}'");
                Object.Destroy(button);
                return;
            }

            Mute(toggle.onValueChanged);
            Mute(toggle.ToggleClicked);
            Mute(toggle.WhenTogglingOn);
            Mute(toggle.WhenTogglingOff);
            toggle.SetIsOnWithoutNotify(false);

            // Keep hover handling, drop whatever the stock button did on click
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            trigger?.triggers.RemoveAll(entry =>
                entry.eventID is EventTriggerType.PointerClick or EventTriggerType.Submit or EventTriggerType.PointerDown or EventTriggerType.PointerUp);

            toggle.ToggleClicked ??= new UnityEvent();
            toggle.ToggleClicked.AddListener(() => onToggled(toggle.isOn));

            button.SetActive(true);
            logger.LogInfo($"Added toolbar button {id}");
        }

        private static void Mute(UnityEventBase unityEvent)
        {
            if (unityEvent == null) return;

            unityEvent.RemoveAllListeners();
            for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        // For buttons driven by a bound state property (their stock action was removed by Isolate)
        private static void FinishWithProperty(GameObject button, string id, Action<bool> onToggled, ILogger logger)
        {
            var value = button.GetComponent<UIValue_WriteBool_Toggle>();
            if (value == null)
            {
                logger.LogError($"Couldn't set up the K.E.R.N.E.L toolbar button '{id}'");
                Object.Destroy(button);
                return;
            }

            // Clicking writes this property; LedgerWindowController also sets it when the window opens or closes
            var state = new Property<bool>(false);
            state.OnChangedValue += onToggled;
            value.ValueKey = string.Empty;
            value.BindValue(state);

            button.SetActive(true);
            logger.LogInfo($"Added toolbar button {id}");
        }
    }
}
