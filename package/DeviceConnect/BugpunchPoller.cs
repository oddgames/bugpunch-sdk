// =============================================================================
// LANE: Editor + Standalone (C#)
//
// BugpunchPoller — managed-runtime poll client. Mirrors the native
// `BugpunchPoller.java` (Android) and `BugpunchPoller.mm` (iOS) by name and
// purpose so a feature that exists on all three lanes can be navigated by
// grepping for the same identifier.
//
// On Editor + Standalone builds (no native lane available), this class owns
// the always-on QA chat-reply heartbeat: poll `/api/v1/chat/thread`, count
// unread QA messages, drive the chat banner via `BugpunchNative`, and
// auto-fulfill `playerprefs` / `file` / `deviceinfo` data requests through
// the Unity-bound callback supplied by the host.
//
// On Android / iOS player builds the same work is owned by the native
// `BugpunchPoller` on each lane — see `BugpunchPlatform.cs` for the lane
// router. The C# heartbeat still runs on those lanes (auto-fulfill of
// dataRequest rows is Unity-bound and has to live in C#) but its banner
// trigger no-ops so the native pill isn't double-fired.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Always-on QA chat-reply heartbeat for the C# lane. Static — there's
    /// only ever one device-scoped poll loop running at a time, mirroring
    /// the singleton-style design of the native pollers.
    ///
    /// Cross-lane rule: this class reads its inputs (config, host, suppress
    /// state, auto-fulfill delegate) from <see cref="BugpunchRuntime"/>
    /// only. It does not import or reference <see cref="BugpunchClient"/>,
    /// the same way `BugpunchPoller.java` doesn't import any "client"
    /// class — it just calls `BugpunchRuntime.getServerUrl()`. The C#
    /// lane mirrors that shape so feature owners see the same
    /// dependency graph on every lane.
    /// </summary>
    public static class BugpunchPoller
    {
        // ─── Banner state ────────────────────────────────────────────────
        //
        // These three drive the "snooze until newer / don't churn the pill"
        // behaviour on the C# lane (Editor + Standalone). Native lanes own
        // their own equivalent state in BugpunchPoller.{java,mm}; on those
        // lanes the gate inside Tick() short-circuits before reading these.

        /// <summary>createdAt of the newest unread QA message we've already
        /// surfaced via the chat banner. A newer one re-shows the pill.</summary>
        static string s_lastShownQaAt;

        /// <summary>Snooze high-watermark — set when the player taps X on
        /// the banner. The banner stays hidden as long as the newest unread
        /// QA message is at or before this timestamp; a newer message
        /// clears the snooze implicitly via the Tick gate.</summary>
        static string s_bannerDismissedAt;

        /// <summary>The unread count we last passed to the banner so a tick
        /// with the same newest timestamp but a different unread count
        /// still updates the visible label.</summary>
        static int s_lastShownUnreadCount;

        static bool s_started;

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Start the poll loop. Reads its host + config + auto-fulfill
        /// callback from <see cref="BugpunchRuntime"/>; do not pass them
        /// in. Idempotent — repeat calls are silently ignored. The
        /// coroutine lifetime is tied to the runtime's host MonoBehaviour.
        /// </summary>
        public static void Start()
        {
            if (s_started) return;
            var host = BugpunchRuntime.Host;
            if (host == null) { BugpunchLog.Warn("BugpunchPoller", "Start: BugpunchRuntime.Host not set — call BugpunchRuntime.Init first"); return; }
            s_started = true;
            host.StartCoroutine(HeartbeatLoop());
        }

        /// <summary>Player tapped X on the chat banner — snooze until a
        /// newer QA message lands. Forwarded by `BugpunchClient.OnChatBannerDismissed`.</summary>
        public static void NotifyBannerDismissed()
        {
            s_bannerDismissedAt = s_lastShownQaAt;
        }

        /// <summary>Player tapped the chat banner — opening the chat clears
        /// the snooze so future pills can re-arm. Forwarded by
        /// `BugpunchClient.OnChatBannerOpened`.</summary>
        public static void NotifyBannerOpened()
        {
            s_bannerDismissedAt = null;
        }

        // ─── Internal poll loop ──────────────────────────────────────────

        static IEnumerator HeartbeatLoop()
        {
            // Small initial delay so we don't race the very first connect.
            yield return new WaitForSeconds(10f);
            while (true)
            {
                // Skip the check if the user's in a no-interrupt state; we'll
                // check again on the next tick. Don't even bother hitting the
                // server while suppressed — feels more polite and saves bytes.
                if (!BugpunchRuntime.SuppressActive)
                    yield return Tick();
                // 60s — chat is conversational, not real-time. At 10k users
                // this halves chat-heartbeat QPS vs the previous 30s without
                // a noticeable UX difference (banner appears within a minute
                // of the QA reply landing).
                yield return new WaitForSeconds(60f);
            }
        }

        static IEnumerator Tick()
        {
            var baseUrl = BugpunchRuntime.HttpBaseUrl;
            var apiKey = BugpunchRuntime.ApiKey;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
                yield break;

            // Single-thread-per-device API: GET /chat/thread returns
            // { thread, messages } when a thread exists, 404 when none does.
            // We scan messages for two things on each tick:
            //   1. Unread QA messages (drives the persistent re-open behaviour).
            //   2. Pending typed dataRequest rows whose request_kind is in the
            //      auto-fulfill set (playerprefs / file / deviceinfo) — those
            //      run without a player prompt.
            var url = baseUrl + "/api/v1/chat/thread";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) yield break;
            // 404 is the "no thread yet" sentinel — treat as zero unread and
            // reset the popup guard so the next QA-originated message (if
            // any) re-arms the auto-open.
            if (req.responseCode == 404)
            {
                s_lastShownQaAt = null;
                yield break;
            }
            if (req.responseCode < 200 || req.responseCode >= 300) yield break;

            string newestUnreadQaAt = null;
            int unreadCount = 0;
            var autoFulfillBatch = new List<(string id, string kind, string body)>();
            try
            {
                var bodyStr = req.downloadHandler != null ? req.downloadHandler.text : "";
                var obj = Newtonsoft.Json.Linq.JObject.Parse(string.IsNullOrEmpty(bodyStr) ? "{}" : bodyStr);
                var arr = obj["messages"] as Newtonsoft.Json.Linq.JArray;
                if (arr != null)
                {
                    foreach (var m in arr)
                    {
                        var sender = (string)m["sender"] ?? (string)m["Sender"];
                        if (!string.Equals(sender, "qa", StringComparison.Ordinal)) continue;

                        var msgId       = (string)m["id"] ?? (string)m["Id"];
                        var msgType     = (string)m["type"] ?? (string)m["Type"];
                        var requestKind = (string)m["requestKind"] ?? (string)m["RequestKind"];
                        var requestState= (string)m["requestState"] ?? (string)m["RequestState"];
                        var msgBody     = (string)m["body"] ?? (string)m["Body"] ?? "";
                        var readAt      = (string)m["readBySdkAt"] ?? (string)m["ReadBySdkAt"];
                        var createdAt   = (string)m["createdAt"] ?? (string)m["CreatedAt"];

                        // Track unread for persistent-show.
                        if (string.IsNullOrEmpty(readAt))
                        {
                            unreadCount++;
                            if (newestUnreadQaAt == null
                                || string.CompareOrdinal(createdAt ?? "", newestUnreadQaAt) > 0)
                                newestUnreadQaAt = createdAt;
                        }

                        // Auto-fulfill kinds — collect for execution after the
                        // parse loop (run outside the try so exceptions there
                        // don't blow up parsing).
                        if (msgType == "dataRequest"
                            && requestState == "pending"
                            && (requestKind == "playerprefs" || requestKind == "file" || requestKind == "deviceinfo"))
                        {
                            if (!string.IsNullOrEmpty(msgId))
                                autoFulfillBatch.Add((msgId, requestKind, msgBody));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("BugpunchPoller.Tick", ex);
                yield break;
            }

            // Run auto-fulfill handlers — fire-and-forget; the registered
            // handler manages its own retries / logging. The fulfill
            // callback is Unity-bound (PlayerPrefs / FileService / SystemInfo)
            // so it has to ride managed code regardless of lane; it's
            // registered on BugpunchRuntime by BugpunchClient at startup.
            var autoFulfill = BugpunchRuntime.AutoFulfill;
            if (autoFulfill != null)
            {
                foreach (var entry in autoFulfillBatch)
                    _ = autoFulfill(entry.id, entry.kind, entry.body);
            }

            // Banner ownership lives in BugpunchPoller.java / .mm on device
            // — native polls /api/v1/chat/unread on its own loop and shows
            // / hides the pill. The C# poller only drives the banner on
            // Editor + Standalone (BugpunchPlatform.IsManagedLane).
            if (!BugpunchPlatform.IsManagedLane) yield break;

            if (newestUnreadQaAt == null)
            {
                // QA queue is fully read — hide banner and re-arm so the
                // next QA message re-shows.
                s_lastShownQaAt = null;
                s_bannerDismissedAt = null;
                BugpunchNative.HideChatBanner();
                yield break;
            }
            if (BugpunchRuntime.SuppressActive) { BugpunchNative.HideChatBanner(); yield break; }

            // Snooze gate — if the player tapped X on the banner, don't
            // re-show until a QA message newer than the snoozed one arrives.
            if (s_bannerDismissedAt != null
                && string.CompareOrdinal(newestUnreadQaAt, s_bannerDismissedAt) <= 0)
                yield break;

            // Already-shown gate — don't churn the banner if nothing new.
            if (s_lastShownQaAt != null
                && string.CompareOrdinal(newestUnreadQaAt, s_lastShownQaAt) <= 0
                && unreadCount == s_lastShownUnreadCount)
                yield break;

            s_lastShownQaAt = newestUnreadQaAt;
            s_lastShownUnreadCount = unreadCount;
            BugpunchNative.ShowChatBanner(unreadCount);
        }
    }
}
