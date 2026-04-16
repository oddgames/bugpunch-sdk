using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// HTTP poll client for device registration and periodic polling.
    /// This is the lightweight alternative to TunnelClient (WebSocket).
    /// </summary>
    public class DeviceRegistration
    {
        readonly BugpunchConfig _config;
        string _deviceToken;
        bool _polling;

        public string DeviceId { get; private set; }
        public bool IsRegistered { get; private set; }

        public event Action OnUpgradeRequested;
        public event Action<PendingScript[]> OnScriptsReceived;

        public DeviceRegistration(BugpunchConfig config)
        {
            _config = config;
            DeviceId = DeviceIdentity.GetDeviceId();
            _deviceToken = PlayerPrefs.GetString("Bugpunch_DeviceToken", "");
        }

        public IEnumerator RegisterAndPoll()
        {
            yield return Register();

            if (!IsRegistered)
            {
                Debug.LogError("[Bugpunch] Registration failed, poll mode disabled");
                yield break;
            }

            Debug.Log($"[Bugpunch] Registered in poll mode, polling every {_config.pollInterval}s");

            _polling = true;
            while (_polling)
            {
                yield return new WaitForSeconds(_config.pollInterval);
                if (!_polling) break;
                yield return Poll();
            }
        }

        public void Stop()
        {
            _polling = false;
        }

        IEnumerator Register()
        {
            var url = _config.HttpBaseUrl + "/api/devices/register";
            var body = JsonUtility.ToJson(new RegisterBody
            {
                deviceId = DeviceId,
                name = _config.EffectiveDeviceName,
                platform = Application.platform.ToString(),
                appVersion = Application.version,
                scriptPermission = _config.scriptPermission.ToString().ToLower(),
                installerMode = BugpunchNative.GetInstallerMode()
            });

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Api-Key", _config.apiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Bugpunch] Registration failed: {req.error} — {req.downloadHandler?.text}");
                yield break;
            }

            var response = JsonUtility.FromJson<RegisterResponse>(req.downloadHandler.text);
            if (!string.IsNullOrEmpty(response.token))
            {
                _deviceToken = response.token;
                PlayerPrefs.SetString("Bugpunch_DeviceToken", _deviceToken);
                PlayerPrefs.Save();
            }

            IsRegistered = true;
            Debug.Log("[Bugpunch] Device registered (poll mode)");
        }

        IEnumerator Poll()
        {
            if (string.IsNullOrEmpty(_deviceToken)) yield break;

            var url = _config.HttpBaseUrl + "/api/device-poll";

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Device-Token", _deviceToken);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Bugpunch] Poll failed: {req.error}");
                yield break;
            }

            var response = JsonUtility.FromJson<PollResponse>(req.downloadHandler.text);

            if (response.upgradeToWebSocket)
            {
                Debug.Log("[Bugpunch] Server requested WebSocket upgrade");
                OnUpgradeRequested?.Invoke();
            }

            if (response.scripts != null && response.scripts.Length > 0)
            {
                OnScriptsReceived?.Invoke(response.scripts);
            }
        }

        public IEnumerator SubmitScriptResult(string scheduledScriptId, string output, string errors, bool success, int durationMs)
        {
            var url = _config.HttpBaseUrl + "/api/device-poll/script-result";
            var body = JsonUtility.ToJson(new ScriptResultBody
            {
                scheduledScriptId = scheduledScriptId,
                output = output,
                errors = errors,
                success = success,
                durationMs = durationMs
            });

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Device-Token", _deviceToken);

            yield return req.SendWebRequest();
        }

        // JSON serializable types
        [Serializable] struct RegisterBody { public string deviceId, name, platform, appVersion, scriptPermission, installerMode; }
        [Serializable] struct RegisterResponse { public string token; }
        [Serializable] public struct PendingScript { public string Id, Name, Code; }
        [Serializable] struct PollResponse { public PendingScript[] scripts; public bool upgradeToWebSocket; }
        [Serializable] struct ScriptResultBody { public string scheduledScriptId, output, errors; public bool success; public int durationMs; }
    }
}
