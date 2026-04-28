using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.WebRTC;
using UnityEngine;

namespace ODDGames.Bugpunch.RemoteIDE
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
        // Pixel budget (longest-edge cap) and aspect source. Actual _width/_height
        // are always derived from the device's current Screen.width/Screen.height
        // (game mode) or _overrideAspectW/H (scene mode — dashboard panel aspect)
        // so the stream matches the viewer 1:1 with no distortion.
        int _reqMaxEdge = 1280;
        int _overrideAspectW; // 0 = use Screen.width
        int _overrideAspectH; // 0 = use Screen.height
        // Track the dimensions the RT was last sized for so RenderLoop can
        // detect device rotation / resolution changes and hot-swap.
        int _rtAspectW;
        int _rtAspectH;
        volatile bool _streaming;

        readonly Queue<Action> _mainThreadQueue = new();
        RTCIceServer[] _iceServers;
        readonly List<IceMessage> _pendingIceCandidates = new();
        bool _applicationQuitting;
        Coroutine _webrtcUpdateCoroutine;

        // Data channel for sending camera metadata per frame
        RTCDataChannel _metadataChannel;
        // Time-based pacing — the render loop yields every frame, but we only
        // actually blit / send at these cadences so a 60 fps game driving a
        // 30 fps stream doesn't burn GPU on wasted renders (and cook the
        // device). Using unscaledTime so timeScale=0 pauses don't stall.
        float _nextBlitTimeUnscaled;
        float _nextMetadataTimeUnscaled;
        const float METADATA_INTERVAL_SECONDS = 0.1f; // 10 Hz is plenty for cursor/touch

        // Game-side perf counters piggybacked on the metadata channel.
        // ProfilerRecorder is the official zero-overhead path for these markers.
        ProfilerRecorder _drawCallsRecorder;
        ProfilerRecorder _batchesRecorder;
        ProfilerRecorder _setPassRecorder;
        ProfilerRecorder _trianglesRecorder;
        ProfilerRecorder _verticesRecorder;
        ProfilerRecorder _totalMemRecorder;
        ProfilerRecorder _gcMemRecorder;
        bool _perfRecordersStarted;
        int _frameAccum;
        float _frameAccumStart;
        float _smoothedFps;

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
            BugpunchLog.Info("WebRTCStreamer", $"SetIceServers: configured {servers?.Length ?? 0} ICE server(s)");
            if (servers != null)
            {
                foreach (var s in servers)
                    BugpunchLog.Info("WebRTCStreamer", $"  ICE: {s.urls}");
            }
        }

        /// <summary>
        /// Parse ICE servers from raw JSON and configure them. This is called by
        /// BugpunchClient so it doesn't need to reference Unity.WebRTC types.
        /// Expected format: { "iceServers": [{ "urls": "...", "username": "...", "credential": "..." }] }
        /// </summary>
        public void SetIceServersFromJson(string json)
        {
            BugpunchLog.Info("WebRTCStreamer", $"SetIceServersFromJson: received {json?.Length ?? 0} chars");
            var response = JsonUtility.FromJson<IceServersResponse>(json);
            if (response.iceServers == null || response.iceServers.Length == 0)
            {
                BugpunchLog.Info("WebRTCStreamer", "SetIceServersFromJson: no ICE servers, using default STUN");
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
        public void Initialize(int width = 1280, int height = 720, int fps = 30)
        {
            _reqMaxEdge = Mathf.Clamp(Mathf.Max(width, height), 160, 3840);
            _fps = Mathf.Clamp(fps, 1, 60);
            RecomputeDimensions();

            if (_webrtcUpdateCoroutine == null)
                _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        }

        /// <summary>
        /// Change stream quality at runtime. The requested width/height are
        /// interpreted as a pixel budget (the larger of the two becomes the
        /// long-edge cap) — the actual RT dimensions preserve the device's
        /// current aspect ratio. Hot-swaps the video track without
        /// renegotiation, so the browser keeps streaming through the change.
        /// </summary>
        public string SetQuality(int width, int height, int fps)
        {
            _reqMaxEdge = Mathf.Clamp(Mathf.Max(width, height), 160, 3840);
            _fps = Mathf.Clamp(fps, 1, 60);
            RecomputeDimensions();
            BugpunchLog.Info("WebRTCStreamer", $"WebRTC: quality set to budget={_reqMaxEdge}px @ {_fps}fps → {_width}x{_height}");

            if (_streaming)
                ResizeRenderTarget();

            return $"{{\"ok\":true,\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"reconnectRequired\":false}}";
        }

        /// <summary>
        /// Get current stream settings.
        /// </summary>
        public string GetQuality()
        {
            return $"{{\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"streaming\":{(_streaming ? "true" : "false")}}}";
        }

        /// <summary>
        /// Override the aspect ratio the RT should match. Pass (0,0) to clear
        /// and fall back to the device's live Screen dimensions. Triggers a
        /// hot-swap of the render target if streaming is active.
        /// </summary>
        public void SetTargetAspect(int aspectWidth, int aspectHeight)
        {
            _overrideAspectW = Mathf.Max(0, aspectWidth);
            _overrideAspectH = Mathf.Max(0, aspectHeight);
            RecomputeDimensions();
            if (_streaming)
                ResizeRenderTarget();
        }

        // Derive _width/_height from the current aspect source + pixel budget.
        // Rounds to even dimensions (most encoders require that).
        void RecomputeDimensions()
        {
            int aw = _overrideAspectW > 0 ? _overrideAspectW : Screen.width;
            int ah = _overrideAspectH > 0 ? _overrideAspectH : Screen.height;
            if (aw <= 0 || ah <= 0) { aw = 16; ah = 9; }

            int longEdge = _reqMaxEdge;
            int w, h;
            if (aw >= ah)
            {
                w = longEdge;
                h = Mathf.Max(16, Mathf.RoundToInt(longEdge * (float)ah / aw));
            }
            else
            {
                h = longEdge;
                w = Mathf.Max(16, Mathf.RoundToInt(longEdge * (float)aw / ah));
            }
            // Encoders want even dimensions
            w &= ~1; h &= ~1;
            _width = w;
            _height = h;
            _rtAspectW = aw;
            _rtAspectH = ah;
        }

        /// <summary>
        /// Set which camera to stream from. Defaults to Camera.main.
        /// Switching back to game mode (cam == null) clears any panel-aspect
        /// override so the stream tracks the device's live screen aspect.
        /// </summary>
        public void SetCamera(Camera cam)
        {
            _targetCamera = cam;
            BugpunchLog.Info("WebRTCStreamer", $"WebRTC: SetCamera → {(cam != null ? cam.name : "null (game view)")} streaming={_streaming}");
            if (cam == null && (_overrideAspectW != 0 || _overrideAspectH != 0))
            {
                _overrideAspectW = 0;
                _overrideAspectH = 0;
                RecomputeDimensions();
                if (_streaming)
                    ResizeRenderTarget();
            }
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
                BugpunchLog.Info("WebRTCStreamer", $"DrainIceCandidates: returning {_pendingIceCandidates.Count} candidates");
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
            {
                BugpunchLog.Info("WebRTCStreamer", "HandleOffer: was streaming, stopping first");
                StopStreaming();
            }

            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: received offer sdpLen={sdpJson?.Length ?? 0}");
            float deadline = Time.realtimeSinceStartup + 30f;

            SdpMessage offerData;
            try
            {
                BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: parsing SDP, first 80 chars: {sdpJson?.Substring(0, Math.Min(80, sdpJson?.Length ?? 0))}");
                offerData = JsonUtility.FromJson<SdpMessage>(sdpJson);
                if (string.IsNullOrEmpty(offerData.sdp))
                {
                    BugpunchLog.Error("WebRTCStreamer", "HandleOffer: empty SDP, rejecting");
                    onError?.Invoke("Empty SDP in offer");
                    yield break;
                }
                BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: parsed SDP, sdpLen={offerData.sdp.Length}");
            }
            catch (Exception ex)
            {
                BugpunchLog.Error("WebRTCStreamer", $"HandleOffer: parse failed: {ex.Message}");
                onError?.Invoke($"Failed to parse offer: {ex.Message}");
                yield break;
            }

            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: cleaning up old peer connection");
            CleanupPeerConnection();

            // Create peer connection — use custom ICE servers if set, otherwise fallback to STUN
            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: creating RTCPeerConnection with {_iceServers?.Length ?? 0} ICE servers");
            var config = new RTCConfiguration
            {
                iceServers = _iceServers ?? new[]
                {
                    new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
                }
            };

            _pc = new RTCPeerConnection(ref config);
            var thisPC = _pc;
            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: RTCPeerConnection created");

            // Create data channel for per-frame camera metadata
            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: creating data channel 'camera-metadata'");
            _metadataChannel = _pc.CreateDataChannel("camera-metadata", new RTCDataChannelInit());

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
                BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: ICE candidate queued, sdpMid={candidate.SdpMid} lineIdx={candidate.SdpMLineIndex}");
            };

            _pc.OnConnectionStateChange = state =>
            {
                BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: connection state changed to {state}");
                // `Disconnected` is transient per the WebRTC spec — ICE keepalives
                // can recover without intervention (and often do after a camera
                // swap or brief main-thread hang). Don't tear the session down
                // on Disconnected; only on Failed (terminal) or Closed (local).
                if (state == RTCPeerConnectionState.Failed ||
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

            // Cap outgoing bitrate. Without this, Unity.WebRTC can push 8+ Mbps
            // at 720p which collapses on cellular links; GCC's ramp-up is slow
            // to recover. A 2.5 Mbps ceiling is visually indistinguishable for
            // a debug viewport at 30fps and gives congestion control a sane
            // target to work against.
            SetVideoMaxBitrate(2_500_000, 30);

            // Set remote description (the offer)
            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: setting remote description (offer)...");
            var offerDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offerData.sdp
            };
            var setRemoteOp = _pc.SetRemoteDescription(ref offerDesc);
            yield return setRemoteOp;

            if (Time.realtimeSinceStartup > deadline) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: SetRemoteDescription timed out"); onError?.Invoke("Offer handling timed out"); yield break; }
            if (setRemoteOp.IsError)
            {
                BugpunchLog.Error("WebRTCStreamer", $"HandleOffer: SetRemoteDescription failed: {setRemoteOp.Error.message}");
                onError?.Invoke(setRemoteOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: peer connection replaced during SetRemoteDescription"); onError?.Invoke("Replaced by new offer"); yield break; }
            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: remote description set, creating answer...");

            // Create answer
            var answerOp = _pc.CreateAnswer();
            yield return answerOp;

            if (Time.realtimeSinceStartup > deadline) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: CreateAnswer timed out"); onError?.Invoke("Offer handling timed out"); yield break; }
            if (answerOp.IsError)
            {
                BugpunchLog.Error("WebRTCStreamer", $"HandleOffer: CreateAnswer failed: {answerOp.Error.message}");
                onError?.Invoke(answerOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: peer connection replaced during CreateAnswer"); onError?.Invoke("Replaced by new offer"); yield break; }
            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: answer created, setting local description...");

            var answer = answerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answer);
            yield return setLocalOp;

            if (Time.realtimeSinceStartup > deadline) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: SetLocalDescription timed out"); onError?.Invoke("Offer handling timed out"); yield break; }
            if (setLocalOp.IsError)
            {
                BugpunchLog.Error("WebRTCStreamer", $"HandleOffer: SetLocalDescription failed: {setLocalOp.Error.message}");
                onError?.Invoke(setLocalOp.Error.message);
                yield break;
            }
            if (_pc != thisPC) { BugpunchLog.Error("WebRTCStreamer", "HandleOffer: peer connection replaced during SetLocalDescription"); onError?.Invoke("Replaced by new offer"); yield break; }

            // Return answer to caller
            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer: local desc set, sending answer sdpLen={answer.sdp.Length}");
            var answerJson = JsonUtility.ToJson(new SdpMessage { sdp = answer.sdp, type = "answer" });
            onAnswer?.Invoke(answerJson);

            _streaming = true;
            StartCoroutine(RenderLoop());

            BugpunchLog.Info("WebRTCStreamer", "HandleOffer: streaming started successfully");
        }

        void HandleIceCandidate(string iceJson)
        {
            BugpunchLog.Info("WebRTCStreamer", $"HandleIceCandidate: received {iceJson?.Length ?? 0} chars");
            if (_pc == null) { BugpunchLog.Warn("WebRTCStreamer", "HandleIceCandidate: _pc is null, ignoring"); return; }

            var ice = JsonUtility.FromJson<IceMessage>(iceJson);
            var candPreview = ice.candidate != null ? ice.candidate.Substring(0, Math.Min(50, ice.candidate.Length)) : "null";
            BugpunchLog.Info("WebRTCStreamer", $"HandleIceCandidate: candidate={candPreview} sdpMid={ice.sdpMid} lineIdx={ice.sdpMLineIndex}");
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
            // Always recompute immediately before allocating — the device may
            // have rotated between Initialize() and the first offer.
            RecomputeDimensions();
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            _rt = new RenderTexture(_width, _height, 24, format);
            _rt.Create();
        }

        /// <summary>
        /// Hot-swap the render target: build a new RT + VideoStreamTrack at the
        /// current _width/_height and use RTCRtpSender.ReplaceTrack so the
        /// browser keeps streaming without a renegotiation. Called on quality
        /// change, aspect override change, or device rotation detection.
        /// </summary>
        void ResizeRenderTarget()
        {
            if (_pc == null) return;
            if (_rt != null && _rt.width == _width && _rt.height == _height) return;

            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var newRt = new RenderTexture(_width, _height, 24, format);
            if (!newRt.Create())
            {
                BugpunchLog.Warn("WebRTCStreamer", "ResizeRenderTarget: RT.Create() failed");
                Destroy(newRt);
                return;
            }

            VideoStreamTrack newTrack;
            try { newTrack = new VideoStreamTrack(newRt); }
            catch (Exception ex)
            {
                BugpunchLog.Warn("WebRTCStreamer", $"ResizeRenderTarget: new VideoStreamTrack failed: {ex.Message}");
                newRt.Release(); Destroy(newRt);
                return;
            }

            bool replaced = false;
            foreach (var sender in _pc.GetSenders())
            {
                if (sender?.Track == null || sender.Track.Kind != TrackKind.Video) continue;
                if (sender.ReplaceTrack(newTrack))
                {
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                BugpunchLog.Warn("WebRTCStreamer", "ResizeRenderTarget: ReplaceTrack failed — keeping old RT");
                newTrack.Dispose();
                newRt.Release(); Destroy(newRt);
                return;
            }

            var oldRt = _rt;
            var oldTrack = _videoTrack;
            _rt = newRt;
            _videoTrack = newTrack;

            if (oldTrack != null) oldTrack.Dispose();
            if (oldRt != null) { oldRt.Release(); Destroy(oldRt); }
            if (_screenCapRT != null)
            {
                _screenCapRT.Release();
                Destroy(_screenCapRT);
                _screenCapRT = null;
            }

            // Reapply the bitrate cap — encoders reset encoding parameters on
            // track swap.
            SetVideoMaxBitrate(2_500_000, (uint)_fps);

            BugpunchLog.Info("WebRTCStreamer", $"ResizeRenderTarget: hot-swapped to {_width}x{_height}");
        }

        /// <summary>
        /// Render loop — captures camera to render texture each frame while streaming.
        /// Also sends camera metadata via data channel every other frame.
        /// </summary>
        IEnumerator RenderLoop()
        {
            int aspectCheckCounter = 0;
            _nextBlitTimeUnscaled = 0f;
            _nextMetadataTimeUnscaled = 0f;
            _frameAccum = 0;
            _frameAccumStart = Time.unscaledTime;
            while (_streaming && _pc != null)
            {
                yield return new WaitForEndOfFrame();
                _frameAccum++;
                if (_rt == null) continue;

                float now = Time.unscaledTime;

                // Every ~30 frames (game frames, not blits — rotation detection
                // can be cheap), check if the device's live screen aspect
                // drifted from the RT (phone rotated, window resized on desktop).
                // Only follow Screen when no panel-aspect override is active.
                if (++aspectCheckCounter >= 30)
                {
                    aspectCheckCounter = 0;
                    if (_overrideAspectW == 0 && _overrideAspectH == 0)
                    {
                        int sw = Screen.width, sh = Screen.height;
                        if (sw > 0 && sh > 0 && (sw != _rtAspectW || sh != _rtAspectH))
                        {
                            RecomputeDimensions();
                            ResizeRenderTarget();
                            if (_rt == null) continue;
                        }
                    }
                }

                // Metadata pacing — 10 Hz is enough for cursor/touch feedback.
                if (now >= _nextMetadataTimeUnscaled
                    && _metadataChannel != null
                    && _metadataChannel.ReadyState == RTCDataChannelState.Open)
                {
                    _nextMetadataTimeUnscaled = Mathf.Max(_nextMetadataTimeUnscaled + METADATA_INTERVAL_SECONDS, now);
                    var metaCam = _targetCamera ? _targetCamera : Camera.main;
                    string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                    // FPS over the last metadata interval, EMA-smoothed so the
                    // viewer sees a stable number rather than 10 Hz jitter.
                    float dtAccum = now - _frameAccumStart;
                    if (dtAccum > 0f && _frameAccum > 0)
                    {
                        float instantFps = _frameAccum / dtAccum;
                        _smoothedFps = _smoothedFps <= 0f ? instantFps : _smoothedFps * 0.7f + instantFps * 0.3f;
                    }
                    _frameAccum = 0;
                    _frameAccumStart = now;

                    // Lazy-start the rendering recorders. ProfilerRecorder is
                    // ~free and gives us live drawcalls/batches without a
                    // profiler attached. Runs on all build types in Unity 2020.2+.
                    if (!_perfRecordersStarted)
                    {
                        _perfRecordersStarted = true;
                        try
                        {
                            _drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                            _batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                            _setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                            _trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                            _verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
                            _totalMemRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
                            _gcMemRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
                        }
                        catch { /* unsupported platform — leave recorders invalid, fields will be omitted */ }
                    }
                    long dc = _drawCallsRecorder.Valid ? _drawCallsRecorder.LastValue : -1L;
                    long batches = _batchesRecorder.Valid ? _batchesRecorder.LastValue : -1L;
                    long setpass = _setPassRecorder.Valid ? _setPassRecorder.LastValue : -1L;
                    long tris = _trianglesRecorder.Valid ? _trianglesRecorder.LastValue : -1L;
                    long verts = _verticesRecorder.Valid ? _verticesRecorder.LastValue : -1L;
                    long totalMem = _totalMemRecorder.Valid ? _totalMemRecorder.LastValue : -1L;
                    long gcMem = _gcMemRecorder.Valid ? _gcMemRecorder.LastValue : -1L;

                    var sb = new System.Text.StringBuilder(256);
                    sb.Append('{');
                    if (metaCam != null)
                    {
                        var ct = metaCam.transform;
                        var cp = ct.position;
                        var cr = ct.eulerAngles;
                        sb.Append($"\"px\":{F(cp.x)},\"py\":{F(cp.y)},\"pz\":{F(cp.z)},\"rx\":{F(cr.x)},\"ry\":{F(cr.y)},\"rz\":{F(cr.z)},");
                    }
                    sb.Append($"\"fps\":{F(_smoothedFps)},");
                    if (dc >= 0) sb.Append($"\"dc\":{dc},");
                    if (batches >= 0) sb.Append($"\"b\":{batches},");
                    if (setpass >= 0) sb.Append($"\"sp\":{setpass},");
                    if (tris >= 0) sb.Append($"\"tri\":{tris},");
                    if (verts >= 0) sb.Append($"\"vrt\":{verts},");
                    if (totalMem >= 0) sb.Append($"\"mem\":{totalMem},");
                    if (gcMem >= 0) sb.Append($"\"gc\":{gcMem},");
                    var touchJson = RequestRouter.GetLiveTouchesForStream(500);
                    sb.Append(touchJson);
                    sb.Append('}');
                    _metadataChannel.Send(sb.ToString());
                }

                // Video blit pacing — skip the frame unless we're due for the
                // next one at the target fps. This is the main backpressure:
                // a 60 fps game driving a 30 fps stream should render/blit at
                // 30 fps, not 60.
                if (now < _nextBlitTimeUnscaled) continue;
                float blitInterval = 1f / Mathf.Max(1, _fps);
                // Schedule the next blit. If we've fallen behind (e.g. the
                // app stalled), clamp to `now` so we don't fire a burst of
                // catch-up frames into the encoder.
                _nextBlitTimeUnscaled = Mathf.Max(_nextBlitTimeUnscaled + blitInterval, now);

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
            }
        }

        public void StopStreaming()
        {
            _streaming = false;
            CleanupPeerConnection();
            CleanupRenderTexture();
            if (_perfRecordersStarted)
            {
                if (_drawCallsRecorder.Valid) _drawCallsRecorder.Dispose();
                if (_batchesRecorder.Valid) _batchesRecorder.Dispose();
                if (_setPassRecorder.Valid) _setPassRecorder.Dispose();
                if (_trianglesRecorder.Valid) _trianglesRecorder.Dispose();
                if (_verticesRecorder.Valid) _verticesRecorder.Dispose();
                if (_totalMemRecorder.Valid) _totalMemRecorder.Dispose();
                if (_gcMemRecorder.Valid) _gcMemRecorder.Dispose();
                _perfRecordersStarted = false;
                _smoothedFps = 0f;
            }
            lock (_pendingIceCandidates) { _pendingIceCandidates.Clear(); }
            BugpunchLog.Info("WebRTCStreamer", "WebRTC: streaming stopped");
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

        /// <summary>
        /// Apply a bitrate + framerate ceiling to the video sender. Call any time
        /// after the track has been added (e.g. from a data-channel message sent
        /// by the dashboard adapting to measured link conditions).
        /// </summary>
        public void SetVideoMaxBitrate(ulong bps, uint maxFps)
        {
            if (_pc == null) return;
            foreach (var sender in _pc.GetSenders())
            {
                if (sender?.Track == null || sender.Track.Kind != TrackKind.Video) continue;
                var parameters = sender.GetParameters();
                if (parameters?.encodings == null || parameters.encodings.Length == 0) continue;
                foreach (var enc in parameters.encodings)
                {
                    enc.maxBitrate = bps;
                    enc.maxFramerate = maxFps;
                }
                var err = sender.SetParameters(parameters);
                if (err.errorType != RTCErrorType.None)
                    BugpunchLog.Warn("WebRTCStreamer", $"WebRTC: SetParameters failed: {err.message}");
                else
                    BugpunchLog.Info("WebRTCStreamer", $"WebRTC: video cap → {bps / 1000} kbps, {maxFps} fps");
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
