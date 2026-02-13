using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

using ODDGames.UIAutomation;

using static ODDGames.UIAutomation.ActionExecutor;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// PlayMode tests for camera control via swiping gestures.
    /// Tests all gesture types: swipe, pinch, rotate, two-finger swipe.
    /// </summary>
    [TestFixture]
    public class CameraGestureTests : UIAutomationTestFixture
    {
        private Camera _camera;
        private GameObject _cameraGO;
        private TestCameraController _cameraController;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            _createdObjects = new List<GameObject>();

            // Create EventSystem with Input System module
            var esGO = new GameObject("EventSystem");
            _eventSystem = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            _createdObjects.Add(esGO);

            // Create camera with controller - position it behind the scene looking forward
            _cameraGO = new GameObject("TestCamera");
            _cameraGO.transform.position = new Vector3(0, 0, -10); // Position camera behind the scene
            _camera = _cameraGO.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 8f; // Larger view to see more of the scene
            _camera.backgroundColor = new Color(0.1f, 0.1f, 0.2f); // Dark blue background
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            _cameraController = _cameraGO.AddComponent<TestCameraController>();
            _createdObjects.Add(_cameraGO);

            // Create a grid of visible objects so camera movement is obvious
            CreateVisibleScene();
        }

        private void CreateVisibleScene()
        {
            // Create a central reference cube (red)
            var centerCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerCube.name = "CenterCube";
            centerCube.transform.position = Vector3.zero;
            centerCube.GetComponent<Renderer>().material.color = Color.red;
            _createdObjects.Add(centerCube);

            // Create a grid of smaller cubes around it for visual reference
            var colors = new[] { Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white };
            int colorIndex = 0;

            for (int x = -2; x <= 2; x++)
            {
            for (int y = -2; y <= 2; y++)
            {
                if (x == 0 && y == 0) continue; // Skip center

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cube_{x}_{y}";
                cube.transform.position = new Vector3(x * 2f, y * 2f, 0);
                cube.transform.localScale = Vector3.one * 0.5f;
                cube.GetComponent<Renderer>().material.color = colors[colorIndex % colors.Length];
                _createdObjects.Add(cube);
                colorIndex++;
            }
            }

            // Create corner markers (spheres) at the edges
            var corners = new[]
            {
            (new Vector3(-6, 6, 0), "TopLeft", Color.red),
            (new Vector3(6, 6, 0), "TopRight", Color.green),
            (new Vector3(-6, -6, 0), "BottomLeft", Color.blue),
            (new Vector3(6, -6, 0), "BottomRight", Color.yellow)
            };

            foreach (var (pos, name, color) in corners)
            {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.position = pos;
            sphere.GetComponent<Renderer>().material.color = color;
            _createdObjects.Add(sphere);
            }

            // Add a floor plane for better depth perception (behind the cubes, facing camera)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0, 0, 5); // Behind the cubes (positive Z = further from camera)
            floor.transform.localScale = new Vector3(20, 20, 1);
            floor.GetComponent<Renderer>().material.color = new Color(0.2f, 0.2f, 0.25f);
            _createdObjects.Add(floor);
        }

        [TearDown]
        public override async Task TearDown()
        {
            foreach (var obj in _createdObjects)
            {
            if (obj != null)
                Object.Destroy(obj);
            }
            _createdObjects.Clear();

            await base.TearDown();
        }

        #region Swipe Tests

        [Test]
        public async Task Swipe_Left_MovesCameraRight()
        {
            var initialPos = _camera.transform.position;
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestSwipe(SwipeDirection.Left, distance: 0.3f, duration: 0.3f);

            // Swiping left should pan camera right (opposite direction)
            Assert.Greater(_cameraController.TotalPanDelta.x, 0,
                "Swiping left should result in positive pan delta (camera moves right)");
        }

        [Test]
        public async Task Swipe_Right_MovesCameraLeft()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestSwipe(SwipeDirection.Right, distance: 0.3f, duration: 0.3f);

            Assert.Less(_cameraController.TotalPanDelta.x, 0,
                "Swiping right should result in negative pan delta (camera moves left)");
        }

        [Test]
        public async Task Swipe_Up_MovesCameraDown()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestSwipe(SwipeDirection.Up, distance: 0.3f, duration: 0.3f);

            Assert.Less(_cameraController.TotalPanDelta.y, 0,
                "Swiping up should result in negative pan delta (camera moves down)");
        }

        [Test]
        public async Task Swipe_Down_MovesCameraUp()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestSwipe(SwipeDirection.Down, distance: 0.3f, duration: 0.3f);

            Assert.Greater(_cameraController.TotalPanDelta.y, 0,
                "Swiping down should result in positive pan delta (camera moves up)");
        }

        [Test]
        public async Task Swipe_Diagonal_MovesCameraDiagonally()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Swipe left then down for diagonal movement
            await helper.TestSwipe(SwipeDirection.Left, distance: 0.2f, duration: 0.2f);
            await helper.TestSwipe(SwipeDirection.Down, distance: 0.2f, duration: 0.2f);

            Assert.Greater(_cameraController.TotalPanDelta.x, 0, "Should have positive X pan");
            Assert.Greater(_cameraController.TotalPanDelta.y, 0, "Should have positive Y pan");
        }

        [Test]
        public async Task SwipeAt_CornerPosition_Works()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Swipe from top-left corner
            await helper.TestSwipeAt(0.1f, 0.9f, SwipeDirection.Right, distance: 0.2f, duration: 0.3f);

            Assert.Less(_cameraController.TotalPanDelta.x, 0,
                "SwipeAt should work from corner positions");
        }

        [Test]
        public async Task Swipe_MultipleInSequence_AccumulatesPan()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Multiple swipes in same direction
            await helper.TestSwipe(SwipeDirection.Left, distance: 0.1f, duration: 0.15f);
            var firstPan = _cameraController.TotalPanDelta.x;

            await helper.TestSwipe(SwipeDirection.Left, distance: 0.1f, duration: 0.15f);
            var secondPan = _cameraController.TotalPanDelta.x;

            Assert.Greater(secondPan, firstPan,
                "Multiple swipes should accumulate pan movement");
        }

        #endregion

        #region Pinch/Zoom Tests

        [Test]
        public async Task Pinch_In_ZoomsOut()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Pinch in (scale < 1) = zoom out
            await helper.TestPinch(0.5f, duration: 0.4f);

            Assert.Less(_cameraController.TotalZoomDelta, 0,
                "Pinch in (scale < 1) should result in negative zoom delta (zoom out)");
        }

        [Test]
        public async Task Pinch_Out_ZoomsIn()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Pinch out (scale > 1) = zoom in
            await helper.TestPinch(2.0f, duration: 0.4f);

            Assert.Greater(_cameraController.TotalZoomDelta, 0,
                "Pinch out (scale > 1) should result in positive zoom delta (zoom in)");
        }

        [Test]
        public async Task PinchAt_OffCenter_Works()
        {
            // Wait a few frames to ensure clean state from any previous touch gestures
            await Async.DelayFrames(1);
            await Async.DelayFrames(1);
            await Async.DelayFrames(1);

            // Reset tracking to ensure we only measure this gesture
            _cameraController.ResetTracking();

            var helper = CreateGestureHelper();

            // Pinch at bottom-right area
            await helper.TestPinchAt(0.75f, 0.25f, 1.5f, duration: 0.4f);

            Assert.Greater(_cameraController.TotalZoomDelta, 0,
                "PinchAt should work at off-center positions");
        }

        #endregion

        #region Rotation Tests

        [Test]
        public async Task Rotate_Clockwise_RotatesCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Rotate 45 degrees clockwise
            await helper.TestRotate(45f, duration: 0.4f);

            Assert.Greater(_cameraController.TotalRotationDelta, 0,
                "Clockwise rotation should result in positive rotation delta");
            Assert.Greater(_cameraController.TotalRotationDelta, 30f,
                "Rotation should be close to requested angle");
        }

        [Test]
        public async Task Rotate_CounterClockwise_RotatesCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Rotate 45 degrees counter-clockwise
            await helper.TestRotate(-45f, duration: 0.4f);

            Assert.Less(_cameraController.TotalRotationDelta, 0,
                "Counter-clockwise rotation should result in negative rotation delta");
        }

        [Test]
        public async Task Rotate_FullCircle_Works()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Rotate in increments (full rotation at once may not work well)
            await helper.TestRotate(90f, duration: 0.3f);
            await helper.TestRotate(90f, duration: 0.3f);
            await helper.TestRotate(90f, duration: 0.3f);
            await helper.TestRotate(90f, duration: 0.3f);

            Assert.Greater(_cameraController.TotalRotationDelta, 300f,
                "Multiple rotations should accumulate to near 360 degrees");
        }

        [Test]
        public async Task RotateAt_CustomPosition_Works()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Rotate at top-left quadrant
            await helper.TestRotateAt(0.25f, 0.75f, 30f, duration: 0.4f);

            Assert.Greater(_cameraController.TotalRotationDelta, 0,
                "RotateAt should work at custom positions");
        }

        #endregion

        #region Two-Finger Swipe Tests

        [Test]
        public async Task TwoFingerSwipe_Left_PansCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestTwoFingerSwipe(SwipeDirection.Left, distance: 0.25f, duration: 0.3f);

            Assert.Greater(_cameraController.TotalTwoFingerPanDelta.x, 0,
                "Two-finger swipe left should pan camera right");
        }

        [Test]
        public async Task TwoFingerSwipe_Right_PansCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestTwoFingerSwipe(SwipeDirection.Right, distance: 0.25f, duration: 0.3f);

            Assert.Less(_cameraController.TotalTwoFingerPanDelta.x, 0,
                "Two-finger swipe right should pan camera left");
        }

        [Test]
        public async Task TwoFingerSwipe_Up_PansCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestTwoFingerSwipe(SwipeDirection.Up, distance: 0.25f, duration: 0.3f);

            Assert.Less(_cameraController.TotalTwoFingerPanDelta.y, 0,
                "Two-finger swipe up should pan camera down");
        }

        [Test]
        public async Task TwoFingerSwipe_Down_PansCamera()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();
            await helper.TestTwoFingerSwipe(SwipeDirection.Down, distance: 0.25f, duration: 0.3f);

            Assert.Greater(_cameraController.TotalTwoFingerPanDelta.y, 0,
                "Two-finger swipe down should pan camera up");
        }

        [Test]
        public async Task TwoFingerSwipeAt_Works()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Two-finger swipe from right side of screen
            await helper.TestTwoFingerSwipeAt(0.8f, 0.5f, SwipeDirection.Left, distance: 0.2f, duration: 0.3f);

            Assert.Greater(_cameraController.TotalTwoFingerPanDelta.x, 0,
                "TwoFingerSwipeAt should work at custom positions");
        }

        #endregion

        #region Combined Gesture Tests

        [Test]
        public async Task Combined_PanAndZoom_BothWork()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Pan first
            await helper.TestSwipe(SwipeDirection.Left, distance: 0.2f, duration: 0.2f);
            var panAfterSwipe = _cameraController.TotalPanDelta.x;

            // Then zoom
            await helper.TestPinch(1.5f, duration: 0.3f);

            Assert.Greater(panAfterSwipe, 0, "Pan should have occurred");
            Assert.Greater(_cameraController.TotalZoomDelta, 0, "Zoom should have occurred");
        }

        [Test]
        public async Task Combined_PanAndRotate_BothWork()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Pan first
            await helper.TestSwipe(SwipeDirection.Up, distance: 0.2f, duration: 0.2f);

            // Then rotate
            await helper.TestRotate(30f, duration: 0.3f);

            Assert.Less(_cameraController.TotalPanDelta.y, 0, "Pan should have occurred");
            Assert.Greater(_cameraController.TotalRotationDelta, 0, "Rotation should have occurred");
        }

        [Test]
        public async Task Combined_AllGestures_Work()
        {
            await Async.DelayFrames(1);

            var helper = CreateGestureHelper();

            // Perform all gesture types
            await helper.TestSwipe(SwipeDirection.Right, distance: 0.15f, duration: 0.2f);
            await helper.TestPinch(1.3f, duration: 0.25f);
            await helper.TestRotate(20f, duration: 0.25f);
            await helper.TestTwoFingerSwipe(SwipeDirection.Down, distance: 0.15f, duration: 0.2f);

            // All gesture types should have registered
            Assert.AreNotEqual(0, _cameraController.TotalPanDelta.x, "Single-finger pan should register");
            Assert.Greater(_cameraController.TotalZoomDelta, 0, "Pinch zoom should register");
            Assert.Greater(_cameraController.TotalRotationDelta, 0, "Rotation should register");
            Assert.Greater(_cameraController.TotalTwoFingerPanDelta.y, 0, "Two-finger pan should register");
        }

        #endregion

        #region Helper Methods

        private TestGestureHelper CreateGestureHelper()
        {
            return new TestGestureHelper();
        }

        #endregion
    }

    #region Test Helper Components

    /// <summary>
    /// Simple camera controller that tracks gesture input for testing.
    /// Uses InputSystem actions to properly receive injected input events.
    /// Also visually moves the camera so gestures are visible.
    /// </summary>
    public class TestCameraController : MonoBehaviour
    {
        public Vector2 TotalPanDelta { get; private set; }
        public float TotalZoomDelta { get; private set; }
        public float TotalRotationDelta { get; private set; }
        public Vector2 TotalTwoFingerPanDelta { get; private set; }

        // Sensitivity for visual camera movement
        public float PanSensitivity = 0.01f;
        public float ZoomSensitivity = 0.1f;
        public float RotationSensitivity = 1f;

        private Camera _camera;
        private float _initialOrthoSize;

        private Vector2? _lastMousePosition;
        private Vector2? _lastTouch0Position;
        private Vector2? _lastTouch1Position;
        private float? _lastTouchDistance;
        private float? _lastTouchAngle;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null)
            _initialOrthoSize = _camera.orthographicSize;
        }

        private void Update()
        {
            ProcessMouseInput();
            ProcessTouchInput();
        }

        private void ProcessMouseInput()
        {
            // Read directly from Mouse device (not InputActions) to match how
            // ProcessTouchInput reads from Touchscreen.current. This ensures
            // we see queued StateEvents from InputInjector's virtual mouse.
            var mouse = Mouse.current;
            if (mouse == null) return;

            var currentPos = mouse.position.ReadValue();
            var isPressed = mouse.leftButton.isPressed;

            if (isPressed)
            {
            if (_lastMousePosition.HasValue)
            {
                var delta = currentPos - _lastMousePosition.Value;
                // Invert delta for camera panning (drag left = camera moves right)
                var panDelta = new Vector2(-delta.x, -delta.y);
                TotalPanDelta += panDelta;

                // Debug: Log significant movements
                if (delta.sqrMagnitude > 1f)
                {
                    Debug.Log($"[CameraController] Mouse: last={_lastMousePosition.Value} curr={currentPos} delta={delta} panDelta={panDelta} total={TotalPanDelta}");
                }

                // Actually move the camera so it's visible
                if (_camera != null)
                {
                    transform.position += new Vector3(panDelta.x * PanSensitivity, panDelta.y * PanSensitivity, 0);
                }
            }
            _lastMousePosition = currentPos;
            }
            else
            {
            _lastMousePosition = null;
            }
        }

        private void ProcessTouchInput()
        {
            // Read directly from Touchscreen device instead of InputActions
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

            // Find active touches by checking phase
            var touches = touchscreen.touches;
            Vector2? touch0Pos = null;
            Vector2? touch1Pos = null;

            foreach (var touch in touches)
            {
            var phase = touch.phase.ReadValue();
            if (phase == UnityEngine.InputSystem.TouchPhase.Began ||
                phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                phase == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                if (!touch0Pos.HasValue)
                    touch0Pos = touch.position.ReadValue();
                else if (!touch1Pos.HasValue)
                    touch1Pos = touch.position.ReadValue();
            }
            }

            if (touch0Pos.HasValue && touch1Pos.HasValue)
            {
            // Two-finger gestures
            var currentDistance = Vector2.Distance(touch0Pos.Value, touch1Pos.Value);
            var currentCenter = (touch0Pos.Value + touch1Pos.Value) / 2f;
            var currentAngle = Mathf.Atan2(touch1Pos.Value.y - touch0Pos.Value.y, touch1Pos.Value.x - touch0Pos.Value.x) * Mathf.Rad2Deg;

            // Debug: log when we first detect both touches
            if (!_lastTouchDistance.HasValue)
            {
                Debug.Log($"[CameraController] Two touches started: t0={touch0Pos.Value} t1={touch1Pos.Value} angle={currentAngle:F1}");
            }

            if (_lastTouchDistance.HasValue && _lastTouch0Position.HasValue && _lastTouch1Position.HasValue)
            {
                // Pinch zoom
                var distanceDelta = currentDistance - _lastTouchDistance.Value;
                TotalZoomDelta += distanceDelta * 0.01f; // Scale factor

                // Rotation
                var angleDelta = Mathf.DeltaAngle(_lastTouchAngle.Value, currentAngle);
                TotalRotationDelta += angleDelta;

                Debug.Log($"[CameraController] Rotation: lastAngle={_lastTouchAngle.Value:F1} currentAngle={currentAngle:F1} delta={angleDelta:F1} total={TotalRotationDelta:F1}");

                // Two-finger pan
                var lastCenter = (_lastTouch0Position.Value + _lastTouch1Position.Value) / 2f;
                var panDelta = currentCenter - lastCenter;
                TotalTwoFingerPanDelta += new Vector2(-panDelta.x, -panDelta.y);

                // Actually apply visual changes to the camera
                if (_camera != null)
                {
                    // Zoom (change orthographic size)
                    _camera.orthographicSize = Mathf.Clamp(
                        _camera.orthographicSize - distanceDelta * ZoomSensitivity * 0.01f,
                        1f, 20f);

                    // Rotation
                    transform.Rotate(0, 0, -angleDelta * RotationSensitivity);

                    // Two-finger pan
                    transform.position += new Vector3(-panDelta.x * PanSensitivity, -panDelta.y * PanSensitivity, 0);
                }
            }

            _lastTouch0Position = touch0Pos.Value;
            _lastTouch1Position = touch1Pos.Value;
            _lastTouchDistance = currentDistance;
            _lastTouchAngle = currentAngle;
            }
            else
            {
            // Reset two-finger tracking when not in two-finger mode
            _lastTouch0Position = null;
            _lastTouch1Position = null;
            _lastTouchDistance = null;
            _lastTouchAngle = null;
            }
        }

        public void ResetTracking()
        {
            TotalPanDelta = Vector2.zero;
            TotalZoomDelta = 0f;
            TotalRotationDelta = 0f;
            TotalTwoFingerPanDelta = Vector2.zero;
            _lastMousePosition = null;
            _lastTouch0Position = null;
            _lastTouch1Position = null;
            _lastTouchDistance = null;
            _lastTouchAngle = null;

            // Reset camera position/rotation
            if (_camera != null)
            {
            transform.position = new Vector3(0, 0, -10);
            transform.rotation = Quaternion.identity;
            _camera.orthographicSize = _initialOrthoSize;
            }
        }
    }

    /// <summary>
    /// Test helper that exposes UIAutomation gesture methods for testing.
    /// </summary>
    public class TestGestureHelper
    {
        public async Task TestSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await Swipe(direction, distance, duration);
        }

        public async Task TestSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await SwipeAt(xPercent, yPercent, direction, distance, duration);
        }

        public async Task TestPinch(float scale, float duration = 0.5f)
        {
            await Pinch(scale, duration);
        }

        public async Task TestPinchAt(float xPercent, float yPercent, float scale, float duration = 0.5f)
        {
            await PinchAt(xPercent, yPercent, scale, duration);
        }

        public async Task TestRotate(float degrees, float duration = 0.5f)
        {
            await Rotate(degrees, duration);
        }

        public async Task TestRotateAt(float xPercent, float yPercent, float degrees, float duration = 0.5f)
        {
            await RotateAt(xPercent, yPercent, degrees, duration);
        }

        public async Task TestTwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipe(direction, distance, duration, fingerSpacing);
        }

        public async Task TestTwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipeAt(xPercent, yPercent, direction, distance, duration, fingerSpacing);
        }

        public async Task TestDrag(Vector2 direction, float duration = 0.5f)
        {
            await Drag(direction, duration);
        }

        public async Task TestDragFromTo(Vector2 startPos, Vector2 endPos, float duration = 0.5f)
        {
            await DragFromTo(startPos, endPos, duration);
        }
    }

    #endregion
}
