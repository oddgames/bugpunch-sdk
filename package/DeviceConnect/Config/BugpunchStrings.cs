using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Central string table — every user-facing string drawn by the SDK
    /// (consent sheet, welcome card, request-help picker, crash overlay,
    /// floating widget, bug-report form, native AlertDialog wrappers) lives
    /// here so a customer reskinning the SDK has a single place to rewrite
    /// the wording.
    ///
    /// Defaults are English; the <see cref="translations"/> array holds
    /// optional locale overrides — each entry only needs to fill in the
    /// strings it actually localises, anything missing falls back to the
    /// default. Active locale is picked from <see cref="locale"/> ("auto"
    /// means "use Application.systemLanguage").
    ///
    /// Serialised into the runtime config JSON by
    /// <see cref="BugpunchNative.BuildConfigJson"/> as a flat
    /// {locale, defaults, translations} sub-object so the Java + ObjC
    /// helpers can resolve strings without a managed call.
    /// </summary>
    [Serializable]
    public class BugpunchStrings
    {
        [Tooltip("Active locale. \"auto\" picks the closest match to Application.systemLanguage; otherwise an ISO 639-1 code like \"en\", \"es\", \"fr\", or a region-tagged code like \"es-MX\".")]
        public string locale = "auto";

        // ── Consent Sheet (Bugpunch.EnterDebugMode) ──
        [Header("Consent Sheet")]
        public string consentTitle = "Enable debug recording";
        [TextArea(2, 6)]
        public string consentBody = "Your screen will be recorded so bug reports can include the moments leading up to an issue. Recording stays on your device until you submit a report.";
        public string consentStart = "Start Recording";
        public string consentCancel = "Not now";

        // ── Welcome Card (legacy / one-shot welcome) ──
        [Header("Welcome Card")]
        public string welcomeTitle = "Report a Bug";
        [TextArea(2, 6)]
        public string welcomeBody = "We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details.";
        public string welcomeConfirm = "Got it";
        public string welcomeCancel = "Cancel";

        // ── Request-help picker ──
        [Header("Request Help Picker")]
        public string pickerTitle = "What would you like to do?";
        [TextArea(2, 4)]
        public string pickerSubtitle = "Pick what fits — we'll only bother the dev team with what you send.";
        public string pickerCancel = "Cancel";
        public string pickerAskTitle = "Ask for help";
        public string pickerAskCaption = "Short question to the dev team";
        public string pickerBugTitle = "Record a bug";
        public string pickerBugCaption = "Capture a video + report a problem";
        public string pickerFeatureTitle = "Request a feature";
        public string pickerFeatureCaption = "Suggest / vote on improvements";

        // ── Floating recording widget ──
        [Header("Floating Widget")]
        public string widgetReport = "Report";
        public string widgetTools = "Tools";

        // ── Crash overlay ──
        [Header("Crash Overlay")]
        public string crashHeader = "Crash Report";
        public string crashStackTrace = "Stack Trace:";
        public string crashTitleField = "Title";
        public string crashTitleHint = "Brief description of the issue";
        public string crashDescField = "Description (what were you doing?)";
        public string crashDescHint = "Steps to reproduce, additional context...";
        public string crashSeverity = "Severity";
        public string crashIncludeVideo = "Include video";
        public string crashIncludeLogs = "Include logs";
        public string crashSubmit = "Submit Report";
        public string crashDismiss = "Dismiss";

        // ── Bug-report form ──
        [Header("Bug Report Form")]
        public string reportFormTitle = "Bug Report";
        public string reportFormTitleField = "Title";
        public string reportFormTitleHint = "Brief description of the issue";
        public string reportFormDescField = "Description";
        public string reportFormDescHint = "Optional details";
        public string reportFormSubmit = "Submit";
        public string reportFormCancel = "Cancel";

        // ── Permission prompt (script execution) ──
        [Header("Permission Prompts")]
        public string permissionTitle = "Script Permission";
        public string permissionAllowOnce = "Allow Once";
        public string permissionAllowAlways = "Always Allow";
        public string permissionDeny = "Deny";

        // ── Annotation toolbar (screenshot mark-up) ──
        [Header("Annotate Toolbar")]
        public string annotateUndo = "Undo";
        public string annotateClear = "Clear";
        public string annotateCancel = "Cancel";
        public string annotateDone = "Done";

        // ── Tools panel ──
        [Header("Tools Panel")]
        public string toolsTitle = "Debug Tools";
        public string toolsClose = "Close";
        public string toolsSearchHint = "Search...";
        public string toolsAllCategory = "All";
        public string toolsEmptyState = "No tools registered.";
        public string toolsRunButton = "Run";
        public string toolsRunningButton = "Running…";

        // ── Bug report form (full form, separate from the AlertDialog wrapper) ──
        [Header("Bug Report Form (full)")]
        public string reportFormEyebrow = "BUG REPORT";
        public string reportFormHeader = "Tell us what happened";
        public string reportFormEmailLabel = "Your email";
        public string reportFormEmailHint = "you@studio.com";
        public string reportFormDescPlaceholder = "What went wrong? Steps to reproduce?";
        public string reportFormSeverityLabel = "Severity";
        public string reportFormSeverityLow = "Low";
        public string reportFormSeverityMedium = "Medium";
        public string reportFormSeverityHigh = "High";
        public string reportFormSeverityCritical = "Critical";
        public string reportFormIncludeScreenshot = "Include screenshot";
        public string reportFormIncludeVideo = "Include video";
        public string reportFormIncludeLogs = "Include logs";
        public string reportFormAnnotate = "Annotate";
        public string reportFormAddImage = "Add image";
        public string reportFormSendButton = "Send report";
        public string reportFormTapToAnnotate = "Tap to annotate";
        public string reportFormScreenshotsLabel = "SCREENSHOTS";
        public string reportFormDescRequired = "Please add a description";
        public string reportFormSent = "Report sent";
        public string reportFormFailed = "Failed to send report";
        public string reportFormDeviceIdCopied = "Device ID copied";
        public string reportFormThanks = "Thanks — your report's been sent.";

        [Tooltip("Optional translations. Each entry overrides any subset of the default strings for a specific locale; missing fields fall back to the defaults.")]
        public LocaleOverride[] translations = Array.Empty<LocaleOverride>();

        /// <summary>
        /// One locale override. <see cref="overrides"/> is a flat
        /// key/value list — leave it empty for an inert entry. The key
        /// must match a field name on <see cref="BugpunchStrings"/> exactly
        /// (e.g. "consentTitle"). Unknown keys are ignored at runtime.
        /// </summary>
        [Serializable]
        public class LocaleOverride
        {
            [Tooltip("Locale code, e.g. \"es\", \"fr\", \"es-MX\".")]
            public string locale = "";

            [Tooltip("Per-key overrides. Keys must match the field names on BugpunchStrings (consentTitle, welcomeBody, etc).")]
            public KeyValue[] overrides = Array.Empty<KeyValue>();
        }

        [Serializable]
        public class KeyValue
        {
            public string key;
            [TextArea(1, 4)]
            public string value;
        }

        /// <summary>
        /// Live resolver — every C# UI surface calls this once per element.
        /// Returns the active config's strings, or a fresh default if the
        /// SDK hasn't initialised yet.
        /// </summary>
        public static BugpunchStrings Current
        {
            get
            {
                var client = BugpunchClient.Instance;
                var cfg = client?.Config;
                return cfg?.Strings ?? new BugpunchStrings();
            }
        }

        /// <summary>
        /// Resolve a single string by field name. Returns the active-locale
        /// override if set, the default otherwise. Used by C# UIs that build
        /// labels from a key (parallels the native <c>BugpunchStrings.text</c>
        /// helpers).
        /// </summary>
        public string Text(string key, string fallback = "")
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            string active = ResolveActiveLocale();
            // Locale override first.
            if (!string.IsNullOrEmpty(active) && translations != null)
            {
                foreach (var t in translations)
                {
                    if (t == null || t.locale != active || t.overrides == null) continue;
                    foreach (var kv in t.overrides)
                        if (kv != null && kv.key == key && !string.IsNullOrEmpty(kv.value))
                            return kv.value;
                }
            }
            // Default field by reflection — slower than a switch, but only
            // used for rare keys not exposed via a typed property elsewhere.
            var field = typeof(BugpunchStrings).GetField(key,
                BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
            {
                var v = field.GetValue(this) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return fallback;
        }

        /// <summary>
        /// Resolve the active locale: explicit setting wins, else
        /// auto-detect from <see cref="Application.systemLanguage"/>.
        /// </summary>
        public string ResolveActiveLocale()
        {
            if (!string.IsNullOrEmpty(locale) && locale != "auto") return locale;
            return SystemLanguageToCode(Application.systemLanguage);
        }

        // Map Unity's enum to ISO 639-1 codes the same way native does. Only
        // the languages we genuinely support need to be listed; everything
        // else falls back to "en".
        static string SystemLanguageToCode(SystemLanguage l)
        {
            switch (l)
            {
                case SystemLanguage.English:    return "en";
                case SystemLanguage.Spanish:    return "es";
                case SystemLanguage.French:     return "fr";
                case SystemLanguage.German:     return "de";
                case SystemLanguage.Italian:    return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Russian:    return "ru";
                case SystemLanguage.Japanese:   return "ja";
                case SystemLanguage.Korean:     return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:  return "zh";
                case SystemLanguage.ChineseTraditional: return "zh-TW";
                case SystemLanguage.Dutch:      return "nl";
                case SystemLanguage.Polish:     return "pl";
                case SystemLanguage.Turkish:    return "tr";
                case SystemLanguage.Arabic:     return "ar";
                default:                         return "en";
            }
        }

        // ── JSON serialisation for the native bridge ──

        /// <summary>
        /// Emit this strings table as a JSON sub-object suitable for the
        /// native config blob. Shape:
        /// <c>{"locale":"...","defaults":{...},"translations":{"es":{...},...}}</c>.
        /// Native helpers parse this once at startup.
        /// </summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder(1024);
            sb.Append('{');
            AppendField(sb, "locale", string.IsNullOrEmpty(locale) ? "auto" : locale); sb.Append(',');

            sb.Append("\"defaults\":{");
            bool first = true;
            foreach (var fld in EnumerateStringFields())
            {
                var v = fld.GetValue(this) as string;
                if (!first) sb.Append(',');
                first = false;
                AppendField(sb, fld.Name, v ?? "");
            }
            sb.Append('}');

            sb.Append(",\"translations\":{");
            bool firstLocale = true;
            if (translations != null)
            {
                foreach (var t in translations)
                {
                    if (t == null || string.IsNullOrEmpty(t.locale) || t.overrides == null) continue;
                    if (!firstLocale) sb.Append(',');
                    firstLocale = false;
                    sb.Append('"').Append(BugpunchJson.Esc(t.locale)).Append("\":{");
                    bool firstKv = true;
                    foreach (var kv in t.overrides)
                    {
                        if (kv == null || string.IsNullOrEmpty(kv.key)) continue;
                        if (!firstKv) sb.Append(',');
                        firstKv = false;
                        AppendField(sb, kv.key, kv.value ?? "");
                    }
                    sb.Append('}');
                }
            }
            sb.Append('}');

            sb.Append('}');
            return sb.ToString();
        }

        static IEnumerable<FieldInfo> EnumerateStringFields()
        {
            // Public instance string fields, excluding `locale` (carried
            // alongside the defaults block) and the translations array.
            foreach (var f in typeof(BugpunchStrings).GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(string)) continue;
                if (f.Name == "locale") continue;
                yield return f;
            }
        }

        static void AppendField(System.Text.StringBuilder sb, string k, string v)
        {
            sb.Append('"').Append(BugpunchJson.Esc(k)).Append("\":\"").Append(BugpunchJson.Esc(v)).Append('"');
        }

    }
}
