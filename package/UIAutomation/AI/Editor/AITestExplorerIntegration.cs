using System.Threading.Tasks;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Integration between AI Tests and the UI Automation Explorer window.
    /// Provides discovery, creation, and running of AI tests from the explorer.
    /// </summary>
    public static class AITestExplorerIntegration
    {
        /// <summary>
        /// Find all AITest assets in the project.
        /// </summary>
        public static List<AITestInfo> FindAllAITests()
        {
            var results = new List<AITestInfo>();

            var guids = AssetDatabase.FindAssets("t:AITest");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var test = AssetDatabase.LoadAssetAtPath<AITest>(path);
                if (test != null)
                {
                    results.Add(new AITestInfo
                    {
                        AITest = test,
                        AssetPath = path,
                        DisplayName = test.name,
                        FullName = path,
                        GroupName = test.Group?.displayName ?? test.Group?.name
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Find all AITestGroup assets in the project.
        /// </summary>
        public static List<AITestGroup> FindAllAITestGroups()
        {
            var results = new List<AITestGroup>();

            var guids = AssetDatabase.FindAssets("t:AITestGroup");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var group = AssetDatabase.LoadAssetAtPath<AITestGroup>(path);
                if (group != null)
                {
                    results.Add(group);
                }
            }

            return results;
        }

        /// <summary>
        /// Create a new AITest asset.
        /// </summary>
        public static AITest CreateAITest(string folder = null, AITestGroup group = null)
        {
            folder ??= GetDefaultTestFolder();

            var test = ScriptableObject.CreateInstance<AITest>();
            test.prompt = "Describe what this test should do...";

            if (group != null)
            {
                test.SetGroup(group);
            }

            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, "NewAITest.asset"));
            AssetDatabase.CreateAsset(test, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = test;
            EditorGUIUtility.PingObject(test);

            return test;
        }

        /// <summary>
        /// Create a new AITestGroup asset.
        /// </summary>
        public static AITestGroup CreateAITestGroup(string folder = null)
        {
            folder ??= GetDefaultTestFolder();

            var group = ScriptableObject.CreateInstance<AITestGroup>();
            group.displayName = "New Test Group";
            group.description = "Description of this test group...";

            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, "NewAITestGroup.asset"));
            AssetDatabase.CreateAsset(group, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = group;
            EditorGUIUtility.PingObject(group);

            return group;
        }

        /// <summary>
        /// Run an AI test and return the result.
        /// </summary>
        public static async Task<AITestResult> RunAITestAsync(AITest test)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AITest] Cannot run AI tests outside of Play mode.");
                return null;
            }

            var settings = AITestSettings.Instance;
            var config = settings.CreateRunnerConfig();
            var globalKnowledge = settings.GetGlobalKnowledge();

            var runner = new AITestRunner(test, globalKnowledge, config);

            if (settings.showDebugPanel)
            {
                AIDebugPanel.ShowWindow();
            }

            AITestResult result;
            try
            {
                result = await runner.RunAsync();
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AITest] Test was cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AITest] Test failed with exception: {ex}");
                return null;
            }

            // Check if we're still in play mode before saving
            if (!Application.isPlaying)
            {
                Debug.Log("[AITest] Play mode exited before result could be saved");
                return result;
            }

            // Save result
            try
            {
                var run = AITestRun.FromResult(result, AssetDatabase.GetAssetPath(test));
                AITestResultStore.Instance.SaveRun(run, result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AITest] Failed to save result: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Get the last run result for a test.
        /// </summary>
        public static AITestRunStatus GetLastRunStatus(AITest test)
        {
            var path = AssetDatabase.GetAssetPath(test);
            var store = AITestResultStore.Instance;

            var query = new ResultQuery
            {
                TestName = test.name,
                Limit = 1,
                SortBy = SortBy.Newest
            };

            var lastRun = store.QueryRuns(query).FirstOrDefault();
            if (lastRun == null)
            {
                return new AITestRunStatus { Status = AITestStatus.NotRun };
            }

            return new AITestRunStatus
            {
                Status = ConvertStatus(lastRun.status),
                Duration = lastRun.duration,
                Timestamp = lastRun.Timestamp,
                RunId = lastRun.id
            };
        }

        private static AITestStatus ConvertStatus(TestStatus status)
        {
            return status switch
            {
                TestStatus.Passed => AITestStatus.Passed,
                TestStatus.Failed => AITestStatus.Failed,
                TestStatus.Error => AITestStatus.Error,
                TestStatus.TimedOut => AITestStatus.TimedOut,
                TestStatus.Cancelled => AITestStatus.Cancelled,
                TestStatus.MaxActionsReached => AITestStatus.Failed,
                _ => AITestStatus.NotRun
            };
        }

        private static string GetDefaultTestFolder()
        {
            // Try to find an existing AI Tests folder
            var folders = AssetDatabase.FindAssets("AITests t:folder");
            if (folders.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(folders[0]);
            }

            // Fall back to Assets folder
            return "Assets";
        }
    }

    /// <summary>
    /// Information about an AI test for display in the explorer.
    /// </summary>
    public class AITestInfo
    {
        public AITest AITest { get; set; }
        public string AssetPath { get; set; }
        public string DisplayName { get; set; }
        public string FullName { get; set; }
        public string GroupName { get; set; }
    }

    /// <summary>
    /// Status of the last run for an AI test.
    /// </summary>
    public class AITestRunStatus
    {
        public AITestStatus Status { get; set; }
        public float Duration { get; set; }
        public System.DateTime Timestamp { get; set; }
        public string RunId { get; set; }
    }

    /// <summary>
    /// Simple status enum for AI tests (mirrors TestStatus but for external use).
    /// </summary>
    public enum AITestStatus
    {
        NotRun,
        Running,
        Passed,
        Failed,
        Error,
        TimedOut,
        Cancelled
    }
}
#endif
