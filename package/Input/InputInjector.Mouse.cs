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
    internal static partial class InputInjector
    {
        /// <summary>
        /// Injects a click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// Events are queued and processed by Unity's natural update cycle to ensure
        /// all scripts (including those checking wasPressedThisFrame) see each state change.
        /// </summary>
        public static async Task InjectPointerTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 1);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());

            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Click - No mouse device found, cannot inject click");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons pressed, zero delta)
            // Using full StateEvent ensures no stale button state causes camera jumps
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Press — processed by Unity's automatic update, visible to all scripts
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(4);

            // Release — realistic gap between press and release (~67ms at 60fps)
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            LogDebug("InjectPointerTap complete");
            InputVisualizer.RecordCursorEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a double click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerDoubleTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerDoubleTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 2);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchDoubleTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] DoubleClick - No mouse device found");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // First click — press
            using (StateEvent.From(mouse, out var d1))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, d1);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, d1);
                mouse.leftButton.WriteValueIntoEvent(1f, d1);
                InputSystem.QueueEvent(d1);
            }
            await Async.DelayFrames(4);

            // First click — release
            using (StateEvent.From(mouse, out var u1))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, u1);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, u1);
                mouse.leftButton.WriteValueIntoEvent(0f, u1);
                InputSystem.QueueEvent(u1);
            }
            await Async.DelayFrames(2);

            // Inter-click gap — must be within double-click speed threshold (~300ms)
            await Async.Delay(2, 0.05f);

            // Second click — press
            using (StateEvent.From(mouse, out var d2))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, d2);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, d2);
                mouse.leftButton.WriteValueIntoEvent(1f, d2);
                InputSystem.QueueEvent(d2);
            }
            await Async.DelayFrames(4);

            // Second click — release
            using (StateEvent.From(mouse, out var u2))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, u2);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, u2);
                mouse.leftButton.WriteValueIntoEvent(0f, u2);
                InputSystem.QueueEvent(u2);
            }
            LogDebug("InjectPointerDoubleTap complete");
            InputVisualizer.RecordCursorEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a triple click/tap at the specified screen position.
        /// Triple-click is commonly used to select all text in input fields.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerTripleTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerTripleTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 3);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchTripleTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] TripleClick - No mouse device found");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Three clicks with inter-click gaps within multi-click speed threshold
            for (int click = 0; click < 3; click++)
            {
                if (click > 0)
                    await Async.Delay(2, 0.05f);

                using (StateEvent.From(mouse, out var down))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, down);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, down);
                    mouse.leftButton.WriteValueIntoEvent(1f, down);
                    InputSystem.QueueEvent(down);
                }
                await Async.DelayFrames(4);

                using (StateEvent.From(mouse, out var up))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, up);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, up);
                    mouse.leftButton.WriteValueIntoEvent(0f, up);
                    InputSystem.QueueEvent(up);
                }
                await Async.DelayFrames(2);
            }

            LogDebug("InjectPointerTripleTap complete");
            InputVisualizer.RecordCursorEnd();
        }

        // ──────────────────────────────────────────────────────────────────
        // Pointer lifecycle (hold + drag) — stateful single-pointer injection
        // used by the Remote IDE /input/pointer endpoint.
        //
        // On Android, the native BugpunchTouchRecorder.injectPointer* path
        // fires at the OS input layer and is visible to both legacy
        // Input.touches and the new Input System. These managed methods are
        // also called so games that read *only* via the new Input System (and
        // that happen to miss the native path) still receive events.
        //
        // On iOS / Editor, managed is the only route — private UIKit/Mouse
        // APIs would block an App Store build.
        // ──────────────────────────────────────────────────────────────────

        const int LifecycleTouchId = 7777;
        static bool s_pointerActive;
        static Vector2 s_pointerLastPos;

        public static Task InjectPointerDown(Vector2 screenPosition)
        {
            var useTouch = ShouldUseTouchInput();
            BugpunchLog.Info("InputInjector", $"PointerDown enter pos=({screenPosition.x:F0},{screenPosition.y:F0}) useTouch={useTouch} prevActive={s_pointerActive} frame={Time.frameCount}");
            s_pointerLastPos = screenPosition;
            s_pointerActive = true;
#if ENABLE_INPUT_SYSTEM
            InputVisualizer.RecordCursorPosition(screenPosition, !useTouch);
            if (useTouch)
            {
                var ts = GetTouchscreen();
                BugpunchLog.Info("InputInjector", $"PointerDown touch device={(ts == null ? "NULL" : ts.deviceId.ToString())}");
                if (ts == null) return Task.CompletedTask;
                InputSystem.QueueStateEvent(ts, new TouchState
                {
                    touchId = LifecycleTouchId,
                    position = screenPosition,
                    delta = Vector2.zero,
                    phase = UnityEngine.InputSystem.TouchPhase.Began,
                    pressure = 1f,
                });
                BugpunchLog.Info("InputInjector", $"PointerDown queued Began touchId={LifecycleTouchId}");
            }
            else
            {
                var mouse = GetMouse();
                BugpunchLog.Info("InputInjector", $"PointerDown mouse device={(mouse == null ? "NULL" : mouse.deviceId.ToString())}");
                if (mouse == null) return Task.CompletedTask;
                using (StateEvent.From(mouse, out var e))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, e);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, e);
                    mouse.leftButton.WriteValueIntoEvent(1f, e);
                    InputSystem.QueueEvent(e);
                }
                BugpunchLog.Info("InputInjector", "PointerDown queued mouse press");
            }
