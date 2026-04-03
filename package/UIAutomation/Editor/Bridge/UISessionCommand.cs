using System;
using System.Threading.Tasks;
using clibridge4unity;

namespace ODDGames.UIAutomation.Bridge
{
    public static class UISessionCommand
    {
        [BridgeCommand("UISESSION", "Start or stop a UI automation test session",
            Category = "UIAutomation",
            Usage = "UISESSION start [--name TestName] [--desc \"Navigate to settings and verify\"]\n" +
                    "  UISESSION stop\n" +
                    "  Starts a recording session. Each UIACTION auto-captures a JPEG screenshot.\n" +
                    "  Generates a live HTML report at %TEMP%/clibridge4unity/sessions/{id}/index.html\n" +
                    "  Use 'clibridge4unity serve' to view reports over HTTP.",
            RequiresMainThread = false,
            TimeoutSeconds = 30)]
        public static async Task<string> UISession(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return Response.Error("Usage: UISESSION start [--name sessionName] | UISESSION stop");

            var parts = data.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var subcommand = parts[0].ToLowerInvariant();

            switch (subcommand)
            {
                case "start":
                    return await HandleStart(parts);
                case "stop":
                    return await HandleStop();
                default:
                    return Response.Error($"Unknown subcommand: {subcommand}. Use 'start' or 'stop'.");
            }
        }

        static async Task<string> HandleStart(string[] parts)
        {
            if (UISessionState.IsActive)
                return Response.Error("A session is already active. Stop it first with UISESSION stop.");

            // Parse --name and --desc arguments
            string name = null;
            string desc = null;
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i] == "--name" && i + 1 < parts.Length)
                {
                    name = parts[++i];
                }
                else if (parts[i] == "--desc" && i + 1 < parts.Length)
                {
                    // Collect remaining tokens as description (may be quoted)
                    desc = string.Join(" ", parts, i + 1, parts.Length - i - 1).Trim('"');
                    break;
                }
            }

            var (sessionId, sessionDir) = await UISessionState.StartSession(name, desc);

            var htmlPath = System.IO.Path.Combine(sessionDir, "index.html");
            return Response.Success(
                $"Session started: {sessionId}\n" +
                $"Directory: {sessionDir}\n" +
                $"Report: file:///{htmlPath.Replace('\\', '/')}");
        }

        static async Task<string> HandleStop()
        {
            if (!UISessionState.IsActive)
                return Response.Error("No active session to stop.");

            var sessionDir = await UISessionState.StopSession();
            if (sessionDir == null)
                return Response.Error("Failed to stop session.");

            var htmlPath = System.IO.Path.Combine(sessionDir, "index.html");
            return Response.Success(
                $"Session stopped.\n" +
                $"Directory: {sessionDir}\n" +
                $"Report: file:///{htmlPath.Replace('\\', '/')}");
        }
    }
}