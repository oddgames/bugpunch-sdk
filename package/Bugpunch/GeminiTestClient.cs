#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// A test definition fetched from the server.
    /// </summary>
    [Serializable]
    public class GeminiTestDefinition
    {
        public string Id;
        public string Name;
        public string Prompt;
        public string Scene;
        public int MaxSteps = 20;
        public bool Enabled = true;
        public string Tags;
        public int SortOrder;
        public string ProjectId;
    }

    /// <summary>
    /// Client for fetching Gemini test definitions from the UI Automation server,
    /// running them via GeminiTestRunner, and uploading results back.
    ///
    /// <para>Usage (device batch):</para>
    /// <code>
    /// var client = new GeminiTestClient
    /// {
    ///     ServerUrl = "http://myserver:5000",
    ///     ApiKey = "uiat_abc123",
    ///     GeminiApiKey = "AIza..."
    /// };
    /// var results = await client.RunAll();
    /// </code>
    /// </summary>
    public class GeminiTestClient
    {
        /// <summary>UI Automation server URL (e.g. http://localhost:5000)</summary>
        public string ServerUrl { get; set; } = "http://localhost:5000";

        /// <summary>Server API key (uiat_xxx) for authentication.</summary>
        public string ApiKey { get; set; }

        /// <summary>Google Gemini API key for the AI runner.</summary>
        public string GeminiApiKey { get; set; }

        /// <summary>Gemini model to use. Default: gemini-2.0-flash</summary>
        public string GeminiModel { get; set; } = "gemini-2.0-flash";

        /// <summary>Whether to upload results to the server after each test. Default: true</summary>
        public bool UploadResults { get; set; } = true;

        /// <summary>App version string included in uploaded results.</summary>
        public string AppVersion { get; set; }

        /// <summary>Branch name included in uploaded results.</summary>
        public string Branch { get; set; }

        /// <summary>Commit hash included in uploaded results.</summary>
        public string Commit { get; set; }

        /// <summary>Callback for log messages.</summary>
        public Action<string> OnLog { get; set; }

        /// <summary>Callback fired when a test completes.</summary>
        public Action<GeminiTestResult> OnTestComplete { get; set; }

        private string _currentRunId;

        /// <summary>
        /// Fetch all enabled test definitions from the server.
        /// </summary>
        public async Task<List<GeminiTestDefinition>> FetchTests(CancellationToken ct = default)
        {
            var url = $"{BaseUrl}/api/gemini-tests?enabledOnly=true";

            using var request = UnityWebRequest.Get(url);
            SetAuth(request);

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"Failed to fetch tests: HTTP {request.responseCode} {request.error}");

            return JsonConvert.DeserializeObject<List<GeminiTestDefinition>>(request.downloadHandler.text);
        }

        /// <summary>
        /// Fetch all enabled tests and run them sequentially.
        /// Creates a server run, uploads each result, and finishes the run.
        /// </summary>
        public async Task<List<GeminiTestResult>> RunAll(CancellationToken ct = default)
        {
            Log("Fetching test definitions...");
            var tests = await FetchTests(ct);
            Log($"Found {tests.Count} enabled test(s)");

            // Start a server run
            if (UploadResults)
            {
                try
                {
                    _currentRunId = await StartRun(ct);
                    Log($"Started run: {_currentRunId}");
                }
                catch (Exception ex)
                {
                    Log($"Warning: Failed to start server run: {ex.Message}");
                }
            }

            var results = new List<GeminiTestResult>();

            for (int i = 0; i < tests.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var test = tests[i];
                Log($"[{i + 1}/{tests.Count}] Running: {test.Name}");

                var result = await RunTest(test, ct);
                result.TestName = test.Name;
                result.TestDefinitionId = test.Id;
                results.Add(result);
                OnTestComplete?.Invoke(result);

                // Upload result
                if (UploadResults && !string.IsNullOrEmpty(_currentRunId))
                {
                    try { await UploadResult(_currentRunId, result, ct); }
                    catch (Exception ex) { Log($"Warning: Upload failed: {ex.Message}"); }
                }

                var status = result.Passed ? "PASS" : "FAIL";
                Log($"[{i + 1}/{tests.Count}] {status}: {test.Name} ({result.StepsExecuted} steps, {result.TotalSeconds:F1}s)");
            }

            // Finish run
            if (UploadResults && !string.IsNullOrEmpty(_currentRunId))
            {
                try { await FinishRun(_currentRunId, ct); }
                catch (Exception ex) { Log($"Warning: Failed to finish run: {ex.Message}"); }
            }

            var passed = results.Count(r => r.Passed);
            Log($"Done: {passed}/{results.Count} passed");

            _currentRunId = null;
            return results;
        }

        /// <summary>
        /// Run a single test definition.
        /// </summary>
        public async Task<GeminiTestResult> RunTest(GeminiTestDefinition test, CancellationToken ct = default)
        {
            var runner = new GeminiTestRunner
            {
                ApiKey = GeminiApiKey,
                Model = GeminiModel,
                MaxSteps = test.MaxSteps,
                OnLog = OnLog
            };

            if (ct != default)
                runner.CancelSource = CancellationTokenSource.CreateLinkedTokenSource(ct);

            return await runner.Run(test.Prompt, test.Scene);
        }

        /// <summary>
        /// Upload a single result to an existing run. Useful for ad-hoc uploads.
        /// </summary>
        public async Task UploadAdHocResult(GeminiTestResult result, CancellationToken ct = default)
        {
            var runId = await StartRun(ct);
            await UploadResult(runId, result, ct);
            await FinishRun(runId, ct);
        }

        #region Server API

        private string BaseUrl => ServerUrl.TrimEnd('/');

        private void SetAuth(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(ApiKey))
                request.SetRequestHeader("X-Api-Key", ApiKey);
        }

        private async Task<string> StartRun(CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                Branch,
                Commit,
                AppVersion = AppVersion ?? Application.version,
                Platform = Application.platform.ToString(),
                MachineName = SystemInfo.deviceName,
                GeminiModel
            });

            var responseJson = await PostJson($"{BaseUrl}/api/gemini-runs/start", body, ct);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(responseJson);
            return obj.Value<string>("id");
        }

        private async Task FinishRun(string runId, CancellationToken ct)
        {
            await PostJson($"{BaseUrl}/api/gemini-runs/{runId}/finish", "{}", ct);
        }

        private async Task UploadResult(string runId, GeminiTestResult result, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                testName = result.TestName,
                prompt = result.Prompt,
                passed = result.Passed,
                testDefinitionId = result.TestDefinitionId,
                scene = result.Scene,
                failReason = result.FailReason,
                stepsExecuted = result.StepsExecuted,
                maxSteps = result.MaxSteps,
                durationSeconds = (double)result.TotalSeconds,
                appVersion = result.AppVersion ?? AppVersion ?? Application.version,
                geminiModel = result.GeminiModel ?? GeminiModel,
                branch = Branch,
                platform = Application.platform.ToString(),
                startedAt = result.StartedAt,
                steps = result.Steps.Select(s => new
                {
                    stepIndex = s.StepIndex,
                    actionJson = s.ActionJson,
                    modelResponse = s.ModelResponse,
                    success = s.Success,
                    error = s.Error,
                    elapsedMs = (double)s.ElapsedMs
                }),
                errors = result.Errors.Select(e => new
                {
                    category = e.Category,
                    message = e.Message,
                    stackTrace = e.StackTrace,
                    actionJson = e.ActionJson,
                    stepIndex = e.StepIndex
                })
            });

            await PostJson($"{BaseUrl}/api/gemini-runs/{runId}/results", body, ct);
        }

        private async Task<string> PostJson(string url, string body, CancellationToken ct)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            SetAuth(request);

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorBody = request.downloadHandler?.text ?? "";
                throw new Exception($"HTTP {request.responseCode}: {request.error}\n{errorBody}");
            }

            return request.downloadHandler.text;
        }

        #endregion

        private void Log(string message)
        {
            Debug.Log($"[GeminiTestClient] {message}");
            OnLog?.Invoke(message);
        }
    }
}
#endif
