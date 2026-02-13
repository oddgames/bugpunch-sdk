using System;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Hooks into Unity's Test Runner lifecycle to create/complete test runs on the server.
    /// RunStarted fires once per test batch (before any test SetUp).
    /// RunFinished fires once when all tests in the batch are complete.
    /// </summary>
    [InitializeOnLoad]
    internal static class RunBatchCallbacks
    {
        static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        static RunBatchCallbacks()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        private class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                if (!ServerSettings.AutoUpload) return;

                var serverUrl = ServerSettings.ServerUrl;
                if (string.IsNullOrEmpty(serverUrl)) return;

                try
                {
                    var runId = StartRunOnServer(serverUrl, ServerSettings.ApiKey);
                    if (!string.IsNullOrEmpty(runId))
                    {
                        TestReport.RunId = runId;
                        Debug.Log($"[UIAutomation] Test run started: {runId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UIAutomation] Failed to start run on server: {ex.Message}");
                    // Graceful degradation — tests still work without runId
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                // Drain upload queue before finishing the run — ensures all
                // session uploads complete before the batch exits.
                AutoUploadHook.DrainUploadQueue();

                var runId = TestReport.RunId;
                if (string.IsNullOrEmpty(runId)) return;

                TestReport.RunId = null;

                var serverUrl = ServerSettings.ServerUrl;
                if (string.IsNullOrEmpty(serverUrl)) return;

                try
                {
                    FinishRunOnServer(serverUrl, ServerSettings.ApiKey, runId);
                    Debug.Log($"[UIAutomation] Test run finished: {runId}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UIAutomation] Failed to finish run on server: {ex.Message}");
                }
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        static string StartRunOnServer(string serverUrl, string apiKey)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/runs/start";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            // Synchronous — RunStarted must complete before tests begin
            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[UIAutomation] Server returned {response.StatusCode} for run start");
                return null;
            }

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            // Parse {"runId":"..."} — simple manual parse to avoid JsonUtility issues
            var idx = body.IndexOf("\"runId\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            var colonIdx = body.IndexOf(':', idx);
            if (colonIdx < 0) return null;
            var quoteStart = body.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return null;
            var quoteEnd = body.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;
            return body.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        static void FinishRunOnServer(string serverUrl, string apiKey, string runId)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/runs/{runId}/finish";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            // Fire and forget — don't block test runner completion
            _httpClient.SendAsync(request).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Debug.LogWarning($"[UIAutomation] Failed to finish run: {t.Exception?.InnerException?.Message}");
            });
        }
    }
}
