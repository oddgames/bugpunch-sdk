using System.Threading;
using System.Threading.Tasks;
using clibridge4unity;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Bridge
{
    public static class UIActionCommand
    {
        // Serialize all UI actions — one at a time, like tests do
        static readonly SemaphoreSlim _actionLock = new SemaphoreSlim(1, 1);

        [BridgeCommand("UIACTION", "Execute a UI automation action (JSON format)",
            Category = "UIAutomation",
            Usage = "UIACTION {\"action\":\"click\", \"text\":\"Settings\"}\n" +
                    "  UIACTION {\"action\":\"type\", \"name\":\"InputField\", \"value\":\"hello\"}\n" +
                    "  UIACTION {\"action\":\"swipe\", \"direction\":\"left\"}\n" +
                    "  UIACTION {\"action\":\"wait\", \"seconds\":2}\n" +
                    "  UIACTION {\"action\":\"key\", \"key\":\"escape\"}\n" +
                    "  UIACTION {\"action\":\"drag\", \"from\":{\"name\":\"A\"}, \"to\":{\"name\":\"B\"}}\n" +
                    "  UIACTION {\"action\":\"dropdown\", \"name\":\"DD\", \"option\":2}",
            RequiresMainThread = false,
            TimeoutSeconds = 30)]
        public static async Task<string> UIAction(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return Response.Error("Expected JSON. Example: {\"action\":\"click\", \"text\":\"Settings\"}");

            // Wait for our turn in the queue
            await _actionLock.WaitAsync();
            try
            {
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
