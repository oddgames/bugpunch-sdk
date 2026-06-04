using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.WebRTC;
using UnityEngine;

namespace ODDGames.BugpunchSdk.RemoteIDE
{
    /// <summary>
    /// Manages WebRTC video streaming from Unity cameras to the dashboard.
    /// Signaling flows through the existing WebSocket tunnel — no extra server needed.
    ///
    /// Multi-peer: each dashboard viewer brings its own sessionId; this streamer
    /// holds one <see cref="RTCPeerConnection"/> per session in
    /// <see cref="_peers"/>. A single shared <see cref="RenderTexture"/> + camera
    /// capture pipeline feeds N <see cref="VideoStreamTrack"/> instances (one
    /// per peer), so adding a viewer costs an extra encoder pipeline but reuses
    /// the same GPU blit.
    ///
    /// IMPORTANT: This component is created lazily — only when a debug session starts.
    /// Do NOT add this component at startup. The act of loading this class triggers
    /// the Unity.WebRTC assembly load which loads the native libwebrtc.so. On some
    /// Android 15 devices that native load crashes if done too early.
    /// </summary>
    public class WebRTCStreamer : MonoBehaviour, IStreamer
    {
        // ------------------------------------------------------------------
        // Per-session peer state
        // ------------------------------------------------------------------
        sealed class PeerSession
        {
            public string SessionId;
            public RTCPeerConnection Pc;
            public VideoStreamTrack VideoTrack;
            public RTCDataChannel MetadataChannel;
            // Pending ICE candidates that arrived before SetRemoteDescription —
            // can't be added until the remote description is set, so we queue.
            public readonly List<RTCIceCandidate> PendingIce = new();
            public bool RemoteDescSet;
            // ABR state per peer — viewers may have wildly different links.
            public ulong AbrCurrentBps = ABR_INIT_BPS;
            public ulong AbrLastPacketsSent;
            public ulong AbrLastPacketsLost;
            public int AbrCleanTicks;
        }

        readonly Dictionary<string, PeerSession> _peers = new();
        readonly object _peersLock = new();

        // Shared capture pipeline — one RenderTexture feeds all peers.
        RenderTexture _rt;
        RenderTexture _screenCapRT;
        Camera _targetCamera;

        int _width = 1280;
        int _height = 720;
        int _fps = 30;
        // Pixel budget (longest-edge cap) and aspect source. Actual _width/_height
        // are derived from the device's current Screen.width/Screen.height
        // (game mode) or _overrideAspectW/H (scene mode) so the stream matches
        // the viewer 1:1 with no distortion.
        int _reqMaxEdge = 720;
        int _tierMaxEdge = 720;
        int _overrideAspectW;
        int _overrideAspectH;
        int _rtAspectW;
        int _rtAspectH;
        volatile bool _streaming; // true while at least one peer is alive

        readonly Queue<Action> _mainThreadQueue = new();
        RTCIceServer[] _iceServers;
        bool _applicationQuitting;
        Coroutine _webrtcUpdateCoroutine;
        Coroutine _renderLoop;
        Coroutine _abrLoop;

        // Time-based pacing — render loop yields every frame, but we only
        // actually blit / send at these cadences so a 60 fps game driving a
        // 30 fps stream doesn't burn GPU on wasted renders.
        float _nextBlitTimeUnscaled;
        float _nextMetadataTimeUnscaled;
        const float METADATA_INTERVAL_SECONDS = 0.1f;

        // Game-side perf counters piggybacked on the metadata channel.
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

        public SceneCameraService SceneCameraRef { get; set; }
        public IdeTunnel Tunnel { get; set; }

        // ------------------------------------------------------------------
        // Configuration / public API
        // ------------------------------------------------------------------

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

        public void Initialize(int width = 1280, int height = 720, int fps = 30)
        {
            _tierMaxEdge = ComputeMaxLongEdge();
            _reqMaxEdge = Mathf.Clamp(Mathf.Max(width, height), 160, _tierMaxEdge);
            _fps = Mathf.Clamp(fps, 1, 60);
            RecomputeDimensions();

            if (_webrtcUpdateCoroutine == null)
                _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        }

