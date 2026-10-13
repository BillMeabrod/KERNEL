using UnityEngine;
using UnityEngine.EventSystems;

namespace KERNEL.UI
{
    /// <summary>
    /// Added to the R&D tier scrollbar by ResearchTab's prefab patch, after the stock scrollbar component, so it
    /// hears presses and drags after the stock scrollbar has handled them. Passes them on to the tab, which lets
    /// the handle carry on past the tiers into our page.
    /// </summary>
    public class ResearchScrollbar : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        private ResearchTab _tab;

        /// <summary>
        /// The tab to pass the scrollbar's presses and drags to, set by the tab when the screen starts.
        /// </summary>
        public ResearchTab Tab
        {
            set => _tab = value;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (TryGetX(eventData, out float x)) _tab.OnScrollbarDown(x);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (TryGetX(eventData, out float x)) _tab.OnScrollbarDrag(x);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_tab != null) _tab.OnScrollbarUp();
        }

        // The pointer's x in the scrollbar's own space
        private bool TryGetX(PointerEventData eventData, out float x)
        {
            x = 0f;
            if (_tab == null || eventData.button != PointerEventData.InputButton.Left) return false;

            bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local);
            x = local.x;
            return inside;
        }
    }
}
