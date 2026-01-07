using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Provides visual feedback when UI elements are clicked.
    /// Turns green briefly to confirm the click was registered.
    /// Useful for verifying Input System injection is working.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class ClickFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        [SerializeField] Color clickedColor = new Color(0.3f, 0.9f, 0.3f);
        [SerializeField] float feedbackDuration = 0.3f;

        Graphic graphic;
        Color originalColor;
        float feedbackEndTime;
        int clickCount;

        /// <summary>
        /// Number of times this element has been clicked.
        /// </summary>
        public int ClickCount => clickCount;

        void Awake()
        {
            graphic = GetComponent<Graphic>();
            originalColor = graphic.color;
        }

        void Update()
        {
            if (feedbackEndTime > 0 && Time.unscaledTime > feedbackEndTime)
            {
                graphic.color = originalColor;
                feedbackEndTime = 0;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Slightly darken on press
            graphic.color = originalColor * 0.8f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            graphic.color = originalColor;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            clickCount++;
            graphic.color = clickedColor;
            feedbackEndTime = Time.unscaledTime + feedbackDuration;
            Debug.Log($"[ClickFeedback] {name} clicked! (Total: {clickCount})");
        }

        /// <summary>
        /// Reset the click counter.
        /// </summary>
        public void ResetClickCount()
        {
            clickCount = 0;
        }
    }
}
