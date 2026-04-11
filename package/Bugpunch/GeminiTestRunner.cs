#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ODDGames.Recorder;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Result of a Gemini-driven test run.
    /// </summary>
    public class GeminiTestResult
    {
        public string TestName;
        public string TestDefinitionId;
        public string Prompt;
        public string Scene;
        public bool Passed;
        public string FailReason;
        public int StepsExecuted;
        public int MaxSteps;
        public float TotalSeconds;
        public string AppVersion;
        public string GeminiModel;
        public DateTime StartedAt;
        public DateTime? FinishedAt;
        public string VideoPath;
        public List<GeminiStepResult> Steps = new List<GeminiStepResult>();
        public List<GeminiErrorInfo> Errors = new List<GeminiErrorInfo>();
    }

    /// <summary>
    /// Result of a single step in a Gemini test run.
    /// </summary>
    public class GeminiStepResult
    {
        public int StepIndex;
        public string ActionJson;
        public string ModelResponse;
        public bool Success;
        public string Error;
        public float ElapsedMs;
    }

    /// <summary>
    /// Error or exception captured during a Gemini test.
    /// </summary>
    public class GeminiErrorInfo
    {
        public string Category; // "error", "exception", "timeout", "api_error"
        public string Message;
        public string StackTrace;
        public string ActionJson;
        public int StepIndex;
    }

    /// <summary>
    /// Self-contained AI-driven test runner. No test fixtures needed.
    /// Handles its own input injection, video recording, and lifecycle.
    ///
    /// <para>Usage:</para>
    /// <code>
    /// var runner = new GeminiTestRunner { ApiKey = "..." };
    /// var result = await runner.Run("Navigate to Settings and toggle Dark Mode");
    /// // result.VideoPath contains the recording
    /// </code>
    /// </summary>
    public class GeminiTestRunner
    {
        private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

        /// <summary>Google Gemini API key.</summary>
        public string ApiKey { get; set; }

        /// <summary>Gemini model ID. Default: gemini-2.0-flash</summary>
        public string Model { get; set; } = "gemini-2.0-flash";

        /// <summary>Maximum action steps before aborting. Default: 20</summary>
        public int MaxSteps { get; set; } = 20;

        /// <summary>JPEG quality for screenshots sent to the API (1-100). Default: 70</summary>
        public int ScreenshotQuality { get; set; } = 70;

        /// <summary>Whether to record video during the test. Default: true</summary>
        public bool RecordVideo { get; set; } = true;

        /// <summary>Whether to set up virtual input devices. Default: true.
        /// Set to false if running inside a UIAutomationTestFixture that already manages input.</summary>
        public bool ManageInput { get; set; } = true;

        /// <summary>Callback for log messages. Useful for editor UI.</summary>
        public Action<string> OnLog { get; set; }

        /// <summary>Callback fired after each step completes.</summary>
        public Action<GeminiStepResult> OnStepComplete { get; set; }

        /// <summary>Cancellation token source for stopping a running test.</summary>
        public CancellationTokenSource CancelSource { get; set; }

        /// <summary>
        /// Run a Gemini-driven test. Fully self-contained — sets up input,
        /// starts recording, runs the AI loop, tears down.
        /// </summary>
        public async Task<GeminiTestResult> Run(string prompt, string scene = null)
        {
            if (string.IsNullOrEmpty(ApiKey))
                throw new InvalidOperationException("Gemini API key is not set");

            var ct = CancelSource?.Token ?? CancellationToken.None;
            var result = new GeminiTestResult
            {
                Prompt = prompt,
                Scene = scene,
                TestName = prompt.Length > 60 ? prompt.Substring(0, 57) + "..." : prompt,
                MaxSteps = MaxSteps,
                GeminiModel = Model,
                AppVersion = Application.version,
                StartedAt = DateTime.UtcNow
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var conversation = new List<object>();
            RecordingSession recording = null;
            bool inputSetUp = false;

            try
            {
                // --- Setup ---
                if (ManageInput)
                {
                    InputInjector.Setup();
                    inputSetUp = true;
                    Log("Input injection set up");
                }

                // Use faster search timeouts — Gemini can retry on its own
                var savedSearchTime = ActionExecutor.DefaultSearchTime;
                ActionExecutor.DefaultSearchTime = 3f;

                // Load scene if specified
                if (!string.IsNullOrEmpty(scene))
                {
                    Log($"Loading scene: {scene}");
                    var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(scene);
                    while (!op.isDone)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    await ActionExecutor.WaitForStableFrameRate(20f);
                }

                // Start recording
                if (RecordVideo)
                {
                    try
                    {
                        var videoDir = Path.Combine(Application.temporaryCachePath, "gemini_recordings");
                        Directory.CreateDirectory(videoDir);
                        var videoPath = Path.Combine(videoDir, $"gemini_{DateTime.Now:yyyyMMdd_HHmmss}");

                        recording = await MediaRecorder.StartAsync(new RecorderSettings
                        {
                            OutputPath = videoPath,
                            Width = 1280,
                            Height = 720,
                            FrameRate = 15,
                            IncludeAudio = false
                        });
                        Log("Recording started");
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Recording failed to start: {ex.Message}");
                        recording = null;
                    }
                }

                // --- AI Loop ---
                Log("Capturing initial screenshot...");
                var screenshotBytes = await CaptureScreenBytes();

                conversation.Add(BuildUserMessage(
                    $"Here is the current screen. Complete this task:\n\n{prompt}",
                    screenshotBytes));

                for (int step = 0; step < MaxSteps; step++)
                {
                    ct.ThrowIfCancellationRequested();

                    Log($"Step {step + 1}/{MaxSteps}: Asking Gemini...");
                    var stepResult = new GeminiStepResult { StepIndex = step };
                    var stepSw = System.Diagnostics.Stopwatch.StartNew();

                    // Call Gemini API
                    string modelResponse;
                    try
                    {
                        modelResponse = await CallGemini(conversation, ct);
                    }
                    catch (Exception ex)
                    {
                        stepResult.Success = false;
                        stepResult.Error = $"API error: {ex.Message}";
                        stepResult.ElapsedMs = (float)stepSw.Elapsed.TotalMilliseconds;
                        result.Steps.Add(stepResult);
                        result.Errors.Add(new GeminiErrorInfo
                        {
                            Category = "api_error", Message = ex.Message,
                            StackTrace = ex.StackTrace, StepIndex = step
                        });
                        OnStepComplete?.Invoke(stepResult);
                        Log($"API error: {ex.Message}");
                        result.Passed = false;
                        result.FailReason = $"API error at step {step + 1}: {ex.Message}";
                        break;
                    }

                    stepResult.ModelResponse = modelResponse;
                    Log($"Gemini: {modelResponse}");

                    conversation.Add(new { role = "model", parts = new[] { new { text = modelResponse } } });

                    string actionJson = ExtractActionJson(modelResponse);
                    stepResult.ActionJson = actionJson;

                    if (actionJson == null)
                    {
                        stepResult.Success = false;
                        stepResult.Error = "Could not extract action JSON from response";
                        stepResult.ElapsedMs = (float)stepSw.Elapsed.TotalMilliseconds;
                        result.Steps.Add(stepResult);
                        OnStepComplete?.Invoke(stepResult);
                        Log("Warning: No action JSON found, asking Gemini to retry...");
                        conversation.Add(BuildUserMessage(
                            "I couldn't parse your response. Please respond with ONLY a JSON action object like {\"action\":\"click\",\"text\":\"Button\"} or {\"action\":\"done\"} if the task is complete.",
                            null));
                        continue;
                    }

                    var parsed = JObject.Parse(actionJson);
                    var action = parsed.Value<string>("action")?.ToLowerInvariant();

                    // Done signal
                    if (action == "done")
                    {
                        var doneResult = parsed.Value<string>("result")?.ToLowerInvariant();
                        stepResult.Success = true;
                        stepResult.ElapsedMs = (float)stepSw.Elapsed.TotalMilliseconds;
                        result.Steps.Add(stepResult);
                        OnStepComplete?.Invoke(stepResult);

                        result.Passed = doneResult != "fail";
                        result.FailReason = parsed.Value<string>("reason");
                        result.StepsExecuted = step + 1;
                        Log(result.Passed
                            ? $"Test PASSED in {step + 1} steps"
                            : $"Test FAILED: {result.FailReason}");
                        break;
                    }

                    // Execute action
                    Log($"Executing: {actionJson}");
                    ActionResult actionResult;
                    try
                    {
                        actionResult = await ActionExecutor.Execute(actionJson);
                    }
                    catch (Exception ex)
                    {
                        stepResult.Success = false;
                        stepResult.Error = $"Execution error: {ex.Message}";
                        stepResult.ElapsedMs = (float)stepSw.Elapsed.TotalMilliseconds;
                        result.Steps.Add(stepResult);
                        result.Errors.Add(new GeminiErrorInfo
                        {
                            Category = "exception", Message = ex.Message,
                            StackTrace = ex.StackTrace, ActionJson = actionJson, StepIndex = step
                        });
                        OnStepComplete?.Invoke(stepResult);

                        screenshotBytes = await CaptureScreenBytes();
                        conversation.Add(BuildUserMessage(
                            $"Action FAILED with error: {ex.Message}\nHere is the current screen. Try a different approach.",
                            screenshotBytes));
                        continue;
                    }

                    stepResult.Success = actionResult.Success;
                    stepResult.Error = actionResult.Error;
                    stepResult.ElapsedMs = (float)stepSw.Elapsed.TotalMilliseconds;
                    result.Steps.Add(stepResult);

                    if (!actionResult.Success && !string.IsNullOrEmpty(actionResult.Error))
                    {
                        result.Errors.Add(new GeminiErrorInfo
                        {
                            Category = "error", Message = actionResult.Error,
                            ActionJson = actionJson, StepIndex = step
                        });
                    }

                    OnStepComplete?.Invoke(stepResult);

                    screenshotBytes = await CaptureScreenBytes();
                    string feedback = actionResult.Success
                        ? "Action executed successfully. Here is the current screen. What's next?"
                        : $"Action failed: {actionResult.Error}\nHere is the current screen. Try a different approach.";
                    conversation.Add(BuildUserMessage(feedback, screenshotBytes));

                    result.StepsExecuted = step + 1;

                    if (step == MaxSteps - 1)
                    {
                        result.Passed = false;
                        result.FailReason = $"Reached maximum steps ({MaxSteps}) without completion";
                        result.Errors.Add(new GeminiErrorInfo
                        {
                            Category = "timeout", Message = result.FailReason, StepIndex = step
                        });
                        Log(result.FailReason);
                    }
                }

                // Restore search time
                ActionExecutor.DefaultSearchTime = savedSearchTime;
            }
            catch (OperationCanceledException)
            {
                result.Passed = false;
                result.FailReason = "Cancelled";
                Log("Test cancelled.");
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.FailReason = ex.Message;
                result.Errors.Add(new GeminiErrorInfo
                {
                    Category = "exception", Message = ex.Message,
                    StackTrace = ex.StackTrace, StepIndex = result.StepsExecuted
                });
                Log($"Error: {ex.Message}");
            }
            finally
            {
                // --- Teardown ---
                // Stop recording
                if (recording != null)
                {
                    try
                    {
                        if (recording.IsRecording)
                        {
                            var videoPath = await recording.StopAsync();
                            result.VideoPath = videoPath;
                            Log($"Recording saved: {videoPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Failed to stop recording: {ex.Message}");
                    }
                    finally
                    {
                        recording.Dispose();
                    }
                }

                // Tear down input
                if (inputSetUp)
                {
                    try { InputInjector.TearDown(); }
                    catch (Exception ex) { Log($"Warning: InputInjector teardown failed: {ex.Message}"); }
                }
            }

            sw.Stop();
            result.TotalSeconds = (float)sw.Elapsed.TotalSeconds;
            result.FinishedAt = DateTime.UtcNow;
            return result;
        }

        #region Screenshot Capture

        private static ScreenCaptureHelper _captureHelper;

        private async Task<byte[]> CaptureScreenBytes()
        {
            if (_captureHelper == null)
            {
                var go = new GameObject("[GeminiTestRunner.ScreenCapture]")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                UnityEngine.Object.DontDestroyOnLoad(go);
                _captureHelper = go.AddComponent<ScreenCaptureHelper>();
            }

            var tcs = new TaskCompletionSource<byte[]>();
            _captureHelper.Enqueue(ScreenshotQuality, tcs);
            return await tcs.Task;
        }

        /// <summary>
        /// Captures at WaitForEndOfFrame when the back buffer has rendered content.
        /// </summary>
        private class ScreenCaptureHelper : MonoBehaviour
        {
            private readonly Queue<(int quality, TaskCompletionSource<byte[]> tcs)> _queue = new();

            public void Enqueue(int quality, TaskCompletionSource<byte[]> tcs)
            {
                _queue.Enqueue((quality, tcs));
            }

            System.Collections.IEnumerator Start()
            {
                while (true)
                {
                    yield return new WaitForEndOfFrame();
                    ProcessQueue();
                }
            }

            void ProcessQueue()
            {
                if (_queue.Count == 0) return;
                try
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (tex == null)
                    {
                        while (_queue.Count > 0)
                            _queue.Dequeue().tcs.TrySetException(
                                new Exception("CaptureScreenshotAsTexture returned null"));
                        return;
                    }

                    var quality = _queue.Peek().quality;
                    var bytes = tex.EncodeToJPG(quality);
                    UnityEngine.Object.Destroy(tex);

                    while (_queue.Count > 0)
                        _queue.Dequeue().tcs.TrySetResult(bytes);
                }
                catch (Exception ex)
                {
                    while (_queue.Count > 0)
                        _queue.Dequeue().tcs.TrySetException(ex);
                }
            }
        }

        #endregion

        #region Gemini API

        private static readonly string SystemPrompt = @"You are a UI test automation agent controlling a Unity application. You can see screenshots of the app and must issue ONE action per turn to complete the user's task.

RESPOND WITH ONLY A JSON OBJECT. No explanation, no markdown, no extra text.

== Available Actions ==

-- Click / Tap --
{""action"":""click"", ""text"":""Button Label""}
{""action"":""click"", ""name"":""GameObjectName""}
{""action"":""click"", ""at"":[0.5, 0.5]}  // normalized screen coordinates
{""action"":""doubleclick"", ""text"":""Item""}

-- Type Text --
{""action"":""type"", ""name"":""InputField"", ""value"":""hello""}
{""action"":""type"", ""adjacent"":""Username:"", ""value"":""admin"", ""clear"":true, ""enter"":true}

-- Swipe / Scroll --
{""action"":""swipe"", ""direction"":""left""}  // left, right, up, down
{""action"":""swipe"", ""direction"":""up"", ""name"":""Panel"", ""distance"":0.3}
{""action"":""scroll"", ""name"":""ListView"", ""direction"":""down"", ""amount"":0.3}
{""action"":""scrollto"", ""name"":""ScrollView"", ""target"":{""text"":""TargetItem""}}

-- Drag --
{""action"":""drag"", ""from"":{""name"":""Source""}, ""to"":{""name"":""Target""}}
{""action"":""drag"", ""direction"":[0.2, 0.0], ""name"":""Slider""}

-- Dropdown --
{""action"":""dropdown"", ""name"":""Dropdown"", ""option"":""Option Label""}
{""action"":""dropdown"", ""name"":""Dropdown"", ""option"":2}

-- Slider --
{""action"":""slider"", ""name"":""VolumeSlider"", ""value"":0.5}

-- Wait --
{""action"":""wait"", ""seconds"":1.5}
{""action"":""waitfor"", ""text"":""Loading Complete"", ""seconds"":10}
{""action"":""waitfornot"", ""text"":""Loading..."", ""seconds"":10}

-- Keys --
{""action"":""key"", ""key"":""enter""}
{""action"":""key"", ""key"":""escape""}

-- Gestures --
{""action"":""pinch"", ""scale"":2.0}
{""action"":""rotate"", ""degrees"":45}
{""action"":""hold"", ""text"":""Button"", ""seconds"":2}

-- Done (call this when the task is complete) --
{""action"":""done""}
{""action"":""done"", ""result"":""pass""}
{""action"":""done"", ""result"":""fail"", ""reason"":""Could not find the Settings button""}

== Search Targets ==
Actions that target UI elements accept these fields (can combine multiple):
- ""text"": match by visible text content
- ""name"": match by GameObject name
- ""type"": match by component type (e.g. ""Button"", ""Toggle"")
- ""near"": find element near another element's text
- ""adjacent"": find element adjacent to a label
- ""tag"": match by Unity tag
- ""path"": match by hierarchy path
- ""any"": match by any of the above

== Rules ==
1. Issue EXACTLY ONE action per response
2. Respond with ONLY the JSON object - no explanation text
3. Use {""action"":""done""} when the task is complete (or impossible)
4. If an action fails, try a different approach
5. Prefer ""text"" targeting over ""at"" coordinates when possible
6. Use ""wait"" if the UI needs time to animate or load";

        private async Task<string> CallGemini(List<object> conversation, CancellationToken ct)
        {
            var url = $"{ApiBase}/{Model}:generateContent?key={ApiKey}";

            var requestBody = new
            {
                contents = conversation,
                systemInstruction = new
                {
                    parts = new[] { new { text = SystemPrompt } }
                },
                generationConfig = new
                {
                    temperature = 0.1f,
                    maxOutputTokens = 512
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

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

            var responseJson = request.downloadHandler.text;
            var response = JObject.Parse(responseJson);

            var text = response["candidates"]?[0]?["content"]?["parts"]?[0]?.Value<string>("text");
            if (string.IsNullOrEmpty(text))
                throw new Exception($"Empty response from Gemini: {responseJson}");

            return text.Trim();
        }

        private static object BuildUserMessage(string text, byte[] imageBytes)
        {
            var parts = new List<object>();

            if (imageBytes != null)
            {
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = "image/jpeg",
                        data = Convert.ToBase64String(imageBytes)
                    }
                });
            }

            parts.Add(new { text });
            return new { role = "user", parts };
        }

        #endregion

        #region JSON Extraction

        private static string ExtractActionJson(string response)
        {
            if (string.IsNullOrEmpty(response))
                return null;

            var trimmed = response.Trim();

            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                try { JObject.Parse(trimmed); return trimmed; }
                catch { }
            }

            var match = Regex.Match(response, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
            if (match.Success)
            {
                try { JObject.Parse(match.Groups[1].Value); return match.Groups[1].Value; }
                catch { }
            }

            match = Regex.Match(response, @"\{[^{}]*""action""[^{}]*\}", RegexOptions.Singleline);
            if (match.Success)
            {
                try { JObject.Parse(match.Value); return match.Value; }
                catch { }
            }

            return null;
        }

        #endregion

        private void Log(string message)
        {
            Debug.Log($"[GeminiTestRunner] {message}");
            OnLog?.Invoke(message);
        }
    }
}
#endif
