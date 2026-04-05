#if BUGPUNCH_WEBRTC
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
    /// </summary>
    public class WebRTCStreamer : MonoBehaviour
    {
        RTCPeerConnection _pc;
        VideoStreamTrack _videoTrack;
        RenderTexture _rt;
        Camera _targetCamera;

        int _width = 1280;
        int _height = 720;
        int _fps = 30;
        bool _streaming;

        TunnelClient _tunnel;
        readonly Queue<Action> _mainThreadQueue = new();
        RTCIceServer[] _iceServers;

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
        /// Initialize the streamer with a tunnel client for signaling.
        /// </summary>
        public void Initialize(TunnelClient tunnel, int width = 1280, int height = 720, int fps = 30)
        {
            _tunnel = tunnel;
            _width = width;
            _height = height;
            _fps = fps;

            // Initialize WebRTC
            StartCoroutine(WebRTC.Update());
        }

        /// <summary>
        /// Set which camera to stream from. Defaults to Camera.main.
        /// </summary>
        public void SetCamera(Camera cam)
        {
            _targetCamera = cam;

            // Recreate render texture if streaming
            if (_streaming)
            {
                CleanupRenderTexture();
                CreateRenderTexture();
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
                    StopStreaming();
                    break;
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
            if (_streaming)
                StopStreaming();

            Debug.Log("[Bugpunch] WebRTC: received offer, creating answer...");

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

            // Cleanup previous connection
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

            // ICE candidate handler — queue for later retrieval
            _pc.OnIceCandidate = candidate =>
            {
                if (candidate == null) return;
                // ICE candidates are sent via separate requests from the browser
                // Device-side candidates are currently not forwarded back
                // TODO: implement device→browser ICE via tunnel push
            };

            _pc.OnConnectionStateChange = state =>
            {
                Debug.Log($"[Bugpunch] WebRTC connection state: {state}");
                if (state == RTCPeerConnectionState.Disconnected ||
                    state == RTCPeerConnectionState.Failed ||
                    state == RTCPeerConnectionState.Closed)
                {
                    _streaming = false;
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

        IEnumerator HandleAnswer(string sdpJson)
        {
            if (_pc == null) yield break;

            var answerData = JsonUtility.FromJson<SdpMessage>(sdpJson);
            var desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = answerData.sdp
            };
            var op = _pc.SetRemoteDescription(ref desc);
            yield return op;

            if (op.IsError)
                Debug.LogError($"[Bugpunch] WebRTC: SetRemoteDescription (answer) failed: {op.Error.message}");
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
        /// </summary>
        IEnumerator RenderLoop()
        {
            while (_streaming && _pc != null)
            {
                yield return new WaitForEndOfFrame();

                var cam = _targetCamera ? _targetCamera : Camera.main;
                if (cam == null || _rt == null) continue;

                var prevTarget = cam.targetTexture;
                cam.targetTexture = _rt;
                cam.Render();
                cam.targetTexture = prevTarget;
            }
        }

        public void StopStreaming()
        {
            _streaming = false;
            CleanupPeerConnection();
            CleanupRenderTexture();
            Debug.Log("[Bugpunch] WebRTC: streaming stopped");
        }

        void CleanupPeerConnection()
        {
            if (_videoTrack != null)
            {
                _videoTrack.Dispose();
                _videoTrack = null;
            }
            if (_pc != null)
            {
                _pc.Close();
                _pc.Dispose();
                _pc = null;
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

        void OnDestroy()
        {
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
#endif
