using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        IStreamer _streamer;

        Vector3 _focusPoint;
        float _orbitDistance = 10f;
        bool _gridEnabled = true;
        string _currentRenderMode = "default";

        // Smooth camera lerp
        Vector3? _lerpTargetPos;
        Quaternion? _lerpTargetRot;
        const float LerpSpeed = 8f;

        // Sensitivity multipliers
        const float OrbitSensitivity = 0.3f;
        const float PanSensitivity = 0.01f;
        const float ZoomSensitivity = 1f;
        const float MinOrbitDistance = 0.1f;
        const float FocusDistance = 5f;

        // Collider streaming
        const int MeshVertexBudget = 2000;
        List<(Collider collider, int tier, int instanceId)> _colliderCache;
        HashSet<int> _colliderKnownIds; // tracks IDs already sent to client
        float _colliderSearchRadius;     // current expansion radius

        public void SetStreamer(IStreamer streamer)
        {
            _streamer = streamer;
        }

        // Skip-ahead thresholds: if the camera is further than this behind the
        // target, pull it within range so the lerp doesn't feel laggy after a
        // big drag or teleport. Keeps the smooth feel for normal movement.
        const float SnapPositionThreshold = 15f;   // metres
        const float SnapRotationThreshold = 90f;   // degrees

        void Update()
        {
            if (_sceneCameraGo == null || !_lerpTargetPos.HasValue) return;
            var t = _sceneCameraGo.transform;

            // Skip ahead when way behind — teleport to within lerp range, then
            // let the exponential lerp smooth out the last stretch.
            var toTarget = _lerpTargetPos.Value - t.position;
            if (toTarget.magnitude > SnapPositionThreshold)
                t.position = _lerpTargetPos.Value - toTarget.normalized * SnapPositionThreshold;
            if (Quaternion.Angle(t.rotation, _lerpTargetRot.Value) > SnapRotationThreshold)
                t.rotation = Quaternion.RotateTowards(t.rotation, _lerpTargetRot.Value,
                    Quaternion.Angle(t.rotation, _lerpTargetRot.Value) - SnapRotationThreshold);

            var dt = Time.unscaledDeltaTime;
            var factor = 1f - Mathf.Exp(-LerpSpeed * dt);
            t.position = Vector3.Lerp(t.position, _lerpTargetPos.Value, factor);
            t.rotation = Quaternion.Slerp(t.rotation, _lerpTargetRot.Value, factor);
            // Snap when close enough
            if ((t.position - _lerpTargetPos.Value).sqrMagnitude < 0.0001f)
            {
                t.position = _lerpTargetPos.Value;
                t.rotation = _lerpTargetRot.Value;
                _lerpTargetPos = null;
                _lerpTargetRot = null;
            }
        }

        /// <summary>
        /// Create a scene camera at the current main camera's position.
        /// </summary>
        RenderTexture _sceneRT;

        public string StartSceneCamera(int viewportWidth = 0, int viewportHeight = 0)
        {
            if (_sceneCameraGo != null)
                return "{\"ok\":true,\"message\":\"Scene camera already active\"}";

            _sceneCameraGo = new GameObject("[Bugpunch Scene Camera]");
            DontDestroyOnLoad(_sceneCameraGo);

            _sceneCamera = _sceneCameraGo.AddComponent<Camera>();
            _sceneCamera.depth = 100;

            // Don't set targetTexture here — the WebRTC streamer manages the RT.
            // Just set the aspect ratio if dashboard sent viewport dimensions.
            if (viewportWidth > 0 && viewportHeight > 0)
                _sceneCamera.aspect = (float)viewportWidth / viewportHeight;

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

            _focusPoint = _sceneCameraGo.transform.position + _sceneCameraGo.transform.forward * FocusDistance;
            _orbitDistance = FocusDistance;

            if (_streamer != null)
                _streamer.SetCamera(_sceneCamera);

            Debug.Log($"[Bugpunch] Scene camera started ({viewportWidth}x{viewportHeight})");
            return $"{{\"ok\":true,\"width\":{viewportWidth},\"height\":{viewportHeight}}}";
        }

        /// <summary>
        /// Destroy the scene camera and switch back to main camera.
        /// </summary>
        public string StopSceneCamera()
        {
            if (_sceneCameraGo == null)
                return "{\"ok\":true,\"message\":\"Scene camera not active\"}";

            // Clean up render mode state
            CleanupRenderMode();

            Destroy(_sceneCameraGo);
            _sceneCameraGo = null;
            _sceneCamera = null;


            // Switch WebRTC back to game view (ScreenCapture mode)
            if (_streamer != null)
                _streamer.SetCamera(null);

            Debug.Log("[Bugpunch] Scene camera stopped");
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Update the scene camera aspect ratio (e.g. when dashboard panel resizes).
        /// </summary>
        public string SetAspect(int width, int height)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";
            if (width > 0 && height > 0)
                _sceneCamera.aspect = (float)width / height;
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Set camera position and rotation directly.
        /// </summary>
        public string UpdateTransform(Vector3 position, Vector3 eulerAngles)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            _lerpTargetPos = position;
            _lerpTargetRot = Quaternion.Euler(eulerAngles);

            // Update focus point to stay in front of target
            var fwd = _lerpTargetRot.Value * Vector3.forward;
            _focusPoint = position + fwd * _orbitDistance;

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
        /// Toggle between perspective and orthographic projection.
        /// </summary>
        public string ToggleProjection()
        {
            if (_sceneCamera == null) return "{\"ok\":false,\"error\":\"Scene camera not active\"}";
            _sceneCamera.orthographic = !_sceneCamera.orthographic;
            if (_sceneCamera.orthographic)
                _sceneCamera.orthographicSize = _orbitDistance * 0.5f;
            return $"{{\"ok\":true,\"orthographic\":{(_sceneCamera.orthographic ? "true" : "false")}}}";
        }

        /// <summary>
        /// Snap camera to a specific axis view.
        /// axis: "front", "back", "right", "left", "top", "bottom"
        /// </summary>
        public string SnapToAxis(string axis)
        {
            if (_sceneCamera == null) return "{\"ok\":false,\"error\":\"Scene camera not active\"}";
            var t = _sceneCameraGo.transform;
            Vector3 dir;
            switch (axis?.ToLower())
            {
                case "front": dir = -Vector3.forward; break;
                case "back": dir = Vector3.forward; break;
                case "right": dir = Vector3.right; break;
                case "left": dir = -Vector3.right; break;
                case "top": dir = Vector3.up; break;
                case "bottom": dir = -Vector3.up; break;
                default: return "{\"ok\":false,\"error\":\"Unknown axis\"}";
            }
            t.position = _focusPoint + dir * _orbitDistance;
            t.LookAt(_focusPoint);
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Enable or disable grid rendering.
        /// </summary>
        public string SetGrid(bool enabled)
        {
            _gridEnabled = enabled;
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Zoom via mouse drag delta (Alt + Right-click drag). Same effect as Zoom but from drag input.
        /// </summary>
        public string ZoomDrag(float delta)
        {
            return Zoom(delta);
        }

        // -----------------------------------------------------------------
        // Render modes
        // -----------------------------------------------------------------

        /// <summary>
        /// Set the scene camera's render mode.
        /// Modes: "default", "wireframe", "normals", "uv", "overdraw", "depth"
        /// </summary>
        public string SetRenderMode(string mode)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            // Clean up previous mode
            CleanupRenderMode();

            switch (mode?.ToLower())
            {
                case "default":
                case "shaded":
                    mode = "default";
                    break;

                case "wireframe":
                    // GL.wireframe is global — managed via camera callbacks
                    Camera.onPreRender += OnWireframePreRender;
                    Camera.onPostRender += OnWireframePostRender;
                    break;

                case "normals":
                {
                    var shader = FindOrCreateShader("Hidden/Bugpunch/Normals", NormalsShaderSource);
                    if (shader != null) _sceneCamera.SetReplacementShader(shader, "");
                    else return "{\"ok\":false,\"error\":\"Failed to create normals shader\"}";
                    break;
                }

                case "uv":
                {
                    var shader = FindOrCreateShader("Hidden/Bugpunch/UV", UVShaderSource);
                    if (shader != null) _sceneCamera.SetReplacementShader(shader, "");
                    else return "{\"ok\":false,\"error\":\"Failed to create UV shader\"}";
                    break;
                }

                case "depth":
                {
                    var shader = FindOrCreateShader("Hidden/Bugpunch/Depth", DepthShaderSource);
                    if (shader != null) _sceneCamera.SetReplacementShader(shader, "");
                    else return "{\"ok\":false,\"error\":\"Failed to create depth shader\"}";
                    break;
                }

                case "overdraw":
                {
                    var shader = FindOrCreateShader("Hidden/Bugpunch/Overdraw", OverdrawShaderSource);
                    if (shader != null) _sceneCamera.SetReplacementShader(shader, "");
                    else return "{\"ok\":false,\"error\":\"Failed to create overdraw shader\"}";
                    break;
                }

                default:
                    return $"{{\"ok\":false,\"error\":\"Unknown mode: {mode}\"}}";
            }

            _currentRenderMode = mode.ToLower();
            Debug.Log($"[Bugpunch] Scene camera render mode: {_currentRenderMode}");
            return $"{{\"ok\":true,\"mode\":\"{_currentRenderMode}\"}}";
        }

        void CleanupRenderMode()
        {
            // Reset replacement shader
            if (_sceneCamera != null)
                _sceneCamera.ResetReplacementShader();

            // Remove wireframe callbacks
            Camera.onPreRender -= OnWireframePreRender;
            Camera.onPostRender -= OnWireframePostRender;
            GL.wireframe = false;

            _currentRenderMode = "default";
        }

        void OnWireframePreRender(Camera cam)
        {
            if (cam == _sceneCamera)
                GL.wireframe = true;
        }

        void OnWireframePostRender(Camera cam)
        {
            if (cam == _sceneCamera)
                GL.wireframe = false;
        }

        /// <summary>
        /// Try Shader.Find first, then fall back to creating from source in the Editor.
        /// </summary>
        static Shader FindOrCreateShader(string name, string source)
        {
            var shader = Shader.Find(name);
            if (shader != null) return shader;

#if UNITY_EDITOR
            try
            {
                shader = ShaderUtil.CreateShaderAsset(source, false);
                if (shader != null) return shader;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch] Failed to create shader {name}: {ex.Message}");
            }
#endif
            return null;
        }

        // -----------------------------------------------------------------
        // Shader sources
        // -----------------------------------------------------------------

        const string NormalsShaderSource = @"
Shader ""Hidden/Bugpunch/Normals"" {
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(i.worldNormal * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
    SubShader {
        Tags { ""RenderType""=""Transparent"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(i.worldNormal * 0.5 + 0.5, 0.5);
            }
            ENDCG
        }
    }
}";

        const string UVShaderSource = @"
Shader ""Hidden/Bugpunch/UV"" {
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(i.uv.x, i.uv.y, 0, 1);
            }
            ENDCG
        }
    }
    SubShader {
        Tags { ""RenderType""=""Transparent"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(i.uv.x, i.uv.y, 0, 0.5);
            }
            ENDCG
        }
    }
}";

        const string DepthShaderSource = @"
