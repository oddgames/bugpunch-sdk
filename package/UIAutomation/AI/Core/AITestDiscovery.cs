using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Discovers all AITest ScriptableObjects and runs them as NUnit test cases.
    /// Each AITest asset appears as a separate test in the Unity Test Runner.
    ///
    /// This allows creating tests purely through ScriptableObjects without writing
    /// individual test script files. Just create AITest assets and they automatically
    /// appear in the Test Runner.
    ///
    /// Tests appear as:
    /// <code>
    /// AI
    ///   └─ LoginTest
    ///   └─ MainMenuTest
    ///   └─ ...
    /// </code>
    /// </summary>
#if UNITY_INCLUDE_TESTS
    [TestFixture]
    [Category("AI")]
    public class AI
    {
        /// <summary>
        /// Discovers all AITest assets in the project.
        /// Called by NUnit to generate test cases.
        /// </summary>
        public static IEnumerable<TestCaseData> AllAITests()
        {
#if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets("t:AITest");
            Debug.Log($"[AITestDiscovery] Found {guids.Length} AITest assets");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var test = AssetDatabase.LoadAssetAtPath<AITest>(path);

                if (test == null)
                {
                    Debug.LogWarning($"[AITestDiscovery] Failed to load: {path}");
                    continue;
                }

                if (string.IsNullOrEmpty(test.prompt))
                {
                    Debug.Log($"[AITestDiscovery] Skipping '{test.name}' - no prompt");
                    continue;
                }

                Debug.Log($"[AITestDiscovery] Discovered: {test.name}");
                yield return new TestCaseData(test).SetName(test.name);
            }
#else
            yield break;
#endif
        }

        /// <summary>
        /// Runs a single AITest ScriptableObject as an NUnit test.
        /// </summary>
        [Test, TestCaseSource(nameof(AllAITests))]
        public async Task Test(AITest testConfig)
        {
            Assert.IsNotNull(testConfig, "Test config is null");
            Assert.IsFalse(string.IsNullOrEmpty(testConfig.prompt), "Test prompt is empty");

            // Get settings and create model provider
            var settings = AITestSettings.Instance;
            Assert.IsNotNull(settings, "AITestSettings not found. Configure in Project Settings > UI Automation > AI Testing");

            var modelId = !string.IsNullOrEmpty(testConfig.model) ? testConfig.model : settings.GetEffectiveModel();
            var provider = settings.CreateModelProvider(modelId);
            Assert.IsNotNull(provider, "Could not create model provider. Check API key in AITestSettings.");

            // Create runner with config from settings
            var runner = new AITestRunner(
                testConfig,
                settings.GetGlobalKnowledge(),
                new AITestRunnerConfig
                {
                    ModelProvider = provider,
                    SendScreenshots = true,
                    EnableDebugLogging = settings.verboseLogging
                });

            // Run the test
            var result = await runner.RunAsync();

            // Log summary
            Debug.Log($"[AITestDiscovery] {testConfig.name}: {result.Status} - {result.Message} " +
                      $"(actions: {result.ActionCount}, duration: {result.DurationSeconds:F1}s)");

            // Assert based on result
            switch (result.Status)
            {
                case TestStatus.Passed:
                    Assert.Pass(result.Message);
                    break;
                case TestStatus.Failed:
                    Assert.Fail(result.Message);
                    break;
                case TestStatus.TimedOut:
                    Assert.Fail($"Timeout after {testConfig.timeoutSeconds}s: {result.Message}");
                    break;
                case TestStatus.MaxActionsReached:
                    Assert.Fail($"Max actions ({testConfig.maxActions}) reached: {result.Message}");
                    break;
                case TestStatus.Error:
                    Assert.Fail($"Error: {result.Message}");
                    break;
                case TestStatus.Cancelled:
                    Assert.Inconclusive("Test was cancelled");
                    break;
            }
        }
    }
#endif
}