        static int ComputeMaxLongEdge()
        {
            int memMB = SystemInfo.systemMemorySize;
            int cpus = SystemInfo.processorCount;
            if (memMB >= 6144 && cpus >= 6) return 1440;
            if (memMB >= 3072 && cpus >= 4) return 1080;
            return 720;
        }

        public string SetQuality(int width, int height, int fps)
        {
            if (_tierMaxEdge <= 0) _tierMaxEdge = ComputeMaxLongEdge();
            _reqMaxEdge = Mathf.Clamp(Mathf.Max(width, height), 160, _tierMaxEdge);
            _fps = Mathf.Clamp(fps, 1, 60);
            RecomputeDimensions();
            BugpunchLog.Info("WebRTCStreamer", $"WebRTC: quality set to budget={_reqMaxEdge}px @ {_fps}fps → {_width}x{_height}");

            if (_streaming)
                ResizeRenderTarget();

            return $"{{\"ok\":true,\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"reconnectRequired\":false}}";
        }

        public string GetQuality()
        {
            return $"{{\"width\":{_width},\"height\":{_height},\"fps\":{_fps},\"streaming\":{(_streaming ? "true" : "false")},\"peers\":{PeerCount()}}}";
        }

        int PeerCount()
        {
            lock (_peersLock) { return _peers.Count; }
        }

        public void SetTargetAspect(int aspectWidth, int aspectHeight)
        {
            _overrideAspectW = Mathf.Max(0, aspectWidth);
            _overrideAspectH = Mathf.Max(0, aspectHeight);
            RecomputeDimensions();
            if (_streaming)
                ResizeRenderTarget();
        }

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
            w &= ~1; h &= ~1;
            _width = w;
            _height = h;
            _rtAspectW = aw;
            _rtAspectH = ah;
        }

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

        // ------------------------------------------------------------------
        // Signalling entry points
        // ------------------------------------------------------------------

        public void HandleSignalingMessage(string type, string sessionId, string payload)
        {
            switch (type)
            {
                case "webrtc-ice":
                    HandleIceCandidate(sessionId, payload);
                    break;
                case "webrtc-stop":
                    // Close only the named peer; other viewers keep streaming.
                    if (!string.IsNullOrEmpty(sessionId))
                        ClosePeer(sessionId);
                    break;
            }
        }

        // Legacy poll path retained for callers that haven't migrated to the
        // push-based iceCandidate frame yet. Returns an empty array — every
        // peer pushes its candidates straight through Tunnel.SendIceCandidate.
        public string DrainIceCandidates() => "[]";

