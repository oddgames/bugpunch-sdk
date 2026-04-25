package au.com.oddgames.bugpunch;

import android.content.Context;
import android.graphics.Color;
import android.util.Log;
import android.util.TypedValue;

import org.json.JSONObject;

import java.util.HashMap;
import java.util.Iterator;

/**
 * Central theme helper for all Bugpunch native Android surfaces.
 *
 * <p>The C# side writes a {@code theme} dictionary into the config blob passed to
 * {@link BugpunchRuntime#start(android.app.Activity, String)} /
 * {@link BugpunchDebugMode#enter(android.app.Activity, boolean)}. On startup we
 * read that once via {@link #apply(JSONObject)} and stash each value in a flat
 * map so overlay-builder code can call {@link #color(String, int)},
 * {@link #dp(String, int)}, and {@link #sp(String, int)} without re-parsing.
 *
 * <p>If a key is missing or malformed we fall back to the schema default so a
 * game that never customises anything still renders correctly.
 */
public final class BugpunchTheme {
    private static final String TAG = "[Bugpunch.Theme]";

    // ── Defaults (must mirror the shared theme schema) ──
    // Colours are stored as ARGB ints so Android View APIs can use them
    // directly. Dimensions are stored as raw dp / sp values; callers convert
    // with dp()/sp() using their own Context.
    private static final int DEF_CARD_BG         = 0xFF212121;
    private static final int DEF_CARD_BORDER     = 0xFF474747;
    private static final int DEF_BACKDROP        = 0x99000000;
    private static final int DEF_TEXT_PRIMARY    = 0xFFFFFFFF;
    private static final int DEF_TEXT_SECONDARY  = 0xFFB8B8B8;
    private static final int DEF_TEXT_MUTED      = 0xFF8C8C8C;
    private static final int DEF_ACCENT_PRIMARY  = 0xFF407D4C;
    private static final int DEF_ACCENT_RECORD   = 0xFFD22E2E;
    private static final int DEF_ACCENT_CHAT     = 0xFF336199;
    private static final int DEF_ACCENT_FEEDBACK = 0xFF407D4C;
    private static final int DEF_ACCENT_BUG      = 0xFF943838;
    private static final int DEF_CARD_RADIUS_DP  = 12;
    private static final int DEF_FONT_TITLE_SP   = 20;
    private static final int DEF_FONT_BODY_SP    = 14;
    private static final int DEF_FONT_CAPTION_SP = 12;

    private static final HashMap<String, Integer> sColors = new HashMap<>();
    private static final HashMap<String, Integer> sDps    = new HashMap<>();
    private static final HashMap<String, Integer> sSps    = new HashMap<>();
    private static volatile boolean sApplied = false;

    static {
        // Seed defaults so pre-apply lookups still work (e.g. if an overlay
        // is built before BugpunchDebugMode.startDebugMode runs).
        sColors.put("cardBackground",  DEF_CARD_BG);
        sColors.put("cardBorder",      DEF_CARD_BORDER);
        sColors.put("backdrop",        DEF_BACKDROP);
        sColors.put("textPrimary",     DEF_TEXT_PRIMARY);
        sColors.put("textSecondary",   DEF_TEXT_SECONDARY);
        sColors.put("textMuted",       DEF_TEXT_MUTED);
        sColors.put("accentPrimary",   DEF_ACCENT_PRIMARY);
        sColors.put("accentRecord",    DEF_ACCENT_RECORD);
        sColors.put("accentChat",      DEF_ACCENT_CHAT);
        sColors.put("accentFeedback",  DEF_ACCENT_FEEDBACK);
        sColors.put("accentBug",       DEF_ACCENT_BUG);

        sDps.put("cardRadius", DEF_CARD_RADIUS_DP);

        sSps.put("fontSizeTitle",   DEF_FONT_TITLE_SP);
        sSps.put("fontSizeBody",    DEF_FONT_BODY_SP);
        sSps.put("fontSizeCaption", DEF_FONT_CAPTION_SP);
    }

    private BugpunchTheme() {}

