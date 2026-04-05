using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Creates and controls a temporary scene camera for remote 3D navigation.
    /// Mimics Unity's Scene view controls: orbit, pan, zoom around a focus point.
    /// </summary>
    public class SceneCameraService : MonoBehaviour
    {
        public bool IsActive => _sceneCameraGo != null;
        public Camera SceneCamera => _sceneCamera;

        GameObject _sceneCameraGo;
        Camera _sceneCamera;
        WebRTCStreamer _streamer;

        Vector3 _focusPoint;
        float _orbitDistance = 10f;

        // Sensitivity multipliers
        const float OrbitSensitivity = 0.3f;
        const float PanSensitivity = 0.01f;
        const float ZoomSensitivity = 1f;
        const float MinOrbitDistance = 0.1f;
        const float FocusDistance = 5f;

        public void SetStreamer(WebRTCStreamer streamer)
        {
            _streamer = streamer;
        }

        /// <summary>
        /// Create a scene camera at the current main camera's position.
        /// </summary>
        public string StartSceneCamera()
        {
            if (_sceneCameraGo != null)
                return "{\"ok\":true,\"message\":\"Scene camera already active\"}";

            _sceneCameraGo = new GameObject("[Bugpunch Scene Camera]");
            DontDestroyOnLoad(_sceneCameraGo);

            _sceneCamera = _sceneCameraGo.AddComponent<Camera>();
            _sceneCamera.depth = 100; // render on top

            // Position at main camera location or default
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                _sceneCameraGo.transform.position = mainCam.transform.position;
                _sceneCameraGo.transform.rotation = mainCam.transform.rotation;
                _sceneCamera.fieldOfView = mainCam.fieldOfView;
                _sceneCamera.nearClipPlane = mainCam.nearClipPlane;
                _sceneCamera.farClipPlane = mainCam.farClipPlane;
            }
            else
            {
                _sceneCameraGo.transform.position = new Vector3(0, 5, -10);
                _sceneCameraGo.transform.LookAt(Vector3.zero);
                _sceneCamera.fieldOfView = 60f;
            }

            // Set focus point in front of the camera
            _focusPoint = _sceneCameraGo.transform.position + _sceneCameraGo.transform.forward * FocusDistance;
            _orbitDistance = FocusDistance;

            // Switch WebRTC stream to scene camera
            if (_streamer != null)
                _streamer.SetCamera(_sceneCamera);

            Debug.Log("[Bugpunch] Scene camera started");
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Destroy the scene camera and switch back to main camera.
        /// </summary>
        public string StopSceneCamera()
        {
            if (_sceneCameraGo == null)
                return "{\"ok\":true,\"message\":\"Scene camera not active\"}";

            Destroy(_sceneCameraGo);
            _sceneCameraGo = null;
            _sceneCamera = null;

            // Switch WebRTC back to main camera
            if (_streamer != null)
                _streamer.SetCamera(Camera.main);

            Debug.Log("[Bugpunch] Scene camera stopped");
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Set camera position and rotation directly.
        /// </summary>
        public string UpdateTransform(Vector3 position, Vector3 eulerAngles)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            _sceneCameraGo.transform.position = position;
            _sceneCameraGo.transform.eulerAngles = eulerAngles;

            // Update focus point to stay in front of camera
            _focusPoint = position + _sceneCameraGo.transform.forward * _orbitDistance;

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Orbit around the focus point. deltaX = horizontal, deltaY = vertical (degrees).
        /// </summary>
        public string Orbit(float deltaX, float deltaY)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            var t = _sceneCameraGo.transform;

            // Rotate around focus point on world Y axis (horizontal)
            t.RotateAround(_focusPoint, Vector3.up, deltaX * OrbitSensitivity);

            // Rotate around focus point on camera's local X axis (vertical)
            t.RotateAround(_focusPoint, t.right, -deltaY * OrbitSensitivity);

            // Update orbit distance
            _orbitDistance = Vector3.Distance(t.position, _focusPoint);

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Pan the camera in its local XY plane.
        /// </summary>
        public string Pan(float deltaX, float deltaY)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            var t = _sceneCameraGo.transform;
            var scaledSensitivity = PanSensitivity * _orbitDistance; // pan faster when zoomed out

            var offset = t.right * (-deltaX * scaledSensitivity)
                       + t.up * (-deltaY * scaledSensitivity);

            t.position += offset;
            _focusPoint += offset;

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Dolly zoom — move camera forward/backward along its forward axis.
        /// </summary>
        public string Zoom(float delta)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            var t = _sceneCameraGo.transform;
            var move = t.forward * (delta * ZoomSensitivity);

            // Prevent zooming through the focus point
            var newDistance = Vector3.Distance(t.position + move, _focusPoint);
            if (delta > 0 && newDistance < MinOrbitDistance)
            {
                // Push focus point forward instead of getting too close
                _focusPoint += t.forward * (delta * ZoomSensitivity);
            }

            t.position += move;
            _orbitDistance = Vector3.Distance(t.position, _focusPoint);

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Move camera to look at a specific GameObject by instance ID.
        /// </summary>
        public string FocusOn(int instanceId)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            // Find the object — check all root objects across all scenes
            GameObject target = null;
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj.GetInstanceID() == instanceId && obj.scene.isLoaded)
                {
                    target = obj;
                    break;
                }
            }

            if (target == null)
                return $"{{\"ok\":false,\"error\":\"GameObject not found: {instanceId}\"}}";

            // Calculate bounds for the target
            var bounds = new Bounds(target.transform.position, Vector3.zero);
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);

            _focusPoint = bounds.center;

            // Position camera to frame the object
            var size = Mathf.Max(bounds.extents.magnitude, 0.5f);
            _orbitDistance = size * 2.5f;

            var t = _sceneCameraGo.transform;
            var direction = (t.position - _focusPoint).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = -t.forward;

            t.position = _focusPoint + direction * _orbitDistance;
            t.LookAt(_focusPoint);

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Rotate camera in-place (flythrough look-around). Does NOT orbit around focus.
        /// </summary>
        public string Look(float deltaX, float deltaY)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            var t = _sceneCameraGo.transform;
            // Horizontal rotation around world Y axis
            t.Rotate(Vector3.up, deltaX * OrbitSensitivity, Space.World);
            // Vertical rotation around local X axis
            t.Rotate(Vector3.right, -deltaY * OrbitSensitivity, Space.Self);

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Move camera in its local space (WASD fly mode).
        /// Each call moves a fixed step since Time.deltaTime is not meaningful for HTTP calls.
        /// </summary>
        public string Fly(float forward, float right, float up, float speedMultiplier = 1f)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            var t = _sceneCameraGo.transform;
            float step = 0.5f * speedMultiplier;
            t.position += t.forward * forward * step
                        + t.right * right * step
                        + Vector3.up * up * step;

            // Update focus point to be in front of camera
            _focusPoint = t.position + t.forward * _orbitDistance;

            return "{\"ok\":true}";
        }

        /// <summary>
        /// Get current camera state as JSON.
        /// </summary>
        public string GetState()
        {
            if (_sceneCamera == null)
            {
                return "{\"active\":false}";
            }

            var t = _sceneCameraGo.transform;
            var pos = t.position;
            var rot = t.eulerAngles;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"active\":true,");
            string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
            sb.Append($"\"position\":{{\"x\":{F(pos.x)},\"y\":{F(pos.y)},\"z\":{F(pos.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{F(rot.x)},\"y\":{F(rot.y)},\"z\":{F(rot.z)}}},");
            sb.Append($"\"focusPoint\":{{\"x\":{F(_focusPoint.x)},\"y\":{F(_focusPoint.y)},\"z\":{F(_focusPoint.z)}}},");
            sb.Append($"\"orbitDistance\":{F(_orbitDistance)},");
            sb.Append($"\"fov\":{_sceneCamera.fieldOfView.ToString("F1", CultureInfo.InvariantCulture)}");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
