using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Adds a menu item to copy all failed test logs to clipboard.
    /// Works with Unity's built-in Test Runner.
    /// </summary>
    public static class CopyFailedTestsMenu
    {
        private static TestRunnerApi _api;
        private static ITestResultAdaptor _lastRunResult;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var callbacks = new CopyFailedCallbacks();
            callbacks.OnRunFinished += result => _lastRunResult = result;
            _api.RegisterCallbacks(callbacks);
        }

        [MenuItem("Window/General/Copy Failed Tests to Clipboard %#c")] // Ctrl+Shift+C (Cmd+Shift+C on Mac)
        public static void CopyFailedTests()
        {
            if (_lastRunResult == null)
            {
                Debug.Log("No test results available. Run tests first.");
                return;
            }

            var failed = CollectFailedTests(_lastRunResult);
            if (failed.Count == 0)
            {
                Debug.Log("No failed tests to copy.");
                return;
            }

            var sb = new StringBuilder();
            foreach (var result in failed)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("================================================================================");
                    sb.AppendLine();
                }

                sb.AppendLine($"TEST: {result.Test.FullName}");
                sb.AppendLine($"STATUS: {result.TestStatus}");
                sb.AppendLine($"DURATION: {result.Duration:F2}s");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(result.Message))
                {
                    sb.AppendLine("MESSAGE:");
                    sb.AppendLine(result.Message);
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(result.StackTrace))
                {
                    sb.AppendLine("STACKTRACE:");
                    sb.AppendLine(result.StackTrace);
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(result.Output))
                {
                    sb.AppendLine("OUTPUT:");
                    sb.AppendLine(result.Output);
                }
            }

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"Copied {failed.Count} failed test(s) to clipboard.");
        }

        private static System.Collections.Generic.List<ITestResultAdaptor> CollectFailedTests(ITestResultAdaptor result)
        {
            var failed = new System.Collections.Generic.List<ITestResultAdaptor>();
            CollectFailedTestsRecursive(result, failed);
            return failed;
        }

        private static void CollectFailedTestsRecursive(ITestResultAdaptor result, System.Collections.Generic.List<ITestResultAdaptor> failed)
        {
            if (result == null) return;

            // Only collect leaf tests (not suites)
            if (!result.Test.IsSuite && result.TestStatus == TestStatus.Failed)
            {
                failed.Add(result);
            }

            // Recurse into children
            foreach (var child in result.Children)
            {
                CollectFailedTestsRecursive(child, failed);
            }
        }

        private class CopyFailedCallbacks : ICallbacks
        {
            public event System.Action<ITestResultAdaptor> OnRunFinished;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void RunFinished(ITestResultAdaptor result) => OnRunFinished?.Invoke(result);
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
