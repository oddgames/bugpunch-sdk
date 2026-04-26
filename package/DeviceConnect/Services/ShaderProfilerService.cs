using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Shader / material profiler. Iterates shader (or material) groups, hides
    /// every renderer outside the current group, samples frame timings for a
    /// fixed window, then ranks the groups by added frame cost vs a baseline.
    ///
    /// The sweep runs with Time.timeScale = 0 so spawners, animators, and
    /// physics can't add or remove renderers mid-measurement — Unity's render
    /// loop ignores timeScale, so frame timings still reflect real GPU/CPU work.
    /// </summary>
    public class ShaderProfilerService : MonoBehaviour
    {
        // Group key for renderers whose sharedMaterial / shader is null.
        const string NullGroupKey = "__null__";

        readonly Dictionary<string, ProfileJob> _jobs = new();

        enum Stage { Scanning, Profiling, Done, Error, Cancelled }

        class GroupResult
        {
            public string Key;
            public string DisplayName;
            public int RendererCount;
            public float AvgMs;
            public float P99Ms;
            public float Fps;
        }

        class GroupSpec
        {
            public string Key;
            public string DisplayName;
            public List<Renderer> Renderers;
        }

        class ProfileJob
        {
            public string JobId;
            public string GroupBy;          // "shader" | "material"
            public float SecondsPerGroup;
            public int WarmupFrames;
            public bool PauseGame;
            public HashSet<string> KeyFilter; // null → run all groups

            public Stage Stage;
            public string Error;

            public int CurrentIndex;        // 0-based group index, or -1 during baseline
            public int TotalGroups;
            public string CurrentName;

            public GroupResult Baseline;
            public List<GroupResult> Results;

            public float StartedAt;
            public float CompletedAt;

            // Saved state for restore.
            public Dictionary<Renderer, bool> SavedEnabled;
            public float SavedTimeScale;
            public bool SavedAudioPause;

            public bool CancelRequested;
        }

        ProfileJob _activeJob; // only one sweep at a time

        // Spotlight (manual single-group preview, separate from sweep).
        Dictionary<Renderer, bool> _spotlightSavedEnabled;
        string _spotlightActiveKey;

        // -------------------------------------------------------------------
        // Public endpoints — invoked from RequestRouter.
        // -------------------------------------------------------------------

        /// <summary>
        /// Preview list of groups. Doesn't run a sweep; just enumerates
        /// renderers and groups them so the panel can show counts up front.
        /// </summary>
        public string ListGroups(string by)
        {
            var groups = BuildGroups(string.IsNullOrEmpty(by) ? "shader" : by);
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var g = groups[i];
                sb.Append("{");
                sb.Append($"\"key\":\"{Esc(g.Key)}\",");
                sb.Append($"\"displayName\":\"{Esc(g.DisplayName)}\",");
                sb.Append($"\"rendererCount\":{g.Renderers.Count},");
                sb.Append("\"sampleObjects\":[");
                int sampleMax = Mathf.Min(3, g.Renderers.Count);
                int written = 0;
                for (int r = 0; r < g.Renderers.Count && written < sampleMax; r++)
                {
                    var rr = g.Renderers[r];
                    if (rr == null || rr.gameObject == null) continue;
                    if (written > 0) sb.Append(",");
                    sb.Append($"\"{Esc(rr.gameObject.name)}\"");
                    written++;
                }
                sb.Append("]");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Kick off a sweep. Refuses if another sweep is already running.
        /// <paramref name="keysCsv"/> is an optional comma-separated list of group keys
        /// (e.g. "s:Standard,s:Sprites/Default") — when supplied, only those groups
        /// are profiled. The baseline always uses the full scene.
        /// </summary>
        public string BeginProfile(string by, float secondsPerGroup, int warmupFrames, bool pauseGame, string keysCsv)
        {
            if (_activeJob != null && _activeJob.Stage != Stage.Done && _activeJob.Stage != Stage.Error && _activeJob.Stage != Stage.Cancelled)
                return $"{{\"ok\":false,\"error\":\"A sweep is already running\",\"jobId\":\"{Esc(_activeJob.JobId)}\"}}";

            // Spotlight must be cleared before a sweep — both touch the same renderer-state slot.
            if (_spotlightSavedEnabled != null)
            {
                RestoreRendererStates(_spotlightSavedEnabled);
                _spotlightSavedEnabled = null;
                _spotlightActiveKey = null;
            }

            HashSet<string> filter = null;
            if (!string.IsNullOrEmpty(keysCsv))
            {
                filter = new HashSet<string>();
                foreach (var raw in keysCsv.Split(','))
                {
                    var k = raw.Trim();
                    if (k.Length > 0) filter.Add(k);
                }
                if (filter.Count == 0) filter = null;
            }

            var job = new ProfileJob
            {
                JobId = System.Guid.NewGuid().ToString("N"),
                GroupBy = string.IsNullOrEmpty(by) ? "shader" : by,
                SecondsPerGroup = Mathf.Clamp(secondsPerGroup <= 0 ? 3f : secondsPerGroup, 0.5f, 30f),
                WarmupFrames = Mathf.Clamp(warmupFrames <= 0 ? 30 : warmupFrames, 0, 240),
                PauseGame = pauseGame,
                KeyFilter = filter,
                Stage = Stage.Scanning,
                CurrentIndex = -1,
                Results = new List<GroupResult>(),
                StartedAt = Time.unscaledTime,
            };

            // Drop any prior finished jobs — the web client only fetches the
            // most recent /result once. Lightweight result data on the active
            // job is enough; we don't need to retain history.
            _jobs.Clear();

            _jobs[job.JobId] = job;
            _activeJob = job;
            StartCoroutine(RunSweep(job));

            return $"{{\"ok\":true,\"jobId\":\"{Esc(job.JobId)}\"}}";
        }

        public string GetStatus(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_jobs.TryGetValue(jobId, out var job))
                return "{\"ok\":false,\"error\":\"Unknown jobId\"}";

            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,");
            sb.Append($"\"jobId\":\"{Esc(job.JobId)}\",");
            sb.Append($"\"stage\":\"{StageName(job.Stage)}\",");
            sb.Append($"\"currentIndex\":{job.CurrentIndex},");
            sb.Append($"\"totalGroups\":{job.TotalGroups},");
            sb.Append($"\"currentName\":\"{Esc(job.CurrentName ?? "")}\"");
            if (job.Baseline != null)
            {
                sb.Append(",\"baseline\":");
                AppendGroupJson(sb, job.Baseline, baselineMs: 0f);
            }
            if (job.Error != null)
                sb.Append($",\"error\":\"{Esc(job.Error)}\"");
            sb.Append("}");
            return sb.ToString();
        }

        public string GetResult(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_jobs.TryGetValue(jobId, out var job))
                return "{\"ok\":false,\"error\":\"Unknown jobId\"}";
            if (job.Stage == Stage.Error)
                return $"{{\"ok\":false,\"error\":\"{Esc(job.Error ?? "Unknown error")}\"}}";
            if (job.Stage == Stage.Cancelled)
                return "{\"ok\":false,\"cancelled\":true}";
            if (job.Stage != Stage.Done)
                return "{\"ok\":false,\"pending\":true}";

            // Sort by descending added cost vs baseline.
            float baseMs = job.Baseline?.AvgMs ?? 0f;
            var sorted = new List<GroupResult>(job.Results);
            sorted.Sort((a, b) => (b.AvgMs - baseMs).CompareTo(a.AvgMs - baseMs));

            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,");
            sb.Append($"\"jobId\":\"{Esc(job.JobId)}\",");
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"durationSec\":{0:F2},", job.CompletedAt - job.StartedAt));
            sb.Append($"\"groupBy\":\"{Esc(job.GroupBy)}\",");
            sb.Append("\"baseline\":");
            AppendGroupJson(sb, job.Baseline, baselineMs: 0f);
            sb.Append(",\"groups\":[");
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendGroupJson(sb, sorted[i], baseMs);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public string Cancel(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_jobs.TryGetValue(jobId, out var job))
                return "{\"ok\":false,\"error\":\"Unknown jobId\"}";
            if (job.Stage == Stage.Done || job.Stage == Stage.Cancelled || job.Stage == Stage.Error)
                return "{\"ok\":true,\"alreadyStopped\":true}";
            job.CancelRequested = true;
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Toggle visibility of a single group without running a sweep — lets the
        /// user "spotlight" one shader/material to eyeball it. Pass an empty
        /// key to restore everything.
        /// </summary>
        public string Spotlight(string by, string key)
        {
            // Refuse during a sweep — too easy to corrupt the saved state.
            if (_activeJob != null && (_activeJob.Stage == Stage.Profiling || _activeJob.Stage == Stage.Scanning))
                return "{\"ok\":false,\"error\":\"Sweep in progress\"}";

            // First spotlight call snapshots state; subsequent calls reuse it.
            if (_spotlightSavedEnabled == null)
                _spotlightSavedEnabled = SnapshotRendererStates();

            // Empty key → restore.
            if (string.IsNullOrEmpty(key))
            {
                RestoreRendererStates(_spotlightSavedEnabled);
                _spotlightSavedEnabled = null;
                _spotlightActiveKey = null;
                return "{\"ok\":true,\"spotlight\":\"\"}";
            }

            var groups = BuildGroups(string.IsNullOrEmpty(by) ? "shader" : by);
            var match = groups.Find(g => g.Key == key);
            if (match == null)
                return "{\"ok\":false,\"error\":\"Unknown group key\"}";

            var inGroup = new HashSet<Renderer>(match.Renderers);
            foreach (var kv in _spotlightSavedEnabled)
            {
                var r = kv.Key;
                if (r == null) continue;
                bool keep = kv.Value && inGroup.Contains(r);
                if (r.enabled != keep) r.enabled = keep;
            }
            _spotlightActiveKey = key;
            return $"{{\"ok\":true,\"spotlight\":\"{Esc(key)}\"}}";
        }

        // -------------------------------------------------------------------
        // Sweep coroutine
        // -------------------------------------------------------------------

        IEnumerator RunSweep(ProfileJob job)
        {
            // Snapshot — capture true initial state before pausing.
            job.SavedEnabled = SnapshotRendererStates();
            job.SavedTimeScale = Time.timeScale;
            job.SavedAudioPause = AudioListener.pause;

            // Group renderers from the snapshot keys (so destroyed-mid-sweep
            // renderers stay null-checked uniformly).
            var groups = BuildGroupsFromSnapshot(job.SavedEnabled, job.GroupBy);
            if (job.KeyFilter != null)
                groups.RemoveAll(g => !job.KeyFilter.Contains(g.Key));
            job.TotalGroups = groups.Count;

            if (job.PauseGame)
            {
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }

            try
            {
                // -- Baseline: leave the scene exactly as-is --------------------
                job.Stage = Stage.Profiling;
                job.CurrentIndex = -1;
                job.CurrentName = "Baseline";

                yield return WarmupFrames(job);
                if (job.CancelRequested) { job.Stage = Stage.Cancelled; yield break; }

                var baselineHolder = new SampleResult();
                yield return SampleFor(job.SecondsPerGroup, baselineHolder);
                job.Baseline = new GroupResult
                {
                    Key = "__baseline__",
                    DisplayName = "Baseline (everything visible)",
                    RendererCount = CountEnabled(job.SavedEnabled),
                    AvgMs = baselineHolder.AvgMs,
                    P99Ms = baselineHolder.P99Ms,
                    Fps = baselineHolder.Fps,
                };

                // -- Per-group sweep --------------------------------------------
                for (int i = 0; i < groups.Count; i++)
                {
                    if (job.CancelRequested) { job.Stage = Stage.Cancelled; yield break; }

                    var g = groups[i];
                    job.CurrentIndex = i;
                    job.CurrentName = g.DisplayName;

                    var inGroup = new HashSet<Renderer>(g.Renderers);
                    foreach (var kv in job.SavedEnabled)
                    {
                        var r = kv.Key;
                        if (r == null) continue;
                        bool keep = kv.Value && inGroup.Contains(r);
                        if (r.enabled != keep) r.enabled = keep;
                    }

                    yield return WarmupFrames(job);
                    if (job.CancelRequested) { job.Stage = Stage.Cancelled; yield break; }

                    var sample = new SampleResult();
                    yield return SampleFor(job.SecondsPerGroup, sample);
                    job.Results.Add(new GroupResult
                    {
                        Key = g.Key,
                        DisplayName = g.DisplayName,
                        RendererCount = g.Renderers.Count,
                        AvgMs = sample.AvgMs,
                        P99Ms = sample.P99Ms,
                        Fps = sample.Fps,
                    });
                }

                job.Stage = Stage.Done;
                job.CurrentIndex = job.TotalGroups;
                job.CurrentName = "Done";
            }
            finally
            {
                // Always restore — leaving the game paused with half its
                // renderers off would brick the device.
                RestoreRendererStates(job.SavedEnabled);
                if (job.PauseGame)
                {
                    Time.timeScale = job.SavedTimeScale;
                    AudioListener.pause = job.SavedAudioPause;
                }
                // Drop the heavy renderer-state snapshot now that restore is done.
                job.SavedEnabled = null;
                job.CompletedAt = Time.unscaledTime;
                if (_activeJob == job) _activeJob = null;
            }
        }

        IEnumerator WarmupFrames(ProfileJob job)
        {
            for (int i = 0; i < job.WarmupFrames; i++)
            {
                if (job.CancelRequested) yield break;
                yield return null;
            }
        }

        class SampleResult
        {
            public float AvgMs;
            public float P99Ms;
            public float Fps;
        }

        IEnumerator SampleFor(float seconds, SampleResult result)
        {
            // Use unscaledDeltaTime since timeScale may be 0 — Unity still ticks
            // unscaledTime + renders frames.
            var deltas = new List<float>(256);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (dt > 0f) deltas.Add(dt);
                elapsed += dt;
            }

            if (deltas.Count == 0)
            {
                result.AvgMs = 0f; result.P99Ms = 0f; result.Fps = 0f;
                yield break;
            }

            float sum = 0f;
            for (int i = 0; i < deltas.Count; i++) sum += deltas[i];
            float avg = sum / deltas.Count;

            // p99
            deltas.Sort();
            int p99Index = Mathf.Clamp(Mathf.CeilToInt(deltas.Count * 0.99f) - 1, 0, deltas.Count - 1);
            float p99 = deltas[p99Index];

            result.AvgMs = avg * 1000f;
            result.P99Ms = p99 * 1000f;
            result.Fps = avg > 0f ? 1f / avg : 0f;
        }

        // -------------------------------------------------------------------
        // Renderer enumeration + grouping
        // -------------------------------------------------------------------

        Dictionary<Renderer, bool> SnapshotRendererStates()
        {
            var dict = new Dictionary<Renderer, bool>();
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (r.hideFlags != HideFlags.None) continue;
                dict[r] = r.enabled;
            }
            return dict;
        }

        void RestoreRendererStates(Dictionary<Renderer, bool> saved)
        {
            if (saved == null) return;
            foreach (var kv in saved)
            {
                var r = kv.Key;
                if (r == null) continue;
                if (r.enabled != kv.Value) r.enabled = kv.Value;
            }
        }

        List<GroupSpec> BuildGroups(string by)
        {
            var snap = SnapshotRendererStates();
            return BuildGroupsFromSnapshot(snap, by);
        }

        List<GroupSpec> BuildGroupsFromSnapshot(Dictionary<Renderer, bool> snapshot, string by)
        {
            bool byMaterial = by == "material";
            var byKey = new Dictionary<string, GroupSpec>();

            foreach (var kv in snapshot)
            {
                var r = kv.Key;
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    AddToGroup(byKey, NullGroupKey, "(no material)", r);
                    continue;
                }
                bool any = false;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    string key, name;
                    if (byMaterial)
                    {
                        key = "m:" + m.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                        name = string.IsNullOrEmpty(m.name) ? "(unnamed material)" : m.name;
                    }
                    else
                    {
                        var sh = m.shader;
                        if (sh == null)
                        {
                            key = NullGroupKey;
                            name = "(no shader)";
                        }
                        else
                        {
                            key = "s:" + sh.name;
                            name = sh.name;
                        }
                    }
                    AddToGroup(byKey, key, name, r);
                    any = true;
                }
                if (!any) AddToGroup(byKey, NullGroupKey, "(no material)", r);
            }

            var list = new List<GroupSpec>(byKey.Values);
            list.Sort((a, b) => b.Renderers.Count.CompareTo(a.Renderers.Count));
            return list;
        }

        static void AddToGroup(Dictionary<string, GroupSpec> map, string key, string name, Renderer r)
        {
            if (!map.TryGetValue(key, out var g))
            {
                g = new GroupSpec { Key = key, DisplayName = name, Renderers = new List<Renderer>() };
                map[key] = g;
            }
            // Avoid double-adding the same renderer when it has multiple materials in the same group.
            if (g.Renderers.Count == 0 || g.Renderers[g.Renderers.Count - 1] != r)
            {
                if (!g.Renderers.Contains(r)) g.Renderers.Add(r);
            }
        }

        static int CountEnabled(Dictionary<Renderer, bool> snap)
        {
            int n = 0;
            foreach (var kv in snap) if (kv.Value) n++;
            return n;
        }

        // -------------------------------------------------------------------
        // JSON helpers
        // -------------------------------------------------------------------

        static void AppendGroupJson(StringBuilder sb, GroupResult g, float baselineMs)
        {
            if (g == null) { sb.Append("null"); return; }
            sb.Append("{");
            sb.Append($"\"key\":\"{Esc(g.Key)}\",");
            sb.Append($"\"displayName\":\"{Esc(g.DisplayName)}\",");
            sb.Append($"\"rendererCount\":{g.RendererCount},");
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"avgMs\":{0:F3},", g.AvgMs));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"p99Ms\":{0:F3},", g.P99Ms));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"fps\":{0:F1},", g.Fps));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"deltaMs\":{0:F3}", g.AvgMs - baselineMs));
            sb.Append("}");
        }

        static string StageName(Stage s) => s switch
        {
            Stage.Scanning => "scanning",
            Stage.Profiling => "profiling",
            Stage.Done => "done",
            Stage.Error => "error",
            Stage.Cancelled => "cancelled",
            _ => "unknown",
        };

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";

        // -------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------

        void OnDestroy()
        {
            // If we go down with a sweep in flight or a spotlight active, restore.
            if (_activeJob != null && _activeJob.SavedEnabled != null)
            {
                RestoreRendererStates(_activeJob.SavedEnabled);
                if (_activeJob.PauseGame)
                {
                    Time.timeScale = _activeJob.SavedTimeScale;
                    AudioListener.pause = _activeJob.SavedAudioPause;
                }
            }
            if (_spotlightSavedEnabled != null)
            {
                RestoreRendererStates(_spotlightSavedEnabled);
                _spotlightSavedEnabled = null;
            }
        }
    }
}
