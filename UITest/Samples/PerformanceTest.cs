using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Demonstrates performance testing capabilities.
    /// Shows framerate monitoring, performance tracking, and scene load timing.
    /// </summary>
    [UITest(
        Scenario = 9005,
        Feature = "Performance",
        Story = "Application maintains acceptable performance",
        Severity = TestSeverity.Critical,
        Description = "Tests framerate stability, scene load times, and tracks performance metrics",
        Tags = new[] { "sample", "performance", "framerate" },
        TimeoutSeconds = 300
    )]
    public class PerformanceTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            string startScene = SceneManager.GetActiveScene().name;
            AddParameter("start_scene", startScene);

            // Step 1: Measure initial framerate
            using (BeginStep("Measure Initial Framerate"))
            {
                // Wait for framerate to stabilize at 30+ fps
                await WaitFramerate(averageFps: 30, sampleDuration: 2f, timeout: 30f);
                LogStep("Initial framerate meets 30fps threshold");

                float currentFps = 1f / Time.smoothDeltaTime;
                AddParameter("initial_fps", currentFps.ToString("F1"));
            }

            // Step 2: Track UI navigation performance
            using (BeginStep("Track Navigation Performance"))
            {
                using (TrackPerformance("UI Navigation"))
                {
                    // Click through some menus
                    await ClickAny("*Settings*", "*Options*", "*Menu*");
                    await Wait(1);
                    await ClickAny("*Back*", "*Close*", "*Return*");
                    await Wait(1);
                }
                LogStep("Navigation performance tracked");
            }

            // Step 3: Stress test with rapid interactions
            using (BeginStep("Rapid Interaction Stress Test"))
            {
                using (TrackPerformance("Rapid Clicks"))
                {
                    // Find any clickable button and click it rapidly
                    var button = await Find<Component>(
                        new[] { "*Button*", "*Btn*" },
                        throwIfMissing: false,
                        seconds: 3
                    );

                    if (button != null)
                    {
                        // Rapid clicks with short intervals
                        int originalInterval = Interval;
                        Interval = 100; // 100ms between clicks

                        await Click(button.name, repeat: 10);

                        Interval = originalInterval;
                        LogStep("Rapid click stress test completed");
                    }
                }

                // Verify framerate didn't drop too much
                await WaitFramerate(averageFps: 25, sampleDuration: 1f, timeout: 10f);
                LogStep("Framerate recovered after stress test");
            }

            // Step 4: Scene load performance (if applicable)
            using (BeginStep("Scene Load Performance"))
            {
                var playButton = await Find<Component>(
                    new[] { "*Play*", "*Start*", "*Begin*" },
                    throwIfMissing: false,
                    seconds: 3
                );

                if (playButton != null)
                {
                    using (TrackPerformance("Scene Load"))
                    {
                        await Click(playButton.name);
                        await SceneChange(seconds: 60);
                    }

                    string newScene = SceneManager.GetActiveScene().name;
                    AddParameter("loaded_scene", newScene);
                    LogStep($"Scene changed to: {newScene}");

                    // Wait for new scene to stabilize
                    await WaitFramerate(averageFps: 30, sampleDuration: 3f, timeout: 60f);
                    LogStep("New scene framerate stable");
                }
                else
                {
                    LogStep("No play button found - skipping scene load test");
                }
            }

            // Step 5: Extended playability test
            using (BeginStep("Extended Play Test"))
            {
                using (TrackPerformance("Extended Play"))
                {
                    // Simulate playing for a bit
                    await SimulatePlay(seconds: 10);
                }

                float finalFps = 1f / Time.smoothDeltaTime;
                AddParameter("final_fps", finalFps.ToString("F1"));
                LogStep($"Final framerate: {finalFps:F1} fps");
            }

            CaptureScreenshot("performance_test_complete");
            LogStep("Performance test completed");
        }
    }
}
