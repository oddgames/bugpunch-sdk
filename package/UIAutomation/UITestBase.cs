using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ODDGames.UIAutomation.AI;

#if UNITY_INCLUDE_TESTS
using UnityEngine.TestTools;
#endif

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Base class for UI automation tests. Provides:
    /// - Test data restoration via RestoreGameState()
    /// - Configurable recovery for handling dialogs/obstacles
    /// - Unobserved exception capture
    /// - Automatic test instability detection when recovery was needed
    ///
    /// Usage:
    /// <code>
    /// public class MyTests : UITestBase
    /// {
    ///     protected override bool EnableRecovery => true;
    ///
    ///     [Test]
    ///     public async Task TestSomething()
    ///     {
    ///         await RestoreGameState("TestData/SaveGame");
    ///         await LoadScene("MainMenu");
    ///         await Click(Name("StartButton"));
    ///     }
    /// }
    /// </code>
    /// </summary>
#if UNITY_INCLUDE_TESTS
    [TestMustExpectAllLogs(false)]
#endif
    public abstract class UITestBase
    {

        /// <summary>
        /// Override to return false if you don't want to capture unobserved exceptions.
        /// Default is true.
        /// </summary>
        protected virtual bool CaptureUnobservedException => true;

        /// <summary>
        /// Override to enable recovery mode.
        /// When true, TryRecover will be called when actions fail.
        /// If recovery was needed, the test will be marked as unstable.
        /// Default is false.
        /// </summary>
        protected virtual bool EnableRecovery => false;

        /// <summary>
        /// Override to fail the test when recovery was needed.
        /// Default is false (test passes but logs a warning).
        /// </summary>
        protected virtual bool FailOnRecovery => false;

        /// <summary>
        /// Override to keep hardware input enabled during tests.
        /// Default is true (hardware input is disabled, only simulated input works).
        /// </summary>
        protected virtual bool DisableHardwareInput => true;

        /// <summary>
        /// Override to ignore error/exception log messages during the test.
        /// When true, LogAssert.ignoreFailingMessages is set to true.
        /// Default is true (errors won't fail the test).
        /// </summary>
        protected virtual bool IgnoreErrorLogs => true;

        [SetUp]
        public virtual void SetUp()
        {
            // Disable profiler to avoid "Non-matching Profiler.EndSample" errors
            // caused by async operations crossing frame boundaries (UniTask issue)
            UnityEngine.Profiling.Profiler.enabled = false;

            Debug.Log($"[UITestBase] SetUp starting (frame {Time.frameCount})");

            // Ignore error logs by default - game errors shouldn't fail UI tests
            if (IgnoreErrorLogs)
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            }

            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = true;
                ActionExecutor.ClearCapturedExceptions();
            }

            ActionExecutor.ResetRecoveryTracking();
            ActionExecutor.ClearRecoveryFlows();

            if (EnableRecovery)
            {
                ActionExecutor.RecoveryHandler = TryRecover;
                Debug.Log("[UITestBase] Recovery enabled");
            }
            else
            {
                ActionExecutor.RecoveryHandler = null;
            }

            if (DisableHardwareInput)
            {
                InputInjector.DisableHardwareInput();
            }

            Debug.Log("[UITestBase] SetUp complete");
        }

        [TearDown]
        public virtual void TearDown()
        {
            Debug.Log($"[UITestBase] TearDown starting (frame {Time.frameCount})");

            if (ActionExecutor.RecoveryUsed)
            {
                var message = $"[UNSTABLE TEST] Recovery was used {ActionExecutor.RecoveryCount} time(s).\n" +
                              $"Reason: {ActionExecutor.RecoveryExplanation}";

                if (FailOnRecovery)
                {
                    Assert.Fail(message);
                }
                else
                {
                    Debug.LogWarning(message);
#if UNITY_INCLUDE_TESTS
                    TestContext.WriteLine(message);
#endif
                }
            }

            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = false;
            }

            ActionExecutor.RecoveryHandler = null;

            if (DisableHardwareInput)
            {
                InputInjector.EnableHardwareInput();
            }

            // Clean up any virtual devices created during the test
            InputInjector.CleanupVirtualDevices();

            // Re-enable profiler
            UnityEngine.Profiling.Profiler.enabled = true;

            Debug.Log("[UITestBase] TearDown complete");
        }


        /// <summary>
        /// Loads a scene by name and waits for it to complete.
        /// Waits for stable frame rate (default 20 FPS) before returning.
        /// </summary>
        /// <param name="sceneName">The scene name to load</param>
        /// <param name="minFps">Minimum stable frame rate before considering scene ready. Set to 0 to skip FPS check.</param>
        protected async Task LoadScene(string sceneName, float minFps = 20f)
        {
            Debug.Log($"[UITestBase] LoadScene({sceneName}) starting (frame {Time.frameCount})");
            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
            while (!op.isDone)
            {
                await Task.Yield();
            }
            Debug.Log($"[UITestBase] LoadScene({sceneName}) scene loaded (frame {Time.frameCount})");

            // Wait for stable frame rate
            if (minFps > 0)
            {
                await ActionExecutor.WaitForStableFrameRate(minFps);
            }
            else
            {
                // Just wait a few frames
                for (int i = 0; i < 3; i++)
                {
                    await Task.Yield();
                }
            }
            Debug.Log($"[UITestBase] LoadScene({sceneName}) complete (frame {Time.frameCount})");
        }

        /// <summary>
        /// Restores game state from a test data zip file.
        /// The zip can contain:
        /// - files/ folder: extracted to persistentDataPath
        /// - playerprefs.json: restored to PlayerPrefs
        ///
        /// Call this BEFORE loading the scene to ensure state is restored before scene initialization.
        /// </summary>
        /// <param name="resourcePath">Path relative to Resources folder, without extension (e.g., "TestData/SaveGame")</param>
        /// <param name="clearExistingFiles">If true, clears existing files in persistentDataPath before loading (default true)</param>
        protected async Task RestoreGameState(string resourcePath, bool clearExistingFiles = true)
        {
            await ActionExecutor.LoadTestData(resourcePath, clearExistingFiles);
            LogPersistentDataState();
        }

        /// <summary>
        /// Clears all files in Application.persistentDataPath.
        /// Does not clear PlayerPrefs (to avoid breaking Unity systems like Addressables).
        /// </summary>
        protected void ClearPersistentFiles()
        {
            var path = Application.persistentDataPath;
            Debug.Log($"[UITestBase] Clearing persistent data: {path}");
            if (System.IO.Directory.Exists(path))
            {
                foreach (var file in System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { System.IO.File.Delete(file); }
                    catch (Exception ex) { Debug.LogWarning($"[UITestBase] Failed to delete {file}: {ex.Message}"); }
                }
                foreach (var dir in System.IO.Directory.GetDirectories(path))
                {
                    try { System.IO.Directory.Delete(dir, true); }
                    catch (Exception ex) { Debug.LogWarning($"[UITestBase] Failed to delete {dir}: {ex.Message}"); }
                }
            }
            Debug.Log("[UITestBase] Persistent data files cleared");
        }

        /// <summary>
        /// Called when an action fails and recovery is enabled.
        /// Override to customize recovery behavior.
        ///
        /// Default implementation:
        /// 1. Tries DialogDismissal to click common close/cancel buttons
        /// 2. Falls back to AI recovery if configured
        ///
        /// Example override:
        /// <code>
        /// protected override async Task&lt;RecoveryResult&gt; TryRecover(RecoveryContext context)
        /// {
        ///     // Try your custom recovery first
        ///     if (await TryClickCloseButton())
        ///         return new RecoveryResult { Success = true, Explanation = "Clicked close" };
        ///
        ///     // Fall back to base implementation
        ///     return await base.TryRecover(context);
        /// }
        /// </code>
        /// </summary>
        protected virtual async Task<RecoveryResult> TryRecover(RecoveryContext context)
        {
            Debug.Log($"[UITestBase] TryRecover: {context.FailedAction}");

            // 1. Try registered detected flows (in case something just appeared)
            await ActionExecutor.RunDetectedFlows();

            // 2. Try dialog dismissal
            if (await DialogDismissal.TryDismiss())
            {
                return new RecoveryResult
                {
                    Success = true,
                    Explanation = "Dismissed dialog"
                };
            }

            // 3. Try failure flows
            if (await ActionExecutor.RunFailureFlows())
            {
                return new RecoveryResult
                {
                    Success = true,
                    Explanation = "Ran fallback recovery flow"
                };
            }

            // 4. Try AI recovery if configured
            var settings = AITestSettings.Instance;
            if (settings != null)
            {
                var provider = settings.CreateModelProvider();
                if (provider != null)
                {
                    AINavigator.SetModelProvider(provider);
                    var aiHandler = AINavigator.CreateRecoveryHandler();
                    return await aiHandler(context);
                }
            }

            return new RecoveryResult
            {
                Success = false,
                NoBlockerFound = true,
                Explanation = "No recovery method succeeded"
            };
        }

        /// <summary>
        /// Gets exceptions captured during the test (if CaptureUnobservedException is true).
        /// </summary>
        protected System.Collections.Generic.IReadOnlyList<Exception> CapturedExceptions
            => ActionExecutor.CapturedExceptions;

        /// <summary>
        /// Returns true if recovery was used during the current test.
        /// </summary>
        protected bool WasRecoveryUsed => ActionExecutor.RecoveryUsed;

        /// <summary>
        /// Gets the explanation of what recovery actions were taken.
        /// </summary>
        protected string RecoveryReason => ActionExecutor.RecoveryExplanation;

        /// <summary>
        /// Logs the current persistent data state (files and PlayerPrefs).
        /// </summary>
        private void LogPersistentDataState()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[UITestBase] === Persistent Data State ===");

            // Log files
            var path = Application.persistentDataPath;
            sb.AppendLine($"Path: {path}");

            if (System.IO.Directory.Exists(path))
            {
                var files = System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
                sb.AppendLine($"Files ({files.Length}):");
                foreach (var file in files)
                {
                    var info = new System.IO.FileInfo(file);
                    var relativePath = file.Replace(path, "").TrimStart('\\', '/');
                    var size = FormatFileSize(info.Length);
                    sb.AppendLine($"  {relativePath} ({size})");
                }
            }
            else
            {
                sb.AppendLine("  (directory does not exist)");
            }

            // Note: Unity doesn't provide a way to enumerate all PlayerPrefs keys
            sb.AppendLine("PlayerPrefs: (cannot enumerate - use Editor menu to capture known keys)");

            Debug.Log(sb.ToString());
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        /// <summary>
        /// Gets the persistent data path for the current platform.
        /// </summary>
        protected static string PersistentDataPath => Application.persistentDataPath;

        /// <summary>
        /// Checks if a file exists in persistent data.
        /// </summary>
        protected static bool PersistentFileExists(string relativePath)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, relativePath));
        }

        /// <summary>
        /// Reads a file from persistent data as text.
        /// </summary>
        protected static string ReadPersistentFile(string relativePath)
        {
            return System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, relativePath));
        }

        /// <summary>
        /// Writes text to a file in persistent data.
        /// </summary>
        protected static void WritePersistentFile(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Application.persistentDataPath, relativePath);
            var dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(fullPath, content);
        }

        /// <summary>
        /// Deletes a file from persistent data.
        /// </summary>
        protected static void DeletePersistentFile(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Application.persistentDataPath, relativePath);
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
    }
}
