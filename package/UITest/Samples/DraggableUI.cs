using UnityEngine;
using UnityEngine.EventSystems;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Simple draggable UI component for sample scenes.
    /// Allows dragging the attached RectTransform by mouse/touch.
    /// </summary>
    public class DraggableUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform rectTransform;
        private Canvas canvas;
        private Vector2 originalPosition;
        private CanvasGroup canvasGroup;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            originalPosition = rectTransform.anchoredPosition;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Debug.Log($"[DraggableUI] OnBeginDrag - {name}");
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0.7f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            Debug.Log($"[DraggableUI] OnDrag - {name} delta={eventData.delta}");
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
            }
            else
            {
                rectTransform.anchoredPosition += eventData.delta;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Debug.Log($"[DraggableUI] OnEndDrag - {name}");
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;
        }

        /// <summary>
        /// Resets the item to its original position.
        /// </summary>
        public void ResetPosition()
        {
            rectTransform.anchoredPosition = originalPosition;
        }
    }
}
