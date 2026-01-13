using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// PlayMode tests for camera control via swiping gestures.
    /// Tests all gesture types: swipe, pinch, rotate, two-finger swipe.
    /// </summary>
    [TestFixture]
    public class CameraGestureTests
    {
        private Camera _camera;
        private GameObject _cameraGO;
        private TestCameraController _cameraController;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
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
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _createdObjects.Clear();
        }

        #region Swipe Tests

        [UnityTest]
        public IEnumerator Swipe_Left_MovesCameraRight()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var initialPos = _camera.transform.position;
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.3f, duration: 0.3f);

                // Swiping left should pan camera right (opposite direction)
                Assert.Greater(_cameraController.TotalPanDelta.x, 0,
                    "Swiping left should result in positive pan delta (camera moves right)");
            });
        }

        [UnityTest]
        public IEnumerator Swipe_Right_MovesCameraLeft()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Right, distance: 0.3f, duration: 0.3f);

                Assert.Less(_cameraController.TotalPanDelta.x, 0,
                    "Swiping right should result in negative pan delta (camera moves left)");
            });
        }

        [UnityTest]
        public IEnumerator Swipe_Up_MovesCameraDown()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Up, distance: 0.3f, duration: 0.3f);

                Assert.Less(_cameraController.TotalPanDelta.y, 0,
                    "Swiping up should result in negative pan delta (camera moves down)");
            });
        }

        [UnityTest]
        public IEnumerator Swipe_Down_MovesCameraUp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Down, distance: 0.3f, duration: 0.3f);

                Assert.Greater(_cameraController.TotalPanDelta.y, 0,
                    "Swiping down should result in positive pan delta (camera moves up)");
            });
        }

        [UnityTest]
        public IEnumerator Swipe_Diagonal_MovesCameraDiagonally()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Swipe left then down for diagonal movement
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.2f, duration: 0.2f);
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Down, distance: 0.2f, duration: 0.2f);

                Assert.Greater(_cameraController.TotalPanDelta.x, 0, "Should have positive X pan");
                Assert.Greater(_cameraController.TotalPanDelta.y, 0, "Should have positive Y pan");
            });
        }

        [UnityTest]
        public IEnumerator SwipeAt_CornerPosition_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Swipe from top-left corner
                await helper.TestSwipeAt(0.1f, 0.9f, UITestBehaviour.SwipeDirection.Right, distance: 0.2f, duration: 0.3f);

                Assert.Less(_cameraController.TotalPanDelta.x, 0,
                    "SwipeAt should work from corner positions");
            });
        }

        [UnityTest]
        public IEnumerator Swipe_MultipleInSequence_AccumulatesPan()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Multiple swipes in same direction
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.1f, duration: 0.15f);
                var firstPan = _cameraController.TotalPanDelta.x;

                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.1f, duration: 0.15f);
                var secondPan = _cameraController.TotalPanDelta.x;

                Assert.Greater(secondPan, firstPan,
                    "Multiple swipes should accumulate pan movement");
            });
        }

        #endregion

        #region Pinch/Zoom Tests

        [UnityTest]
        public IEnumerator Pinch_In_ZoomsOut()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Pinch in (scale < 1) = zoom out
                await helper.TestPinch(0.5f, duration: 0.4f);

                Assert.Less(_cameraController.TotalZoomDelta, 0,
                    "Pinch in (scale < 1) should result in negative zoom delta (zoom out)");
            });
        }

        [UnityTest]
        public IEnumerator Pinch_Out_ZoomsIn()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Pinch out (scale > 1) = zoom in
                await helper.TestPinch(2.0f, duration: 0.4f);

                Assert.Greater(_cameraController.TotalZoomDelta, 0,
                    "Pinch out (scale > 1) should result in positive zoom delta (zoom in)");
            });
        }

        [UnityTest]
        public IEnumerator PinchAt_OffCenter_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Wait a few frames to ensure clean state from any previous touch gestures
                await UniTask.Yield();
                await UniTask.Yield();
                await UniTask.Yield();

                // Reset tracking to ensure we only measure this gesture
                _cameraController.ResetTracking();

                var helper = CreateGestureHelper();

                // Pinch at bottom-right area
                await helper.TestPinchAt(0.75f, 0.25f, 1.5f, duration: 0.4f);

                Assert.Greater(_cameraController.TotalZoomDelta, 0,
                    "PinchAt should work at off-center positions");
            });
        }

        #endregion

        #region Rotation Tests

        [UnityTest]
        public IEnumerator Rotate_Clockwise_RotatesCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Rotate 45 degrees clockwise
                await helper.TestRotate(45f, duration: 0.4f);

                Assert.Greater(_cameraController.TotalRotationDelta, 0,
                    "Clockwise rotation should result in positive rotation delta");
                Assert.Greater(_cameraController.TotalRotationDelta, 30f,
                    "Rotation should be close to requested angle");
            });
        }

        [UnityTest]
        public IEnumerator Rotate_CounterClockwise_RotatesCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Rotate 45 degrees counter-clockwise
                await helper.TestRotate(-45f, duration: 0.4f);

                Assert.Less(_cameraController.TotalRotationDelta, 0,
                    "Counter-clockwise rotation should result in negative rotation delta");
            });
        }

        [UnityTest]
        public IEnumerator Rotate_FullCircle_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Rotate in increments (full rotation at once may not work well)
                await helper.TestRotate(90f, duration: 0.3f);
                await helper.TestRotate(90f, duration: 0.3f);
                await helper.TestRotate(90f, duration: 0.3f);
                await helper.TestRotate(90f, duration: 0.3f);

                Assert.Greater(_cameraController.TotalRotationDelta, 300f,
                    "Multiple rotations should accumulate to near 360 degrees");
            });
        }

        [UnityTest]
        public IEnumerator RotateAt_CustomPosition_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Rotate at top-left quadrant
                await helper.TestRotateAt(0.25f, 0.75f, 30f, duration: 0.4f);

                Assert.Greater(_cameraController.TotalRotationDelta, 0,
                    "RotateAt should work at custom positions");
            });
        }

        #endregion

        #region Two-Finger Swipe Tests

        [UnityTest]
        public IEnumerator TwoFingerSwipe_Left_PansCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestTwoFingerSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.25f, duration: 0.3f);

                Assert.Greater(_cameraController.TotalTwoFingerPanDelta.x, 0,
                    "Two-finger swipe left should pan camera right");
            });
        }

        [UnityTest]
        public IEnumerator TwoFingerSwipe_Right_PansCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestTwoFingerSwipe(UITestBehaviour.SwipeDirection.Right, distance: 0.25f, duration: 0.3f);

                Assert.Less(_cameraController.TotalTwoFingerPanDelta.x, 0,
                    "Two-finger swipe right should pan camera left");
            });
        }

        [UnityTest]
        public IEnumerator TwoFingerSwipe_Up_PansCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestTwoFingerSwipe(UITestBehaviour.SwipeDirection.Up, distance: 0.25f, duration: 0.3f);

                Assert.Less(_cameraController.TotalTwoFingerPanDelta.y, 0,
                    "Two-finger swipe up should pan camera down");
            });
        }

        [UnityTest]
        public IEnumerator TwoFingerSwipe_Down_PansCamera()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();
                await helper.TestTwoFingerSwipe(UITestBehaviour.SwipeDirection.Down, distance: 0.25f, duration: 0.3f);

                Assert.Greater(_cameraController.TotalTwoFingerPanDelta.y, 0,
                    "Two-finger swipe down should pan camera up");
            });
        }

        [UnityTest]
        public IEnumerator TwoFingerSwipeAt_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Two-finger swipe from right side of screen
                await helper.TestTwoFingerSwipeAt(0.8f, 0.5f, UITestBehaviour.SwipeDirection.Left, distance: 0.2f, duration: 0.3f);

                Assert.Greater(_cameraController.TotalTwoFingerPanDelta.x, 0,
                    "TwoFingerSwipeAt should work at custom positions");
            });
        }

        #endregion

        #region Combined Gesture Tests

        [UnityTest]
        public IEnumerator Combined_PanAndZoom_BothWork()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Pan first
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Left, distance: 0.2f, duration: 0.2f);
                var panAfterSwipe = _cameraController.TotalPanDelta.x;

                // Then zoom
                await helper.TestPinch(1.5f, duration: 0.3f);

                Assert.Greater(panAfterSwipe, 0, "Pan should have occurred");
                Assert.Greater(_cameraController.TotalZoomDelta, 0, "Zoom should have occurred");
            });
        }

        [UnityTest]
        public IEnumerator Combined_PanAndRotate_BothWork()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Pan first
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Up, distance: 0.2f, duration: 0.2f);

                // Then rotate
                await helper.TestRotate(30f, duration: 0.3f);

                Assert.Less(_cameraController.TotalPanDelta.y, 0, "Pan should have occurred");
                Assert.Greater(_cameraController.TotalRotationDelta, 0, "Rotation should have occurred");
            });
        }

        [UnityTest]
        public IEnumerator Combined_AllGestures_Work()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var helper = CreateGestureHelper();

                // Perform all gesture types
                await helper.TestSwipe(UITestBehaviour.SwipeDirection.Right, distance: 0.15f, duration: 0.2f);
                await helper.TestPinch(1.3f, duration: 0.25f);
                await helper.TestRotate(20f, duration: 0.25f);
                await helper.TestTwoFingerSwipe(UITestBehaviour.SwipeDirection.Down, distance: 0.15f, duration: 0.2f);

                // All gesture types should have registered
                Assert.AreNotEqual(0, _cameraController.TotalPanDelta.x, "Single-finger pan should register");
                Assert.Greater(_cameraController.TotalZoomDelta, 0, "Pinch zoom should register");
                Assert.Greater(_cameraController.TotalRotationDelta, 0, "Rotation should register");
                Assert.Greater(_cameraController.TotalTwoFingerPanDelta.y, 0, "Two-finger pan should register");
            });
        }

        #endregion

        #region Helper Methods

        private TestGestureHelper CreateGestureHelper()
        {
            var helperGO = new GameObject("GestureHelper");
            _createdObjects.Add(helperGO);
            return helperGO.AddComponent<TestGestureHelper>();
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

        private InputAction _pointerPositionAction;
        private InputAction _pointerPressAction;
        private InputAction _touch0PositionAction;
        private InputAction _touch1PositionAction;
        private InputAction _touch0PressAction;
        private InputAction _touch1PressAction;

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

        private void OnEnable()
        {
            // Create InputActions that will properly receive injected events
            _pointerPositionAction = new InputAction("PointerPosition", InputActionType.Value, "<Mouse>/position");
            _pointerPressAction = new InputAction("PointerPress", InputActionType.Button, "<Mouse>/leftButton");
            _touch0PositionAction = new InputAction("Touch0Position", InputActionType.Value, "<Touchscreen>/touch0/position");
            _touch1PositionAction = new InputAction("Touch1Position", InputActionType.Value, "<Touchscreen>/touch1/position");
            _touch0PressAction = new InputAction("Touch0Press", InputActionType.Button, "<Touchscreen>/touch0/press");
            _touch1PressAction = new InputAction("Touch1Press", InputActionType.Button, "<Touchscreen>/touch1/press");

            _pointerPositionAction.Enable();
            _pointerPressAction.Enable();
            _touch0PositionAction.Enable();
            _touch1PositionAction.Enable();
            _touch0PressAction.Enable();
            _touch1PressAction.Enable();
        }

        private void OnDisable()
        {
            _pointerPositionAction?.Disable();
            _pointerPressAction?.Disable();
            _touch0PositionAction?.Disable();
            _touch1PositionAction?.Disable();
            _touch0PressAction?.Disable();
            _touch1PressAction?.Disable();

            _pointerPositionAction?.Dispose();
            _pointerPressAction?.Dispose();
            _touch0PositionAction?.Dispose();
            _touch1PositionAction?.Dispose();
            _touch0PressAction?.Dispose();
            _touch1PressAction?.Dispose();
        }

        private void Update()
        {
            ProcessMouseInput();
            ProcessTouchInput();
        }

        private void ProcessMouseInput()
        {
            if (_pointerPositionAction == null || _pointerPressAction == null) return;

            var currentPos = _pointerPositionAction.ReadValue<Vector2>();
            var isPressed = _pointerPressAction.IsPressed();

            if (isPressed)
            {
                if (_lastMousePosition.HasValue)
                {
                    var delta = currentPos - _lastMousePosition.Value;
                    // Invert delta for camera panning (drag left = camera moves right)
                    var panDelta = new Vector2(-delta.x, -delta.y);
                    TotalPanDelta += panDelta;

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
            if (_touch0PressAction == null || _touch1PressAction == null) return;

            var touch0Active = _touch0PressAction.IsPressed();
            var touch1Active = _touch1PressAction.IsPressed();

            if (touch0Active && touch1Active)
            {
                var touch0Pos = _touch0PositionAction.ReadValue<Vector2>();
                var touch1Pos = _touch1PositionAction.ReadValue<Vector2>();

                // Two-finger gestures
                var currentDistance = Vector2.Distance(touch0Pos, touch1Pos);
                var currentCenter = (touch0Pos + touch1Pos) / 2f;
                var currentAngle = Mathf.Atan2(touch1Pos.y - touch0Pos.y, touch1Pos.x - touch0Pos.x) * Mathf.Rad2Deg;

                if (_lastTouchDistance.HasValue && _lastTouch0Position.HasValue && _lastTouch1Position.HasValue)
                {
                    // Pinch zoom
                    var distanceDelta = currentDistance - _lastTouchDistance.Value;
                    TotalZoomDelta += distanceDelta * 0.01f; // Scale factor

                    // Rotation
                    var angleDelta = Mathf.DeltaAngle(_lastTouchAngle.Value, currentAngle);
                    TotalRotationDelta += angleDelta;

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

                _lastTouch0Position = touch0Pos;
                _lastTouch1Position = touch1Pos;
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
    /// Test helper that exposes UITestBehaviour gesture methods for testing.
    /// </summary>
    [UITest(Scenario = 9990, Feature = "Test Helper", Story = "Gesture Helper")]
    public class TestGestureHelper : UITestBehaviour
    {
        private void Awake() { enabled = false; }
        protected override UniTask Test() => UniTask.CompletedTask;

        public async UniTask TestSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await Swipe(direction, distance, duration);
        }

        public async UniTask TestSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await SwipeAt(xPercent, yPercent, direction, distance, duration);
        }

        public async UniTask TestPinch(float scale, float duration = 0.5f)
        {
            await Pinch(scale, duration);
        }

        public async UniTask TestPinchAt(float xPercent, float yPercent, float scale, float duration = 0.5f)
        {
            await PinchAt(xPercent, yPercent, scale, duration);
        }

        public async UniTask TestRotate(float degrees, float duration = 0.5f)
        {
            await Rotate(degrees, duration);
        }

        public async UniTask TestRotateAt(float xPercent, float yPercent, float degrees, float duration = 0.5f)
        {
            await RotateAt(xPercent, yPercent, degrees, duration);
        }

        public async UniTask TestTwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipe(direction, distance, duration, fingerSpacing);
        }

        public async UniTask TestTwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipeAt(xPercent, yPercent, direction, distance, duration, fingerSpacing);
        }

        public async UniTask TestDrag(Vector2 direction, float duration = 0.5f)
        {
            await Drag(direction, duration);
        }

        public async UniTask TestDragFromTo(Vector2 startPos, Vector2 endPos, float duration = 0.5f)
        {
            await DragFromTo(startPos, endPos, duration);
        }
    }

    #endregion
}