        public void HandleOfferAsync(string sessionId, string sdpJson, Action<string> onAnswer, Action<string> onError)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                onError?.Invoke("missing sessionId");
                return;
            }
            StartCoroutine(HandleOffer(sessionId, sdpJson, onAnswer, onError));
        }

        IEnumerator HandleOffer(string sessionId, string sdpJson, Action<string> onAnswer, Action<string> onError)
        {
            // Wait one frame to let WebRTC.Update() coroutine initialize
            // (critical when Initialize() and HandleOffer run in the same frame)
            yield return null;

            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer sid={sessionId} sdpLen={sdpJson?.Length ?? 0}");
            float deadline = Time.realtimeSinceStartup + 30f;

            SdpMessage offerData;
            try
            {
                offerData = JsonUtility.FromJson<SdpMessage>(sdpJson);
                if (string.IsNullOrEmpty(offerData.sdp))
                {
                    onError?.Invoke("Empty SDP in offer");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Failed to parse offer: {ex.Message}");
                yield break;
            }

            // If a peer for this sessionId already exists (e.g. dashboard
            // tab reloaded with the same sessionId — shouldn't happen with
            // randomUUID, but guard), tear it down first.
            ClosePeer(sessionId);

            var session = new PeerSession { SessionId = sessionId };

            var config = new RTCConfiguration
            {
                iceServers = _iceServers ?? new[]
                {
                    new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
                }
            };

            session.Pc = new RTCPeerConnection(ref config);
            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer sid={sessionId}: RTCPeerConnection created");

            // Per-peer metadata data channel — each viewer sees its own
            // camera-metadata stream. Same shape as before, just one per peer.
            session.MetadataChannel = session.Pc.CreateDataChannel("camera-metadata", new RTCDataChannelInit());

            var tunnelRef = Tunnel;
            session.Pc.OnIceCandidate = candidate =>
            {
                if (candidate == null) return;
                // Lane-aware: managed → IdeTunnel WebSocket; iOS/Android → native tunnel.
                StreamSignal.SendIceCandidate(
                    tunnelRef,
                    sessionId,
                    candidate.Candidate,
                    candidate.SdpMid,
                    candidate.SdpMLineIndex ?? 0);
            };

            session.Pc.OnConnectionStateChange = state =>
            {
                BugpunchLog.Info("WebRTCStreamer", $"HandleOffer sid={sessionId}: connection state → {state}");
                if (state == RTCPeerConnectionState.Failed ||
                    state == RTCPeerConnectionState.Closed)
                {
                    lock (_mainThreadQueue)
                    {
                        _mainThreadQueue.Enqueue(() => ClosePeer(sessionId));
                    }
                }
            };

            // Ensure the shared capture pipeline is alive before adding the
            // first track to this peer.
            EnsureCapturePipeline();

            session.VideoTrack = new VideoStreamTrack(_rt);
            session.Pc.AddTrack(session.VideoTrack);

            // Initial cap before stats roll in — ABR loop adjusts from here.
            SetVideoMaxBitrateForPeer(session, ABR_INIT_BPS, (uint)_fps);

            // Register the peer BEFORE remote description so any iceCandidate
            // racing the offer can queue against it.
            lock (_peersLock) { _peers[sessionId] = session; }

            var offerDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offerData.sdp
            };
            var setRemoteOp = session.Pc.SetRemoteDescription(ref offerDesc);
            yield return setRemoteOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); ClosePeer(sessionId); yield break; }
            if (setRemoteOp.IsError)
            {
                onError?.Invoke(setRemoteOp.Error.message);
                ClosePeer(sessionId);
                yield break;
            }

            session.RemoteDescSet = true;
            // Flush any candidates that arrived before SetRemoteDescription.
            FlushPendingIce(session);

            var answerOp = session.Pc.CreateAnswer();
            yield return answerOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); ClosePeer(sessionId); yield break; }
            if (answerOp.IsError)
            {
                onError?.Invoke(answerOp.Error.message);
                ClosePeer(sessionId);
                yield break;
            }

            var answer = answerOp.Desc;
            var setLocalOp = session.Pc.SetLocalDescription(ref answer);
            yield return setLocalOp;

            if (Time.realtimeSinceStartup > deadline) { onError?.Invoke("Offer handling timed out"); ClosePeer(sessionId); yield break; }
            if (setLocalOp.IsError)
            {
                onError?.Invoke(setLocalOp.Error.message);
                ClosePeer(sessionId);
                yield break;
            }

            var answerJson = JsonUtility.ToJson(new SdpMessage { sdp = answer.sdp, type = "answer" });
            onAnswer?.Invoke(answerJson);

            _streaming = true;
            if (_renderLoop == null) _renderLoop = StartCoroutine(RenderLoop());
            if (_abrLoop == null) _abrLoop = StartCoroutine(AdaptiveBitrateLoop());

            BugpunchLog.Info("WebRTCStreamer", $"HandleOffer sid={sessionId}: streaming started, peerCount={PeerCount()}");
        }

        void HandleIceCandidate(string sessionId, string iceJson)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            PeerSession session;
            lock (_peersLock) { _peers.TryGetValue(sessionId, out session); }
            if (session == null)
            {
                BugpunchLog.Warn("WebRTCStreamer", $"HandleIceCandidate sid={sessionId}: no such peer");
                return;
            }

            IceMessage ice;
            try { ice = JsonUtility.FromJson<IceMessage>(iceJson); }
            catch { return; }

            var candidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = ice.candidate,
                sdpMid = ice.sdpMid,
                sdpMLineIndex = ice.sdpMLineIndex
            });

            if (!session.RemoteDescSet)
            {
                session.PendingIce.Add(candidate);
                return;
            }
            session.Pc.AddIceCandidate(candidate);
        }

        void FlushPendingIce(PeerSession session)
        {
            if (session.PendingIce.Count == 0) return;
            foreach (var c in session.PendingIce)
            {
                try { session.Pc.AddIceCandidate(c); } catch { /* invalid */ }
            }
            session.PendingIce.Clear();
        }

        // ------------------------------------------------------------------
        // Shared capture pipeline
        // ------------------------------------------------------------------

        void EnsureCapturePipeline()
        {
            if (_rt != null && _rt.width == _width && _rt.height == _height) return;
            // Drop the old RT — every peer's existing VideoStreamTrack keeps
            // its own RT reference until disposed; new peers grab the fresh
            // one below.
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            RecomputeDimensions();
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            _rt = new RenderTexture(_width, _height, 24, format);
            _rt.Create();
        }

        void ResizeRenderTarget()
        {
            if (_rt != null && _rt.width == _width && _rt.height == _height) return;

            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var newRt = new RenderTexture(_width, _height, 24, format);
            if (!newRt.Create())
            {
                BugpunchLog.Warn("WebRTCStreamer", "ResizeRenderTarget: RT.Create() failed");
                Destroy(newRt);
                return;
            }

            // For each peer, build a new VideoStreamTrack on the new RT and
            // ReplaceTrack on the existing sender. ReplaceTrack avoids a full
            // renegotiation so browsers keep streaming through the change.
            lock (_peersLock)
            {
                foreach (var session in _peers.Values)
                {
                    if (session.Pc == null) continue;
                    VideoStreamTrack newTrack;
                    try { newTrack = new VideoStreamTrack(newRt); }
                    catch (Exception ex)
                    {
                        BugpunchLog.Warn("WebRTCStreamer", $"ResizeRenderTarget sid={session.SessionId}: track create failed: {ex.Message}");
                        continue;
                    }
                    bool replaced = false;
                    foreach (var sender in session.Pc.GetSenders())
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
                        newTrack.Dispose();
                        continue;
                    }
                    var oldTrack = session.VideoTrack;
                    session.VideoTrack = newTrack;
                    if (oldTrack != null) oldTrack.Dispose();
                    SetVideoMaxBitrateForPeer(session, session.AbrCurrentBps, (uint)_fps);
                }
            }

            var oldRt = _rt;
            _rt = newRt;
            if (oldRt != null) { oldRt.Release(); Destroy(oldRt); }
            if (_screenCapRT != null)
            {
                _screenCapRT.Release();
                Destroy(_screenCapRT);
                _screenCapRT = null;
            }

            BugpunchLog.Info("WebRTCStreamer", $"ResizeRenderTarget: hot-swapped to {_width}x{_height} for {PeerCount()} peer(s)");
        }

        // ------------------------------------------------------------------
        // Render + metadata loop — runs while at least one peer is alive
        // ------------------------------------------------------------------

        IEnumerator RenderLoop()
        {
            int aspectCheckCounter = 0;
            _nextBlitTimeUnscaled = 0f;
            _nextMetadataTimeUnscaled = 0f;
            _frameAccum = 0;
            _frameAccumStart = Time.unscaledTime;
            while (_streaming && PeerCount() > 0)
            {
                yield return new WaitForEndOfFrame();
                _frameAccum++;
                if (_rt == null) continue;

                float now = Time.unscaledTime;

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

                if (now >= _nextMetadataTimeUnscaled)
                {
                    _nextMetadataTimeUnscaled = Mathf.Max(_nextMetadataTimeUnscaled + METADATA_INTERVAL_SECONDS, now);
                    var metaCam = _targetCamera ? _targetCamera : Camera.main;
                    string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                    float dtAccum = now - _frameAccumStart;
                    if (dtAccum > 0f && _frameAccum > 0)
                    {
                        float instantFps = _frameAccum / dtAccum;
                        _smoothedFps = _smoothedFps <= 0f ? instantFps : _smoothedFps * 0.7f + instantFps * 0.3f;
                    }
                    _frameAccum = 0;
                    _frameAccumStart = now;

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
                        catch { /* unsupported platform */ }
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
                    var msg = sb.ToString();

                    // Fan out to every peer's data channel that's open.
                    lock (_peersLock)
                    {
                        foreach (var s in _peers.Values)
                        {
                            if (s.MetadataChannel != null
                                && s.MetadataChannel.ReadyState == RTCDataChannelState.Open)
                            {
                                try { s.MetadataChannel.Send(msg); } catch { /* peer dying */ }
                            }
                        }
                    }
                }

                // Skip blit when no peer is in Connected state.
                bool anyConnected = false;
                lock (_peersLock)
                {
                    foreach (var s in _peers.Values)
                    {
                        if (s.Pc != null && s.Pc.ConnectionState == RTCPeerConnectionState.Connected)
                        {
                            anyConnected = true;
                            break;
                        }
                    }
                }
                if (!anyConnected) continue;

                if (now < _nextBlitTimeUnscaled) continue;
                float blitInterval = 1f / Mathf.Max(1, _fps);
                _nextBlitTimeUnscaled = Mathf.Max(_nextBlitTimeUnscaled + blitInterval, now);

                if (_targetCamera != null)
                {
                    _targetCamera.targetTexture = _rt;
                    _targetCamera.Render();
                    _targetCamera.targetTexture = null;
                }
                else
                {
                    if (_screenCapRT == null || _screenCapRT.width != Screen.width || _screenCapRT.height != Screen.height)
                    {
                        if (_screenCapRT != null) { _screenCapRT.Release(); Destroy(_screenCapRT); }
                        _screenCapRT = new RenderTexture(Screen.width, Screen.height, 0);
                        _screenCapRT.Create();
                    }
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenCapRT);
                    Graphics.Blit(_screenCapRT, _rt, new Vector2(1, -1), new Vector2(0, 1));
                }
            }

            _renderLoop = null;
        }

        // ------------------------------------------------------------------
        // Per-peer adaptive bitrate
        // ------------------------------------------------------------------

        const float ABR_INTERVAL_S = 2.0f;
        const ulong ABR_MIN_BPS = 200_000;
        const ulong ABR_MAX_BPS = 6_000_000;
        const ulong ABR_INIT_BPS = 2_500_000;

        IEnumerator AdaptiveBitrateLoop()
        {
            var wait = new WaitForSeconds(ABR_INTERVAL_S);
            while (_streaming && PeerCount() > 0)
            {
                yield return wait;
                if (!_streaming) break;

                // Snapshot peers under lock; tick each outside the lock so the
                // GetStats yield doesn't hold contention.
                List<PeerSession> snap;
                lock (_peersLock) { snap = new List<PeerSession>(_peers.Values); }
                foreach (var session in snap)
                {
                    if (session.Pc == null) continue;
                    var op = session.Pc.GetStats();
                    yield return op;
                    if (op.IsError) continue;
                    var report = op.Value;
                    if (report == null) continue;

                    ulong packetsSent = 0, packetsLost = 0;
                    foreach (var s in report.Stats.Values)
                    {
                        if (s is RTCOutboundRTPStreamStats out_ && out_.kind == "video")
                            packetsSent = (ulong)out_.packetsSent;
                        else if (s is RTCInboundRTPStreamStats rin && rin.kind == "video")
                            packetsLost = (ulong)rin.packetsLost;
                    }

                    if (packetsSent == 0 || packetsSent <= session.AbrLastPacketsSent)
                    {
                        session.AbrLastPacketsSent = packetsSent;
                        session.AbrLastPacketsLost = packetsLost;
                        continue;
                    }
                    ulong sentDelta = packetsSent - session.AbrLastPacketsSent;
                    ulong lostDelta = packetsLost > session.AbrLastPacketsLost ? packetsLost - session.AbrLastPacketsLost : 0;
                    session.AbrLastPacketsSent = packetsSent;
                    session.AbrLastPacketsLost = packetsLost;
                    if (sentDelta == 0) continue;

                    float lossRatio = (float)lostDelta / (float)sentDelta;
                    ulong newBps = session.AbrCurrentBps;

                    if (lossRatio > 0.05f) {
                        newBps = (ulong)System.Math.Max(ABR_MIN_BPS, session.AbrCurrentBps / 2);
                        session.AbrCleanTicks = 0;
                    } else if (lossRatio > 0.02f) {
                        newBps = (ulong)System.Math.Max(ABR_MIN_BPS, (session.AbrCurrentBps * 4) / 5);
                        session.AbrCleanTicks = 0;
                    } else if (lossRatio < 0.005f) {
                        session.AbrCleanTicks++;
                        if (session.AbrCleanTicks >= 3) {
                            newBps = (ulong)System.Math.Min(ABR_MAX_BPS, (session.AbrCurrentBps * 23) / 20);
                            session.AbrCleanTicks = 0;
                        }
                    } else {
                        session.AbrCleanTicks = 0;
                    }

                    if (newBps != session.AbrCurrentBps) {
                        session.AbrCurrentBps = newBps;
                        SetVideoMaxBitrateForPeer(session, newBps, (uint)_fps);
                    }
                }
            }
            _abrLoop = null;
        }

        public void SetVideoMaxBitrate(ulong bps, uint maxFps)
        {
            // Convenience for back-compat callers — apply to every peer.
            lock (_peersLock)
            {
                foreach (var s in _peers.Values) SetVideoMaxBitrateForPeer(s, bps, maxFps);
            }
        }

        void SetVideoMaxBitrateForPeer(PeerSession session, ulong bps, uint maxFps)
        {
            if (session?.Pc == null) return;
            foreach (var sender in session.Pc.GetSenders())
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
                    BugpunchLog.Warn("WebRTCStreamer", $"SetParameters failed sid={session.SessionId}: {err.message}");
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle — per-peer close + global stop
        // ------------------------------------------------------------------

        void ClosePeer(string sessionId)
        {
            PeerSession session;
            lock (_peersLock)
            {
                if (!_peers.TryGetValue(sessionId, out session)) return;
                _peers.Remove(sessionId);
            }
            try
            {
                if (session.MetadataChannel != null)
                {
                    session.MetadataChannel.Close();
                    session.MetadataChannel.Dispose();
                }
            }
            catch { /* ignore */ }
            try
            {
                if (session.Pc != null)
                {
                    if (session.VideoTrack != null)
                    {
                        foreach (var sender in session.Pc.GetSenders())
                        {
                            if (sender.Track == session.VideoTrack)
                            {
                                session.Pc.RemoveTrack(sender);
                                break;
                            }
                        }
                    }
                    session.Pc.Close();
                    session.Pc.Dispose();
                }
            }
            catch { /* ignore */ }
            try { session.VideoTrack?.Dispose(); } catch { /* ignore */ }

            BugpunchLog.Info("WebRTCStreamer", $"Closed peer sid={sessionId}, remaining={PeerCount()}");

            // If that was the last peer, stop the global capture pipeline.
            if (PeerCount() == 0)
            {
                _streaming = false;
                CleanupRenderTexture();
                StopPerfRecorders();
            }
        }

        public void StopStreaming()
        {
            _streaming = false;
            List<string> ids;
            lock (_peersLock) { ids = new List<string>(_peers.Keys); }
            foreach (var id in ids) ClosePeer(id);
            CleanupRenderTexture();
            StopPerfRecorders();
            BugpunchLog.Info("WebRTCStreamer", "WebRTC: streaming stopped (all peers closed)");
        }

        void StopPerfRecorders()
        {
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
        }

        void CleanupRenderTexture()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_screenCapRT != null)
            {
                _screenCapRT.Release();
                Destroy(_screenCapRT);
                _screenCapRT = null;
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
            if (_applicationQuitting) return; // skip cleanup during app quit
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
