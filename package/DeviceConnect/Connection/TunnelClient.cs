#if !(UNITY_ANDROID || UNITY_IOS) || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Managed WebSocket client for the Editor / Standalone path. On Android
    /// and iOS the native <c>BugpunchTunnel</c> owns the wire and this class
    /// is excluded from the build entirely — cross-platform code uses
    /// <see cref="TunnelBridge"/>.
    /// </summary>
    public class TunnelClient
    {
        readonly BugpunchConfig _config;
        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        // Managed state backing fields — set only by the Editor-path Connect.
        // On device the getters below read directly from native so every
        // consumer of Tunnel.IsConnected / .DeviceId sees the real state.
        bool _managedConnected;
        bool _managedConnecting;
        string _managedDeviceId;

        public bool IsConnected
        {
            get
            {
#if UNITY_EDITOR
                return _managedConnected;
#else
                return BugpunchNative.TunnelIsConnected();
#endif
            }
            private set { _managedConnected = value; }
        }

        public bool IsConnecting
        {
            get
            {
#if UNITY_EDITOR
                return _managedConnecting;
#else
                return false;   // native tunnel is either connected or in backoff
#endif
            }
            private set { _managedConnecting = value; }
        }

        public string DeviceId
        {
            get
            {
#if UNITY_EDITOR
                return _managedDeviceId;
#else
                return BugpunchNative.TunnelDeviceId();
#endif
            }
            private set { _managedDeviceId = value; }
        }

        /// <summary>
        /// Signed pin config received in the last register ack. Null until a
        /// successful registration completes. Consumed by the pin-enforcement
        /// pipeline (Phase 3c) which verifies the HMAC, stores to native
        /// cache, and applies the flags.
        /// </summary>
        public string PinConfigJson { get; private set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        // JSON of the signed pin config from the registered ack / pinUpdate frames
        public event Action<string> OnPinConfig;

        // requestId, method, path, body
        public event Action<string, string, string, string> OnRequest;

        public TunnelClient(BugpunchConfig config)
        {
            _config = config;
        }

        public void Connect()
        {
            if (IsConnected || IsConnecting) return;
            _ = RunReconnectLoop();
        }

        async Task RunReconnectLoop()
        {
            int attempt = 0;
            while (true)
            {
                if (IsConnected) return;

                IsConnecting = true;
                try
                {
                    _ws = new ClientWebSocket();
                    _cts = new CancellationTokenSource();

                    var uri = new Uri(_config.TunnelUrl);
                    Debug.Log($"[Bugpunch.TunnelClient] Connecting to {uri}...");

                    await _ws.ConnectAsync(uri, _cts.Token);

                    IsConnected = true;
                    IsConnecting = false;
                    attempt = 0; // reset backoff

                    if (string.IsNullOrEmpty(DeviceId))
                        DeviceId = DeviceIdentity.GetDeviceId();

                    var registerMsg = JsonUtility.ToJson(new RegisterMessage
                    {
                        type = "register",
                        name = _config.EffectiveDeviceName,
                        platform = Application.platform.ToString(),
                        appVersion = Application.version,
                        remoteIdePort = 0,
                        token = _config.apiKey,
                        projectId = "",
                        deviceId = DeviceId,
                        stableDeviceId = BugpunchNative.GetStableDeviceId(),
                        buildChannel = _config.buildChannel.ToString().ToLowerInvariant(),
                    });
                    await SendAsync(registerMsg);

                    _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());

                    await Task.WhenAny(ReceiveLoop(), HeartbeatLoop());
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException;
                    var innerStr = inner != null ? $" — inner {inner.GetType().Name}: {inner.Message}" : "";
                    Debug.LogWarning($"[Bugpunch.TunnelClient] Connect/reconnect failed: {ex.GetType().Name}: {ex.Message}{innerStr}");
                    _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message));
                }

                // connection dropped
                IsConnected = false;
                IsConnecting = false;
                _mainThreadQueue.Enqueue(() => OnDisconnected?.Invoke());

                // exponential backoff (max 10s)
                attempt++;
                int delayMs = Math.Min(1000 * (int)Math.Pow(2, attempt - 1), 10000);
                await Task.Delay(delayMs);
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            IsConnecting = false;
            _cts?.Cancel();

            try
            {
                if (_ws?.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }
            catch { }

            _ws?.Dispose();
            _ws = null;
        }

        /// <summary>
        /// Must be called from Update() to process queued messages on the main thread
        /// </summary>
        public void ProcessMessages()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogError($"[Bugpunch.TunnelClient] Queue error: {ex}"); }
            }
        }

        /// <summary>
        /// Send a JSON text response back to the server. On-device builds
        /// ship the frame through the native WebSocket (N3); the Editor
        /// keeps using the managed socket for local-dev flow.
        /// </summary>
        public void SendResponse(string requestId, int status, string body, string contentType = "application/json")
        {
            var msg = JsonUtility.ToJson(new ResponseMessage
            {
                type = "response",
                requestId = requestId,
                status = status,
                body = body,
                contentType = contentType
            });
            _ = SendAsync(msg);
        }

        /// <summary>
        /// Send a binary response (e.g., screenshot JPEG). Base64-encoded
        /// inside the JSON envelope.
        /// </summary>
        public void SendBinaryResponse(string requestId, byte[] data, string contentType)
        {
            var base64 = Convert.ToBase64String(data);
            var msg = JsonUtility.ToJson(new ResponseMessage
            {
                type = "response",
                requestId = requestId,
                status = 200,
                body = base64,
                contentType = contentType,
                isBase64 = true
            });
            _ = SendAsync(msg);
        }

        async Task HeartbeatLoop()
        {
            var interval = (int)(_config.heartbeatInterval * 1000);
            try
            {
                while (IsConnected && _ws?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, _cts.Token);
                    await SendAsync("{\"type\":\"heartbeat\"}");
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        /// <summary>
        /// Fire-and-forget text send. Used by the log sink streamer to push
        /// pre-formatted JSON envelopes without building a ResponseMessage.
        /// Safe to call from any thread.
        /// </summary>
        public void SendRaw(string message)
        {
            _ = SendAsync(message);
        }

        async Task SendAsync(string message)
        {
            if (_ws?.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(message);
            try
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts?.Token ?? CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch.TunnelClient] Send error: {ex.Message}");
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[65536]; // 64KB buffer

            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // Handle fragmented messages
                        var sb = new StringBuilder();
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        while (!result.EndOfMessage)
                        {
                            result = await _ws.ReceiveAsync(
                                new ArraySegment<byte>(buffer),
                                _cts.Token
                            );
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }

                        var message = sb.ToString();
                        HandleMessage(message);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException ex)
            {
                _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message));
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message));
            }

            IsConnected = false;
            _mainThreadQueue.Enqueue(() => OnDisconnected?.Invoke());
        }

        void HandleMessage(string json)
        {
            try
            {
                // Parse the type field to route the message
                // Using simple string parsing to avoid JsonUtility limitations with polymorphic types
                if (json.Contains("\"type\":\"request\"") || json.Contains("\"type\": \"request\""))
                {
                    var msg = JsonUtility.FromJson<RequestMessage>(json);
                    _mainThreadQueue.Enqueue(() =>
                        OnRequest?.Invoke(msg.requestId, msg.method, msg.path, msg.body));
                }
                else if (json.Contains("\"type\":\"registered\"") || json.Contains("\"type\": \"registered\""))
                {
                    // Server confirmed registration. Extract the nested
                    // pinConfig object (if present) and hand it to listeners.
                    Debug.Log("[Bugpunch.TunnelClient] Device registered with server");
                    var pinConfig = ExtractPinConfig(json);
                    if (!string.IsNullOrEmpty(pinConfig))
                    {
                        PinConfigJson = pinConfig;
                        _mainThreadQueue.Enqueue(() => OnPinConfig?.Invoke(pinConfig));
                    }
                }
                else if (json.Contains("\"type\":\"pinUpdate\"") || json.Contains("\"type\": \"pinUpdate\""))
                {
                    // Live pin change pushed by the server (admin toggled
                    // pins in the dashboard). Same payload shape as the
                    // `registered` ack's pinConfig.
                    var pinConfig = ExtractPinConfig(json);
                    if (!string.IsNullOrEmpty(pinConfig))
                    {
                        PinConfigJson = pinConfig;
                        _mainThreadQueue.Enqueue(() => OnPinConfig?.Invoke(pinConfig));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch.TunnelClient] Message parse error: {ex.Message}\n{json}");
            }
        }

        /// <summary>
        /// Extracts the <c>pinConfig</c> (or <c>config</c> for pinUpdate
        /// frames) object substring from the raw JSON. Avoids pulling in a
        /// full JSON parser — we just hand the bytes to native, which
        /// verifies the HMAC and parses them itself.
        /// </summary>
        static string ExtractPinConfig(string json)
        {
            int keyIdx = json.IndexOf("\"pinConfig\"", StringComparison.Ordinal);
            if (keyIdx < 0) keyIdx = json.IndexOf("\"config\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int braceIdx = json.IndexOf('{', keyIdx);
            if (braceIdx < 0) return null;
            int depth = 0;
            for (int i = braceIdx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return json.Substring(braceIdx, i - braceIdx + 1);
                }
            }
            return null;
        }

        // ── Message types (matching server protocol) ──

        [Serializable]
        class RegisterMessage
        {
            public string type;
            public string name;
            public string platform;
            public string appVersion;
            public int remoteIdePort;
            public string token;
            public string projectId;
            public string deviceId;
            public string stableDeviceId;
            public string buildChannel;
        }

        [Serializable]
        class RequestMessage
        {
            public string type;
            public string requestId;
            public string method;
            public string path;
            public string body;
        }

        [Serializable]
        class ResponseMessage
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
#endif
