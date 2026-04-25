using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Fluent builder for creating key sequences with hold durations.
    /// Supports both simultaneous (Together) and sequential (Then) key combinations.
    ///
    /// Examples:
    ///   // Walk forward for 2 seconds
    ///   await Keys.Hold(Key.W, 2f);
    ///
    ///   // Walk forward-left for 2 seconds (diagonal movement)
    ///   await Keys.Hold(Key.W, Key.A).For(2f);
    ///
    ///   // Sprint forward: hold Shift+W for 3 seconds
    ///   await Keys.Hold(Key.LeftShift, Key.W).For(3f);
    ///
    ///   // Complex sequence: W for 1s, then A for 0.5s, then W+D together for 2s
    ///   await Keys.Hold(Key.W).For(1f).Then(Key.A).For(0.5f).Then(Key.W, Key.D).For(2f);
    ///
    ///   // Tap then hold: press Space, then hold W for 2s
    ///   await Keys.Press(Key.Space).Then(Key.W).For(2f);
    /// </summary>
    public class Keys
    {
        readonly List<KeyStep> _steps = new();

        Keys() { }

        /// <summary>
        /// Start a sequence by holding one or more keys.
        /// Call For() to specify the hold duration.
        /// </summary>
        public static Keys Hold(params Key[] keys) => new Keys().AddStep(keys, 0f, false);

        /// <summary>
        /// Start a sequence by pressing (tap) one or more keys.
        /// </summary>
        public static Keys Press(params Key[] keys) => new Keys().AddStep(keys, 0f);

        /// <summary>
        /// Set the duration for the current step.
        /// </summary>
        public Keys For(float seconds)
        {
            if (_steps.Count > 0)
                _steps[_steps.Count - 1] = _steps[_steps.Count - 1].WithDuration(seconds);
            return this;
        }

        /// <summary>
        /// Chain another key hold after the current step.
        /// </summary>
        public Keys Then(params Key[] keys) => AddStep(keys, 0f, false);

        /// <summary>
        /// Chain a key press (tap) after the current step.
        /// </summary>
        public Keys ThenPress(params Key[] keys) => AddStep(keys, 0f);

        Keys AddStep(Key[] keys, float duration, bool isPress = true)
        {
            _steps.Add(new KeyStep(keys, duration, isPress));
            return this;
        }

        /// <summary>
        /// Execute the key sequence.
        /// </summary>
        public async Task Execute()
        {
            foreach (var step in _steps)
            {
                if (step.IsPress || step.Duration <= 0f)
                {
                    // Quick press for each key
                    foreach (var key in step.Keys)
                        await InputInjector.PressKey(key);
                }
                else if (step.Keys.Length == 1)
                {
                    await InputInjector.HoldKey(step.Keys[0], step.Duration);
                }
                else
                {
                    await InputInjector.HoldKeys(step.Keys, step.Duration);
                }
            }
        }

        /// <summary>
        /// Gets the awaiter for direct await support on Keys builder.
        /// </summary>
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => Execute().GetAwaiter();

        readonly struct KeyStep
        {
            public readonly Key[] Keys;
            public readonly float Duration;
            public readonly bool IsPress;

            public KeyStep(Key[] keys, float duration, bool isPress)
            {
                Keys = keys;
                Duration = duration;
                IsPress = isPress;
            }

            public KeyStep WithDuration(float duration) => new KeyStep(Keys, duration, IsPress);
        }
    }

    internal static partial class InputInjector
    {
        /// <summary>
        /// Clears an input field's text content.
        /// Note: Uses direct text manipulation as a workaround because Unity's IMGUI keyboard
        /// shortcuts (Ctrl+A, Backspace) cannot be injected via the new Input System.
        /// </summary>
        public static async Task ClearInputField(GameObject inputFieldGO)
        {
            // Check if there's text to clear
            var tmpInput = inputFieldGO.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                if (string.IsNullOrEmpty(tmpInput.text))
                    return;

                // Click to focus the field first
                var screenPos = GetScreenPosition(inputFieldGO);
                await InjectPointerTap(screenPos);
                await Async.DelayFrames(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                tmpInput.text = "";
                tmpInput.ActivateInputField();
                tmpInput.MoveTextEnd(false);
                await Async.DelayFrames(2);
                return;
            }

            var legacyInput = inputFieldGO.GetComponent<InputField>();
            if (legacyInput != null)
            {
                if (string.IsNullOrEmpty(legacyInput.text))
                    return;

                // Click to focus the field first
                var screenPos = GetScreenPosition(inputFieldGO);
                await InjectPointerTap(screenPos);
                await Async.DelayFrames(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                legacyInput.text = "";
                legacyInput.ActivateInputField();
                legacyInput.MoveTextEnd(false);
                await Async.DelayFrames(2);
            }
        }

        /// <summary>
        /// Presses a key while holding a modifier key (e.g., Ctrl+A, Shift+Tab).
        /// This simulates the real sequence: modifier down, key down, key up, modifier up.
        /// </summary>
        public static async Task PressKeyWithModifier(Key modifier, Key key)
        {


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKeyWithModifier - No keyboard device found");
                return;
            }

            // Modifier down
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            await Async.DelayFrames(2);

            // Key down (with modifier held)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier, key));
            await Async.DelayFrames(2);

            // Key up (modifier still held)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            await Async.DelayFrames(2);

            // Modifier up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Types text into an input field.
        ///
        /// Note: TMP_InputField uses IMGUI's Event.PopEvent() for keyboard input, not the Input System's
        /// Keyboard.onTextInput. InputSystem.QueueTextEvent() does not work with TMP_InputField.
        /// This method uses direct text manipulation as the only reliable cross-platform approach.
        /// See: https://discussions.unity.com/t/code-to-fix-tmp_inputfield-to-support-new-inputsystem/774250
        /// </summary>
        public static async Task TypeIntoField(GameObject inputFieldGO, string text, bool clearFirst = true, bool pressEnter = false)
        {
            var screenPos = GetScreenPosition(inputFieldGO);
            LogDebug($"TypeIntoField '{inputFieldGO.name}' at ({screenPos.x:F0},{screenPos.y:F0}) text='{text}'");
            InputVisualizer.RecordText(text ?? "", screenPos);

            // Click to focus the field
            await InjectPointerTap(screenPos);
            await Async.DelayFrames(2);

            // TMP_InputField uses IMGUI Event.PopEvent() for keyboard input, not Input System.
            // InputSystem.QueueTextEvent() does NOT work with TMP_InputField.
            // We must use direct text manipulation.
            var tmpInput = inputFieldGO.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                if (clearFirst)
                    tmpInput.text = text ?? "";
                else
                    tmpInput.text += text ?? "";

                // Re-activate and move cursor to end
                tmpInput.ActivateInputField();
                tmpInput.MoveTextEnd(false);
                await Async.DelayFrames(2);
            }
            else
            {
                var legacyInput = inputFieldGO.GetComponent<InputField>();
                if (legacyInput != null)
                {
                    if (clearFirst)
                        legacyInput.text = text ?? "";
                    else
                        legacyInput.text += text ?? "";

                    // Re-activate and move cursor to end
                    legacyInput.ActivateInputField();
                    legacyInput.MoveTextEnd(false);
                    await Async.DelayFrames(2);
                }
                else
                {
                    Debug.LogWarning($"[InputInjector] TypeIntoField - No TMP_InputField or InputField found on '{inputFieldGO.name}'");
                }
            }

            // Press Enter if requested
            if (pressEnter)
            {
                await Async.DelayFrames(2);
                await PressKey(Key.Enter);
            }
        }

        /// <summary>
        /// Types text character by character using InputSystem.QueueTextEvent.
        /// This triggers Keyboard.onTextInput callback for any subscribers.
        ///
        /// Note: TMP_InputField does NOT use Keyboard.onTextInput - it uses IMGUI Event.PopEvent().
        /// For TMP_InputField, use TypeIntoField() instead which handles this limitation.
        /// </summary>
        public static async Task TypeText(string text)
        {

            LogDebug($"TypeText '{text}' ({text?.Length ?? 0} chars)");

            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] TypeText - No keyboard device found");
                return;
            }

            foreach (char c in text)
            {
                if (!Application.isPlaying) break;

                // Queue text event - this is how Unity's InputTestFixture does it
                // See: https://github.com/Unity-Technologies/InputSystem/blob/develop/Assets/Tests/InputSystem/CoreTests_Devices.cs
                InputSystem.QueueTextEvent(keyboard, c);
                await Async.DelayFrames(1);
            }

            LogDebug("TypeText complete");
        }

        /// <summary>
        /// Selects all text in the currently focused input field using Ctrl+A.
        /// </summary>
        public static async Task SelectAllText()
        {

            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] SelectAllText - No keyboard device found");
                return;
            }

            // Press Ctrl+A
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.A));
            await Async.DelayFrames(3);

            // Release keys
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Deletes all text in the currently focused input field using Ctrl+A followed by Backspace.
        /// </summary>
        public static async Task DeleteAllText()
        {
            await SelectAllText();
            await PressKey(Key.Backspace);
        }

        /// <summary>
        /// Presses and releases a keyboard key.
        /// Also sends legacy Input system events (Event.KeyboardEvent) so games using
        /// the old Input class (Input.anyKeyDown, Input.GetKeyDown) receive the key press.
        /// </summary>
        public static async Task PressKey(Key key)
        {
            InputVisualizer.RecordKeyPress(key.ToString());


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKey - No keyboard device found");
                return;
            }

            // New Input System: key down
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));

            // Legacy Input System: send KeyDown event so Input.anyKeyDown / Input.GetKeyDown works
            SendLegacyKeyEvent(key);

            await Async.DelayFrames(3);

            // New Input System: key up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Sends a keyboard event through Unity's IMGUI event system so the legacy
        /// Input class (Input.anyKeyDown, Input.GetKeyDown) picks it up.
        /// Uses reflection to access EditorWindow.SendEvent since this is a runtime assembly.
        /// </summary>
        static void SendLegacyKeyEvent(Key key)
        {
            var legacyKeyCode = KeyToKeyCode(key);
            if (legacyKeyCode == KeyCode.None) return;

            try
            {
                var evt = Event.KeyboardEvent(legacyKeyCode.ToString());
                evt.type = EventType.KeyDown;
                evt.keyCode = legacyKeyCode;

                // Use reflection to find GameView and send the event
                // (runtime assembly can't reference UnityEditor directly)
                var editorAssembly = System.Reflection.Assembly.Load("UnityEditor");
                if (editorAssembly == null) return;

                var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
                if (gameViewType == null) return;

                var allGameViews = Resources.FindObjectsOfTypeAll(gameViewType);
                if (allGameViews.Length == 0) return;

                // EditorWindow.Focus() + SendEvent(Event)
                var focusMethod = gameViewType.GetMethod("Focus", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var sendEventMethod = gameViewType.GetMethod("SendEvent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (focusMethod != null && sendEventMethod != null)
                {
                    var gv = allGameViews[0];
                    focusMethod.Invoke(gv, null);
                    sendEventMethod.Invoke(gv, new object[] { evt });
                }
            }
            catch
            {
                // Non-critical — new Input System path still works
            }
        }

        /// <summary>
        /// Converts an Input System Key to a legacy KeyCode for the old Input system.
        /// </summary>
        static KeyCode KeyToKeyCode(Key key)
        {
            return key switch
            {
                Key.A => KeyCode.A, Key.B => KeyCode.B, Key.C => KeyCode.C, Key.D => KeyCode.D,
                Key.E => KeyCode.E, Key.F => KeyCode.F, Key.G => KeyCode.G, Key.H => KeyCode.H,
                Key.I => KeyCode.I, Key.J => KeyCode.J, Key.K => KeyCode.K, Key.L => KeyCode.L,
                Key.M => KeyCode.M, Key.N => KeyCode.N, Key.O => KeyCode.O, Key.P => KeyCode.P,
                Key.Q => KeyCode.Q, Key.R => KeyCode.R, Key.S => KeyCode.S, Key.T => KeyCode.T,
                Key.U => KeyCode.U, Key.V => KeyCode.V, Key.W => KeyCode.W, Key.X => KeyCode.X,
                Key.Y => KeyCode.Y, Key.Z => KeyCode.Z,
                Key.Digit0 => KeyCode.Alpha0, Key.Digit1 => KeyCode.Alpha1,
                Key.Digit2 => KeyCode.Alpha2, Key.Digit3 => KeyCode.Alpha3,
                Key.Digit4 => KeyCode.Alpha4, Key.Digit5 => KeyCode.Alpha5,
                Key.Digit6 => KeyCode.Alpha6, Key.Digit7 => KeyCode.Alpha7,
                Key.Digit8 => KeyCode.Alpha8, Key.Digit9 => KeyCode.Alpha9,
                Key.Space => KeyCode.Space,
                Key.Enter => KeyCode.Return, Key.NumpadEnter => KeyCode.KeypadEnter,
                Key.Escape => KeyCode.Escape,
                Key.Tab => KeyCode.Tab,
                Key.Backspace => KeyCode.Backspace,
                Key.Delete => KeyCode.Delete,
                Key.Insert => KeyCode.Insert,
                Key.Home => KeyCode.Home, Key.End => KeyCode.End,
                Key.PageUp => KeyCode.PageUp, Key.PageDown => KeyCode.PageDown,
                Key.UpArrow => KeyCode.UpArrow, Key.DownArrow => KeyCode.DownArrow,
                Key.LeftArrow => KeyCode.LeftArrow, Key.RightArrow => KeyCode.RightArrow,
                Key.LeftShift => KeyCode.LeftShift, Key.RightShift => KeyCode.RightShift,
                Key.LeftCtrl => KeyCode.LeftControl, Key.RightCtrl => KeyCode.RightControl,
                Key.LeftAlt => KeyCode.LeftAlt, Key.RightAlt => KeyCode.RightAlt,
                Key.F1 => KeyCode.F1, Key.F2 => KeyCode.F2, Key.F3 => KeyCode.F3,
                Key.F4 => KeyCode.F4, Key.F5 => KeyCode.F5, Key.F6 => KeyCode.F6,
                Key.F7 => KeyCode.F7, Key.F8 => KeyCode.F8, Key.F9 => KeyCode.F9,
                Key.F10 => KeyCode.F10, Key.F11 => KeyCode.F11, Key.F12 => KeyCode.F12,
                Key.Minus => KeyCode.Minus, Key.Equals => KeyCode.Equals,
                Key.LeftBracket => KeyCode.LeftBracket, Key.RightBracket => KeyCode.RightBracket,
                Key.Backslash => KeyCode.Backslash, Key.Semicolon => KeyCode.Semicolon,
                Key.Quote => KeyCode.Quote, Key.Comma => KeyCode.Comma,
                Key.Period => KeyCode.Period, Key.Slash => KeyCode.Slash,
                Key.Backquote => KeyCode.BackQuote,
                _ => KeyCode.None
            };
        }

        /// <summary>
        /// Holds a keyboard key for the specified duration.
        /// </summary>
        /// <param name="key">The key to hold</param>
        /// <param name="duration">How long to hold the key in seconds</param>
        public static async Task HoldKey(Key key, float duration)
        {
            InputVisualizer.RecordKeyHoldStart(key.ToString());


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKey - No keyboard device found");
                InputVisualizer.RecordKeyHoldEnd();
                return;
            }

            var keyState = new KeyboardState(key);

            // Key down
            InputSystem.QueueStateEvent(keyboard, keyState);
            await Async.DelayFrames(1);

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(duration * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                await Async.DelayFrames(1);
            }

            // Key up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputVisualizer.RecordKeyHoldEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Holds multiple keyboard keys simultaneously for the specified duration.
        /// </summary>
        /// <param name="keys">The keys to hold together</param>
        /// <param name="duration">How long to hold the keys in seconds</param>
        public static async Task HoldKeys(Key[] keys, float duration)
        {
            if (keys == null || keys.Length == 0)
                return;

            string keyNames = string.Join("+", System.Array.ConvertAll(keys, k => k.ToString()));
            InputVisualizer.RecordKeyHoldStart(keyNames);


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKeys - No keyboard device found");
                InputVisualizer.RecordKeyHoldEnd();
                return;
            }

            var keyState = new KeyboardState(keys);

            // Keys down (all at once)
            InputSystem.QueueStateEvent(keyboard, keyState);
            await Async.DelayFrames(1);

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(duration * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                await Async.DelayFrames(1);
            }

            // Keys up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputVisualizer.RecordKeyHoldEnd();
            await Async.DelayFrames(2);
        }
    }
}
