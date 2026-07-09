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
    [UnityEngine.Scripting.Preserve]
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

        // Cached yield instruction — the render loop yields every frame, so
        // `yield return new WaitForEndOfFrame()` would allocate one object per
        // frame for the whole stream. WaitForEndOfFrame is stateless, so a
        // single shared instance is safe to reuse every iteration.
        static readonly WaitForEndOfFrame s_waitForEndOfFrame = new WaitForEndOfFrame();

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

        // Reused StringBuilder for the per-tick metadata JSON. The build runs
        // single-threaded on the main thread (the render loop) — _peersLock
        // guards only the fan-out send below, not the build — so a field is
        // safe. Cleared at the top of each build. At 10 Hz while streaming this
        // saves a 256-char buffer allocation per tick for the stream's life.
        readonly System.Text.StringBuilder _metaSb = new System.Text.StringBuilder(256);

        // ── Thermal throttle ──
        // The live streamer caps fps + resolution under thermal pressure so it
        // stops cooking the device while a viewer is watching. Mirrors the native
        // BugpunchFpsGovernor tiers (the crash recorder's throttle) so both
        // subsystems shed encode load the same way. Tier is read from native each
        // poll (NSProcessInfo.thermalState / PowerManager thermal status); it is
        // always 0 on the managed lane (no portable thermal API → never throttles
        // on desktop). Surfaced live to the dashboard HUD via the metadata channel
        // ("thrm" = tier, "tfps" = the fps cap currently in force).
        int _thermalTier;             // 0 nominal · 1 fair · 2 serious · 3 critical
        int _thermalFpsCap = 60;      // fps ceiling implied by the tier
        float _thermalEdgeScale = 1f; // resolution scale implied by the tier
        float _nextThermalPollUnscaled;
        const float THERMAL_POLL_SECONDS = 2f;

        // Effective stream fps after thermal capping — drives both the blit
        // cadence (fewer captures+encodes when hot) and the encoder rate hint.
        int EffectiveFps() => Mathf.Clamp(Mathf.Min(_fps, _thermalFpsCap), 1, 60);

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
            if (string.IsNullOrEmpty(json)) return;

            // Parsed with Newtonsoft (not JsonUtility) because the payload varies
            // on two axes and JsonUtility is too rigid for either:
            //   • Top-level shape — HTTP /api/devices/ice-servers returns an object
            //     {"iceServers":[...]}, while the native poll fold (Android/iOS
            //     OnIceServers) sends a bare array [...]. JsonUtility.FromJson throws
            //     "JSON must represent an object type" on a top-level array.
            //   • urls field — the server types it `string | string[]`; a single
            //     string field can't hold both, and JsonUtility silently drops the
            //     array case (→ empty url).
            Newtonsoft.Json.Linq.JArray entries;
            try
            {
                var root = Newtonsoft.Json.Linq.JToken.Parse(json);
                entries = root as Newtonsoft.Json.Linq.JArray
                          ?? root["iceServers"] as Newtonsoft.Json.Linq.JArray;
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("WebRTCStreamer", $"SetIceServersFromJson: parse failed ({ex.Message}) — using default STUN");
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                BugpunchLog.Info("WebRTCStreamer", "SetIceServersFromJson: no ICE servers, using default STUN");
                return;
            }

            var servers = new List<RTCIceServer>(entries.Count);
            foreach (var entry in entries)
            {
                var urlsToken = entry["urls"];
                string[] urls;
                if (urlsToken is Newtonsoft.Json.Linq.JArray urlsArray)
                {
                    var list = new List<string>(urlsArray.Count);
                    foreach (var u in urlsArray)
                    {
                        var s = (string)u;
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                    urls = list.ToArray();
                }
                else
                {
                    var s = (string)urlsToken;
                    urls = string.IsNullOrEmpty(s) ? Array.Empty<string>() : new[] { s };
                }

                if (urls.Length == 0) continue;

                servers.Add(new RTCIceServer
                {
                    urls = urls,
                    username = (string)entry["username"] ?? "",
                    credential = (string)entry["credential"] ?? ""
                });
            }

            if (servers.Count == 0)
            {
                BugpunchLog.Info("WebRTCStreamer", "SetIceServersFromJson: no usable ICE servers, using default STUN");
                return;
            }

            SetIceServers(servers.ToArray());
        }

        public void Initialize(int width = 1280, int height = 720, int fps = 30)
        {
            _tierMaxEdge = ComputeMaxLongEdge();
            _reqMaxEdge = Mathf.Clamp(Mathf.Max(width, height), 160, _tierMaxEdge);
            _fps = Mathf.Clamp(fps, 1, 60);
            RecomputeDimensions();
            // The WebRTC.Update() operation pump is NOT started here — it costs a
            // native libwebrtc call every frame, and with eager init this
            // component exists from tunnel connect. HandleOffer starts it for the
            // first peer; it stops when the last peer closes.
        }

        static int ComputeMaxLongEdge()
        {
            // Long-edge ceiling for the live Remote IDE stream, capped at 720 on
            // every tier so the live view matches the 720 crash recording. Encode
            // cost scales with pixels×fps; a debug stream doesn't need native res.
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

            // Thermal throttle shrinks the long edge on top of the requested
            // budget (0.66× serious, 0.5× critical) — cuts capture-blit and
            // encode cost roughly with the square of the scale.
            int longEdge = Mathf.Max(160, Mathf.RoundToInt(_reqMaxEdge * _thermalEdgeScale));
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

            // (Re)start the WebRTC.Update() operation pump — it only runs while
            // peers are alive (last-peer close stops it, including the stale
            // ClosePeer just above), and SetRemoteDescription / CreateAnswer
            // below only resolve while it ticks. Give it one frame to spin up
            // before creating the peer connection.
            if (_webrtcUpdateCoroutine == null)
            {
                _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
                yield return null;
            }

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
            SetVideoMaxBitrateForPeer(session, ABR_INIT_BPS, (uint)EffectiveFps());

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
            // Pause the native perf monitor's FPS sampling while we stream —
            // the WebRTC encode load tanks the frame rate, and that's a
            // debugging artefact, not the player experience. Without this the
            // tester's degraded FPS trips false "Low FPS" problems on the
            // dashboard. Cleared when the last peer closes / StopStreaming.
            BugpunchNative.SetPerfInstrumented("stream", true);
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
                    SetVideoMaxBitrateForPeer(session, session.AbrCurrentBps, (uint)EffectiveFps());
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

        // Poll native thermal state and recompute the fps/resolution caps.
        // Self-throttled to THERMAL_POLL_SECONDS so callers can invoke it every
        // render-loop iteration cheaply. Only touches the GPU (resize) when the
        // tier actually changes, which is rare.
        void PollThermalThrottle(float now)
        {
            if (now < _nextThermalPollUnscaled) return;
            _nextThermalPollUnscaled = now + THERMAL_POLL_SECONDS;

            int tier = BugpunchNative.GetThermalTier();
            if (tier == _thermalTier) return;
            _thermalTier = tier;

            int fpsCap; float edge;
            switch (tier)
            {
                case 3:  fpsCap = 10; edge = 0.50f; break; // critical
                case 2:  fpsCap = 15; edge = 0.66f; break; // serious
                case 1:  fpsCap = 24; edge = 0.85f; break; // fair → proactive gentle
                                                           // back-off, mirrors the
                                                           // native governor (24fps,
                                                           // 0.8x) so the device
                                                           // doesn't climb to serious
                default: fpsCap = 60; edge = 1.00f; break; // nominal → no cap
            }

            bool edgeChanged = !Mathf.Approximately(edge, _thermalEdgeScale);
            _thermalFpsCap = fpsCap;
            _thermalEdgeScale = edge;
            BugpunchLog.Info("WebRTCStreamer",
                $"thermal tier={tier} → fpsCap={fpsCap} edge×{edge:0.00} (configured {_fps}fps/{_reqMaxEdge}px)");

            if (edgeChanged && _streaming)
            {
                RecomputeDimensions();
                ResizeRenderTarget();
            }
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
                yield return s_waitForEndOfFrame;
                _frameAccum++;
                if (_rt == null) continue;

                float now = Time.unscaledTime;

                // Adjust fps/resolution caps to current thermal pressure
                // (self-throttled to THERMAL_POLL_SECONDS internally).
                PollThermalThrottle(now);

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

                // Skip metadata + blit while no peer is Connected — peers that
                // are still negotiating (or stale) would otherwise burn the
                // 10 Hz JSON build, 7 ProfilerRecorder reads and the JNI touch
                // fetch with nobody able to receive any of it.
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

                if (now >= _nextMetadataTimeUnscaled)
                {
                    _nextMetadataTimeUnscaled = Mathf.Max(_nextMetadataTimeUnscaled + METADATA_INTERVAL_SECONDS, now);
                    var metaCam = _targetCamera ? _targetCamera : Camera.main;

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

                    // Build into the reused field buffer with chained
                    // literal+value appends — the old `Append($"...")`
                    // shape materialised an interim composite string per field,
                    // defeating the StringBuilder. FLOAT fields go through
                    // BugpunchJson.AppendFixed (invariant "F2") — DON'T
                    // Append(float) directly, that uses the current culture and
                    // would emit a comma decimal under a comma-decimal locale,
                    // breaking the JSON. LONG/INT counters use Append(value)
                    // directly: Append(long/int) is culture-invariant for
                    // integers (no separators), so it's byte-identical.
                    _metaSb.Clear();
                    _metaSb.Append('{');
                    if (metaCam != null)
                    {
                        var ct = metaCam.transform;
                        var cp = ct.position;
                        var cr = ct.eulerAngles;
                        _metaSb.Append("\"px\":"); BugpunchJson.AppendFixed(_metaSb, cp.x, 2);
                        _metaSb.Append(",\"py\":"); BugpunchJson.AppendFixed(_metaSb, cp.y, 2);
                        _metaSb.Append(",\"pz\":"); BugpunchJson.AppendFixed(_metaSb, cp.z, 2);
                        _metaSb.Append(",\"rx\":"); BugpunchJson.AppendFixed(_metaSb, cr.x, 2);
                        _metaSb.Append(",\"ry\":"); BugpunchJson.AppendFixed(_metaSb, cr.y, 2);
                        _metaSb.Append(",\"rz\":"); BugpunchJson.AppendFixed(_metaSb, cr.z, 2);
                        _metaSb.Append(',');
                    }
                    _metaSb.Append("\"fps\":"); BugpunchJson.AppendFixed(_metaSb, _smoothedFps, 2); _metaSb.Append(',');
                    if (dc >= 0) _metaSb.Append("\"dc\":").Append(dc).Append(',');
                    if (batches >= 0) _metaSb.Append("\"b\":").Append(batches).Append(',');
                    if (setpass >= 0) _metaSb.Append("\"sp\":").Append(setpass).Append(',');
                    if (tris >= 0) _metaSb.Append("\"tri\":").Append(tris).Append(',');
                    if (verts >= 0) _metaSb.Append("\"vrt\":").Append(verts).Append(',');
                    if (totalMem >= 0) _metaSb.Append("\"mem\":").Append(totalMem).Append(',');
                    if (gcMem >= 0) _metaSb.Append("\"gc\":").Append(gcMem).Append(',');
                    // Thermal tier + the fps cap it's currently forcing, so the
                    // dashboard HUD can show "throttled" the moment the device
                    // heats up (and confirm the throttle is actually engaging).
                    _metaSb.Append("\"thrm\":").Append(_thermalTier).Append(',');
                    _metaSb.Append("\"tfps\":").Append(EffectiveFps()).Append(',');
                    var touchJson = RequestRouter.GetLiveTouchesForStream(500);
                    _metaSb.Append(touchJson);
                    _metaSb.Append('}');
                    var msg = _metaSb.ToString();

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

                if (now < _nextBlitTimeUnscaled) continue;
                float blitInterval = 1f / Mathf.Max(1, EffectiveFps());
                _nextBlitTimeUnscaled = Mathf.Max(_nextBlitTimeUnscaled + blitInterval, now);

                // Capture-path selection — DON'T re-render a camera the GPU has
                // already drawn this frame:
                //  • A target camera that does NOT draw itself (managed-lane scene
                //    cam, enabled=false) → the streamer is its only renderer, so
                //    render it into the stream RT.
                //  • Everything else — game view (no target cam) OR a camera that
                //    already auto-renders to the screen (native scene cam,
                //    enabled=true) → copy the backbuffer the GPU already produced.
                //    Re-rendering an on-screen camera would be a redundant SECOND
                //    full scene pass (pure heat). The old assumption that native
                //    streamed the device screen via MediaProjection is retired —
                //    the C# streamer is the capture path on every lane now, so an
                //    enabled scene cam must be copied, not re-rendered.
                if (_targetCamera != null && !_targetCamera.enabled)
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
                    // RTCStatsReport wraps native memory and is never collected
                    // by the GC — undisposed reports are a known Unity.WebRTC
                    // leak at one report per peer per ABR tick.
                    report.Dispose();

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
                        SetVideoMaxBitrateForPeer(session, newBps, (uint)EffectiveFps());
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
                // Last viewer gone — resume perf FPS sampling.
                BugpunchNative.SetPerfInstrumented("stream", false);
                StopStreamLoops();
                CleanupRenderTexture();
                StopPerfRecorders();
            }
        }

        /// <summary>
        /// Stop the render/ABR loops and the WebRTC.Update() operation pump —
        /// idle peers-gone state must not keep a per-frame native libwebrtc
        /// call alive. The loops are stopped BEFORE the pump: the ABR loop can
        /// be parked on a GetStats yield that only completes while the pump
        /// ticks; killing the pump first would strand that coroutine (it never
        /// resumes, never nulls _abrLoop, and blocks the next stream's
        /// restart guard).
        /// </summary>
        void StopStreamLoops()
        {
            if (_abrLoop != null) { StopCoroutine(_abrLoop); _abrLoop = null; }
            if (_renderLoop != null) { StopCoroutine(_renderLoop); _renderLoop = null; }
            if (_webrtcUpdateCoroutine != null)
            {
                StopCoroutine(_webrtcUpdateCoroutine);
                _webrtcUpdateCoroutine = null;
            }
        }

        public void StopStreaming()
        {
            _streaming = false;
            // Resume perf FPS sampling now that streaming is torn down.
            BugpunchNative.SetPerfInstrumented("stream", false);
            List<string> ids;
            lock (_peersLock) { ids = new List<string>(_peers.Keys); }
            foreach (var id in ids) ClosePeer(id);
            StopStreamLoops();
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
    }
}
