using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Single API the rest of the SDK uses to talk to the WebSocket tunnel,
    /// regardless of platform. On Android/iOS the native <c>BugpunchTunnel</c>
    /// owns the socket end-to-end and this class forwards to JNI / P-Invoke.
    /// In the Editor and other non-mobile builds we fall back to the managed
    /// <see cref="TunnelClient"/> instance held by <see cref="BugpunchClient"/>.
    ///
    /// <para>Why a bridge: callers should never need to know which side is on
    /// the wire. Putting all the platform splitting here keeps
    /// <c>BugpunchClient.cs</c> from blooming into a forest of <c>#if</c>s and
    /// matches the rule "if you want to send something through the tunnel
    /// you call native".</para>
    /// </summary>
    public static class TunnelBridge
    {
        /// <summary>True if the underlying tunnel has an open WebSocket.</summary>
        public static bool IsConnected
        {
            get
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return BugpunchNative.TunnelIsConnected();
#else
                return BugpunchClient.Instance?.Tunnel?.IsConnected ?? false;
#endif
            }
        }

        /// <summary>
        /// Persistent device id the tunnel registered under. Empty if unstarted.
        /// </summary>
        public static string DeviceId
        {
            get
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return BugpunchNative.TunnelDeviceId();
#else
                return BugpunchClient.Instance?.Tunnel?.DeviceId ?? "";
#endif
            }
        }

        /// <summary>
        /// Send a JSON text response back to the server for an in-flight
        /// request. Builds the standard envelope and ships it through whichever
        /// transport this build uses.
        /// </summary>
        public static void SendResponse(string requestId, int status, string body, string contentType = "application/json")
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            var msg = JsonUtility.ToJson(new ResponseEnvelope
            {
                type = "response",
                requestId = requestId,
                status = status,
                body = body ?? "",
                contentType = contentType ?? "application/json",
                isBase64 = false,
            });
            BugpunchNative.TunnelSendResponse(msg);
#else
            BugpunchClient.Instance?.Tunnel?.SendResponse(requestId, status, body, contentType);
#endif
        }

        /// <summary>
        /// Send a binary response (base64-encoded over the wire because the
        /// envelope is JSON). Used for screenshots / material thumbnails / etc.
        /// </summary>
        public static void SendBinaryResponse(string requestId, byte[] binary, string contentType)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            var base64 = binary != null ? Convert.ToBase64String(binary) : "";
            var msg = JsonUtility.ToJson(new ResponseEnvelope
            {
                type = "response",
                requestId = requestId,
                status = 200,
                body = base64,
                contentType = contentType ?? "application/octet-stream",
                isBase64 = true,
            });
            BugpunchNative.TunnelSendResponse(msg);
#else
            BugpunchClient.Instance?.Tunnel?.SendBinaryResponse(requestId, binary, contentType);
#endif
        }

        [Serializable]
        class ResponseEnvelope
        {
            public string type;
            public string requestId;
            public int status;
            public string body;
            public string contentType;
            public bool isBase64;
        }
    }
}
