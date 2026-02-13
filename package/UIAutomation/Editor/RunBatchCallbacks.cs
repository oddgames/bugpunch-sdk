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
            bool _runCreated;
            bool _savedRunInBackground;

            public void RunStarted(ITestAdaptor testsToRun)
            {
                _runCreated = false;

                // Ensure Unity keeps running when the editor loses focus —
                // must be set here (before any test SetUp) so the player loop
                // doesn't pause if the user clicks away during test execution.
                _savedRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
            }

            public void TestStarted(ITestAdaptor test)
            {
                // Only create a run for actual test methods (not suites/fixtures)
                if (_runCreated || test.IsSuite) return;
                _runCreated = true;

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
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                // Restore runInBackground to its original value
                Application.runInBackground = _savedRunInBackground;

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
