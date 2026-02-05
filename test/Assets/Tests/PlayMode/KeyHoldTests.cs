using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// PlayMode tests for keyboard hold/sequence functionality.
    /// Tests InputInjector.HoldKey, HoldKeys, and the Keys fluent builder.
    /// </summary>
    [TestFixture]
    public class KeyHoldTests
    {
        private Keyboard _keyboard;
        private List<Key> _pressedKeys;
        private List<Key> _releasedKeys;
        private float _keyHoldDuration;

        [SetUp]
        public void SetUp()
        {
            _pressedKeys = new List<Key>();
            _releasedKeys = new List<Key>();
            _keyHoldDuration = 0f;

            // Ensure we have a keyboard device
            _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                _keyboard = InputSystem.AddDevice<Keyboard>();
            }

            // Reset keyboard state
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }

        [TearDown]
        public void TearDown()
        {
            // Reset keyboard state
            if (_keyboard != null)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                InputSystem.Update();
            }
        }

        [Test]
        public async Task HoldKey_SingleKey_HoldsForDuration()
        {
            var key = Key.W;
            float duration = 0.2f;
            float startTime = Time.realtimeSinceStartup;
            bool wasPressed = false;
            bool wasReleased = false;

            // Start monitoring key state in parallel
            var monitorTask = MonitorKeyState(key, duration + 0.1f, pressed =>
            {
                if (pressed && !wasPressed)
                {
                    wasPressed = true;
                    _keyHoldDuration = Time.realtimeSinceStartup;
                }
                else if (!pressed && wasPressed && !wasReleased)
                {
                    wasReleased = true;
                    _keyHoldDuration = Time.realtimeSinceStartup - _keyHoldDuration;
                }
            });

            // Hold the key
            await InputInjector.HoldKey(key, duration);

            // Wait for monitor to complete
            await monitorTask;

            Assert.IsTrue(wasPressed, "Key should have been pressed");
            Assert.IsTrue(wasReleased, "Key should have been released");
            Assert.GreaterOrEqual(_keyHoldDuration, duration * 0.8f, $"Key should have been held for approximately {duration}s");
        }

        [Test]
        public async Task HoldKeys_MultipleKeys_HoldsSimultaneously()
        {
            var keys = new[] { Key.W, Key.A };
            float duration = 0.15f;
            bool bothPressed = false;

            // Check if both keys are pressed at some point
            var monitorTask = Task.Run(async () =>
            {
                var startTime = System.DateTime.UtcNow;
                var maxDuration = System.TimeSpan.FromSeconds(duration + 0.1);
                while ((System.DateTime.UtcNow - startTime) < maxDuration)
                {
                    if (_keyboard[Key.W].isPressed && _keyboard[Key.A].isPressed)
                    {
                        bothPressed = true;
                    }
                    await Task.Delay(10);
                }
            });

            // Hold both keys
            await InputInjector.HoldKeys(keys, duration);
            await monitorTask;

            Assert.IsTrue(bothPressed, "Both W and A keys should have been pressed simultaneously");

            // Verify both are released after
            Assert.IsFalse(_keyboard[Key.W].isPressed, "W key should be released");
            Assert.IsFalse(_keyboard[Key.A].isPressed, "A key should be released");
        }

        [Test]
        public async Task KeysBuilder_SingleKeyHold_Works()
        {
            float duration = 0.1f;
            bool wasPressed = false;

            var monitorTask = Task.Run(async () =>
            {
                var startTime = System.DateTime.UtcNow;
                var maxDuration = System.TimeSpan.FromSeconds(duration + 0.1);
                while ((System.DateTime.UtcNow - startTime) < maxDuration)
                {
                    if (_keyboard[Key.S].isPressed)
                    {
                        wasPressed = true;
                    }
                    await Task.Delay(10);
                }
            });

            // Use the Keys builder
            await Keys.Hold(Key.S).For(duration);
            await monitorTask;

            Assert.IsTrue(wasPressed, "S key should have been pressed");
            Assert.IsFalse(_keyboard[Key.S].isPressed, "S key should be released after");
        }

        [Test]
        public async Task KeysBuilder_MultipleKeysHold_Works()
        {
            float duration = 0.1f;
            bool bothPressed = false;

            var monitorTask = Task.Run(async () =>
            {
                var startTime = System.DateTime.UtcNow;
                var maxDuration = System.TimeSpan.FromSeconds(duration + 0.1);
                while ((System.DateTime.UtcNow - startTime) < maxDuration)
                {
                    if (_keyboard[Key.LeftShift].isPressed && _keyboard[Key.W].isPressed)
                    {
                        bothPressed = true;
                    }
                    await Task.Delay(10);
                }
            });

            // Use the Keys builder with multiple keys (sprint)
            await Keys.Hold(Key.LeftShift, Key.W).For(duration);
            await monitorTask;

            Assert.IsTrue(bothPressed, "Shift+W should have been pressed simultaneously");
        }

        [Test]
        public async Task KeysBuilder_ChainedSequence_ExecutesInOrder()
        {
            var sequence = new List<Key>();

            // Monitor which keys are pressed in what order
            var monitorTask = Task.Run(async () =>
            {
                var startTime = System.DateTime.UtcNow;
                var maxDuration = System.TimeSpan.FromSeconds(0.5);
                Key? lastKey = null;
                while ((System.DateTime.UtcNow - startTime) < maxDuration)
                {
                    foreach (var key in new[] { Key.W, Key.A, Key.D })
                    {
                        if (_keyboard[key].isPressed && lastKey != key)
                        {
                            sequence.Add(key);
                            lastKey = key;
                        }
                    }
                    await Task.Delay(10);
                }
            });

            // Chain: W for 0.1s, then A for 0.1s, then D for 0.1s
            await Keys.Hold(Key.W).For(0.1f)
                      .Then(Key.A).For(0.1f)
                      .Then(Key.D).For(0.1f);

            await monitorTask;

            Assert.GreaterOrEqual(sequence.Count, 3, "Should have detected at least 3 key presses");
            Assert.AreEqual(Key.W, sequence[0], "First key should be W");
            Assert.AreEqual(Key.A, sequence[1], "Second key should be A");
            Assert.AreEqual(Key.D, sequence[2], "Third key should be D");
        }

        [Test]
        public async Task KeysBuilder_PressKey_QuickTap()
        {
            bool wasPressed = false;

            var monitorTask = Task.Run(async () =>
            {
                var startTime = System.DateTime.UtcNow;
                var maxDuration = System.TimeSpan.FromSeconds(0.2);
                while ((System.DateTime.UtcNow - startTime) < maxDuration)
                {
                    if (_keyboard[Key.Space].wasPressedThisFrame || _keyboard[Key.Space].isPressed)
                    {
                        wasPressed = true;
                    }
                    await Task.Delay(10);
                }
            });

            // Quick press (tap)
            await Keys.Press(Key.Space);
            await monitorTask;

            Assert.IsTrue(wasPressed, "Space should have been pressed");
        }

        private async Task MonitorKeyState(Key key, float duration, System.Action<bool> onStateChange)
        {
            var startTime = System.DateTime.UtcNow;
            var maxDuration = System.TimeSpan.FromSeconds(duration);
            bool lastState = false;

            while ((System.DateTime.UtcNow - startTime) < maxDuration)
            {
                bool currentState = _keyboard[key].isPressed;
                if (currentState != lastState)
                {
                    onStateChange?.Invoke(currentState);
                    lastState = currentState;
                }
                await Task.Delay(10);
            }
        }
    }
}
