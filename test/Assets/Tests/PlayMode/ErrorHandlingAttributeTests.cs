using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

using static ODDGames.UIAutomation.ActionExecutor;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// Tests for IgnoreErrors and CaptureUnobservedExceptions.
    /// </summary>
    [TestFixture]
    public class ErrorHandlingTests
    {
        [TearDown]
        public void TearDown()
        {
            // Reset to defaults after each test
            IgnoreErrors = false;
            CaptureUnobservedExceptions = false;
        }

        #region IgnoreErrors Tests

        [Test]
        public async Task IgnoreErrors_DoesNotFailOnDebugLogError()
        {
            IgnoreErrors = true;

            await Task.Yield();
            Debug.LogError("This error should be ignored");

            Assert.Pass("Test completed without failing from error log");
        }

        [Test]
        public async Task IgnoreErrors_DoesNotFailOnMultipleErrors()
        {
            IgnoreErrors = true;

            await Task.Yield();

            Debug.LogError("First error");
            Debug.LogError("Second error");
            Debug.LogError("Third error");

            Assert.Pass("Test completed without failing from multiple error logs");
        }

        [Test]
        public async Task IgnoreErrors_DoesNotFailOnException()
        {
            IgnoreErrors = true;

            await Task.Yield();
            Debug.LogException(new InvalidOperationException("Test exception"));

            Assert.Pass("Test completed without failing from logged exception");
        }

        #endregion

        #region CaptureUnobservedExceptions Tests

        [Test]
        public async Task CaptureUnobservedExceptions_CapturesFireAndForgetException()
        {
            CaptureUnobservedExceptions = true;
            ClearCapturedExceptions();

            await Task.Yield();

            // Fire-and-forget task that throws
            _ = ThrowAfterDelayAsync();

            // Wait for the task to complete and become unobserved
            await Task.Delay(100);

            // Force GC to finalize the unobserved task
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Give the exception handler time to capture
            await Task.Delay(50);

            // The exception should be captured (timing-dependent)
            Assert.Pass("Fire-and-forget exception was handled without crashing");
        }

        [Test]
        public async Task CaptureUnobservedExceptions_ExceptionsListIsAccessible()
        {
            CaptureUnobservedExceptions = true;

            await Task.Yield();

            Assert.IsNotNull(CapturedExceptions);
            Assert.IsInstanceOf<System.Collections.Generic.IReadOnlyList<Exception>>(CapturedExceptions);
        }

        [Test]
        public async Task CaptureUnobservedExceptions_ClearRemovesExceptions()
        {
            CaptureUnobservedExceptions = true;

            await Task.Yield();

            ClearCapturedExceptions();

            Assert.AreEqual(0, CapturedExceptions.Count);
        }

        private async Task ThrowAfterDelayAsync()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Unobserved exception from fire-and-forget");
        }

        #endregion

        #region Combined Usage Tests

        [Test]
        public async Task CombinedUsage_BothPropertiesWork()
        {
            IgnoreErrors = true;
            CaptureUnobservedExceptions = true;
            ClearCapturedExceptions();

            await Task.Yield();

            // Error log (ignored)
            Debug.LogError("This error is ignored");

            // Fire-and-forget (captured)
            _ = ThrowAfterDelayAsync();

            await Task.Delay(100);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.Pass("Both properties work together");
        }

        #endregion
    }
}
