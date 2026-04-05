using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Persists server connection settings via EditorPrefs.
    /// Used by the auto-upload system and the Server Settings window.
    /// </summary>
    public static class ServerSettings
    {
        private const string KeyServerUrl = "UIAutomation.ServerUrl";
        private const string KeyApiKey = "UIAutomation.ApiKey";
        private const string KeyAutoUpload = "UIAutomation.AutoUpload";
        private const string KeyUploadPasses = "UIAutomation.UploadPasses";
        private const string KeyCapturePersistentData = "UIAutomation.CapturePersistentData";

        private const string DefaultServerUrl = "http://localhost:5000";

        /// <summary>Server base URL (e.g. "http://localhost:5000").</summary>
        public static string ServerUrl
        {
            get => EditorPrefs.GetString(KeyServerUrl, DefaultServerUrl);
            set => EditorPrefs.SetString(KeyServerUrl, value);
        }

        /// <summary>Optional API key for authenticated servers.</summary>
        public static string ApiKey
        {
            get => EditorPrefs.GetString(KeyApiKey, "");
            set => EditorPrefs.SetString(KeyApiKey, value);
        }

        /// <summary>When true, test sessions are auto-uploaded after each test.</summary>
        public static bool AutoUpload
        {
            get => EditorPrefs.GetBool(KeyAutoUpload, false);
            set => EditorPrefs.SetBool(KeyAutoUpload, value);
        }

        /// <summary>When true, passed tests are also uploaded (not just failures).</summary>
        public static bool UploadPasses
        {
            get => EditorPrefs.GetBool(KeyUploadPasses, true);
            set => EditorPrefs.SetBool(KeyUploadPasses, value);
        }

        /// <summary>When true, Application.persistentDataPath contents are included in diagnostic zips on test failure.</summary>
        public static bool CapturePersistentData
        {
            get => EditorPrefs.GetBool(KeyCapturePersistentData, true);
            set => EditorPrefs.SetBool(KeyCapturePersistentData, value);
        }
    }

    /// <summary>
    /// Queues uploads instead of awaiting them per-test. Uploads run concurrently
    /// in the background. The queue is drained in RunFinished (via DrainUploadQueue)
    /// so all uploads complete before the test batch exits.
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoUploadHook
    {
        static readonly List<Task> _pendingUploads = new();
        static readonly object _lock = new();

        static AutoUploadHook()
        {
            TestReport.OnSessionEndedAsync = OnSessionEndedAsync;
            TestReport.CapturePersistentData = ServerSettings.CapturePersistentData;
        }

        private static Task OnSessionEndedAsync(string zipPath)
        {
            if (!ServerSettings.AutoUpload) return Task.CompletedTask;
            if (string.IsNullOrEmpty(zipPath)) return Task.CompletedTask;

            var serverUrl = ServerSettings.ServerUrl;
            if (string.IsNullOrEmpty(serverUrl)) return Task.CompletedTask;

            var apiKey = ServerSettings.ApiKey;

            // Run upload on thread pool — NOT on Unity's sync context.
            // This prevents deadlock when DrainUploadQueue blocks the main thread.
            var task = Task.Run(() =>
                TestReport.UploadSession(zipPath, serverUrl, string.IsNullOrEmpty(apiKey) ? null : apiKey));

            lock (_lock)
            {
                // Remove completed tasks to avoid unbounded growth
                _pendingUploads.RemoveAll(t => t.IsCompleted);
                _pendingUploads.Add(task);
            }

            // Return immediately — don't block TearDown
            return Task.CompletedTask;
        }

        /// <summary>
        /// Waits for all queued uploads to finish. Called from RunFinished.
        /// Safe to call from the main thread — uploads run on thread pool.
        /// </summary>
        internal static void DrainUploadQueue()
        {
            Task[] pending;
            lock (_lock)
            {
                pending = _pendingUploads.ToArray();
                _pendingUploads.Clear();
            }

            if (pending.Length == 0) return;

            Debug.Log($"[UIAutomation] Waiting for {pending.Length} upload(s) to complete...");
            try
            {
                Task.WaitAll(pending);
                Debug.Log("[UIAutomation] All uploads complete.");
            }
            catch (System.AggregateException ex)
            {
                foreach (var inner in ex.InnerExceptions)
                    Debug.LogWarning($"[UIAutomation] Upload failed: {inner.Message}");
            }
        }
    }
}