#endif
            return Task.CompletedTask;
        }

        public static Task InjectPointerMove(Vector2 screenPosition)
        {
            BugpunchLog.Info("InputInjector", $"PointerMove enter pos=({screenPosition.x:F0},{screenPosition.y:F0}) active={s_pointerActive} frame={Time.frameCount}");
            if (!s_pointerActive) return Task.CompletedTask;
            var delta = screenPosition - s_pointerLastPos;
            s_pointerLastPos = screenPosition;
#if ENABLE_INPUT_SYSTEM
            if (ShouldUseTouchInput())
            {
                var ts = GetTouchscreen();
                if (ts == null) return Task.CompletedTask;
                InputSystem.QueueStateEvent(ts, new TouchState
                {
                    touchId = LifecycleTouchId,
                    position = screenPosition,
                    delta = delta,
                    phase = UnityEngine.InputSystem.TouchPhase.Moved,
                    pressure = 1f,
                });
            }
            else
            {
                var mouse = GetMouse();
                if (mouse == null) return Task.CompletedTask;
                using (StateEvent.From(mouse, out var e))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, e);
                    mouse.delta.WriteValueIntoEvent(delta, e);
                    mouse.leftButton.WriteValueIntoEvent(1f, e);
                    InputSystem.QueueEvent(e);
                }
            }
#endif
            return Task.CompletedTask;
        }

        public static Task InjectPointerUp(Vector2 screenPosition)
        {
            var useTouch = ShouldUseTouchInput();
            BugpunchLog.Info("InputInjector", $"PointerUp enter pos=({screenPosition.x:F0},{screenPosition.y:F0}) useTouch={useTouch} active={s_pointerActive} frame={Time.frameCount}");
            if (!s_pointerActive)
            {
                BugpunchLog.Warn("InputInjector", "PointerUp early-return: s_pointerActive==false (no preceding Down)");
                return Task.CompletedTask;
            }
            s_pointerActive = false;
#if ENABLE_INPUT_SYSTEM
            if (useTouch)
            {
                var ts = GetTouchscreen();
                BugpunchLog.Info("InputInjector", $"PointerUp touch device={(ts == null ? "NULL" : ts.deviceId.ToString())}");
                if (ts == null) { InputVisualizer.RecordCursorEnd(); return Task.CompletedTask; }
                InputSystem.QueueStateEvent(ts, new TouchState
                {
                    touchId = LifecycleTouchId,
                    position = screenPosition,
                    delta = Vector2.zero,
                    phase = UnityEngine.InputSystem.TouchPhase.Ended,
                    pressure = 0f,
                });
                BugpunchLog.Info("InputInjector", $"PointerUp queued Ended touchId={LifecycleTouchId}");
            }
            else
            {
                var mouse = GetMouse();
                BugpunchLog.Info("InputInjector", $"PointerUp mouse device={(mouse == null ? "NULL" : mouse.deviceId.ToString())}");
                if (mouse == null) { InputVisualizer.RecordCursorEnd(); return Task.CompletedTask; }
                using (StateEvent.From(mouse, out var e))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, e);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, e);
                    mouse.leftButton.WriteValueIntoEvent(0f, e);
                    InputSystem.QueueEvent(e);
                }
                BugpunchLog.Info("InputInjector", "PointerUp queued mouse release");
            }
            InputVisualizer.RecordCursorEnd();
            BugpunchLog.Info("InputInjector", "PointerUp RecordCursorEnd called (hand should fade)");
