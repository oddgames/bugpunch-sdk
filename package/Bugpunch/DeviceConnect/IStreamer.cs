using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Interface for the WebRTC video streamer. Lives in the main assembly so
    /// BugpunchClient can interact with streaming without directly referencing
    /// Unity.WebRTC types (which would trigger native lib loading at startup).
    ///
    /// The concrete implementation (WebRTCStreamer) lives in a separate assembly
    /// (ODDGames.Bugpunch.WebRTC) that is loaded on demand.
    /// </summary>
    public interface IStreamer
    {
        /// <summary>
        /// Initialize the streamer with connection info for signaling.
        /// </summary>
        void Initialize(TunnelClient tunnel, int width, int height, int fps);

        /// <summary>
        /// Configure ICE servers from raw JSON (avoids exposing RTCIceServer type).
        /// Expected: { "iceServers": [{ "urls": "...", "username": "...", "credential": "..." }] }
        /// </summary>
        void SetIceServersFromJson(string json);

        /// <summary>Set which camera to stream from.</summary>
        void SetCamera(Camera cam);

        /// <summary>Change stream resolution/fps at runtime.</summary>
        string SetQuality(int width, int height, int fps);

        /// <summary>Get current quality settings as JSON.</summary>
        string GetQuality();

        /// <summary>Get queued device-side ICE candidates as JSON array.</summary>
        string DrainIceCandidates();

        /// <summary>Handle a WebRTC offer asynchronously.</summary>
        void HandleOfferAsync(string sdpJson, Action<string> onAnswer, Action<string> onError);

        /// <summary>Handle ICE candidate or stop message.</summary>
        void HandleSignalingMessage(string type, string sessionId, string payload);

        /// <summary>Stop streaming and clean up peer connection.</summary>
        void StopStreaming();

        /// <summary>Reference to scene camera service for metadata.</summary>
        SceneCameraService SceneCameraRef { get; set; }
    }
}
