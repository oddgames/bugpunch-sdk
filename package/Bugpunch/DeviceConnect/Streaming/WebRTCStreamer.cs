using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Manages WebRTC video streaming from Unity cameras to the dashboard.
    /// Signaling flows through the existing WebSocket tunnel — no extra server needed.
    ///
    /// IMPORTANT: This component is created lazily — only when a debug session starts.
    /// Do NOT add this component at startup. The act of loading this class triggers
    /// the Unity.WebRTC assembly load which loads the native libwebrtc.so. On some
    /// Android 15 devices that native load crashes if done too early.
    /// </summary>
    public class WebRTCStreamer : MonoBehaviour, IStreamer
    {
        RTCPeerConnection _pc;
        VideoStreamTrack _videoTrack;
        RenderTexture _rt;
        RenderTexture _screenCapRT;
        Camera _targetCamera;
        Camera _cachedCamera;

        int _width = 1280;
        int _height = 720;
        int _fps = 30;
        volatile bool _streaming;

        TunnelClient _tunnel;
        readonly Queue<Action> _mainThreadQueue = new();
        RTCIceServer[] _iceServers;
        readonly List<IceMessage> _pendingIceCandidates = new();
        bool _applicationQuitting;
        Coroutine _webrtcUpdateCoroutine;

        // Data channel for sending camera metadata per frame
        RTCDataChannel _metadataChannel;
        int _metadataFrameSkip;

        /// <summary>
        /// Reference to the scene camera service for sending metadata via data channel.
        /// </summary>
        public SceneCameraService SceneCameraRef { get; set; }

        /// <summary>
        /// Set custom ICE servers (STUN + TURN) fetched from the server.
        /// If not set, falls back to default STUN server.
        /// </summary>
        public void SetIceServers(RTCIceServer[] servers)
        {
            _iceServers = servers;
            Debug.Log($"[Bugpunch] WebRTC: configured {servers?.Length ?? 0} ICE server(s)");
        }

        /// <summary>
        /// Parse ICE servers from raw JSON and configure them. This is called by
        /// BugpunchClient so it doesn't need to reference Unity.WebRTC types.
        /// Expected format: { "iceServers": [{ "urls": "...", "username": "...", "credential": "..." }] }
        /// </summary>
        public void SetIceServersFromJson(string json)
        {
            var response = JsonUtility.FromJson<IceServersResponse>(json);
            if (response.iceServers == null || response.iceServers.Length == 0)
            {
                Debug.Log("[Bugpunch] No ICE servers returned, using default STUN");
                return;
            }

            var servers = new RTCIceServer[response.iceServers.Length];
            for (int i = 0; i < response.iceServers.Length; i++)
            {
                var s = response.iceServers[i];
                servers[i] = new RTCIceServer
                {
                    urls = new[] { s.urls },
                    username = s.username ?? "",
                    credential = s.credential ?? ""
                };
            }

            SetIceServers(servers);
        }

        /// <summary>
        /// Initialize the streamer with a tunnel client for signaling.
        /// This is the point where WebRTC.Update() coroutine starts — the native
        /// lib is fully active after this call.
        /// </summary>
        public void Initialize(TunnelClient tunnel, int width = 1280, int height = 720, int fps = 30)
        {
            _tunnel = tunnel;
            _width = width;
            _height = height;
            _fps = fps;

            if (_webrtcUpdateCoroutine == null)
                _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        }

        /// <summary>
        /// Change stream quality at runtime. Stops and restarts streaming if active.
        /// </summary>
        public string SetQuality(int width, int height, int fps)
        {
            _width = Mathf.Clamp(width, 160, 3840);
            _height = Mathf.Clamp(height, 120, 2160);
            _fps = Mathf.Clamp(fps, 1, 60);
            Debug.Log($"[Bugpunch] WebRTC: quality set to {_width}x{_height}@{_fps}fps");

            // Stop current stream so browser reconnects at new resolution
            if (_streaming)
            {
                StopStreaming();
                Debug.Log("[Bugpunch] WebRTC: stream stopped for quality change — waiting for new offer");
            }

            return $"{{\"ok\":true,\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"reconnectRequired\":true}}";
        }

        /// <summary>
        /// Get current stream settings.
        /// </summary>
        public string GetQuality()
        {
            return $"{{\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"streaming\":{(_streaming ? "true" : "false")}}}";
        }

        /// <summary>
        /// Set which camera to stream from. Defaults to Camera.main.
        /// </summary>
        public void SetCamera(Camera cam)
        {
            _targetCamera = cam;
            Debug.Log($"[Bugpunch] WebRTC: SetCamera → {(cam != null ? cam.name : "null (game view)")} streaming={_streaming}");
        }

        /// <summary>
        /// Handle a WebRTC signaling message from the dashboard (via tunnel).
        /// Called from BugpunchClient for ICE and stop messages.
        /// </summary>
        public void HandleSignalingMessage(string type, string sessionId, string payload)
        {
            switch (type)
            {
                case "webrtc-ice":
                    HandleIceCandidate(payload);
                    break;
                case "webrtc-stop":
                    // Ignore — stream lifecycle is managed by new offers (which
                    // clean up the old connection) and component destruction.
                    // The dashboard sends webrtc-stop on mode switch but we want
                    // the stream to survive camera changes.
                    break;
            }
        }

        /// <summary>
        /// Get queued device-side ICE candidates as JSON array, then clear the queue.
        /// Called by the browser via GET /webrtc-ice-candidates.
        /// </summary>
        public string DrainIceCandidates()
        {
            lock (_pendingIceCandidates)
            {
                if (_pendingIceCandidates.Count == 0) return "[]";
                var sb = new System.Text.StringBuilder();
                sb.Append("[");
                for (int i = 0; i < _pendingIceCandidates.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(JsonUtility.ToJson(_pendingIceCandidates[i]));
                }
                sb.Append("]");
                _pendingIceCandidates.Clear();
                return sb.ToString();
            }
        }

        /// <summary>
        /// Handle an offer asynchronously. Calls onAnswer with the SDP answer JSON,
        /// or onError if something fails. Used by BugpunchClient to return the answer
        /// in the same HTTP response as the offer request.
        /// </summary>
        public void HandleOfferAsync(string sdpJson, Action<string> onAnswer, Action<string> onError)
        {
            StartCoroutine(HandleOffer(sdpJson, onAnswer, onError));
        }

        IEnumerator HandleOffer(string sdpJson, Action<string> onAnswer, Action<string> onError)
        {
            // Wait one frame to let WebRTC.Update() coroutine initialize
            // (critical when Initialize() and HandleOffer run in the same frame)
            yield return null;

            if (_streaming)
                StopStreaming();

            Debug.Log("[Bugpunch] WebRTC: received offer, creating answer...");
            float deadline = Time.realtimeSinceStartup + 30f;

            SdpMessage offerData;
            try
            {
                Debug.Log($"[Bugpunch] WebRTC: offer body length={sdpJson?.Length ?? 0}, starts with: {sdpJson?.Substring(0, Math.Min(100, sdpJson?.Length ?? 0))}");
                offerData = JsonUtility.FromJson<SdpMessage>(sdpJson);
                if (string.IsNullOrEmpty(offerData.sdp))
                {
                    onError?.Invoke("Empty SDP in offer");
                    yield break;
                }
                Debug.Log($"[Bugpunch] WebRTC: parsed SDP, length={offerData.sdp.Length}");
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Failed to parse offer: {ex.Message}");
                yield break;
            }

            CleanupPeerConnection();

            // Create peer connection — use custom ICE servers if set, otherwise fallback to STUN
            var config = new RTCConfiguration
            {
                iceServers = _iceServers ?? new[]
                {
                    new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
                }
            };

            _pc = new RTCPeerConnection(ref config);
            var thisPC = _pc;

            // Create data channel for per-frame camera metadata
            _metadataChannel = _pc.CreateDataChannel("camera-metadata", new RTCDataChannelInit());
            _metadataFrameSkip = 0;

            // ICE candidate handler — queue candidates for browser to poll
            _pc.OnIceCandidate = candidate =>
            {
                if (candidate == null) return;
                lock (_pendingIceCandidates)
                {
                    _pendingIceCandidates.Add(new IceMessage
                    {
                        candidate = candidate.Candidate,
                        sdpMid = candidate.SdpMid,
                        sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                    });
                }
            };

            _pc.OnConnectionStateChange = state =>
            {
                Debug.Log($"[Bugpunch] WebRTC connection state: {state}");
                if (state == RTCPeerConnectionState.Disconnected ||
                    state == RTCPeerConnectionState.Failed ||
                    state == RTCPeerConnectionState.Closed)
                {
                    _streaming = false;
                    lock (_mainThreadQueue)
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            CleanupPeerConnection();
                            CleanupRenderTexture();
                            lock (_pendingIceCandidates) { _pendingIceCandidates.Clear(); }
                        });
                    }
                }
            };

            // Create video track from camera render texture
            CreateRenderTexture();
            _videoTrack = new VideoStreamTrack(_rt);
            _pc.AddTrack(_videoTrack);

            // Set remote description (the offer)
            Debug.Log("[Bugpunch] WebRTC: setting remote description...");
            var offerDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offerData.sdp
            };
            var setRemoteOp = _pc.SetRemoteDescription(ref offerDesc);
            yield return setRemoteOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); yield break; }
            if (setRemoteOp.IsError)
            {
                Debug.LogError($"[Bugpunch] WebRTC: SetRemoteDescription failed: {setRemoteOp.Error.message}");
                onError?.Invoke(setRemoteOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { onError?.Invoke("Replaced by new offer"); yield break; }
            Debug.Log("[Bugpunch] WebRTC: remote description set, creating answer...");

            // Create answer
            var answerOp = _pc.CreateAnswer();
            yield return answerOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); yield break; }
            if (answerOp.IsError)
            {
                Debug.LogError($"[Bugpunch] WebRTC: CreateAnswer failed: {answerOp.Error.message}");
                onError?.Invoke(answerOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { onError?.Invoke("Replaced by new offer"); yield break; }
            Debug.Log("[Bugpunch] WebRTC: answer created, setting local description...");

            var answer = answerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answer);
            yield return setLocalOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); yield break; }
            if (setLocalOp.IsError)
            {
                Debug.LogError($"[Bugpunch] WebRTC: SetLocalDescription failed: {setLocalOp.Error.message}");
                onError?.Invoke(setLocalOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { onError?.Invoke("Replaced by new offer"); yield break; }

            // Return answer to caller
            Debug.Log("[Bugpunch] WebRTC: sending answer back...");
            var answerJson = JsonUtility.ToJson(new SdpMessage { sdp = answer.sdp, type = "answer" });
            onAnswer?.Invoke(answerJson);

            _streaming = true;
            StartCoroutine(RenderLoop());

            Debug.Log("[Bugpunch] WebRTC: streaming started");
        }

        void HandleIceCandidate(string iceJson)
        {
            if (_pc == null) return;

            var ice = JsonUtility.FromJson<IceMessage>(iceJson);
            var candidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = ice.candidate,
                sdpMid = ice.sdpMid,
                sdpMLineIndex = ice.sdpMLineIndex
            });
            _pc.AddIceCandidate(candidate);
        }

        void CreateRenderTexture()
        {
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            _rt = new RenderTexture(_width, _height, 24, format);
            _rt.Create();
        }

        /// <summary>
        /// Render loop — captures camera to render texture each frame while streaming.
        /// Also sends camera metadata via data channel every other frame.
        /// </summary>
        IEnumerator RenderLoop()
        {
            while (_streaming && _pc != null)
            {
                yield return new WaitForEndOfFrame();
                if (_rt == null) continue;

                // Check which camera is being used — log once on switch
                if (_targetCamera != null)
                {
                    // Scene camera mode — render specific camera to RT
                    _targetCamera.targetTexture = _rt;
                    _targetCamera.Render();
                    _targetCamera.targetTexture = null;
                }
                else
                {
                    // Game view mode — capture the full screen (all cameras, UI, post-fx)
                    // CaptureScreenshotIntoRenderTexture captures at screen res, may be flipped
                    if (_screenCapRT == null || _screenCapRT.width != Screen.width || _screenCapRT.height != Screen.height)
                    {
                        if (_screenCapRT != null) { _screenCapRT.Release(); Destroy(_screenCapRT); }
                        _screenCapRT = new RenderTexture(Screen.width, Screen.height, 0);
                        _screenCapRT.Create();
                    }
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenCapRT);
                    // Blit with vertical flip and scale to streaming resolution
                    Graphics.Blit(_screenCapRT, _rt, new Vector2(1, -1), new Vector2(0, 1));
                }

                // Send camera metadata + touch data via data channel (every other frame)
                _metadataFrameSkip++;
                var metaCam = _targetCamera ? _targetCamera : Camera.main;
                if (_metadataFrameSkip >= 2 && _metadataChannel != null && _metadataChannel.ReadyState == RTCDataChannelState.Open)
                {
                    _metadataFrameSkip = 0;
                    string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                    var sb = new System.Text.StringBuilder(256);
                    sb.Append('{');

                    // Camera metadata
                    if (metaCam != null)
                    {
                        var ct = metaCam.transform;
                        var cp = ct.position;
                        var cr = ct.eulerAngles;
                        sb.Append($"\"px\":{F(cp.x)},\"py\":{F(cp.y)},\"pz\":{F(cp.z)},\"rx\":{F(cr.x)},\"ry\":{F(cr.y)},\"rz\":{F(cr.z)},");
                    }

                    // Native OS touch data — read from platform touch recorder
                    var touchJson = RequestRouter.GetLiveTouchesForStream(500);
                    sb.Append(touchJson);

                    sb.Append('}');
                    _metadataChannel.Send(sb.ToString());
                }
            }
        }

        public void StopStreaming()
        {
            _streaming = false;
            CleanupPeerConnection();
            CleanupRenderTexture();
            lock (_pendingIceCandidates) { _pendingIceCandidates.Clear(); }
            Debug.Log("[Bugpunch] WebRTC: streaming stopped");
        }

        void CleanupPeerConnection()
        {
            if (_metadataChannel != null)
            {
                _metadataChannel.Close();
                _metadataChannel.Dispose();
                _metadataChannel = null;
            }
            if (_pc != null)
            {
                if (_videoTrack != null)
                {
                    foreach (var sender in _pc.GetSenders())
                    {
                        if (sender.Track == _videoTrack)
                        {
                            _pc.RemoveTrack(sender);
                            break;
                        }
                    }
                }
                _pc.Close();
                _pc.Dispose();
                _pc = null;
            }
            if (_videoTrack != null)
            {
                _videoTrack.Dispose();
                _videoTrack = null;
            }
        }

        void CleanupRenderTexture()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        void Update()
        {
            lock (_mainThreadQueue)
            {
                while (_mainThreadQueue.Count > 0)
                    _mainThreadQueue.Dequeue()?.Invoke();
            }
        }

        void OnApplicationQuit() => _applicationQuitting = true;

        void OnDestroy()
        {
            if (_applicationQuitting) return; // skip cleanup during app quit — mono is shutting down
            if (_webrtcUpdateCoroutine != null)
            {
                StopCoroutine(_webrtcUpdateCoroutine);
                _webrtcUpdateCoroutine = null;
            }
            StopStreaming();
        }

        // Signaling message types (JSON serializable)
        [Serializable]
        struct SdpMessage
        {
            public string type;
            public string sdp;
        }

        [Serializable]
        struct IceMessage
        {
            public string candidate;
            public string sdpMid;
            public int sdpMLineIndex;
        }

        [Serializable]
        struct IceServersResponse
        {
            public IceServerEntry[] iceServers;
        }

        [Serializable]
        struct IceServerEntry
        {
            public string urls;
            public string username;
            public string credential;
        }
    }
}
