using ODDGames.Bugpunch.DeviceConnect;
using ODDGames.Bugpunch.DeviceConnect.UI;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Static entry point for Bugpunch bug reporting. Thin facade over the
    /// native coordinator — see <see cref="BugpunchNative"/>. All methods are
    /// safe to call before <c>BugpunchClient</c> has initialized (they'll
    /// no-op and log a warning).
    /// </summary>
    public static class Bugpunch
    {
        /// <summary>
        /// Enable debug recording (starts the native screen ring buffer).
        /// By default shows a consent sheet with Start / Cancel; pass
        /// <paramref name="skipConsent"/> = true for debug/alpha builds where
        /// the tester has already opted in. Android's OS-level MediaProjection
        /// consent dialog still appears regardless — that can't be bypassed.
        /// No-op if already recording.
        /// </summary>
        public static void EnterDebugMode(bool skipConsent = false)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.EnterDebugMode(skipConsent);
        }

        /// <summary>
        /// File a bug report. Native captures screenshot + dumps video (if
        /// recording) + assembles metadata + enqueues upload. Fire-and-forget.
        /// </summary>
        public static void Report(string title = null, string description = null,
            string type = "bug")
        {
            if (!EnsureStarted()) return;
            BugpunchNative.ReportBug(type, title, description, null);
        }

        /// <summary>
        /// Send a feedback message (native will attach a screenshot).
        /// </summary>
        public static void Feedback(string message)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.ReportBug("feedback", null, message, null);
        }

        /// <summary>
        /// Show a 3-button picker — "Record a bug" (enters debug mode),
        /// "Ask for help" (opens the existing bug-report form routed as type="help"),
        /// or "Send feedback" (opens the feedback board). Intended as a single
        /// convenient entry point for a tester / player to choose what they want to
        /// do without the game binding three separate UI buttons.
        /// </summary>
        public static void RequestHelp()
        {
            if (!EnsureStarted()) return;
            BugpunchRequestHelpPicker.Show();
        }

        /// <summary>
        /// Drop a custom marker into the breadcrumb ring. Surfaces in the
        /// crash Storyboard alongside tap / scene / log events so you can
        /// see the game's own narrative ("User entered checkout", "IAP.
        /// begin", "Analytics: purchase_click") leading up to a crash.
        ///
        /// Lives in native memory (same ring as SDK-captured input events),
        /// so it survives a Mono/IL2CPP meltdown and lands in the next
        /// crash report even when the managed runtime is dead.
        ///
        /// <paramref name="category"/> is a short free-form tag the
        /// dashboard shows in brackets — e.g. "flow", "iap", "net".
        /// <paramref name="message"/> is the body the user will read.
        /// Either may be null.
        /// </summary>
        public static void Breadcrumb(string category, string message)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.PushInputCustom(category, message);
        }

        // ── Analytics events ──
        //
        // Mobile-game analytics taxonomy modelled on GameAnalytics + Firebase
        // Analytics. Sessions, installs, and user identity are auto-tracked
        // by native; the API below covers the events the game itself emits.

        /// <summary>
        /// Record a custom design event — flexible catch-all. Cheap,
        /// fire-and-forget: native holds an in-memory ring and flushes in
        /// batches to the server. Event id is required; properties are
        /// optional flat key→value pairs (strings, numbers, bools — complex
        /// types are ToString'd).
        ///
        /// Use the typed helpers below (<see cref="LogPurchase"/>,
        /// <see cref="LogAd"/>, <see cref="LogResource"/>,
        /// <see cref="LogProgression"/>, <see cref="LogScreen"/>) when the
        /// event matches a standard mobile-F2P shape — they populate typed
        /// columns server-side so the dashboard charts work without per-game
        /// configuration.
        /// </summary>
        public static void TrackEvent(string name, System.Collections.IDictionary properties = null)
            => LogDesign(name, properties);

        /// <summary>
        /// Record a free-form 'design' event. Same as <see cref="TrackEvent"/>
        /// — kept as the typed-API name; TrackEvent stays for back-compat.
        /// </summary>
        public static void LogDesign(string eventId, System.Collections.IDictionary properties = null)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(eventId)) return;
            var sb = new System.Text.StringBuilder(128);
            sb.Append('{');
            sb.Append("\"name\":\"").Append(BugpunchJson.Esc(eventId)).Append('"');
            if (properties != null && properties.Count > 0)
            {
                sb.Append(",\"properties\":").Append(SerializeDict(properties));
            }
            sb.Append('}');
            BugpunchNative.LogTypedEvent("design", sb.ToString());
        }

        /// <summary>
        /// Real-money in-app purchase. Call from your purchase callback —
        /// Unity IAP's ProcessPurchase, Google Play Billing's
        /// onPurchasesUpdated, StoreKit's paymentQueue transaction observer.
        /// <paramref name="price"/> is in <paramref name="currency"/>;
        /// fractional values are preserved (server stores micros).
        /// </summary>
        public static void LogPurchase(string sku, double price, string currency = "USD",
            string transactionId = null)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(sku)) return;
            long micros = (long)System.Math.Round(price * 1_000_000.0);
            var sb = new System.Text.StringBuilder(160);
            sb.Append('{');
            sb.Append("\"amountMicros\":").Append(micros);
            sb.Append(",\"currency\":\"").Append(BugpunchJson.Esc(currency ?? "USD")).Append('"');
            sb.Append(",\"itemId\":\"").Append(BugpunchJson.Esc(sku)).Append('"');
            if (!string.IsNullOrEmpty(transactionId))
                sb.Append(",\"transactionId\":\"").Append(BugpunchJson.Esc(transactionId)).Append('"');
            sb.Append('}');
            BugpunchNative.LogTypedEvent("business", sb.ToString());
        }

        /// <summary>
        /// Ad event. <paramref name="action"/> is one of "shown", "click",
        /// "reward", "fail". Call from your ad-SDK callbacks — AdMob's
        /// OnAdImpression, AppLovin MAX's onAdDisplayed, etc.
        /// <paramref name="revenueUSD"/> is the reported impression revenue
        /// in USD (nullable — pass null if the network doesn't expose it).
        /// </summary>
        public static void LogAd(string action, string adType, string sdkName,
            string placement, double? revenueUSD = null)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(action)) return;
            var sb = new System.Text.StringBuilder(160);
            sb.Append('{');
            sb.Append("\"adAction\":\"").Append(BugpunchJson.Esc(action)).Append('"');
            if (!string.IsNullOrEmpty(adType))
                sb.Append(",\"adType\":\"").Append(BugpunchJson.Esc(adType)).Append('"');
            if (!string.IsNullOrEmpty(sdkName))
                sb.Append(",\"adSdk\":\"").Append(BugpunchJson.Esc(sdkName)).Append('"');
            if (!string.IsNullOrEmpty(placement))
                sb.Append(",\"adPlacement\":\"").Append(BugpunchJson.Esc(placement)).Append('"');
            if (revenueUSD.HasValue)
            {
                long micros = (long)System.Math.Round(revenueUSD.Value * 1_000_000.0);
                sb.Append(",\"amountMicros\":").Append(micros);
                sb.Append(",\"currency\":\"USD\"");
            }
            sb.Append('}');
            BugpunchNative.LogTypedEvent("ad", sb.ToString());
        }

        /// <summary>
        /// Back-compat wrapper around <see cref="LogAd"/>. New code should
        /// call LogAd directly — it maps every action ('shown' / 'click' /
        /// 'reward' / 'fail') instead of just impressions.
        /// </summary>
        public static void LogAdImpression(string placement, string format,
            string network = null, double? revenue = null)
            => LogAd("shown", format, network, placement, revenue);

        /// <summary>
        /// Virtual currency flow. <paramref name="flow"/> is "source" (earn)
        /// or "sink" (spend). <paramref name="currency"/> is your in-game
        /// currency name (gold, gems, ...).
        /// </summary>
        public static void LogResource(string flow, string currency, long amount,
            string itemType, string itemId)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(flow) || string.IsNullOrEmpty(currency)) return;
            var sb = new System.Text.StringBuilder(160);
            sb.Append('{');
            sb.Append("\"resourceFlow\":\"").Append(BugpunchJson.Esc(flow)).Append('"');
            sb.Append(",\"currency\":\"").Append(BugpunchJson.Esc(currency)).Append('"');
            sb.Append(",\"amountMicros\":").Append(amount * 1_000_000L);
            if (!string.IsNullOrEmpty(itemType))
                sb.Append(",\"itemType\":\"").Append(BugpunchJson.Esc(itemType)).Append('"');
            if (!string.IsNullOrEmpty(itemId))
                sb.Append(",\"itemId\":\"").Append(BugpunchJson.Esc(itemId)).Append('"');
            sb.Append('}');
            BugpunchNative.LogTypedEvent("resource", sb.ToString());
        }

        /// <summary>
        /// Progression event. <paramref name="status"/> is "start", "complete",
        /// or "fail". Hierarchical: <paramref name="level1"/> is the top of
        /// the progression tree (world / chapter), <paramref name="level2"/>
        /// is the next level (level / mission), <paramref name="level3"/> is
        /// the leaf (stage / wave). Server stores the path as
        /// "level1/level2/level3" so prefix grouping works for funnel analysis.
        /// </summary>
        public static void LogProgression(string status, string level1,
            string level2 = null, string level3 = null,
            double? score = null, int? attempt = null)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(status) || string.IsNullOrEmpty(level1)) return;
            var path = level1;
            if (!string.IsNullOrEmpty(level2)) { path += "/" + level2; }
            if (!string.IsNullOrEmpty(level3)) { path += "/" + level3; }
            var sb = new System.Text.StringBuilder(160);
            sb.Append('{');
            sb.Append("\"progStatus\":\"").Append(BugpunchJson.Esc(status)).Append('"');
            sb.Append(",\"progPath\":\"").Append(BugpunchJson.Esc(path)).Append('"');
            if (score.HasValue)
                sb.Append(",\"score\":").Append(score.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (attempt.HasValue)
                sb.Append(",\"attemptN\":").Append(attempt.Value);
            sb.Append('}');
            BugpunchNative.LogTypedEvent("progression", sb.ToString());
        }

        /// <summary>
        /// Screen / view transition. Auto-emitted on Unity scene changes when
        /// <c>BugpunchConfig.autoScreenViews</c> is true; call manually for
        /// in-scene UI screens that don't trigger a SceneManager event.
        /// </summary>
        public static void LogScreen(string screenName, string previousScreen = null)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(screenName)) return;
            var sb = new System.Text.StringBuilder(128);
            sb.Append('{');
            sb.Append("\"screenName\":\"").Append(BugpunchJson.Esc(screenName)).Append('"');
            if (!string.IsNullOrEmpty(previousScreen))
                sb.Append(",\"previousScreen\":\"").Append(BugpunchJson.Esc(previousScreen)).Append('"');
            sb.Append('}');
            BugpunchNative.LogTypedEvent("screen_view", sb.ToString());
        }

        // ── User identity ──

        /// <summary>
        /// Set the app-supplied user id. Survives across sessions on the same
        /// install. Pass null to clear (e.g. on logout). Native install_id
        /// remains the stable key for retention math regardless.
        /// </summary>
        public static void SetUserId(string userId)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetUserId(userId);
        }

        /// <summary>Set a user property (string).</summary>
        public static void SetUserProperty(string key, string value)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(key)) return;
            BugpunchNative.SetUserProperty(key, value);
        }

        /// <summary>Set a user property (number).</summary>
        public static void SetUserProperty(string key, double value)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(key)) return;
            BugpunchNative.SetUserProperty(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Set a user property (bool).</summary>
        public static void SetUserProperty(string key, bool value)
        {
            if (!EnsureStarted() || string.IsNullOrEmpty(key)) return;
            BugpunchNative.SetUserProperty(key, value ? "true" : "false");
        }

        /// <summary>
        /// Attach a custom key/value to subsequent reports. Lives in the native
        /// side's custom-data map; persists for the session. The typed
        /// overloads (int/long/float/double/bool/Enum/object) format the value
        /// with InvariantCulture so decimal separators etc. stay consistent
        /// across locales — no need to call <c>.ToString()</c> at every site.
        /// </summary>
        public static void SetCustomData(string key, string value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value);
        }

        public static void SetCustomData(string key, int value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void SetCustomData(string key, long value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void SetCustomData(string key, float value)
        {
            if (!EnsureStarted()) return;
            // "R" round-trip format preserves full precision — "0.1f" stays "0.1" not "0.10000000149..."
            BugpunchNative.SetCustomData(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void SetCustomData(string key, double value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void SetCustomData(string key, bool value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value ? "true" : "false");
        }

        public static void SetCustomData(string key, System.Enum value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value != null ? value.ToString() : null);
        }

        /// <summary>
        /// Fallback for any other type — <c>ToString()</c> using InvariantCulture
        /// when the type supports it, otherwise the plain <c>ToString()</c>.
        /// Handles <c>Vector2/3</c>, <c>Color</c>, user-defined types, etc. without
        /// needing a dedicated overload for each.
        /// </summary>
        public static void SetCustomData(string key, object value)
        {
            if (!EnsureStarted()) return;
            string s = null;
            if (value != null)
            {
                s = value is System.IFormattable f
                    ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                    : value.ToString();
            }
            BugpunchNative.SetCustomData(key, s);
        }

        /// <summary>
        /// Clear a custom data entry.
        /// </summary>
        public static void ClearCustomData(string key)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, null);
        }

        /// <summary>
        /// Add a runtime attachment allow-list rule. Prefer configuring rules
        /// in the BugpunchConfig ScriptableObject (reviewable in the Inspector)
        /// \u2014 this API is the escape hatch for genuinely dynamic paths. Server
        /// "Request More Info" directives can only target paths that match an
        /// allow-list rule, so the game stays in control of what may leave the
        /// device. Call any time; rules take effect on the next config sync.
        /// </summary>
        public static void AddAttachmentRule(string name, string path, string pattern, long maxBytes)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pattern)) return;
            BugpunchClient.AddRuntimeAttachmentRule(new BugpunchConfig.AttachmentRule
            {
                name = name ?? "rule",
                path = path,
                pattern = pattern,
                maxBytes = maxBytes,
            });
        }

        /// <summary>
        /// Mark a moment in the session with a label. Included in any
        /// subsequent bug report as a trace event (ring buffer, max 50).
        /// No-op if BugpunchClient isn't initialized.
        /// </summary>
        public static void Trace(string label)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.Trace(label, null);
        }

        /// <summary>
        /// Mark a moment with a label and extra string tags.
        /// </summary>
        public static void Trace(string label, System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.Trace(label, SerializeTags(tags));
        }

        /// <summary>
        /// Mark a moment with a label and arbitrary key-value data.
        /// Values are serialized by type: numbers stay numeric, bools stay
        /// boolean, null stays null, everything else is ToString'd.
        /// </summary>
        public static void Trace(string label, System.Collections.IDictionary data)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.Trace(label, SerializeDict(data));
        }

        /// <summary>
        /// Same as <see cref="Trace(string)"/>, but also captures a screenshot
        /// at call time and attaches it to the next bug report.
        /// </summary>
        public static void TraceScreenshot(string label)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.TraceScreenshot(label, null);
        }

        /// <summary>
        /// Same as Trace with tags, but also captures a screenshot.
        /// </summary>
        public static void TraceScreenshot(string label, System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.TraceScreenshot(label, SerializeTags(tags));
        }

        /// <summary>
        /// Same as TraceScreenshot but with arbitrary key-value data.
        /// </summary>
        public static void TraceScreenshot(string label, System.Collections.IDictionary data)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.TraceScreenshot(label, SerializeDict(data));
        }

        static string SerializeTags(System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (tags == null || tags.Count == 0) return null;
            var sb = new System.Text.StringBuilder(64);
            sb.Append('{');
            bool first = true;
            foreach (var kv in tags)
            {
                if (kv.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(BugpunchJson.Esc(kv.Key)).Append("\":\"")
                    .Append(BugpunchJson.Esc(kv.Value ?? "")).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static string SerializeDict(System.Collections.IDictionary dict)
        {
            if (dict == null || dict.Count == 0) return null;
            var sb = new System.Text.StringBuilder(128);
            sb.Append('{');
            bool first = true;
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                if (entry.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(BugpunchJson.Esc(entry.Key.ToString())).Append("\":");
                AppendJsonValue(sb, entry.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendJsonValue(System.Text.StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is int i) { sb.Append(i); return; }
            if (value is long l) { sb.Append(l); return; }
            if (value is float f) { sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
            if (value is double d) { sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
            sb.Append('"').Append(BugpunchJson.Esc(value.ToString())).Append('"');
        }

        // ── Game Config Variables ──

        /// <summary>
        /// Read a game config variable set on the Bugpunch dashboard.
        /// Variables are fetched from the server on startup. Overrides
        /// matched to this device's GPU/memory/screen/platform are
        /// automatically applied — the caller gets the resolved value.
        /// Returns <paramref name="defaultValue"/> if the key is not set
        /// or config hasn't been fetched yet.
        /// </summary>
        public static string GetVariable(string key, string defaultValue = null)
            => BugpunchClient.GetVariable(key, defaultValue);

        /// <summary>Convenience: read a boolean game config variable.</summary>
        public static bool GetVariableBool(string key, bool defaultValue = false)
        {
            var v = GetVariable(key);
            if (v == null) return defaultValue;
            if (v == "true" || v == "1") return true;
            if (v == "false" || v == "0") return false;
            return defaultValue;
        }

        /// <summary>Convenience: read an integer game config variable.</summary>
        public static int GetVariableInt(string key, int defaultValue = 0)
        {
            var v = GetVariable(key);
            return v != null && int.TryParse(v, out var n) ? n : defaultValue;
        }

        /// <summary>Convenience: read a float game config variable.</summary>
        public static float GetVariableFloat(string key, float defaultValue = 0f)
        {
            var v = GetVariable(key);
            return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
        }

        /// <summary>
        /// Toggle the on-screen SDK self-diagnostic banner at runtime. Defaults
        /// to <c>BugpunchConfig.showSdkErrorOverlay</c> at startup. Errors are
        /// always logged to the Unity console regardless — this only controls
        /// the visible banner.
        /// </summary>
        public static void SetSdkErrorOverlay(bool enabled) => BugpunchNative.SetSdkErrorOverlay(enabled);

        /// <summary>
        /// Report an internal SDK problem to the on-screen diagnostic banner.
        /// Intended for use inside the SDK itself — game code shouldn't normally
        /// call this. Safe to call before init and never throws.
        /// </summary>
        public static void SdkError(string source, string message)
            => BugpunchNative.ReportSdkError(source, message, null);

        /// <summary>
        /// Report an internal SDK exception to the on-screen diagnostic banner.
        /// Intended for use inside the SDK itself — game code shouldn't normally
        /// call this. Safe to call before init and never throws.
        /// </summary>
        public static void SdkError(string source, System.Exception exception)
            => BugpunchNative.ReportSdkError(source, exception);

        /// <summary>
        /// Override the release branch for this process. Takes precedence over
        /// the BugpunchConfig asset's `branch` field. Call before SDK init — e.g.
        /// from a CI-generated partial class that bakes the current git branch
        /// into the build.
        /// </summary>
        public static void SetBranch(string branch) => BugpunchClient.SetBranchOverride(branch);

        /// <summary>
        /// Override the changeset (commit SHA, CI build number, etc) for this
        /// process. Takes precedence over the BugpunchConfig asset's `changeset`
        /// field. Call before SDK init — typically from a CI-generated partial
        /// class populated at build time.
        /// </summary>
        public static void SetChangeset(string changeset) => BugpunchClient.SetChangesetOverride(changeset);

        static bool EnsureStarted()
        {
            if (BugpunchClient.Instance != null) return true;
            BugpunchLog.Warn("Bugpunch", "BugpunchClient not initialized. Call BugpunchClient.StartConnection() first.");
            return false;
        }
    }
}