    /**
     * Apply the theme dictionary from the startup config. Safe to call more
     * than once; each call replaces the previously loaded values.
     */
    public static synchronized void apply(JSONObject themeJson) {
        if (themeJson == null) {
            sApplied = true;
            return;
        }
        for (Iterator<String> it = themeJson.keys(); it.hasNext(); ) {
            String k = it.next();
            Object v = themeJson.opt(k);
            if (v == null) continue;

            if (sColors.containsKey(k)) {
                Integer parsed = parseColor(v);
                if (parsed != null) sColors.put(k, parsed);
            } else if (sDps.containsKey(k) || k.endsWith("Radius")) {
                int n = intFromJson(v, Integer.MIN_VALUE);
                if (n != Integer.MIN_VALUE) sDps.put(k, n);
            } else if (sSps.containsKey(k) || k.startsWith("fontSize")) {
                int n = intFromJson(v, Integer.MIN_VALUE);
                if (n != Integer.MIN_VALUE) sSps.put(k, n);
            }
        }
        sApplied = true;
        Log.i(TAG, "theme applied: cardBg=" + Integer.toHexString(sColors.get("cardBackground"))
                + " accentPrimary=" + Integer.toHexString(sColors.get("accentPrimary"))
                + " radius=" + sDps.get("cardRadius")
                + " titleSp=" + sSps.get("fontSizeTitle"));
    }

    /** Was {@link #apply(JSONObject)} called at least once? */
    public static boolean isApplied() { return sApplied; }

    /** ARGB colour for the given key, or {@code fallback} if missing. */
    public static int color(String name, int fallback) {
        Integer v = sColors.get(name);
        return v != null ? v : fallback;
    }

    /** Raw dp value for the given key (caller multiplies by density as needed). */
    public static int dpRaw(String name, int fallback) {
        Integer v = sDps.get(name);
        return v != null ? v : fallback;
    }

    /**
     * Convert the theme's raw dp value for {@code name} into px using the
     * given Context's display metrics.
     */
    public static int dp(Context ctx, String name, int fallback) {
        int raw = dpRaw(name, fallback);
        return (int) (raw * ctx.getResources().getDisplayMetrics().density + 0.5f);
    }

    /** Raw sp value for the given key (use with {@code setTextSize(SP, …)}). */
    public static int sp(String name, int fallback) {
        Integer v = sSps.get(name);
        return v != null ? v : fallback;
    }

    /**
     * Helper — applies {@code setTextSize(COMPLEX_UNIT_SP, sp(name, fallback))}
     * on the given TextView. Kept as a one-liner so overlay code stays tidy.
     */
    public static void applyTextSize(android.widget.TextView tv, String name, int fallback) {
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(name, fallback));
    }

    // ── Parsing helpers ──

    /**
     * Parse a hex colour string ({@code #RRGGBB} or {@code #RRGGBBAA}) into an
     * ARGB int. Accepts {@code java.lang.Number} values too (already an int).
     * Returns {@code null} if the value can't be decoded.
     */
    private static Integer parseColor(Object v) {
        if (v instanceof Number) return ((Number) v).intValue();
        if (!(v instanceof String)) return null;
        String s = ((String) v).trim();
        if (s.isEmpty()) return null;
        if (s.charAt(0) != '#') s = "#" + s;

        // Android's Color.parseColor handles #RRGGBB and #AARRGGBB. Our schema
        // uses #RRGGBBAA, so reshuffle that format before delegating.
        try {
            if (s.length() == 9) {
                // #RRGGBBAA → #AARRGGBB
                String aa = s.substring(7, 9);
                String rrggbb = s.substring(1, 7);
                return Color.parseColor("#" + aa + rrggbb);
            }
            return Color.parseColor(s);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private static int intFromJson(Object v, int fallback) {
        if (v instanceof Number) return ((Number) v).intValue();
        if (v instanceof String) {
            try { return Integer.parseInt(((String) v).trim()); }
            catch (NumberFormatException ignored) {}
        }
        return fallback;
    }
}
