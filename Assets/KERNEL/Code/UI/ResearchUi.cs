using System;
using System.Linq;
using KSP.Audio;
using KSP.Game;
using KSP.Game.Science;
using KSP.UI;
using TMPro;
using UnityEngine;

namespace KERNEL.UI
{
    /// <summary>
    /// Look and sound shared by K.E.R.N.E.L's parts of the R&D screen, taken from the screen's own elements so
    /// they match the stock ones.
    /// </summary>
    internal static class ResearchUi
    {
        // The R&D sidebar's title. Redux swaps that sidebar in when the screen starts, so it's found at runtime
        private const string FontSourcePath = "Container/TechNodeInfo/Content/TechNodeName";

        // The tier tabs, whose sounds are set on their visualizers
        private const string TierTabsPath = "Container/TechTree/TierScroll/TierSelectionContainer";

        private static string[] _hoverSounds, _pressSounds;

        public static RDCenterUIController Screen(Component part) => part.GetComponentInParent<RDCenterUIController>(true);

        /// <summary>
        /// The text style of the R&D sidebar's title, to copy onto our labels.
        /// </summary>
        public static TextMeshProUGUI FontSource(Component part)
        {
            RDCenterUIController screen = Screen(part);
            Transform source = screen != null ? screen.transform.Find(FontSourcePath) : null;
            return source != null ? source.GetComponent<TextMeshProUGUI>() : null;
        }

        public static void PlayHover(Component part, Transform at) => Play(_hoverSounds ??= TierTabSounds(part, hover: true), at);

        public static void PlayPress(Component part, Transform at) => Play(_pressSounds ??= TierTabSounds(part, hover: false), at);

        private static string[] TierTabSounds(Component part, bool hover)
        {
            RDCenterUIController screen = Screen(part);
            Transform tabs = screen != null ? screen.transform.Find(TierTabsPath) : null;
            var visualizer = tabs != null ? tabs.GetComponentInChildren<ToggleExtendedEventsVisualizer>(true) : null;
            if (visualizer == null) return null;

            AK.Wwise.Event[] events = hover ? visualizer.OnButtonHoverEvents
                : visualizer.OnButtonPressedEvents is { Length: > 0 } pressed ? pressed : visualizer.OnToggleEnabledButtonPressedEvents;
            return events?.Where(e => e != null).Select(e => e.Name).ToArray() ?? Array.Empty<string>();
        }

        // As stock UI does, with the sound placed where the element is on screen
        private static void Play(string[] names, Transform at)
        {
            if (names == null)
            {
                // The tabs weren't found yet; try again next time
                _hoverSounds = _pressSounds = null;
                return;
            }

            if (names.Length == 0 || GameManager.Instance == null || GameManager.Instance.Game == null) return;

            Vector2 position = GameManager.Instance.Game.UI.GetViewportPositionFromUITransformInOverlayCanvas(at);
            KSPBaseAudio.SetRTPCValue(KSPAudioParams.ui_position_x_rtpc, position.x, KSPAudioEventManager.UIAudioGameObject);
            KSPBaseAudio.SetRTPCValue(KSPAudioParams.ui_position_y_rtpc, position.y, KSPAudioEventManager.UIAudioGameObject);
            foreach (string name in names) KSPBaseAudio.PostEvent(name, KSPAudioEventManager.UIAudioGameObject);
        }
    }
}
