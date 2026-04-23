#if UNITY_INCLUDE_TESTS
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Base class for UI automation tests. Follows Unity's InputTestFixture pattern.
    /// Provides:
    /// - Input system setup/teardown (virtual devices, hardware isolation)
    /// - Unobserved exception capture
    /// - Scene loading with frame rate stabilization
    ///
    /// Usage:
    /// <code>
    /// public class MyTests : UIAutomationTestFixture
    /// {
    ///     [Test]
    ///     public async Task TestSomething()
    ///     {
    ///         await LoadScene("MainMenu");
    ///         await Click(Name("StartButton"));
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class UIAutomationTestFixture
    {
        private Camera _fixtureCamera;

        /// <summary>
        /// Override to return false if you don't want to capture unobserved exceptions.
        /// Default is true.
        /// </summary>
        protected virtual bool CaptureUnobservedException => true;

        /// <summary>
        /// Override to keep hardware input enabled during tests.
        /// Default is true (hardware input is disabled, only simulated input works).
        /// </summary>
        protected virtual bool DisableHardwareInput => true;

        [SetUp]
        public virtual void SetUp()
        {

            // Ensure a camera exists so recordings/screenshots have proper depth buffer.
            // Tests that need a specific camera setup can destroy this and create their own.
            if (Camera.allCamerasCount == 0)
            {
                var camGO = new GameObject("TestCamera");
                _fixtureCamera = camGO.AddComponent<Camera>();
                _fixtureCamera.clearFlags = CameraClearFlags.SolidColor;
                _fixtureCamera.backgroundColor = Color.black;
                _fixtureCamera.depth = -100;
            }

            // Start diagnostic session (no-op in batch mode)
            TestReport.StartSession(TestContext.CurrentContext.Test.Name);

            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = true;
                ActionExecutor.ClearCapturedExceptions();
            }

            if (DisableHardwareInput)
            {
                InputInjector.Setup();
            }

        }

        [TearDown]
        public virtual async Task TearDown()
        {
            Debug.Log($"[UIAutomationTestFixture] TearDown starting (frame {Time.frameCount})");

            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = false;
            }

            if (DisableHardwareInput)
            {
                InputInjector.TearDown();
            }

            // End diagnostic session last — awaits upload so it completes before next test
            await TestReport.EndSession();

            // Destroy the fixture camera if we created one
            if (_fixtureCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(_fixtureCamera.gameObject);
                _fixtureCamera = null;
            }

            Debug.Log("[UIAutomationTestFixture] TearDown complete");
        }

        /// <summary>
        /// Loads a scene by name and waits for it to complete.
        /// Waits for stable frame rate (default 20 FPS) before returning.
        /// </summary>
        /// <param name="sceneName">The scene name to load</param>
        /// <param name="minFps">Minimum stable frame rate before considering scene ready. Set to 0 to skip FPS check.</param>
        protected async Task LoadScene(string sceneName, float minFps = 20f)
        {
            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
            while (!op.isDone)
            {
                await Task.Yield();
            }

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
        }

        /// <summary>
        /// Clears all files in Application.persistentDataPath.
        /// Does not clear PlayerPrefs (to avoid breaking Unity systems like Addressables).
        /// </summary>
        protected void ClearPersistentFiles()
        {
            var path = Application.persistentDataPath;
            Debug.Log($"[UIAutomationTestFixture] Clearing persistent data: {path}");
            if (System.IO.Directory.Exists(path))
            {
                foreach (var file in System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { System.IO.File.Delete(file); }
                    catch (Exception ex) { Debug.LogWarning($"[UIAutomationTestFixture] Failed to delete {file}: {ex.Message}"); }
                }
                foreach (var dir in System.IO.Directory.GetDirectories(path))
                {
                    try { System.IO.Directory.Delete(dir, true); }
                    catch (Exception ex) { Debug.LogWarning($"[UIAutomationTestFixture] Failed to delete {dir}: {ex.Message}"); }
                }
            }
            Debug.Log("[UIAutomationTestFixture] Persistent data files cleared");
        }

        /// <summary>
        /// Gets exceptions captured during the test (if CaptureUnobservedException is true).
        /// </summary>
        protected System.Collections.Generic.IReadOnlyList<Exception> CapturedExceptions
            => ActionExecutor.CapturedExceptions;

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
#endif
