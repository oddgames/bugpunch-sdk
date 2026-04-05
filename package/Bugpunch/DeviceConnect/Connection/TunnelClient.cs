using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    public class TunnelClient
    {
        readonly BugpunchConfig _config;
        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        public bool IsConnected { get; private set; }
        public bool IsConnecting { get; private set; }
        public string DeviceId { get; private set; }

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        // requestId, method, path, body
        public event Action<string, string, string, string> OnRequest;

        public TunnelClient(BugpunchConfig config)
        {
            _config = config;
        }

        public async void Connect()
        {
            if (IsConnected || IsConnecting) return;
            IsConnecting = true;

            try
            {
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();

                var uri = new Uri(_config.TunnelUrl);
                Debug.Log($"[Bugpunch] Connecting to {uri}...");

                await _ws.ConnectAsync(uri, _cts.Token);

                IsConnected = true;
                IsConnecting = false;

                if (string.IsNullOrEmpty(DeviceId))
                    DeviceId = DeviceIdentity.GetDeviceId();

                // Send registration
                var registerMsg = JsonUtility.ToJson(new RegisterMessage
                {
                    type = "register",
                    name = _config.EffectiveDeviceName,
                    platform = Application.platform.ToString(),
                    appVersion = Application.version,
                    remoteIdePort = 0, // not using local port, everything through tunnel
                    token = _config.apiKey,
                    projectId = _config.projectId,
                    deviceId = DeviceId
                });
                await SendAsync(registerMsg);

                _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());

                // Start receive loop and heartbeat
                _ = ReceiveLoop();
                _ = HeartbeatLoop();
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                IsConnected = false;
                _mainThreadQueue.Enqueue(() => OnError?.Invoke(ex.Message));
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
                catch (Exception ex) { Debug.LogError($"[Bugpunch] Queue error: {ex}"); }
            }
        }

        /// <summary>
        /// Send a JSON text response back to the server
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
        /// Send a binary response (e.g., screenshot JPEG)
        /// </summary>
        public void SendBinaryResponse(string requestId, byte[] data, string contentType)
        {
            // Encode binary as base64 in the JSON response
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
                Debug.LogError($"[Bugpunch] Send error: {ex.Message}");
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
                    // Server confirmed registration
                    Debug.Log("[Bugpunch] Device registered with server");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Message parse error: {ex.Message}\n{json}");
            }
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