Shader ""Hidden/Bugpunch/Depth"" {
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.depth = -(UnityObjectToViewPos(v.vertex).z * _ProjectionParams.w);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                float d = saturate(1.0 - i.depth);
                return fixed4(d, d, d, 1);
            }
            ENDCG
        }
    }
    SubShader {
        Tags { ""RenderType""=""Transparent"" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.depth = -(UnityObjectToViewPos(v.vertex).z * _ProjectionParams.w);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                float d = saturate(1.0 - i.depth);
                return fixed4(d, d, d, 0.5);
            }
            ENDCG
        }
    }
}";

        const string OverdrawShaderSource = @"
Shader ""Hidden/Bugpunch/Overdraw"" {
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        ZTest Always
        ZWrite Off
        Blend One One
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(0.1, 0.04, 0.02, 0);
            }
            ENDCG
        }
    }
    SubShader {
        Tags { ""RenderType""=""Transparent"" }
        ZTest Always
        ZWrite Off
        Blend One One
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct v2f {
                float4 pos : SV_POSITION;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                return fixed4(0.1, 0.04, 0.02, 0);
            }
            ENDCG
        }
    }
}";

        void OnDestroy()
        {
            CleanupRenderMode();
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
            sb.Append($"\"fov\":{_sceneCamera.fieldOfView.ToString("F1", CultureInfo.InvariantCulture)},");
            sb.Append($"\"orthographic\":{(_sceneCamera.orthographic ? "true" : "false")},");
            sb.Append($"\"gridEnabled\":{(_gridEnabled ? "true" : "false")},");
            sb.Append($"\"renderMode\":\"{_currentRenderMode}\"");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Cast a ray from the scene camera at normalized screen coordinates (0-1).
        /// Returns a JSON array of hit GameObjects sorted by distance.
        /// </summary>
        public string Raycast(float nx, float ny, int maxHits = 10)
        {
            var cam = _sceneCamera ?? Camera.main;
            if (cam == null)
                return "[]";

            nx = Mathf.Clamp01(nx);
            ny = Mathf.Clamp01(ny);
            var screenPos = new Vector3(nx * Screen.width, (1f - ny) * Screen.height, 0);
            var ray = cam.ScreenPointToRay(screenPos);

            var hits = Physics.RaycastAll(ray, cam.farClipPlane);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var sb = new StringBuilder();
            sb.Append("[");
            int count = 0;
            foreach (var hit in hits)
            {
                if (count >= maxHits) break;
                if (count > 0) sb.Append(",");
                var go = hit.collider.gameObject;
                string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
                sb.Append($"{{\"instanceId\":{go.GetInstanceID()},\"name\":\"{EscapeJson(go.name)}\",\"distance\":{F(hit.distance)},\"point\":{{\"x\":{F(hit.point.x)},\"y\":{F(hit.point.y)},\"z\":{F(hit.point.z)}}}}}");
                count++;
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get world-space bounding boxes for renderers visible to the scene camera.
        /// Uses frustum culling so only objects the camera can see are returned.
        /// Falls back to distance-based filtering if no scene camera is active.
        /// </summary>
        public string GetSceneBounds(float maxDistance = 200f, int maxCount = 500)
        {
            var cam = _sceneCamera ?? Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            var maxDistSq = maxDistance * maxDistance;

            var sb = new StringBuilder();
            sb.Append("[");
            int count = 0;

            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (count >= maxCount) break;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                var b = renderer.bounds;
                if ((b.center - camPos).sqrMagnitude > maxDistSq) continue;

                if (count > 0) sb.Append(",");
                string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
                sb.Append($"{{\"id\":{renderer.gameObject.GetInstanceID()},\"name\":\"{EscapeJson(renderer.gameObject.name)}\",");
                sb.Append($"\"center\":{{\"x\":{F(b.center.x)},\"y\":{F(b.center.y)},\"z\":{F(b.center.z)}}},");
                sb.Append($"\"extents\":{{\"x\":{F(b.extents.x)},\"y\":{F(b.extents.y)},\"z\":{F(b.extents.z)}}}}}");
                count++;
            }

            sb.Append("]");
            return sb.ToString();
        }

        // -----------------------------------------------------------------
        // Collider streaming
        // -----------------------------------------------------------------

        static int ClassifyTier(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null) return 0;           // static
            if (rb.isKinematic) return 1;       // kinematic
            return 2;                            // dynamic
        }

        /// <summary>
        /// Incremental collider snapshot — returns NEW colliders not yet sent to client.
        /// First call (reset=true) clears state and starts from a small radius.
        /// Subsequent calls expand the search radius progressively.
        /// Uses Physics.OverlapSphere for spatial queries (leverages Unity's broadphase).
        /// </summary>
        public string GetColliders(float maxDistance = 500f, int maxCount = 200, bool reset = false)
        {
            var cam = _sceneCamera ?? Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;

            if (reset || _colliderKnownIds == null)
            {
                _colliderCache = new List<(Collider, int, int)>();
                _colliderKnownIds = new HashSet<int>();
                _colliderSearchRadius = 0f;
            }

            // Expand search radius: 50 → 150 → 350 → 750 → maxDistance
            float nextRadius = _colliderSearchRadius == 0f ? 50f
                : Mathf.Min(_colliderSearchRadius * 2f + 50f, maxDistance);

            var hits = Physics.OverlapSphere(camPos, nextRadius, ~0, QueryTriggerInteraction.Collide);
            _colliderSearchRadius = nextRadius;

            var sb = new StringBuilder();
            sb.Append("{\"colliders\":[");
            int count = 0;
            bool done = nextRadius >= maxDistance;

            foreach (var col in hits)
            {
                if (count >= maxCount) break;

                int id = col.gameObject.GetInstanceID();
                if (_colliderKnownIds.Contains(id)) continue; // already sent

                int tier = ClassifyTier(col);
                _colliderKnownIds.Add(id);
                _colliderCache.Add((col, tier, id));

                if (count > 0) sb.Append(",");
                sb.Append("{");
                string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

                var go = col.gameObject;
                var t = col.transform;
                var wPos = t.position;
                var rot = t.eulerAngles;
                var ls = t.lossyScale;
                sb.Append($"\"id\":{id},\"name\":\"{EscapeJson(go.name)}\",");
                sb.Append($"\"tier\":{tier},");

                SerializeShape(sb, col, F);

                sb.Append($",\"px\":{F(wPos.x)},\"py\":{F(wPos.y)},\"pz\":{F(wPos.z)},");
                sb.Append($"\"rx\":{F(rot.x)},\"ry\":{F(rot.y)},\"rz\":{F(rot.z)},");
                sb.Append($"\"lsx\":{F(ls.x)},\"lsy\":{F(ls.y)},\"lsz\":{F(ls.z)}");

                sb.Append("}");
                count++;
            }

            sb.Append("],\"done\":");
            sb.Append(done && count == 0 ? "true" : "false");
            sb.Append(",\"radius\":");
            sb.Append(nextRadius.ToString("F0", CultureInfo.InvariantCulture));
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Compact transform-only update for a given tier.
        /// Returns {"t":[[id,px,py,pz,rx,ry,rz],...], "r":[removedIds]}
        /// </summary>
        public string GetColliderTransforms(int tier)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":[");

            List<int> removed = null;
            int written = 0;

            if (_colliderCache != null)
            {
                for (int i = _colliderCache.Count - 1; i >= 0; i--)
                {
                    var (col, t, cachedId) = _colliderCache[i];
                    if (t != tier) continue;

                    // Collider destroyed or disabled since snapshot
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    {
                        if (removed == null) removed = new List<int>();
                        removed.Add(cachedId);
                        _colliderCache.RemoveAt(i);
                        continue;
                    }

                    var pos = col.transform.position;
                    var rot = col.transform.eulerAngles;
                    string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

                    if (written > 0) sb.Append(",");
                    sb.Append($"[{cachedId},{F(pos.x)},{F(pos.y)},{F(pos.z)},{F(rot.x)},{F(rot.y)},{F(rot.z)}]");
                    written++;
                }
            }

            sb.Append("],\"r\":[");
            if (removed != null)
            {
                for (int i = 0; i < removed.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(removed[i]);
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static void SerializeAabb(StringBuilder sb, Bounds b, Func<float, string> F)
        {
            sb.Append("\"type\":\"mesh-aabb\",\"shape\":{");
            sb.Append($"\"cx\":{F(b.center.x)},\"cy\":{F(b.center.y)},\"cz\":{F(b.center.z)},");
            sb.Append($"\"ex\":{F(b.extents.x)},\"ey\":{F(b.extents.y)},\"ez\":{F(b.extents.z)}");
            sb.Append("}");
        }

        static void SerializeShape(StringBuilder sb, Collider col, Func<float, string> F)
        {
            if (col is BoxCollider box)
            {
                sb.Append("\"type\":\"box\",\"shape\":{");
                sb.Append($"\"cx\":{F(box.center.x)},\"cy\":{F(box.center.y)},\"cz\":{F(box.center.z)},");
                sb.Append($"\"sx\":{F(box.size.x)},\"sy\":{F(box.size.y)},\"sz\":{F(box.size.z)}");
                sb.Append("}");
            }
            else if (col is SphereCollider sphere)
            {
                sb.Append("\"type\":\"sphere\",\"shape\":{");
                sb.Append($"\"cx\":{F(sphere.center.x)},\"cy\":{F(sphere.center.y)},\"cz\":{F(sphere.center.z)},");
                sb.Append($"\"r\":{F(sphere.radius)}");
                sb.Append("}");
            }
            else if (col is CapsuleCollider capsule)
            {
                sb.Append("\"type\":\"capsule\",\"shape\":{");
                sb.Append($"\"cx\":{F(capsule.center.x)},\"cy\":{F(capsule.center.y)},\"cz\":{F(capsule.center.z)},");
                sb.Append($"\"r\":{F(capsule.radius)},\"h\":{F(capsule.height)},\"d\":{capsule.direction}");
                sb.Append("}");
            }
            else if (col is MeshCollider mesh)
            {
                var sharedMesh = mesh.sharedMesh;
                if (sharedMesh == null)
                {
                    // No mesh — local-space zero-centered box
                    SerializeAabb(sb, new Bounds(Vector3.zero, Vector3.one), F);
                    return;
                }

                var verts = sharedMesh.vertices;
                if (!mesh.convex && verts.Length >= MeshVertexBudget)
                {
                    // Too large — use mesh's local-space bounds as fallback
                    SerializeAabb(sb, sharedMesh.bounds, F);
                    return;
                }

                sb.Append("\"type\":\"mesh\",\"shape\":{\"verts\":[");
                for (int i = 0; i < verts.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(F(verts[i].x)); sb.Append(",");
                    sb.Append(F(verts[i].y)); sb.Append(",");
                    sb.Append(F(verts[i].z));
                }
                sb.Append("],\"tris\":[");
                var tris = sharedMesh.triangles;
                for (int i = 0; i < tris.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(tris[i]);
                }
                sb.Append("]}");
            }
            else
            {
                // Unknown collider type — local-space zero-centered box
                SerializeAabb(sb, new Bounds(Vector3.zero, Vector3.one), F);
            }
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
