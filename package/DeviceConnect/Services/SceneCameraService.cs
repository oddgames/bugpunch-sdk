using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

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
        // Movement thresholds for the transform-delta poll.
        const float ColliderMoveSqrEpsilon = 1e-6f;   // ~1 mm
        const float ColliderRotateEpsilonDeg = 0.5f;
        class ColliderCacheEntry
        {
            public Collider collider;
            public int tier;
            public int instanceId;
            public Vector3 lastPos;
            public Quaternion lastRot;
        }
        List<ColliderCacheEntry> _colliderCache;
        HashSet<int> _colliderKnownIds; // tracks IDs already sent to client
        float _colliderSearchRadius;     // current expansion radius

        // Render-mode state (cross-pipeline: Built-in + URP + HDRP)
        Material _replacementMaterial; // used for material-swap modes (normals/uv/depth/overdraw)
        Material _wireframeMaterial;   // used for the edge-mesh overlay in wireframe mode
        bool _wireframeEnabled;
        bool _callbacksHooked;
        bool _materialsSwapped;
        bool _clearOverridden;
        CameraClearFlags _savedClearFlags;
        Color _savedBackgroundColor;
        readonly List<(Renderer r, Material[] mats)> _savedMaterials = new();
        Renderer[] _cachedRenderers;
        int _cachedRenderersFrame = -1000;
        const int RendererCacheValidForFrames = 30;
        // Mesh → edge-mesh (MeshTopology.Lines) cache for the wireframe overlay.
        // Built once per unique source Mesh and reused every frame.
        readonly Dictionary<Mesh, Mesh> _edgeMeshes = new();
        // Unity Scene-view gray for wireframe/depth backdrops
        static readonly Color SceneViewClearColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        // Cap per-frame overlay draws so huge scenes don't tank the editor.
        const int WireframeMaxDrawsPerFrame = 1024;

        // Collider wireframe overlay (Physics-Debugger style)
        Material _colliderWireMaterial;
        bool _collidersOverlayEnabled;
        Mesh _unitBoxLines;
        Mesh _unitSphereLines;
        // Per-instance line meshes for capsule colliders (parameterized by radius/height/direction)
        readonly Dictionary<int, (Mesh mesh, float r, float h, int dir)> _capsuleLineMeshes = new();
        Collider[] _cachedColliders;
        int _cachedCollidersFrame = -1000;
        const int ColliderCacheValidForFrames = 30;
        const int ColliderMaxDrawsPerFrame = 2048;
        // Physics Debugger colour conventions
        static readonly Color ColliderColorStatic    = new Color(0.55f, 0.85f, 0.55f, 1f); // green
        static readonly Color ColliderColorKinematic = new Color(0.40f, 0.70f, 1.00f, 1f); // blue
        static readonly Color ColliderColorDynamic   = new Color(1.00f, 0.55f, 0.30f, 1f); // orange
        static readonly Color ColliderColorTrigger   = new Color(1.00f, 0.95f, 0.40f, 0.85f); // yellow, slightly translucent
        static readonly int ShaderPropTintColor = Shader.PropertyToID("_TintColor");

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
            // Streamer is the sole renderer (manual Render() at WaitForEndOfFrame).
            // With enabled=true + no targetTexture, Unity would auto-render this
            // to the device display each frame, AND consume the wireframe
            // overlay's queued Graphics.DrawMesh before the streamer's manual
            // render reaches the RT — so wireframe would show on-device but not
            // in the WebRTC stream. Disabling fixes both.
            _sceneCamera.enabled = false;

            // Don't set targetTexture here — the WebRTC streamer manages the RT.
            // Just set the aspect ratio if dashboard sent viewport dimensions.
            if (viewportWidth > 0 && viewportHeight > 0)
            {
                _sceneCamera.aspect = (float)viewportWidth / viewportHeight;
                _streamer?.SetTargetAspect(viewportWidth, viewportHeight);
            }

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

            Debug.Log($"[Bugpunch.SceneCameraService] Scene camera started ({viewportWidth}x{viewportHeight})");
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

            Debug.Log("[Bugpunch.SceneCameraService] Scene camera stopped");
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
            {
                _sceneCamera.aspect = (float)width / height;
                _streamer?.SetTargetAspect(width, height);
            }
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
        ///
        /// Pipeline-agnostic: works in Built-in (Camera.onPreRender/onPostRender)
        /// and URP/HDRP (RenderPipelineManager.begin/endCameraRendering) by
        /// temporarily swapping materials on visible renderers when the scene
        /// camera is the one rendering. Materials are restored immediately
        /// after, so the main game camera is unaffected.
        /// </summary>
        public string SetRenderMode(string mode)
        {
            if (_sceneCamera == null)
                return "{\"ok\":false,\"error\":\"Scene camera not active\"}";

            CleanupRenderMode();

            mode = (mode ?? "default").ToLowerInvariant();
            if (mode == "shaded") mode = "default";

            string shaderName = null;
            Color? clearColorOverride = null;

            switch (mode)
            {
                case "default":
                    break;
                case "wireframe":
                    // Wireframe is a true-line overlay driven from LateUpdate —
                    // scene renders shaded normally, then edge-meshes draw on
                    // top via Graphics.DrawMesh. No GL.wireframe (won't work on
                    // GLES/Vulkan mobile), no material swap.
                    shaderName = "Hidden/Bugpunch/Wireframe";
                    clearColorOverride = SceneViewClearColor;
                    break;
                case "colliders":
                    // Collider wireframe overlay — like Unity's Physics Debugger.
                    // Scene renders shaded normally; collider geometry draws on
                    // top via Graphics.DrawMesh from LateUpdate. Tier-coloured.
                    shaderName = "Hidden/Bugpunch/ColliderWire";
                    break;
                case "normals":
                    shaderName = "Hidden/Bugpunch/Normals";
                    break;
                case "uv":
                    shaderName = "Hidden/Bugpunch/UV";
                    break;
                case "depth":
                    shaderName = "Hidden/Bugpunch/Depth";
                    clearColorOverride = Color.black;
                    break;
                case "overdraw":
                    shaderName = "Hidden/Bugpunch/Overdraw";
                    clearColorOverride = Color.black;
                    break;
                default:
                    return $"{{\"ok\":false,\"error\":\"Unknown mode: {mode}\"}}";
            }

            if (shaderName != null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    return $"{{\"ok\":false,\"error\":\"Shader not found: {shaderName}. Ensure sdk/package/Resources/Shaders/*.shader are imported.\"}}";
                }
                var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                if (mode == "wireframe")
                {
                    _wireframeMaterial = mat;
                    _wireframeEnabled = true;
                }
                else if (mode == "colliders")
                {
                    _colliderWireMaterial = mat;
                    _collidersOverlayEnabled = true;
                }
                else
                {
                    _replacementMaterial = mat;
                }
            }

            if (clearColorOverride.HasValue)
                OverrideClearColor(clearColorOverride.Value);

            // Material-swap modes need the camera callbacks. Wireframe does not —
            // it's drawn from LateUpdate with Graphics.DrawMesh.
            if (_replacementMaterial != null)
                HookRenderCallbacks();

            _currentRenderMode = mode;
            Debug.Log($"[Bugpunch.SceneCameraService] Scene camera render mode: {_currentRenderMode}");
            return $"{{\"ok\":true,\"mode\":\"{_currentRenderMode}\"}}";
        }

        void CleanupRenderMode()
        {
            // Restore any renderers that are still swapped (safety net — should
            // already be restored by EndRender for the last camera).
            RestoreMaterials();

            if (_replacementMaterial != null)
            {
                Destroy(_replacementMaterial);
                _replacementMaterial = null;
            }
            if (_wireframeMaterial != null)
            {
                Destroy(_wireframeMaterial);
                _wireframeMaterial = null;
            }
            if (_colliderWireMaterial != null)
            {
                Destroy(_colliderWireMaterial);
                _colliderWireMaterial = null;
            }

            _wireframeEnabled = false;
            _collidersOverlayEnabled = false;
            GL.wireframe = false; // safety: clean state in case something else set it

            RestoreClearColor();
            UnhookRenderCallbacks();
            _currentRenderMode = "default";
        }

        // -----------------------------------------------------------------
        // Cross-pipeline render callbacks (Built-in + URP + HDRP)
        // -----------------------------------------------------------------

        void HookRenderCallbacks()
        {
            if (_callbacksHooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingSRP;
            RenderPipelineManager.endCameraRendering += OnEndCameraRenderingSRP;
            Camera.onPreRender += OnPreRenderBuiltin;
            Camera.onPostRender += OnPostRenderBuiltin;
            _callbacksHooked = true;
        }

        void UnhookRenderCallbacks()
        {
            if (!_callbacksHooked) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingSRP;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRenderingSRP;
            Camera.onPreRender -= OnPreRenderBuiltin;
            Camera.onPostRender -= OnPostRenderBuiltin;
            _callbacksHooked = false;
        }

        void OnBeginCameraRenderingSRP(ScriptableRenderContext _, Camera cam) => BeginRender(cam);
        void OnEndCameraRenderingSRP(ScriptableRenderContext _, Camera cam) => EndRender(cam);
        void OnPreRenderBuiltin(Camera cam) => BeginRender(cam);
        void OnPostRenderBuiltin(Camera cam) => EndRender(cam);

        void BeginRender(Camera cam)
        {
            if (cam != _sceneCamera) return;
            if (_replacementMaterial != null)
                SwapInReplacementMaterials();
            if (_wireframeEnabled)
                GL.wireframe = true;
        }

        void EndRender(Camera cam)
        {
            if (cam != _sceneCamera) return;
            RestoreMaterials();
            if (_wireframeEnabled)
                GL.wireframe = false;
        }

        void SwapInReplacementMaterials()
        {
            if (_materialsSwapped || _replacementMaterial == null) return;
            RefreshRendererCache();
            _savedMaterials.Clear();
            foreach (var r in _cachedRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // Skip UI, particles, lines — swapping them usually breaks more
                // than it reveals. Mesh/Skinned renderers are what scene-view
                // render modes care about.
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

                var original = r.sharedMaterials;
                _savedMaterials.Add((r, original));
                int count = original.Length == 0 ? 1 : original.Length;
                var replacements = new Material[count];
                for (int i = 0; i < count; i++) replacements[i] = _replacementMaterial;
                r.sharedMaterials = replacements;
            }
            _materialsSwapped = true;
        }

        void RestoreMaterials()
        {
            if (!_materialsSwapped) return;
            for (int i = 0; i < _savedMaterials.Count; i++)
            {
                var (r, mats) = _savedMaterials[i];
                if (r != null) r.sharedMaterials = mats;
            }
            _savedMaterials.Clear();
            _materialsSwapped = false;
        }

        void RefreshRendererCache()
        {
            int frame = Time.frameCount;
            if (_cachedRenderers != null && frame - _cachedRenderersFrame < RendererCacheValidForFrames)
                return;
            _cachedRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            _cachedRenderersFrame = frame;
        }

        void OverrideClearColor(Color color)
        {
            if (_clearOverridden || _sceneCamera == null) return;
            _savedClearFlags = _sceneCamera.clearFlags;
            _savedBackgroundColor = _sceneCamera.backgroundColor;
            _sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            _sceneCamera.backgroundColor = color;
            _clearOverridden = true;
        }

        void RestoreClearColor()
        {
            if (!_clearOverridden || _sceneCamera == null) return;
            _sceneCamera.clearFlags = _savedClearFlags;
            _sceneCamera.backgroundColor = _savedBackgroundColor;
            _clearOverridden = false;
        }

        // -----------------------------------------------------------------
        // Wireframe overlay — draws line-topology edge meshes for every
        // visible MeshRenderer on top of the normal shaded scene. Because
        // we use Graphics.DrawMesh(...camera) with the scene camera, the
        // overlay only shows on the scene view and works on any pipeline
        // and any platform (including GLES/Vulkan mobile where GL.wireframe
        // is unavailable).
        // -----------------------------------------------------------------
        void LateUpdate()
        {
            if (_sceneCamera == null) return;
            if (_collidersOverlayEnabled && _colliderWireMaterial != null)
                DrawColliderOverlay();
            if (!_wireframeEnabled || _wireframeMaterial == null) return;

            RefreshRendererCache();
            int drawn = 0;
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                if (drawn >= WireframeMaxDrawsPerFrame) break;
                var r = _cachedRenderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                Mesh src = null;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) src = mf.sharedMesh;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    // Draw the skinned mesh in its bind pose — good enough for
                    // a wireframe debug view and avoids the per-frame BakeMesh
                    // cost. TODO: bake when we care about accurate skinning.
                    src = smr.sharedMesh;
                }
                else
                {
                    continue;
                }
                if (src == null) continue;

                var edge = GetOrBuildEdgeMesh(src);
                if (edge == null) continue;

                Graphics.DrawMesh(edge, r.localToWorldMatrix, _wireframeMaterial,
                                  r.gameObject.layer, _sceneCamera);
                drawn++;
            }
        }

        Mesh GetOrBuildEdgeMesh(Mesh src)
        {
            if (src == null) return null;
            if (_edgeMeshes.TryGetValue(src, out var cached)) return cached;

            var vertices = src.vertices;
            if (vertices.Length == 0)
            {
                _edgeMeshes[src] = null;
                return null;
            }

            var indices = new List<int>(src.triangles.Length);
            var seen = new HashSet<long>();
            // Merge edges across all submeshes.
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    AddUniqueEdge(seen, indices, a, b);
                    AddUniqueEdge(seen, indices, b, c);
                    AddUniqueEdge(seen, indices, c, a);
                }
            }

            var mesh = new Mesh
            {
                name = src.name + "_bpEdges",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.vertices = vertices;
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            // Keep the source mesh bounds so frustum culling matches the visible render.
            mesh.bounds = src.bounds;
            _edgeMeshes[src] = mesh;
            return mesh;
        }

        static void AddUniqueEdge(HashSet<long> seen, List<int> indices, int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            if (seen.Add(key)) { indices.Add(a); indices.Add(b); }
        }

        void ClearEdgeMeshCache()
        {
            foreach (var m in _edgeMeshes.Values)
            {
                if (m != null) Destroy(m);
            }
            _edgeMeshes.Clear();
        }

        // -----------------------------------------------------------------
        // Collider overlay — draws Box/Sphere/Capsule/Mesh wireframes for
        // every collider in the scene on top of the rendered view, using
        // Physics-Debugger style colours (static green / kinematic blue /
        // dynamic orange / trigger yellow). Pipeline-agnostic: rendered via
        // Graphics.DrawMesh on MeshTopology.Lines edge meshes, restricted to
        // the scene camera so the main game view is unaffected.
        // -----------------------------------------------------------------
        void DrawColliderOverlay()
        {
            RefreshColliderCache();
            EnsureUnitPrimitiveMeshes();

            int drawn = 0;
            var mpb = ColliderPropertyBlock;
            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                if (drawn >= ColliderMaxDrawsPerFrame) break;
                var col = _cachedColliders[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                int tier = ClassifyTier(col);
                Color colour = col.isTrigger
                    ? ColliderColorTrigger
                    : tier == 0 ? ColliderColorStatic
                    : tier == 1 ? ColliderColorKinematic
                    : ColliderColorDynamic;

                Mesh mesh;
                Matrix4x4 matrix;
                if (!TryBuildColliderDraw(col, out mesh, out matrix)) continue;

                mpb.SetColor(ShaderPropTintColor, colour);
                int layer = col.gameObject.layer;
                Graphics.DrawMesh(mesh, matrix, _colliderWireMaterial, layer, _sceneCamera, 0, mpb);
                drawn++;
            }
        }

        MaterialPropertyBlock _colliderMpb;
        MaterialPropertyBlock ColliderPropertyBlock => _colliderMpb ??= new MaterialPropertyBlock();

        void RefreshColliderCache()
        {
            if (_cachedColliders != null && Time.frameCount - _cachedCollidersFrame < ColliderCacheValidForFrames) return;
            _cachedColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            _cachedCollidersFrame = Time.frameCount;
        }

        bool TryBuildColliderDraw(Collider col, out Mesh mesh, out Matrix4x4 matrix)
        {
            var t = col.transform;
            if (col is BoxCollider box)
            {
                mesh = _unitBoxLines;
                var ls = t.lossyScale;
                var size = new Vector3(box.size.x * ls.x, box.size.y * ls.y, box.size.z * ls.z);
                matrix = Matrix4x4.TRS(t.TransformPoint(box.center), t.rotation, size);
                return true;
            }
            if (col is SphereCollider sphere)
            {
                mesh = _unitSphereLines;
                var ls = t.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                float r = sphere.radius * maxScale;
                matrix = Matrix4x4.TRS(t.TransformPoint(sphere.center), t.rotation, new Vector3(r, r, r));
                return true;
            }
            if (col is CapsuleCollider cap)
            {
                mesh = GetOrBuildCapsuleLineMesh(cap);
                if (mesh == null) { matrix = default; return false; }
                matrix = Matrix4x4.TRS(t.TransformPoint(cap.center), t.rotation, Vector3.one);
                return true;
            }
            if (col is MeshCollider mc)
            {
                var src = mc.sharedMesh;
                if (src == null) { mesh = null; matrix = default; return false; }
                mesh = GetOrBuildEdgeMesh(src);
                if (mesh == null) { matrix = default; return false; }
                matrix = t.localToWorldMatrix;
                return true;
            }
            // Unsupported collider type (TerrainCollider etc.) — skip.
            mesh = null;
            matrix = default;
            return false;
        }

        void EnsureUnitPrimitiveMeshes()
        {
            if (_unitBoxLines == null) _unitBoxLines = BuildUnitBoxLineMesh();
            if (_unitSphereLines == null) _unitSphereLines = BuildUnitSphereLineMesh();
        }

        static Mesh BuildUnitBoxLineMesh()
        {
            // Unit cube centred at origin with extents ±0.5
            var verts = new Vector3[8];
            int idx = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                verts[idx++] = new Vector3(x * 0.5f, y * 0.5f, z * 0.5f);

            int Bit(int x, int y, int z) => ((x > 0 ? 1 : 0) << 2) | ((y > 0 ? 1 : 0) << 1) | (z > 0 ? 1 : 0);
            var edges = new List<int>(24);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                { edges.Add(Bit(x, y, -1)); edges.Add(Bit(x, y, 1)); }
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                { edges.Add(Bit(x, -1, z)); edges.Add(Bit(x, 1, z)); }
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                { edges.Add(Bit(-1, y, z)); edges.Add(Bit(1, y, z)); }

            var m = new Mesh { name = "bp_unitBoxLines", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = verts;
            m.SetIndices(edges.ToArray(), MeshTopology.Lines, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one);
            return m;
        }

        static Mesh BuildUnitSphereLineMesh(int segments = 48)
        {
            // Three great circles in XY, XZ, YZ planes — radius 1.
            var verts = new List<Vector3>(segments * 3);
            var idx = new List<int>(segments * 6);
            void Ring(Func<float, Vector3> f)
            {
                int baseI = verts.Count;
                for (int i = 0; i < segments; i++)
                {
                    float a = (i / (float)segments) * Mathf.PI * 2f;
                    verts.Add(f(a));
                }
                for (int i = 0; i < segments; i++)
                {
                    idx.Add(baseI + i);
                    idx.Add(baseI + (i + 1) % segments);
                }
            }
            Ring(a => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            Ring(a => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            Ring(a => new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)));

            var m = new Mesh { name = "bp_unitSphereLines", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = verts.ToArray();
            m.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        Mesh GetOrBuildCapsuleLineMesh(CapsuleCollider cap)
        {
            var t = cap.transform;
            var ls = t.lossyScale;
            // Unity scales capsule radius by max of the two lateral axes,
            // height by the main-axis scale.
            float radiusScale, heightScale;
            switch (cap.direction)
            {
                case 0: // X
                    radiusScale = Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                    heightScale = Mathf.Abs(ls.x);
                    break;
                case 2: // Z
                    radiusScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
                    heightScale = Mathf.Abs(ls.z);
                    break;
                default: // Y
                    radiusScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
                    heightScale = Mathf.Abs(ls.y);
                    break;
            }
            float r = cap.radius * radiusScale;
            float h = Mathf.Max(cap.height * heightScale, r * 2f);

            int id = cap.GetInstanceID();
            if (_capsuleLineMeshes.TryGetValue(id, out var entry))
            {
                if (entry.mesh != null
                    && Mathf.Approximately(entry.r, r)
                    && Mathf.Approximately(entry.h, h)
                    && entry.dir == cap.direction)
                {
                    return entry.mesh;
                }
                if (entry.mesh != null) Destroy(entry.mesh);
            }

            var mesh = BuildCapsuleLineMesh(r, h, cap.direction);
            _capsuleLineMeshes[id] = (mesh, r, h, cap.direction);
            return mesh;
        }

        static Mesh BuildCapsuleLineMesh(float radius, float height, int direction, int segments = 32)
        {
            // Build along Y, then rotate to direction. Capsule = cylinder of
            // length (height - 2r) with two hemispheres on top/bottom.
            float halfCyl = Mathf.Max(0f, (height * 0.5f) - radius);

            var verts = new List<Vector3>();
            var idx = new List<int>();

            void Ring(Vector3 centre, Vector3 axisA, Vector3 axisB)
            {
                int baseI = verts.Count;
                for (int i = 0; i < segments; i++)
                {
                    float a = (i / (float)segments) * Mathf.PI * 2f;
                    verts.Add(centre + (axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a)) * radius);
                }
                for (int i = 0; i < segments; i++)
                {
                    idx.Add(baseI + i);
                    idx.Add(baseI + (i + 1) % segments);
                }
            }

            void HalfArc(Vector3 centre, Vector3 axisA, Vector3 axisB, bool positive)
            {
                int half = segments / 2;
                int baseI = verts.Count;
                for (int i = 0; i <= half; i++)
                {
                    float a = (i / (float)half) * Mathf.PI;
                    float ya = positive ? Mathf.Sin(a) : -Mathf.Sin(a);
                    verts.Add(centre + (axisA * Mathf.Cos(a)) * radius + axisB * ya * radius);
                }
                for (int i = 0; i < half; i++) { idx.Add(baseI + i); idx.Add(baseI + i + 1); }
            }

            Vector3 top = Vector3.up * halfCyl;
            Vector3 bot = -top;

            // Top + bottom rings (cylinder caps in XZ plane)
            Ring(top, Vector3.right, Vector3.forward);
            Ring(bot, Vector3.right, Vector3.forward);

            // Cylinder side lines (4 around)
            void Side(Vector3 dir)
            {
                int baseI = verts.Count;
                verts.Add(top + dir * radius);
                verts.Add(bot + dir * radius);
                idx.Add(baseI); idx.Add(baseI + 1);
            }
            Side(Vector3.right);
            Side(-Vector3.right);
            Side(Vector3.forward);
            Side(-Vector3.forward);

            // Hemispheres — two arcs each (XY + ZY planes)
            HalfArc(top, Vector3.right,   Vector3.up, true);
            HalfArc(top, Vector3.forward, Vector3.up, true);
            HalfArc(bot, Vector3.right,   Vector3.up, false);
            HalfArc(bot, Vector3.forward, Vector3.up, false);

            // Rotate from Y-axis canonical to requested direction
            if (direction != 1)
            {
                Quaternion rot = direction == 0
                    ? Quaternion.Euler(0f, 0f, -90f)   // Y → X
                    : Quaternion.Euler(90f, 0f, 0f);   // Y → Z
                for (int i = 0; i < verts.Count; i++) verts[i] = rot * verts[i];
            }

            var m = new Mesh { name = "bp_capsuleLines", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = verts.ToArray();
            m.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            float halfBound = Mathf.Max(height * 0.5f, radius);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * (halfBound * 2f));
            return m;
        }

        void ClearColliderPrimitiveCache()
        {
            if (_unitBoxLines != null) { Destroy(_unitBoxLines); _unitBoxLines = null; }
            if (_unitSphereLines != null) { Destroy(_unitSphereLines); _unitSphereLines = null; }
            foreach (var entry in _capsuleLineMeshes.Values)
            {
                if (entry.mesh != null) Destroy(entry.mesh);
            }
            _capsuleLineMeshes.Clear();
        }

        void OnDestroy()
        {
            CleanupRenderMode();
            ClearEdgeMeshCache();
            ClearColliderPrimitiveCache();
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
                _colliderCache = new List<ColliderCacheEntry>();
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

                if (count > 0) sb.Append(",");
                sb.Append("{");
                string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

                var go = col.gameObject;
                var t = col.transform;
                var wPos = t.position;
                var wRot = t.rotation;
                var rot = wRot.eulerAngles;
                var ls = t.lossyScale;
                _colliderCache.Add(new ColliderCacheEntry
                {
                    collider = col,
                    tier = tier,
                    instanceId = id,
                    lastPos = wPos,
                    lastRot = wRot,
                });
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
        /// Compact transform-only update — emits any cached collider whose world
        /// transform changed since the last call, regardless of Rigidbody tier.
        /// World-space comparison catches parent-driven motion (animated rigs,
        /// scripted parents) that Transform.hasChanged would miss.
        /// Returns {"t":[[id,px,py,pz,rx,ry,rz],...], "r":[removedIds]}
        /// </summary>
        public string GetColliderTransforms()
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":[");

            List<int> removed = null;
            int written = 0;

            if (_colliderCache != null)
            {
                for (int i = _colliderCache.Count - 1; i >= 0; i--)
                {
                    var entry = _colliderCache[i];
                    var col = entry.collider;

                    // Collider destroyed or disabled since snapshot
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    {
                        if (removed == null) removed = new List<int>();
                        removed.Add(entry.instanceId);
                        _colliderCache.RemoveAt(i);
                        continue;
                    }

                    var tr = col.transform;
                    var pos = tr.position;
                    var rotQ = tr.rotation;

                    if ((pos - entry.lastPos).sqrMagnitude < ColliderMoveSqrEpsilon
                        && Quaternion.Angle(rotQ, entry.lastRot) < ColliderRotateEpsilonDeg)
                        continue;

                    entry.lastPos = pos;
                    entry.lastRot = rotQ;

                    var rot = rotQ.eulerAngles;
                    string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

                    if (written > 0) sb.Append(",");
                    sb.Append($"[{entry.instanceId},{F(pos.x)},{F(pos.y)},{F(pos.z)},{F(rot.x)},{F(rot.y)},{F(rot.z)}]");
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
