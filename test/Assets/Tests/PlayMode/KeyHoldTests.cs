using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.Tests
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

        [UnityTest]
        public IEnumerator HoldKey_SingleKey_HoldsForDuration()
        {
            return UniTask.ToCoroutine(async () =>
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
            });
        }

        [UnityTest]
        public IEnumerator HoldKeys_MultipleKeys_HoldsSimultaneously()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var keys = new[] { Key.W, Key.A };
                float duration = 0.15f;
                bool bothPressed = false;

                // Check if both keys are pressed at some point
                var monitorTask = UniTask.Create(async () =>
                {
                    float elapsed = 0f;
                    while (elapsed < duration + 0.1f)
                    {
                        if (_keyboard[Key.W].isPressed && _keyboard[Key.A].isPressed)
                        {
                            bothPressed = true;
                        }
                        await UniTask.Yield();
                        elapsed += Time.deltaTime;
                    }
                });

                // Hold both keys
                await InputInjector.HoldKeys(keys, duration);
                await monitorTask;

                Assert.IsTrue(bothPressed, "Both W and A keys should have been pressed simultaneously");

                // Verify both are released after
                Assert.IsFalse(_keyboard[Key.W].isPressed, "W key should be released");
                Assert.IsFalse(_keyboard[Key.A].isPressed, "A key should be released");
            });
        }

        [UnityTest]
        public IEnumerator KeysBuilder_SingleKeyHold_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                float duration = 0.1f;
                bool wasPressed = false;

                var monitorTask = UniTask.Create(async () =>
                {
                    float elapsed = 0f;
                    while (elapsed < duration + 0.1f)
                    {
                        if (_keyboard[Key.S].isPressed)
                        {
                            wasPressed = true;
                        }
                        await UniTask.Yield();
                        elapsed += Time.deltaTime;
                    }
                });

                // Use the Keys builder
                await Keys.Hold(Key.S).For(duration);
                await monitorTask;

                Assert.IsTrue(wasPressed, "S key should have been pressed");
                Assert.IsFalse(_keyboard[Key.S].isPressed, "S key should be released after");
            });
        }

        [UnityTest]
        public IEnumerator KeysBuilder_MultipleKeysHold_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                float duration = 0.1f;
                bool bothPressed = false;

                var monitorTask = UniTask.Create(async () =>
                {
                    float elapsed = 0f;
                    while (elapsed < duration + 0.1f)
                    {
                        if (_keyboard[Key.LeftShift].isPressed && _keyboard[Key.W].isPressed)
                        {
                            bothPressed = true;
                        }
                        await UniTask.Yield();
                        elapsed += Time.deltaTime;
                    }
                });

                // Use the Keys builder with multiple keys (sprint)
                await Keys.Hold(Key.LeftShift, Key.W).For(duration);
                await monitorTask;

                Assert.IsTrue(bothPressed, "Shift+W should have been pressed simultaneously");
            });
        }

        [UnityTest]
        public IEnumerator KeysBuilder_ChainedSequence_ExecutesInOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var sequence = new List<Key>();

                // Monitor which keys are pressed in what order
                var monitorTask = UniTask.Create(async () =>
                {
                    float elapsed = 0f;
                    Key? lastKey = null;
                    while (elapsed < 0.5f)
                    {
                        foreach (var key in new[] { Key.W, Key.A, Key.D })
                        {
                            if (_keyboard[key].isPressed && lastKey != key)
                            {
                                sequence.Add(key);
                                lastKey = key;
                            }
                        }
                        await UniTask.Yield();
                        elapsed += Time.deltaTime;
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
            });
        }

        [UnityTest]
        public IEnumerator KeysBuilder_PressKey_QuickTap()
        {
            return UniTask.ToCoroutine(async () =>
            {
                bool wasPressed = false;

                var monitorTask = UniTask.Create(async () =>
                {
                    float elapsed = 0f;
                    while (elapsed < 0.2f)
                    {
                        if (_keyboard[Key.Space].wasPressedThisFrame || _keyboard[Key.Space].isPressed)
                        {
                            wasPressed = true;
                        }
                        await UniTask.Yield();
                        elapsed += Time.deltaTime;
                    }
                });

                // Quick press (tap)
                await Keys.Press(Key.Space);
                await monitorTask;

                Assert.IsTrue(wasPressed, "Space should have been pressed");
            });
        }

        private async UniTask MonitorKeyState(Key key, float duration, System.Action<bool> onStateChange)
        {
            float elapsed = 0f;
            bool lastState = false;

            while (elapsed < duration)
            {
                bool currentState = _keyboard[key].isPressed;
                if (currentState != lastState)
                {
                    onStateChange?.Invoke(currentState);
                    lastState = currentState;
                }
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }
        }
    }
}
