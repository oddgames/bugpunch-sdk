package au.com.oddgames.bugpunch;

import android.content.Context;
import android.os.LocaleList;
import android.util.Log;

import org.json.JSONObject;

import java.util.HashMap;
import java.util.Iterator;
import java.util.Locale;

/**
 * Central string-table helper for all Bugpunch native Android surfaces.
 *
 * <p>The C# side serialises a {@code strings} sub-object into the runtime
 * config blob: <code>{ locale, defaults, translations }</code>. We read it
 * once via {@link #apply(JSONObject)} and stash everything in flat maps so
 * {@link #text(String, String)} can resolve a key without re-parsing.
 *
 * <p>Resolution order on a {@code text} call:
 * <ol>
 *   <li>Active locale's override (locale = explicit setting if non-"auto",
 *       otherwise the device's primary language).</li>
 *   <li>Default ({@code en}) value supplied by the C# table.</li>
 *   <li>The caller's hardcoded fallback (final safety net).</li>
 * </ol>
 *
 * <p>We do NOT fall back to a partial language match (e.g. "es-MX" → "es")
 * — that's the C# side's job to manage explicitly via the override list,
 * keeping native parsing trivial and predictable.
 */
public final class BugpunchStrings {
    private static final String TAG = "[Bugpunch.Strings]";

    private static final HashMap<String, String> sDefaults = new HashMap<>();
    private static final HashMap<String, HashMap<String, String>> sTranslations = new HashMap<>();
    private static volatile String sLocale = "auto";
    private static volatile boolean sApplied = false;

    private BugpunchStrings() {}

    /**
     * Apply the strings dictionary from the startup config. Safe to call
     * more than once; each call replaces the previously loaded values.
     */
    public static synchronized void apply(JSONObject stringsJson) {
        sDefaults.clear();
        sTranslations.clear();
        if (stringsJson == null) {
            sApplied = true;
            return;
        }

        sLocale = stringsJson.optString("locale", "auto");

        JSONObject defaults = stringsJson.optJSONObject("defaults");
        if (defaults != null) {
            for (Iterator<String> it = defaults.keys(); it.hasNext(); ) {
                String k = it.next();
                String v = defaults.optString(k, null);
                if (v != null) sDefaults.put(k, v);
            }
        }

        JSONObject translations = stringsJson.optJSONObject("translations");
        if (translations != null) {
            for (Iterator<String> it = translations.keys(); it.hasNext(); ) {
                String localeCode = it.next();
                JSONObject set = translations.optJSONObject(localeCode);
                if (set == null) continue;
                HashMap<String, String> bucket = new HashMap<>();
                for (Iterator<String> it2 = set.keys(); it2.hasNext(); ) {
                    String k = it2.next();
                    String v = set.optString(k, null);
                    if (v != null) bucket.put(k, v);
                }
                sTranslations.put(localeCode, bucket);
            }
        }

        sApplied = true;
        Log.i(TAG, "strings applied: defaults=" + sDefaults.size()
                + " translations=" + sTranslations.size()
                + " locale=" + sLocale);
    }

    /** Was {@link #apply(JSONObject)} called at least once? */
    public static boolean isApplied() { return sApplied; }

    /**
     * Resolve a string by key. Active-locale override → default → fallback.
     * Always non-null.
     */
    public static String text(String key, String fallback) {
        if (key == null || key.isEmpty()) return fallback != null ? fallback : "";

        String active = activeLocale();
        if (active != null) {
            HashMap<String, String> bucket = sTranslations.get(active);
            if (bucket != null) {
                String v = bucket.get(key);
                if (v != null && !v.isEmpty()) return v;
            }
            // Two-letter region-stripped fallback: try "es" if "es-MX"
            // wasn't an exact hit. Cheap and matches what most apps want.
            int dash = active.indexOf('-');
            if (dash > 0) {
                String shortCode = active.substring(0, dash);
                HashMap<String, String> shortBucket = sTranslations.get(shortCode);
                if (shortBucket != null) {
                    String v = shortBucket.get(key);
                    if (v != null && !v.isEmpty()) return v;
                }
            }
        }

        String d = sDefaults.get(key);
        if (d != null && !d.isEmpty()) return d;
        return fallback != null ? fallback : "";
    }

    /**
     * Returns the active locale code resolved against the device's primary
     * language when {@code locale == "auto"}. Empty string if we couldn't
     * detect anything (very old Android with no LocaleList).
     */
    public static String activeLocale() {
        if (sLocale != null && !"auto".equals(sLocale)) return sLocale;
        return deviceLocale();
    }

    private static String deviceLocale() {
        try {
            LocaleList list = LocaleList.getDefault();
            if (list != null && list.size() > 0) {
                Locale primary = list.get(0);
                String lang = primary.getLanguage();
                String region = primary.getCountry();
                if (lang == null || lang.isEmpty()) return "";
                return (region != null && !region.isEmpty())
                    ? lang + "-" + region : lang;
            }
        } catch (Throwable ignore) { /* fall through */ }
        try {
            Locale primary = Locale.getDefault();
            if (primary != null) {
                String lang = primary.getLanguage();
                if (lang != null && !lang.isEmpty()) return lang;
            }
        } catch (Throwable ignore) { /* fall through */ }
        return "";
    }
}
