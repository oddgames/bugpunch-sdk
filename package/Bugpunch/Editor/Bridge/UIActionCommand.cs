using System.Threading;
using System.Threading.Tasks;
using clibridge4unity;
using ODDGames.Bugpunch;

namespace ODDGames.Bugpunch.Bridge
{
    public static class UIActionCommand
    {
        // Serialize all UI actions — one at a time, like tests do
        static readonly SemaphoreSlim _actionLock = new SemaphoreSlim(1, 1);

        [BridgeCommand("UIACTION", "Execute a UI automation action (JSON format)",
            Category = "Bugpunch",
            Usage = "UIACTION {\"action\":\"click\", \"text\":\"Settings\"}\n" +
                    "  UIACTION {\"action\":\"click\", \"name\":\"Button\"}\n" +
                    "  UIACTION {\"action\":\"click\", \"at\":[0.5, 0.5]}\n" +
                    "  UIACTION {\"action\":\"type\", \"name\":\"InputField\", \"value\":\"hello\"}\n" +
                    "  UIACTION {\"action\":\"key\", \"key\":\"space\"}\n" +
                    "  UIACTION {\"action\":\"swipe\", \"direction\":\"left\"}\n" +
                    "  UIACTION {\"action\":\"scroll\", \"name\":\"List\", \"delta\":-120}\n" +
                    "  UIACTION {\"action\":\"wait\", \"seconds\":2}\n" +
                    "  UIACTION {\"action\":\"hold\", \"text\":\"Button\", \"seconds\":2}\n" +
                    "  UIACTION {\"action\":\"drag\", \"from\":{\"name\":\"A\"}, \"to\":{\"name\":\"B\"}}\n" +
                    "  UIACTION {\"action\":\"dropdown\", \"name\":\"DD\", \"option\":2}\n" +
                    "  UIACTION {\"action\":\"pinch\", \"scale\":2.0}\n" +
                    "  UIACTION {\"action\":\"rotate\", \"degrees\":45}\n" +
                    "  Search fields: text, name, type, near, adjacent, tag, path, any, at\n" +
                    "  Actions are queued and executed one at a time.\n" +
                    "  If a UISESSION is active, screenshots are auto-captured after each action.",
            RequiresMainThread = false,
            TimeoutSeconds = 30)]
        public static async Task<string> UIAction(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return Response.Error("Expected JSON. Example: {\"action\":\"click\", \"text\":\"Settings\"}");

            if (!UnityEditor.EditorApplication.isPlaying)
                return Response.Error("Not in play mode. Use PLAY first.");

            // Wait for our turn in the queue
            await _actionLock.WaitAsync();
            try
            {
                // Bridge calls prefer fast failure + retry over long waits
                ActionExecutor.DefaultSearchTime = 1f;

                // Execute on main thread
                var tcs = new TaskCompletionSource<ActionResult>();
                await CommandRegistry.RunOnMainThreadAsync<int>(() =>
                {
                    ActionExecutor.Execute(data).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            tcs.SetException(t.Exception.InnerException ?? t.Exception);
                        else
                            tcs.SetResult(t.Result);
                    });
                    return 0;
                });

                var result = await tcs.Task;

                // Log to active session if one exists
                if (UISessionState.IsActive)
                {
                    try
                    {
                        await UISessionState.LogAction(data, result.Success, result.Error, result.ElapsedMs);
                    }
                    catch { }
                }

                if (!result.Success)
                    return Response.Error($"{result.Error} ({result.ElapsedMs:F0}ms)");

                return Response.Success($"OK ({result.ElapsedMs:F0}ms)");
            }
            finally
            {
                _actionLock.Release();
            }
        }
    }
}
