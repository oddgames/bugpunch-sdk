using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using clibridge4unity;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Bridge
{
    /// <summary>
    /// Manages UI automation session state: action log, screenshots, and HTML report generation.
    /// Uses SessionState for persistence across domain reloads.
    /// </summary>
    public static class UISessionState
    {
        const string SK_SessionId = "UISession_Id";
        const string SK_SessionDir = "UISession_Dir";
        const string SK_SessionName = "UISession_Name";
        const string SK_SessionScene = "UISession_Scene";
        const string SK_SessionStart = "UISession_Start";
        const string SK_ActionCount = "UISession_ActionCount";
        const string SK_ActionsJson = "UISession_ActionsJson";

        // Cached on any thread — set/cleared in StartSession/StopSession, restored after domain reload
        static volatile bool _isActive;
        static string _description;

        /// <summary>Thread-safe check whether a session is active (no main thread required).</summary>
        public static bool IsActive => _isActive;

        [UnityEditor.InitializeOnLoadMethod]
        static void RestoreAfterDomainReload()
        {
            // SessionState survives domain reloads; restore the cached flag
            _isActive = !string.IsNullOrEmpty(SessionState.GetString(SK_SessionId, ""));
        }

        /// <summary>
        /// Start a new session. Must be called via RunOnMainThreadAsync (SessionState needs main thread).
        /// </summary>
        public static async Task<(string sessionId, string sessionDir)> StartSession(string name, string description = null)
        {
            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var baseDir = Path.Combine(Path.GetTempPath(), "clibridge4unity", "sessions", sessionId);
            var screenshotDir = Path.Combine(baseDir, "screenshots");
            Directory.CreateDirectory(screenshotDir);

            var sceneName = await CommandRegistry.RunOnMainThreadAsync(() =>
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            var startTime = DateTime.UtcNow.ToString("o");

            await CommandRegistry.RunOnMainThreadAsync(() =>
            {
                SessionState.SetString(SK_SessionId, sessionId);
                SessionState.SetString(SK_SessionDir, baseDir);
                SessionState.SetString(SK_SessionName, name ?? "");
                SessionState.SetString(SK_SessionScene, sceneName);
                SessionState.SetString(SK_SessionStart, startTime);
                SessionState.SetInt(SK_ActionCount, 0);
                SessionState.SetString(SK_ActionsJson, "[]");
                return 0;
            });

            _isActive = true;
            _description = description;
            RegenerateReportFiles(sessionId, baseDir, name ?? "", sceneName, startTime, null, new List<ActionEntry>(), "running");
            return (sessionId, baseDir);
        }

        /// <summary>
        /// Stop the current session. Returns the session directory.
        /// </summary>
        public static async Task<string> StopSession()
        {
            var (sessionId, sessionDir, sessionName, scene, startTime, actions) = await GetSessionData();

            if (string.IsNullOrEmpty(sessionId))
                return null;

            var endTime = DateTime.UtcNow.ToString("o");
            RegenerateReportFiles(sessionId, sessionDir, sessionName, scene, startTime, endTime, actions, "completed");

            _isActive = false;

            await CommandRegistry.RunOnMainThreadAsync(() =>
            {
                SessionState.SetString(SK_SessionId, "");
                SessionState.SetString(SK_SessionDir, "");
                return 0;
            });

            return sessionDir;
        }

        /// <summary>
        /// Log an action and optionally capture a screenshot. Call after action execution.
        /// </summary>
        public static async Task LogAction(string actionJson, bool success, string error, float elapsedMs)
        {
            var (sessionId, sessionDir, sessionName, scene, startTime, actions) = await GetSessionData();
            if (string.IsNullOrEmpty(sessionId)) return;

            int index = actions.Count;

            // Determine action name for screenshot filename
            string actionName = "action";
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(actionJson);
                actionName = obj.Value<string>("action") ?? "action";
            }
            catch { }

            // Capture screenshot as resized PNG — simple and fast
            string screenshotRelative = null;
            try
            {
                var pngFilename = $"{index:D3}_{actionName}.png";
                var pngPath = Path.Combine(sessionDir, "screenshots", pngFilename);

                // CaptureScreenshot writes to file asynchronously, use superSize=1 for native res
                await CommandRegistry.RunOnMainThreadAsync(() =>
                {
                    UnityEngine.ScreenCapture.CaptureScreenshot(pngPath);
                    return 0;
                });

                // Wait for file (usually 1-2 frames)
                for (int wait = 0; wait < 20 && !File.Exists(pngPath); wait++)
                    await Task.Delay(50);

                if (File.Exists(pngPath))
                    screenshotRelative = $"screenshots/{pngFilename}";
            }
            catch { }

            var entry = new ActionEntry
            {
                index = index,
                timestamp = DateTime.UtcNow.ToString("o"),
                action = actionJson,
                result = success ? "success" : "error",
                error = success ? null : error,
                elapsedMs = elapsedMs,
                screenshot = screenshotRelative
            };
            actions.Add(entry);

            // Fire-and-forget: persist state and regenerate report without blocking the response
            var actionsJsonStr = JsonConvert.SerializeObject(actions);
            var actionsCopy = new List<ActionEntry>(actions);
            _ = Task.Run(() =>
            {
                try
                {
                    RegenerateReportFiles(sessionId, sessionDir, sessionName, scene, startTime, null, actionsCopy, "running");
                }
                catch { }
            });
            _ = CommandRegistry.RunOnMainThreadAsync(() =>
            {
                SessionState.SetInt(SK_ActionCount, actions.Count);
                SessionState.SetString(SK_ActionsJson, actionsJsonStr);
                return 0;
            });
        }

        static async Task<(string sessionId, string sessionDir, string sessionName, string scene, string startTime, List<ActionEntry> actions)> GetSessionData()
        {
            return await CommandRegistry.RunOnMainThreadAsync(() =>
            {
                var id = SessionState.GetString(SK_SessionId, "");
                var dir = SessionState.GetString(SK_SessionDir, "");
                var name = SessionState.GetString(SK_SessionName, "");
                var scene = SessionState.GetString(SK_SessionScene, "");
                var start = SessionState.GetString(SK_SessionStart, "");
                var actionsStr = SessionState.GetString(SK_ActionsJson, "[]");
                List<ActionEntry> actions;
                try { actions = JsonConvert.DeserializeObject<List<ActionEntry>>(actionsStr); }
                catch { actions = new List<ActionEntry>(); }
                return (id, dir, name, scene, start, actions);
            });
        }

        static void RegenerateReportFiles(string sessionId, string sessionDir, string name, string scene,
            string startTime, string endTime, List<ActionEntry> actions, string status)
        {
            // Write session.json
            var session = new SessionJson
            {
                sessionId = sessionId,
                name = name,
                description = _description,
                scene = scene,
                startTime = startTime,
                endTime = endTime,
                status = status,
                actions = actions
            };
            var jsonPath = Path.Combine(sessionDir, "session.json");
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(session, Formatting.Indented));

            // Write index.html
            var htmlPath = Path.Combine(sessionDir, "index.html");
            File.WriteAllText(htmlPath, GenerateHtml(session));
        }

        static string GenerateHtml(SessionJson session)
        {
            var isRunning = session.status == "running";
            var actionItems = new System.Text.StringBuilder();

            for (int i = 0; i < session.actions.Count; i++)
            {
                var a = session.actions[i];
                var badge = a.result == "success"
                    ? "<span class=\"badge ok\">OK</span>"
                    : "<span class=\"badge fail\">FAIL</span>";
                var errorHtml = a.error != null ? $"<div class=\"error\">{Escape(a.error)}</div>" : "";
                var screenshotHtml = a.screenshot != null
                    ? $"<img src=\"{Escape(a.screenshot)}\" class=\"thumb\" onclick=\"openLightbox(this.src)\" />"
                    : "<span class=\"no-ss\">no screenshot</span>";

                // Add arrow before each action except the first
                if (i > 0)
                    actionItems.Append("<div class=\"arrow\">&rarr;</div>");

                actionItems.Append($@"
<div class=""action"">
  <div class=""action-header"">
    <span class=""index"">#{a.index}</span>
    {badge}
    <span class=""elapsed"">{a.elapsedMs:F0}ms</span>
  </div>
  <div class=""screenshot"">{screenshotHtml}</div>
  <pre class=""action-json"">{Escape(a.action)}</pre>
  {errorHtml}
</div>");
            }

            var duration = "";
            if (DateTime.TryParse(session.startTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var st))
            {
                var end = session.endTime != null && DateTime.TryParse(session.endTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var et)
                    ? et : DateTime.UtcNow;
                duration = (end - st).TotalSeconds.ToString("F1") + "s";
            }

            var autoRefresh = isRunning ? "<script>setTimeout(()=>location.reload(), 3000)</script>" : "";
            var statusClass = isRunning ? "running" : "completed";
            var displayName = !string.IsNullOrEmpty(session.name) ? $" &mdash; {Escape(session.name)}" : "";

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8""/>
<title>UI Session {Escape(session.sessionId)}</title>
{autoRefresh}
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{background:#0d1117;color:#c9d1d9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;padding:24px}}
.header{{border-bottom:1px solid #21262d;padding-bottom:16px;margin-bottom:24px}}
.header h1{{font-size:20px;color:#58a6ff}}
.header .meta{{color:#8b949e;font-size:13px;margin-top:4px}}
.status{{display:inline-block;padding:2px 8px;border-radius:12px;font-size:12px;font-weight:600}}
.status.running{{background:#1f6feb33;color:#58a6ff}}
.status.completed{{background:#23863633;color:#3fb950}}
.summary{{display:flex;gap:24px;margin-bottom:24px;font-size:14px;color:#8b949e}}
.summary span{{color:#c9d1d9;font-weight:600}}
.timeline{{display:flex;flex-wrap:wrap;align-items:flex-start;gap:4px}}
.arrow{{display:flex;align-items:center;color:#484f58;font-size:24px;padding:0 2px;align-self:center}}
.action{{background:#161b22;border:1px solid #21262d;border-radius:8px;padding:10px;width:220px;flex-shrink:0}}
.action-header{{display:flex;align-items:center;gap:8px;margin-bottom:6px}}
.index{{color:#8b949e;font-weight:600;font-size:12px}}
.badge{{padding:1px 6px;border-radius:10px;font-size:10px;font-weight:700;text-transform:uppercase}}
.badge.ok{{background:#23863633;color:#3fb950}}
.badge.fail{{background:#da363033;color:#f85149}}
.elapsed{{color:#8b949e;font-size:11px}}
.action-json{{background:#0d1117;border:1px solid #21262d;border-radius:4px;padding:6px 8px;font-size:11px;color:#c9d1d9;overflow-x:auto;white-space:pre-wrap;word-break:break-all;margin-top:6px}}
.error{{color:#f85149;font-size:11px;margin-top:4px;padding:4px 6px;background:#da363010;border-radius:4px}}
.screenshot{{margin-bottom:4px}}
.thumb{{width:200px;border-radius:4px;border:1px solid #21262d;cursor:pointer;transition:opacity .2s}}
.thumb:hover{{opacity:.8}}
.no-ss{{color:#484f58;font-size:11px}}
.lightbox{{display:none;position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,.9);z-index:1000;cursor:pointer;justify-content:center;align-items:center}}
.lightbox.show{{display:flex}}
.lightbox img{{max-width:95%;max-height:95%;border-radius:8px}}
.empty{{color:#484f58;text-align:center;padding:48px}}
</style>
</head>
<body>
<div class=""header"">
  <h1>UI Session {Escape(session.sessionId)}{displayName}</h1>
  <div class=""meta"">
    Scene: <strong>{Escape(session.scene)}</strong> &middot;
    Started: {Escape(session.startTime)} &middot;
    <span class=""status {statusClass}"">{session.status}</span>
  </div>
{(!string.IsNullOrEmpty(session.description) ? $"  <div class=\"meta\" style=\"margin-top:8px;color:#8b949e;font-style:italic\">{Escape(session.description)}</div>" : "")}
</div>
<div class=""summary"">
  <div>Actions: <span>{session.actions.Count}</span></div>
  <div>Duration: <span>{duration}</span></div>
</div>
<div class=""timeline"">
{(session.actions.Count == 0 ? "<div class=\"empty\">No actions recorded yet.</div>" : actionItems.ToString())}
</div>
<div class=""lightbox"" id=""lb"" onclick=""this.classList.remove('show')"">
  <img id=""lbImg"" src="""" />
</div>
<script>
function openLightbox(src){{document.getElementById('lbImg').src=src;document.getElementById('lb').classList.add('show')}}
document.addEventListener('keydown',e=>{{if(e.key==='Escape')document.getElementById('lb').classList.remove('show')}});
</script>
</body>
</html>";
        }

        static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        [Serializable]
        class SessionJson
        {
            public string sessionId;
            public string name;
            public string description;
            public string scene;
            public string startTime;
            public string endTime;
            public string status;
            public List<ActionEntry> actions;
        }

        [Serializable]
        class ActionEntry
        {
            public int index;
            public string timestamp;
            public string action;
            public string result;
            public string error;
            public float elapsedMs;
            public string screenshot;
        }
    }
}