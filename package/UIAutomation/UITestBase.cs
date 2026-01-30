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
    /// - Automatic scene loading with error log suppression
    /// - Configurable recovery handlers (AI or coded) for handling dialogs/obstacles
    /// - Unobserved exception capture
    /// - Automatic test instability detection when recovery was needed
    ///
    /// Usage:
    /// <code>
    /// public class MyTests : UITestBase
    /// {
    ///     protected override string SceneName => "MainMenu";
    ///     protected override bool EnableRecovery => true;
    ///
    ///     [Test]
    ///     public async Task TestSomething()
    ///     {
    ///         await Click(Name("StartButton"));
    ///     }
    /// }
    /// </code>
    ///
    /// Recovery uses AITestSettings for AI mode. Configure in:
    /// - Editor: Project Settings > UI Automation > AI Testing
    /// - Runtime: AITestSettings.SetInstance() or place in Resources folder
    /// </summary>
    public abstract class UITestBase
    {
        /// <summary>
        /// Override to specify the scene to load before each test.
        /// Return null to skip scene loading.
        /// </summary>
        protected virtual string SceneName => null;

        /// <summary>
        /// Override to specify timeout for scene loading in seconds.
        /// </summary>
        protected virtual float SceneLoadTimeout => 30f;

        /// <summary>
        /// Override to return false if you want error logs to fail tests.
        /// Default is true (errors are ignored).
        /// </summary>
        protected virtual bool SuppressErrorLogs => true;

        /// <summary>
        /// Override to return false if you don't want to capture unobserved exceptions.
        /// Default is true.
        /// </summary>
        protected virtual bool CaptureUnobservedException => true;

        /// <summary>
        /// Override to enable recovery mode.
        /// When true, the RecoveryHandler will be invoked when actions fail.
        /// If recovery was needed, the test will be marked as unstable.
        /// Default is false.
        /// </summary>
        protected virtual bool EnableRecovery => false;

        /// <summary>
        /// Override to fail the test when recovery was needed.
        /// Default is false (test passes but logs a warning).
        /// </summary>
        protected virtual bool FailOnRecovery => false;

        // Legacy aliases
        protected virtual bool EnableAINavigation => EnableRecovery;
        protected virtual bool FailOnAINavigation => FailOnRecovery;

        [SetUp]
        public virtual async Task SetUp()
        {
#if UNITY_INCLUDE_TESTS
            // Suppress error logs (must be set within test context)
            if (SuppressErrorLogs)
            {
                LogAssert.ignoreFailingMessages = true;
            }
#endif

            // Enable unobserved exception capture
            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = true;
                ActionExecutor.ClearCapturedExceptions();
            }

            // Configure recovery
            ActionExecutor.ResetRecoveryTracking();

            if (EnableRecovery)
            {
                ConfigureRecoveryHandler();
            }
            else
            {
                ActionExecutor.RecoveryHandler = null;
            }

            // Load scene if specified
            if (!string.IsNullOrEmpty(SceneName))
            {
                await ActionExecutor.EnsureSceneLoaded(SceneName, SceneLoadTimeout);

                // Wait a frame for scene to stabilize
                await Task.Yield();

                // Dismiss any dialogs that appeared during load
                await DismissDialogs();
            }
        }

        [TearDown]
        public virtual void TearDown()
        {
            // Check if recovery was used
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
                    // Log warning but let test pass
                    Debug.LogWarning(message);
#if UNITY_INCLUDE_TESTS
                    // Add to test result output
                    TestContext.WriteLine(message);
#endif
                }
            }

#if UNITY_INCLUDE_TESTS
            // Restore error log behavior
            if (SuppressErrorLogs)
            {
                LogAssert.ignoreFailingMessages = false;
            }
#endif

            // Disable exception capture
            if (CaptureUnobservedException)
            {
                ActionExecutor.CaptureUnobservedExceptions = false;
            }

            // Clear recovery handler
            ActionExecutor.RecoveryHandler = null;
        }

        /// <summary>
        /// Override to dismiss any dialogs that appear during scene load.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual Task DismissDialogs()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Configures the recovery handler. Override to provide a custom handler.
        /// Default implementation uses AI recovery via AITestSettings.
        ///
        /// Example custom handler:
        /// <code>
        /// protected override void ConfigureRecoveryHandler()
        /// {
        ///     ActionExecutor.RecoveryHandler = async (context) =>
        ///     {
        ///         // Check for common dialogs
        ///         var closeBtn = Search.Name("CloseButton").FindFirst();
        ///         if (closeBtn != null)
        ///         {
        ///             await ActionExecutor.Click(Search.Name("CloseButton"));
        ///             return new RecoveryResult { Success = true, Explanation = "Closed dialog" };
        ///         }
        ///         return new RecoveryResult { Success = false, NoBlockerFound = true };
        ///     };
        /// }
        /// </code>
        /// </summary>
        protected virtual void ConfigureRecoveryHandler()
        {
            var settings = AITestSettings.Instance;
            if (settings == null)
            {
                Debug.LogWarning("[UITestBase] Recovery enabled but AITestSettings not found. " +
                    "Configure in Project Settings or call AITestSettings.SetInstance().");
                return;
            }

            var provider = settings.CreateModelProvider();
            if (provider != null)
            {
                AINavigator.SetModelProvider(provider);
                ActionExecutor.RecoveryHandler = AINavigator.CreateRecoveryHandler();
                Debug.Log($"[UITestBase] AI recovery configured with model: {settings.GetEffectiveModel()}");
            }
            else
            {
                Debug.LogWarning("[UITestBase] Recovery enabled but no API key configured in AITestSettings.");
            }
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

        // Legacy aliases
        protected bool WasAINavigationUsed => WasRecoveryUsed;
        protected string AINavigationReason => RecoveryReason;
    }
}