#endif
            return Task.CompletedTask;
        }

        public static Task InjectPointerCancel()
        {
            BugpunchLog.Info("InputInjector", $"PointerCancel active={s_pointerActive}");
            return s_pointerActive ? InjectPointerUp(s_pointerLastPos) : Task.CompletedTask;
        }

        /// <summary>
        /// Injects a drag gesture using the appropriate input method for the platform.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement (minimum 1 second)</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging (ignored on touch devices)</param>
        public static async Task InjectPointerDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            InputVisualizer.RecordDragStart(startPos);
            InputVisualizer.RecordCursorPosition(startPos, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                await InjectTouchDrag(startPos, endPos, duration, holdTime);
                InputVisualizer.RecordDragEnd(endPos);
                InputVisualizer.RecordCursorEnd();
                return;
            }

            await InjectMouseDrag(startPos, endPos, duration, holdTime, button);
            InputVisualizer.RecordDragEnd(endPos);
            InputVisualizer.RecordCursorEnd();
        }

        /// <summary>
        /// Injects a mouse drag from start to end position using the Input System.
        /// Uses frame-based yields to ensure Unity processes events each frame (matching touch behavior).
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async Task InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] MouseDrag - No mouse device found, cannot inject drag");
                return;
            }

            // Get the appropriate button control based on the enum
            var buttonControl = button switch
            {
                PointerButton.Right => mouse.rightButton,
                PointerButton.Middle => mouse.middleButton,
                _ => mouse.leftButton
            };

            LogDebug($"MouseDrag start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s hold={holdTime}s button={button}");

            // Wait for any previous input state to settle before starting new drag
            await Async.DelayFrames(2);

            startPos = ClampToScreen(startPos);
            endPos = ClampToScreen(endPos);
            Vector2 previousPos = startPos;

            // Move mouse to start position — processed next frame
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Mouse button down at start
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                buttonControl.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(2); // Allow PointerDown to register

            LogDebug($"MouseDrag mouse down at ({startPos.x:F0},{startPos.y:F0})");

            // Hold at start position before dragging (wall-clock based using Stopwatch)
            if (holdTime > 0)
            {
                long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdTime * Stopwatch.Frequency);
                while (Stopwatch.GetTimestamp() < holdEndTicks)
                {
                    using (StateEvent.From(mouse, out var holdPtr))
                    {
                        mouse.position.WriteValueIntoEvent(startPos, holdPtr);
                        mouse.delta.WriteValueIntoEvent(Vector2.zero, holdPtr);
                        buttonControl.WriteValueIntoEvent(1f, holdPtr);
                        InputSystem.QueueEvent(holdPtr);
                    }
                    await Async.DelayFrames(1);
                }
                LogDebug($"MouseDrag held for {holdTime}s");
            }
            else
            {
                // Even with no hold, send one event at start position to register the initial press
                // This ensures listeners can capture lastMousePosition before movement begins
                using (StateEvent.From(mouse, out var initPtr))
                {
                    mouse.position.WriteValueIntoEvent(startPos, initPtr);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, initPtr);
                    buttonControl.WriteValueIntoEvent(1f, initPtr);
                    InputSystem.QueueEvent(initPtr);
                }
                await Async.DelayFrames(1);
            }

            // Interpolate mouse position over duration
            // Use frame-based interpolation with minimum frames for realistic drag speed
            const int minFrames = 10; // ~167ms at 60fps — realistic minimum drag duration
            int frameCount = 0;
            long dragStartTicks = Stopwatch.GetTimestamp();
            long durationTicks = (long)(duration * Stopwatch.Frequency);

            while (frameCount < minFrames || Stopwatch.GetTimestamp() < dragStartTicks + durationTicks)
            {
                frameCount++;
                // Use frame count for interpolation to ensure smooth progression regardless of timing
                float t = Mathf.Clamp01((float)frameCount / minFrames);
                long elapsed = Stopwatch.GetTimestamp() - dragStartTicks;
                if (duration > 0 && elapsed < durationTicks)
                {
                    // If still within duration, use time-based interpolation for smoother motion
                    t = Mathf.Max(t, (float)elapsed / durationTicks);
                }
                t = Mathf.Clamp01(t);

                Vector2 currentPos = ClampToScreen(Vector2.Lerp(startPos, endPos, t));
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(mouse, out var movePtr))
                {
                    mouse.position.WriteValueIntoEvent(currentPos, movePtr);
                    mouse.delta.WriteValueIntoEvent(delta, movePtr);
                    buttonControl.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }

                // Update cursor visualization with trail
                InputVisualizer.RecordCursorPosition(currentPos, isMouse: true, fingerIndex: 0, isPressed: true);

                previousPos = currentPos;
                await Async.DelayFrames(1);

                // Exit early if we've reached the end position and minimum frames
                if (frameCount >= minFrames && t >= 1f) break;
            }

            // Ensure we reach the final position
            using (StateEvent.From(mouse, out var finalPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, finalPtr);
                mouse.delta.WriteValueIntoEvent(endPos - previousPos, finalPtr);
                buttonControl.WriteValueIntoEvent(1f, finalPtr);
                InputSystem.QueueEvent(finalPtr);
            }
            await Async.DelayFrames(1);

            // Mouse button up at end
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                buttonControl.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }

            LogDebug($"MouseDrag mouse up at ({endPos.x:F0},{endPos.y:F0})");

            // Allow UI to fully process drag end and pointer up before next action
            await Async.DelayFrames(4);
        }

        /// <summary>
        /// Injects a scroll event at the specified position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async Task InjectScroll(Vector2 position, float delta)
        {
            await InjectScroll(position, new Vector2(0, delta));
        }

        /// <summary>
        /// Injects a scroll event at the specified position with a Vector2 delta.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="scrollDelta">Scroll delta vector (for horizontal and vertical scrolling)</param>
        public static async Task InjectScroll(Vector2 position, Vector2 scrollDelta)
        {
            InputVisualizer.RecordScroll(position, scrollDelta);


            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Scroll - No mouse device found");
                return;
            }

            // Move mouse to position first so EventSystem establishes pointerEnter
            // on the element under the cursor. Without this, scroll events are dropped
            // because InputSystemUIInputModule requires a valid pointerEnter target.
            // Use full StateEvent to ensure clean state (zero delta, no buttons)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(position, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            // Wait for Unity's automatic update to process position, then
            // InputSystemUIInputModule.Process() establishes pointerEnter
            await Async.DelayFrames(2);

            // Inject scroll using DeltaStateEvent on the scroll control.
            // CRITICAL: Do NOT call InputSystem.Update() after queuing — let the player
            // loop process it. Manual Update() would apply the scroll state, but then
            // Mouse.OnNextUpdate() resets scroll to zero at the start of the NEXT frame's
            // InputSystem.Update() — BEFORE InputSystemUIInputModule.Process() reads it.
            // By only queuing, the event is processed in the next frame's natural
            // InputSystem.Update() → Process() sequence, where scroll is still non-zero.
            using (DeltaStateEvent.From(mouse.scroll, out var scrollPtr))
            {
                mouse.scroll.WriteValueIntoEvent(scrollDelta, scrollPtr);
                InputSystem.QueueEvent(scrollPtr);
            }
            // Let the player loop process: InputSystem.Update() applies scroll,
            // then EventSystem.Update() → Process() reads it before next reset.
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a hold/long-press at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerHold(Vector2 screenPosition, float holdSeconds)
        {
            InputVisualizer.RecordHoldStart(screenPosition);


            if (ShouldUseTouchInput())
            {
                await InjectTouchHold(screenPosition, holdSeconds);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Hold - No mouse device found, cannot inject hold");
                return;
            }

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Mouse button down
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(1);

            // Hold for specified duration (wall-clock based)
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                await Async.DelayFrames(1);
            }

            // Mouse button up
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            InputVisualizer.RecordHoldEnd();
            await Async.DelayFrames(2);
        }
    }
}
