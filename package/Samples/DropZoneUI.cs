using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ODDGames.Bugpunch.Samples
{
    /// <summary>
    /// Simple drop zone UI component for sample scenes.
    /// Detects when a draggable item is dropped on it.
    /// </summary>
    public class DropZoneUI : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image image;
        private Color originalColor;
        private Color highlightColor = new Color(0.5f, 1f, 0.5f, 0.8f);

        public bool HasItem { get; private set; }

        private void Awake()
        {
            image = GetComponent<Image>();
            if (image != null)
                originalColor = image.color;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Debug.Log($"[DropZone] OnPointerEnter - dragging={eventData.pointerDrag?.name ?? "null"}");
            if (eventData.pointerDrag != null && image != null)
            {
                image.color = highlightColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Debug.Log($"[DropZone] OnPointerExit");
            if (image != null)
            {
                image.color = originalColor;
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            Debug.Log($"[DropZone] OnDrop - dragging={eventData.pointerDrag?.name ?? "null"}");
            if (eventData.pointerDrag != null)
            {
                var draggable = eventData.pointerDrag.GetComponent<DraggableUI>();
                if (draggable != null)
                {
                    // Snap the dragged item to the drop zone center
                    var draggedRect = eventData.pointerDrag.GetComponent<RectTransform>();
                    var dropRect = GetComponent<RectTransform>();

                    if (draggedRect != null && dropRect != null)
                    {
                        draggedRect.anchoredPosition = dropRect.anchoredPosition;
                        HasItem = true;
                        Debug.Log($"[DropZone] Item dropped successfully: {eventData.pointerDrag.name}");
                    }
                }
            }

            if (image != null)
            {
                image.color = originalColor;
            }
        }
    }
}
