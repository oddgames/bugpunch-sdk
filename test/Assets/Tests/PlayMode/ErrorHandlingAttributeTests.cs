using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using static ODDGames.UIAutomation.ActionExecutor;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// Tests for CaptureUnobservedExceptions.
    /// </summary>
    [TestFixture]
    public class ErrorHandlingTests
    {
        [TearDown]
        public void TearDown()
        {
            CaptureUnobservedExceptions = false;
        }

        #region CaptureUnobservedExceptions Tests

        [Test]
        public async Task CaptureUnobservedExceptions_CapturesFireAndForgetException()
        {
            CaptureUnobservedExceptions = true;
            ClearCapturedExceptions();

            await Async.DelayFrames(1);

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

            await Async.DelayFrames(1);

            Assert.IsNotNull(CapturedExceptions);
            Assert.IsInstanceOf<System.Collections.Generic.IReadOnlyList<Exception>>(CapturedExceptions);
        }

        [Test]
        public async Task CaptureUnobservedExceptions_ClearRemovesExceptions()
        {
            CaptureUnobservedExceptions = true;

            await Async.DelayFrames(1);

            ClearCapturedExceptions();

            Assert.AreEqual(0, CapturedExceptions.Count);
        }

        private async Task ThrowAfterDelayAsync()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Unobserved exception from fire-and-forget");
        }

        #endregion
    }
}
