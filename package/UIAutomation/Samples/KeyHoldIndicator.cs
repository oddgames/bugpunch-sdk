using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UIAutomation.Samples
{
    /// <summary>
    /// Visual indicator for key hold state. Shows which WASD keys are currently pressed.
    /// Used in sample scene to demonstrate key hold functionality.
    /// </summary>
    public class KeyHoldIndicator : MonoBehaviour
    {
        [SerializeField] private Image wKey;
        [SerializeField] private Image aKey;
        [SerializeField] private Image sKey;
        [SerializeField] private Image dKey;
        [SerializeField] private Image shiftKey;
        [SerializeField] private RectTransform positionIndicator;
        [SerializeField] private Text statusText;

        private Color _normalColor = new Color(0.3f, 0.3f, 0.3f);
        private Color _pressedColor = new Color(0.3f, 0.8f, 0.3f);
        private Vector2 _position = Vector2.zero;
        private float _moveSpeed = 100f;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Update key visuals
            UpdateKeyVisual(wKey, keyboard.wKey.isPressed);
            UpdateKeyVisual(aKey, keyboard.aKey.isPressed);
            UpdateKeyVisual(sKey, keyboard.sKey.isPressed);
            UpdateKeyVisual(dKey, keyboard.dKey.isPressed);
            UpdateKeyVisual(shiftKey, keyboard.leftShiftKey.isPressed);

            // Calculate movement direction
            Vector2 moveDir = Vector2.zero;
            if (keyboard.wKey.isPressed) moveDir.y += 1;
            if (keyboard.sKey.isPressed) moveDir.y -= 1;
            if (keyboard.aKey.isPressed) moveDir.x -= 1;
            if (keyboard.dKey.isPressed) moveDir.x += 1;

            // Apply sprint multiplier
            float speed = keyboard.leftShiftKey.isPressed ? _moveSpeed * 2f : _moveSpeed;

            // Move position indicator
            if (moveDir != Vector2.zero && positionIndicator != null)
            {
                _position += moveDir.normalized * speed * Time.deltaTime;
                _position.x = Mathf.Clamp(_position.x, -80, 80);
                _position.y = Mathf.Clamp(_position.y, -40, 40);
                positionIndicator.anchoredPosition = _position;
            }

            // Update status text
            if (statusText != null)
            {
                if (moveDir != Vector2.zero)
                {
                    string dir = "";
                    if (moveDir.y > 0) dir += "Forward ";
                    if (moveDir.y < 0) dir += "Back ";
                    if (moveDir.x < 0) dir += "Left ";
                    if (moveDir.x > 0) dir += "Right ";
                    if (keyboard.leftShiftKey.isPressed) dir += "(Sprint)";
                    statusText.text = dir.Trim();
                }
                else
                {
                    statusText.text = "Press WASD to move";
                }
            }
        }

        private void UpdateKeyVisual(Image keyImage, bool pressed)
        {
            if (keyImage != null)
            {
                keyImage.color = pressed ? _pressedColor : _normalColor;
            }
        }

        /// <summary>
        /// Resets the position indicator to center.
        /// </summary>
        public void ResetPosition()
        {
            _position = Vector2.zero;
            if (positionIndicator != null)
                positionIndicator.anchoredPosition = Vector2.zero;
        }
    }
}
