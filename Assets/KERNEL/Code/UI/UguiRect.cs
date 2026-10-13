using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// Where a uGUI rect is on a UI Toolkit panel, for laying UI Toolkit over stock uGUI (the R&D sidebar, the
    /// system map). Finds the rect's canvas camera once and reuses one corners array, since it runs every frame.
    /// </summary>
    internal sealed class UguiRect
    {
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _target;
        private Camera _camera;

        public RectTransform Target
        {
            get => _target;
            set
            {
                _target = value;
                Canvas canvas = value != null ? value.GetComponentInParent<Canvas>() : null;
                canvas = canvas != null ? canvas.rootCanvas : null;
                _camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            }
        }

        /// <summary>
        /// The target's rect in the panel's coordinates; false while there's no target or panel.
        /// </summary>
        public bool TryGetPanelRect(IPanel panel, out Rect rect)
        {
            rect = default;
            if (_target == null || panel == null) return false;

            _target.GetWorldCorners(_corners);
            Vector2 min = ToPanel(panel, RectTransformUtility.WorldToScreenPoint(_camera, _corners[1]));
            Vector2 max = ToPanel(panel, RectTransformUtility.WorldToScreenPoint(_camera, _corners[3]));
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        // Screen points run bottom up; panel points top down
        private static Vector2 ToPanel(IPanel panel, Vector2 screenPoint) =>
            RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));

        /// <summary>
        /// Places an absolutely positioned element at a panel rect.
        /// </summary>
        public static void Place(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }
    }
}
