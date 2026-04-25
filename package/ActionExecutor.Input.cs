using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ODDGames.Bugpunch
{
    public static partial class ActionExecutor
    {
        #region Text Input (Search-based)

        /// <summary>
        /// Enters text into an input field.
        /// </summary>
        public static async Task TextInput(Search search, string input, float seconds = 10, bool pressEnter = false)
        {
            await using var action = await RunAction($"TextInput({search}, \"{input}\")");

            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            if (tmpInput != null)
            {
                await InputInjector.TypeIntoField(tmpInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
                action.SetResult($"TMP_InputField '{tmpInput.name}'");
                return;
            }

            var legacyInput = await Find<InputField>(search, true, seconds);
            await InputInjector.TypeIntoField(legacyInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
            action.SetResult($"InputField '{legacyInput.name}'");
        }

        #endregion

        #region Keyboard Input

        /// <summary>
        /// Simulates a key press and release using Input System Key enum.
        /// </summary>
        public static async Task PressKey(Key key)
        {
            await using (await RunAction($"PressKey({key})"))
            {
                await InputInjector.PressKey(key);
            }
        }

        /// <summary>
        /// Simulates a key press and release using KeyCode.
        /// </summary>
        public static async Task PressKey(KeyCode key)
        {
            await using (await RunAction($"PressKey({key})"))
            {
                var inputKey = KeyCodeToKey(key);
                if (inputKey != Key.None)
                {
                    await InputInjector.PressKey(inputKey);
                }
            }
        }

        /// <summary>
        /// Types a string of text by pressing each character key.
        /// </summary>
        public static async Task PressKeys(string text)
        {
            await using (await RunAction($"PressKeys(\"{text}\")"))
            {
                foreach (char c in text)
                {
                    var key = CharToKey(c);
                    if (key != Key.None)
                    {
                        await InputInjector.PressKey(key);
                        await Task.Delay(50);
                    }
                }
            }
        }

        /// <summary>
        /// Holds a key down for a specified duration.
        /// </summary>
        public static async Task HoldKey(Key key, float duration)
        {
            await using (await RunAction($"HoldKey({key}, {duration}s)"))
            {
                await InputInjector.HoldKey(key, duration);
            }
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for a specified duration.
        /// </summary>
        public static async Task HoldKeys(float duration, params Key[] keys)
        {
            string keysStr = string.Join(", ", keys);
            await using (await RunAction($"HoldKeys([{keysStr}], {duration}s)"))
            {
                await InputInjector.HoldKeys(keys, duration);
            }
        }

        /// <summary>
        /// Holds a key down for a specified duration using KeyCode.
        /// </summary>
        public static async Task HoldKey(KeyCode key, float duration)
        {
            await using (await RunAction($"HoldKey({key}, {duration}s)"))
            {
                var inputKey = KeyCodeToKey(key);
                if (inputKey != Key.None)
                {
                    await InputInjector.HoldKey(inputKey, duration);
                }
            }
        }

        /// <summary>
        /// Holds multiple keys down simultaneously using KeyCode.
        /// </summary>
        public static async Task HoldKeys(float duration, params KeyCode[] keys)
        {
            string keysStr = string.Join(", ", keys);
            await using (await RunAction($"HoldKeys([{keysStr}], {duration}s)"))
            {
                var inputKeys = keys
                    .Select(k => KeyCodeToKey(k))
                    .Where(k => k != Key.None)
                    .ToArray();

                if (inputKeys.Length > 0)
                {
                    await InputInjector.HoldKeys(inputKeys, duration);
                }
            }
        }

        /// <summary>
        /// Simulates pressing a key using a character.
        /// </summary>
        public static async Task PressKey(char c)
        {
            await using (await RunAction($"PressKey('{c}')"))
            {
                var key = CharToKey(c);
                if (key != Key.None)
                {
                    await InputInjector.PressKey(key);
                }
            }
        }

        /// <summary>
        /// Simulates pressing a key using a key name string.
        /// </summary>
        public static async Task PressKey(string keyName)
        {
            await using (await RunAction($"PressKey(\"{keyName}\")"))
            {
                if (keyName.Length == 1)
                {
                    var charKey = CharToKey(keyName[0]);
                    if (charKey != Key.None)
                        await InputInjector.PressKey(charKey);
                    return;
                }

                if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
                {
                    var inputKey = KeyCodeToKey(keyCode);
                    if (inputKey != Key.None)
                        await InputInjector.PressKey(inputKey);
                    return;
                }

                var mappedKey = keyName.ToLowerInvariant() switch
                {
                    "enter" => KeyCode.Return,
                    "esc" => KeyCode.Escape,
                    "up" => KeyCode.UpArrow,
                    "down" => KeyCode.DownArrow,
                    "left" => KeyCode.LeftArrow,
                    "right" => KeyCode.RightArrow,
                    "bs" or "backspace" => KeyCode.Backspace,
                    "del" => KeyCode.Delete,
                    _ => KeyCode.None
                };

                if (mappedKey != KeyCode.None)
                {
                    var inputKey = KeyCodeToKey(mappedKey);
                    if (inputKey != Key.None)
                        await InputInjector.PressKey(inputKey);
                }
            }
        }

        #endregion

        #region Key Conversion Helpers

        /// <summary>
        /// Converts a Unity KeyCode to an Input System Key.
        /// </summary>
        public static Key KeyCodeToKey(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.A => Key.A, KeyCode.B => Key.B, KeyCode.C => Key.C, KeyCode.D => Key.D,
                KeyCode.E => Key.E, KeyCode.F => Key.F, KeyCode.G => Key.G, KeyCode.H => Key.H,
                KeyCode.I => Key.I, KeyCode.J => Key.J, KeyCode.K => Key.K, KeyCode.L => Key.L,
                KeyCode.M => Key.M, KeyCode.N => Key.N, KeyCode.O => Key.O, KeyCode.P => Key.P,
                KeyCode.Q => Key.Q, KeyCode.R => Key.R, KeyCode.S => Key.S, KeyCode.T => Key.T,
                KeyCode.U => Key.U, KeyCode.V => Key.V, KeyCode.W => Key.W, KeyCode.X => Key.X,
                KeyCode.Y => Key.Y, KeyCode.Z => Key.Z,
                KeyCode.Alpha0 => Key.Digit0, KeyCode.Alpha1 => Key.Digit1,
                KeyCode.Alpha2 => Key.Digit2, KeyCode.Alpha3 => Key.Digit3,
                KeyCode.Alpha4 => Key.Digit4, KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6, KeyCode.Alpha7 => Key.Digit7,
                KeyCode.Alpha8 => Key.Digit8, KeyCode.Alpha9 => Key.Digit9,
                KeyCode.Keypad0 => Key.Numpad0, KeyCode.Keypad1 => Key.Numpad1,
                KeyCode.Keypad2 => Key.Numpad2, KeyCode.Keypad3 => Key.Numpad3,
                KeyCode.Keypad4 => Key.Numpad4, KeyCode.Keypad5 => Key.Numpad5,
                KeyCode.Keypad6 => Key.Numpad6, KeyCode.Keypad7 => Key.Numpad7,
                KeyCode.Keypad8 => Key.Numpad8, KeyCode.Keypad9 => Key.Numpad9,
                KeyCode.F1 => Key.F1, KeyCode.F2 => Key.F2, KeyCode.F3 => Key.F3,
                KeyCode.F4 => Key.F4, KeyCode.F5 => Key.F5, KeyCode.F6 => Key.F6,
                KeyCode.F7 => Key.F7, KeyCode.F8 => Key.F8, KeyCode.F9 => Key.F9,
                KeyCode.F10 => Key.F10, KeyCode.F11 => Key.F11, KeyCode.F12 => Key.F12,
                KeyCode.Space => Key.Space,
                KeyCode.Return => Key.Enter, KeyCode.KeypadEnter => Key.NumpadEnter,
                KeyCode.Escape => Key.Escape,
                KeyCode.Tab => Key.Tab,
                KeyCode.Backspace => Key.Backspace,
                KeyCode.Delete => Key.Delete,
                KeyCode.Insert => Key.Insert,
                KeyCode.Home => Key.Home, KeyCode.End => Key.End,
                KeyCode.PageUp => Key.PageUp, KeyCode.PageDown => Key.PageDown,
                KeyCode.UpArrow => Key.UpArrow, KeyCode.DownArrow => Key.DownArrow,
                KeyCode.LeftArrow => Key.LeftArrow, KeyCode.RightArrow => Key.RightArrow,
                KeyCode.LeftShift => Key.LeftShift, KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftControl => Key.LeftCtrl, KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftAlt => Key.LeftAlt, KeyCode.RightAlt => Key.RightAlt,
                KeyCode.Minus => Key.Minus, KeyCode.Equals => Key.Equals,
                KeyCode.LeftBracket => Key.LeftBracket, KeyCode.RightBracket => Key.RightBracket,
                KeyCode.Backslash => Key.Backslash, KeyCode.Semicolon => Key.Semicolon,
                KeyCode.Quote => Key.Quote, KeyCode.Comma => Key.Comma,
                KeyCode.Period => Key.Period, KeyCode.Slash => Key.Slash,
                KeyCode.BackQuote => Key.Backquote,
                _ => Key.None
            };
        }

        /// <summary>
        /// Converts a character to an Input System Key.
        /// </summary>
        public static Key CharToKey(char c)
        {
            // Letters (case-insensitive)
            if (c >= 'a' && c <= 'z')
                return (Key)((int)Key.A + (c - 'a'));
            if (c >= 'A' && c <= 'Z')
                return (Key)((int)Key.A + (c - 'A'));

            // Digits
            if (c >= '0' && c <= '9')
                return (Key)((int)Key.Digit0 + (c - '0'));

            // Common symbols
            return c switch
            {
                ' ' => Key.Space,
                '-' => Key.Minus,
                '=' => Key.Equals,
                '[' => Key.LeftBracket,
                ']' => Key.RightBracket,
                '\\' => Key.Backslash,
                ';' => Key.Semicolon,
                '\'' => Key.Quote,
                ',' => Key.Comma,
                '.' => Key.Period,
                '/' => Key.Slash,
                '`' => Key.Backquote,
                '\n' => Key.Enter,
                '\t' => Key.Tab,
                _ => Key.None
            };
        }

        #endregion

        #region Type Actions

        /// <summary>
        /// Types text into an input field matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the input field</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within searchTime</exception>
        public static async Task Type(Search search, string text, bool clearFirst = true, bool pressEnter = false, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"Type({search}, \"{text}\")");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                await InputInjector.TypeIntoField(element, text, clearFirst, pressEnter);
                action.SetResult($"'{elementName}' (clear={clearFirst}, enter={pressEnter})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Types text into an input field. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="inputField">The input field GameObject (TMP_InputField or InputField)</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        /// <summary>
        /// Types text without targeting a specific input field (assumes something is focused).
        /// </summary>
        /// <param name="text">The text to type</param>
        public static async Task TypeText(string text)
        {
            await using (await RunAction($"TypeText(\"{text}\")"))
            {
                await InputInjector.TypeText(text);
            }
        }

        #endregion
    }
}
