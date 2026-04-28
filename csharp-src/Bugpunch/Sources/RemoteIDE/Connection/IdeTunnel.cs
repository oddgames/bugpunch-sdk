using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch.RemoteIDE
{
    /// <summary>
    /// Managed WebSocket client for the Remote IDE RPC channel.
    ///
    /// Lives entirely in C# on every platform — Editor, desktop, Android
    /// player, iOS player. The native BugpunchTunnel on mobile no longer
    /// carries Remote IDE traffic; it's the bug-reporting / pin / log sink
    /// pipe only (see project_native_tunnel.md for the split rationale).
    /// Pin config and log chunks flow through that native pipe; nothing on
    /// this managed pipe touches either.
    ///
    /// Frames accepted from server:  request, registered, pong
    /// Frames sent to server:        register, heartbeat, response, event
    /// </summary>
    [ODDGames.Scripting.ScriptProtected(
        ODDGames.Scripting.ScriptTrustLevel.System,
        "IDE tunnel internals are off-limits to scripts at every trust level")]
    public class IdeTunnel
    {
        readonly BugpunchConfig _config;
        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        // ClientWebSocket.SendAsync is not safe for concurrent calls — .NET throws
        // InvalidOperationException("There is already one outstanding 'SendAsync' call").
        // Scene-camera polls + heartbeats + WebRTC ICE events + RPC responses all share
        // this socket, so serialize them through a single-writer gate.
        readonly SemaphoreSlim _sendGate = new(1, 1);

        public bool IsConnected { get; private set; }
        public bool IsConnecting { get; private set; }
        public string DeviceId { get; private set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        /// <summary>Fires on each incoming request frame: requestId, method, path, body.</summary>
        public event Action<string, string, string, string> OnRequest;

        public IdeTunnel(BugpunchConfig config)
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

                    var uri = new Uri(_config.IdeTunnelUrl);
                    BugpunchLog.Info("IdeTunnel", $"Connecting to {uri}...");

                    await _ws.ConnectAsync(uri, _cts.Token);

                    IsConnected = true;
                    IsConnecting = false;
                    attempt = 0;

                    if (string.IsNullOrEmpty(DeviceId))
                        DeviceId = DeviceIdentity.GetDeviceId();

                    var registerMsg = JsonUtility.ToJson(new RegisterMessage
                    {
                        type = "register",
                        name = _config.EffectiveDeviceName,
                        token = _config.apiKey,
                        deviceId = DeviceId,
                        projectId = "",
                        platform = Application.platform.ToString(),
                        appVersion = Application.version,
                    });
                    await SendAsync(registerMsg);

                    _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());

                    await Task.WhenAny(ReceiveLoop(), HeartbeatLoop());
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException;
                    var innerStr = inner != null ? $" — inner {inner.GetType().Name}: {inner.Message}" : "";
                    BugpunchLog.Warn("IdeTunnel", $"Connect/reconnect failed: {ex.GetType().Name}: {ex.Message}{innerStr}");
                    _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message));
                }

                IsConnected = false;
                IsConnecting = false;
                _mainThreadQueue.Enqueue(() => OnDisconnected?.Invoke());

                attempt++;
                int delayMs = BugpunchRetry.ExponentialBackoff(attempt, 1000, 10000);
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

        /// <summary>Must be called from Update() so queued callbacks run on the main thread.</summary>
        public void ProcessMessages()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { BugpunchLog.Error("IdeTunnel", $"Queue error: {ex}"); }
            }
        }

        /// <summary>Send a JSON text response for an in-flight request.</summary>
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

        /// <summary>Send a binary response (base64-encoded in the envelope).</summary>
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

        /// <summary>Send an `event` frame (e.g. webrtc-ice candidate) — no response expected.</summary>
        public void SendEvent(string eventName, string dataJson)
        {
            // Hand-formatted to embed raw JSON without double-stringifying.
            var esc = eventName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            _ = SendAsync("{\"type\":\"event\",\"event\":\"" + esc + "\",\"data\":" + dataJson + "}");
        }

        /// <summary>Fire-and-forget text send for pre-formatted envelopes.</summary>
        public void SendRaw(string message) => _ = SendAsync(message);

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

        async Task SendAsync(string message)
        {
            if (_ws?.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(message);
            await _sendGate.WaitAsync();
            try
            {
                if (_ws?.State != WebSocketState.Open) return;
                await _ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts?.Token ?? CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                BugpunchLog.Error("IdeTunnel", $"Send error: {ex.Message}");
            }
            finally
            {
                _sendGate.Release();
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[65536];
            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var sb = new StringBuilder();
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        while (!result.EndOfMessage)
                        {
                            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        HandleMessage(sb.ToString());
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException ex) { _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message)); }
            catch (Exception ex) { _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message)); }

            IsConnected = false;
            _mainThreadQueue.Enqueue(() => OnDisconnected?.Invoke());
        }

        void HandleMessage(string json)
        {
            try
            {
                if (json.Contains("\"type\":\"request\"") || json.Contains("\"type\": \"request\""))
                {
                    var msg = JsonUtility.FromJson<RequestMessage>(json);
                    _mainThreadQueue.Enqueue(() =>
                        OnRequest?.Invoke(msg.requestId, msg.method, msg.path, msg.body));
                }
                else if (json.Contains("\"type\":\"registered\"") || json.Contains("\"type\": \"registered\""))
                {
                    BugpunchLog.Info("IdeTunnel", "Device registered with server");
                    var cfg = ExtractRoleConfig(json);
                    if (!string.IsNullOrEmpty(cfg))
                        _mainThreadQueue.Enqueue(() => RoleState.ApplyFromJson(cfg));
                }
                // pong / other frames: ignored
            }
            catch (Exception ex)
            {
                BugpunchLog.Error("IdeTunnel", $"Message parse error: {ex.Message}\n{json}");
            }
        }

        /// <summary>
        /// Extract the inner <c>roleConfig</c> object from a <c>registered</c>
        /// frame as a JSON string, or null if absent. Hand-rolled balanced
        /// brace scanner so we don't pull in a dependency just for handshake
        /// parsing.
        /// </summary>
        static string ExtractRoleConfig(string json)
        {
            const string needle = "\"roleConfig\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '{') return null;
            int depth = 0;
            bool inStr = false;
            for (int j = i; j < json.Length; j++)
            {
                char c = json[j];
                if (c == '\\' && inStr) { j++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return json.Substring(i, j - i + 1); }
            }
            return null;
        }

        [Serializable]
        class RegisterMessage
        {
            public string type;
            public string name;
            public string token;
            public string projectId;
            public string deviceId;
            public string platform;
            public string appVersion;
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
