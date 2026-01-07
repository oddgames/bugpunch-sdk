#if HAS_EZ_GUI
using UnityEngine;

namespace ODDGames.UITest
{
    /// <summary>
    /// Automatically registers EZ GUI (AnB Software) clickable types and key handlers for UI testing.
    /// This is only compiled when HAS_EZ_GUI is defined in Project Settings.
    /// </summary>
    public static class EzGUIClickableRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Register EZ GUI clickable types
            UITestBehaviour.RegisterClickable(typeof(AutoSpriteControlBase));
            UITestBehaviour.RegisterClickable(typeof(UIButton3D));

            // Register EZ GUI key press handler
            UITestBehaviour.RegisterKeyPressHandler(HandleEzGUIKeyPress);
        }

        /// <summary>
        /// Handles key presses for EzGUI IKeyFocusable components.
        /// </summary>
        static bool HandleEzGUIKeyPress(GameObject target, KeyCode key)
        {
            // Check if target has IKeyFocusable component
            var keyFocusable = target.GetComponent<IKeyFocusable>();
            if (keyFocusable == null)
                return false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    keyFocusable.GoUp();
                    Debug.Log($"[UITEST] PressKey - EzGUI GoUp on '{target.name}'");
                    return true;

                case KeyCode.DownArrow:
                    keyFocusable.GoDown();
                    Debug.Log($"[UITEST] PressKey - EzGUI GoDown on '{target.name}'");
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    keyFocusable.Commit();
                    Debug.Log($"[UITEST] PressKey - EzGUI Commit on '{target.name}'");
                    return true;

                default:
                    // Let Unity UI handle other keys
                    return false;
            }
        }
    }
}
#endif
