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
        /// Called from BugpunchClient when a webrtc-* message arrives.
        /// </summary>
        public void HandleSignalingMessage(string type, string sessionId, string payload)
        {
            switch (type)
            {
                case "webrtc-offer":
                    StartCoroutine(HandleOffer(sessionId, payload));
                    break;
                case "webrtc-answer":
                    StartCoroutine(HandleAnswer(payload));
                    break;
                case "webrtc-ice":
                    HandleIceCandidate(payload);
                    break;
                case "webrtc-stop":
                    StopStreaming();
                    break;
            }
        }

        IEnumerator HandleOffer(string sessionId, string sdpJson)
        {
            Debug.Log("[Bugpunch] WebRTC: received offer, creating answer...");

            // Parse the offer SDP
            var offerData = JsonUtility.FromJson<SdpMessage>(sdpJson);

            // Cleanup previous connection
            CleanupPeerConnection();

            // Create peer connection
            var config = new RTCConfiguration
            {
                iceServers = new[]
                {
                    new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
                }
            };

            _pc = new RTCPeerConnection(ref config);

            // ICE candidate handler — send back through tunnel
            _pc.OnIceCandidate = candidate =>
            {
                if (candidate == null) return;
                var iceJson = JsonUtility.ToJson(new IceMessage
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                });
                _tunnel?.SendResponse(sessionId, 200,
                    $"{{\"type\":\"webrtc-ice\",\"payload\":{iceJson}}}",
                    "application/json");
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
                yield break;
            }

            // Create answer
            var answerOp = _pc.CreateAnswer();
            yield return answerOp;

            if (answerOp.IsError)
            {
                Debug.LogError($"[Bugpunch] WebRTC: CreateAnswer failed: {answerOp.Error.message}");
                yield break;
            }

            var answer = answerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answer);
            yield return setLocalOp;

            if (setLocalOp.IsError)
            {
                Debug.LogError($"[Bugpunch] WebRTC: SetLocalDescription failed: {setLocalOp.Error.message}");
                yield break;
            }

            // Send answer back through tunnel
            var answerJson = JsonUtility.ToJson(new SdpMessage { sdp = answer.sdp, type = "answer" });
            _tunnel?.SendResponse(sessionId, 200,
                $"{{\"type\":\"webrtc-answer\",\"payload\":{answerJson}}}",
                "application/json");

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
