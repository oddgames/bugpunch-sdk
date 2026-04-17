package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;

/**
 * Native full-screen debug tools panel. Shows categories on the left (or as
 * horizontal tabs in portrait), a search bar, and tool items with button /
 * toggle / slider controls on the right.
 *
 * Tool definitions are passed as a JSON string from C# via the Intent extra
 * "tools_json". Each tool has: name, category, description, controlType
 * (button/toggle/slider), icon, color, id. Callbacks fire via
 * UnitySendMessage when the user interacts with a control.
 */
public class BugpunchToolsActivity extends Activity {
    private static final int COL_BG       = 0xF8141820;
    private static final int COL_PANEL    = 0xFF1C1F28;
    private static final int COL_ACCENT   = 0xFF4090F0;
    private static final int COL_DANGER   = 0xFFDA3838;
    private static final int COL_WARN     = 0xFFD99A2E;
    private static final int COL_TEXT     = 0xFFE6E8EE;
    private static final int COL_DIM      = 0xFF8C90A0;
    private static final int COL_CAT      = 0xFF23262F;
    private static final int COL_CAT_SEL  = 0xFF333849;
    private static final int COL_ITEM     = 0xFF1E222C;
    private static final int COL_SEARCH   = 0xFF282C38;
    private static final int COL_TOG_ON   = 0xFF34B85C;
    private static final int COL_TOG_OFF  = 0xFF4D5060;

    private List<ToolItem> allTools = new ArrayList<>();
    private String activeCategory = "All";
    private String searchFilter = "";
    private LinearLayout toolListLayout;
    private LinearLayout categoryLayout;

    static class ToolItem {
        String id, name, category, description, icon, controlType;
        int color;
        boolean toggleValue;
        float sliderValue, sliderMin, sliderMax;
    }

    public static void launch(String toolsJson) {
        Activity activity = BugpunchUnity.currentActivity();
        if (activity == null) return;
        Intent intent = new Intent(activity, BugpunchToolsActivity.class);
        intent.putExtra("tools_json", toolsJson);
        activity.startActivity(intent);
        activity.overridePendingTransition(android.R.anim.fade_in, 0);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        parseTools(getIntent().getStringExtra("tools_json"));

        boolean landscape = getResources().getConfiguration().orientation
            == android.content.res.Configuration.ORIENTATION_LANDSCAPE;
        int pad = dp(landscape ? 10 : 16);

        // Transparent root — tap outside the panel to dismiss.
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(0x80000000); // 50% black scrim
        root.setOnClickListener(v -> finish());

        // Panel card — smaller than screen, semi-transparent, rounded corners.
        // In landscape use minimal margins so the tool list has room.
        LinearLayout main = new LinearLayout(this);
        main.setOrientation(LinearLayout.VERTICAL);
        main.setPadding(pad, pad, pad, pad);
        main.setOnClickListener(v -> {}); // consume taps so they don't pass through to dismiss
        GradientDrawable panelBg = new GradientDrawable();
        panelBg.setColor(0xE6141820); // 90% opacity dark
        panelBg.setCornerRadius(dp(16));
        main.setBackground(panelBg);
        main.setElevation(dp(12));
        int hMargin = dp(landscape ? 60 : 24);
        int vMargin = dp(landscape ? 12 : 40);
        FrameLayout.LayoutParams mainLp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        mainLp.setMargins(hMargin, vMargin, hMargin, vMargin);
        main.setLayoutParams(mainLp);

        // ── Header: title + search + close ──
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(0, 0, 0, dp(landscape ? 4 : 12));

        TextView title = new TextView(this);
        title.setText("Debug Tools");
        title.setTextColor(COL_TEXT);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 0);
        header.addView(title, titleLp);

        EditText search = new EditText(this);
        search.setHint("Search...");
        search.setHintTextColor(COL_DIM);
        search.setTextColor(COL_TEXT);
        search.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        search.setSingleLine(true);
        search.setImeOptions(EditorInfo.IME_ACTION_DONE);
        search.setPadding(dp(12), dp(8), dp(12), dp(8));
        GradientDrawable searchBg = new GradientDrawable();
        searchBg.setColor(COL_SEARCH);
        searchBg.setCornerRadius(dp(8));
        search.setBackground(searchBg);
        search.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) {
                searchFilter = s.toString();
                rebuildToolList();
            }
        });
        LinearLayout.LayoutParams searchLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1);
        searchLp.leftMargin = dp(12);
        searchLp.rightMargin = dp(12);
        header.addView(search, searchLp);

        TextView closeBtn = new TextView(this);
        closeBtn.setText("X");
        closeBtn.setTextColor(COL_DIM);
        closeBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        closeBtn.setGravity(Gravity.CENTER);
        closeBtn.setPadding(dp(12), dp(8), dp(12), dp(8));
        closeBtn.setOnClickListener(v -> finish());
        header.addView(closeBtn);

        main.addView(header);

        // ── Categories (horizontal scroll) ──
        HorizontalScrollView catScroll = new HorizontalScrollView(this);
        catScroll.setHorizontalScrollBarEnabled(false);
        catScroll.setPadding(0, 0, 0, dp(12));
        categoryLayout = new LinearLayout(this);
        categoryLayout.setOrientation(LinearLayout.HORIZONTAL);
        catScroll.addView(categoryLayout);
        main.addView(catScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        rebuildCategories();

        // ── Tool list (scrollable) ──
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        toolListLayout = new LinearLayout(this);
        toolListLayout.setOrientation(LinearLayout.VERTICAL);
        toolListLayout.setPadding(0, 0, 0, dp(20));
        scroll.addView(toolListLayout);
        main.addView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, 0, 1));

        root.addView(main, mainLp);
        setContentView(root);
        rebuildToolList();
    }

    @Override
    public void finish() {
        super.finish();
        overridePendingTransition(0, android.R.anim.fade_out);
    }

    // ── Parse tool definitions from JSON ──

    private void parseTools(String json) {
        if (json == null || json.isEmpty()) return;
        try {
            JSONArray arr = new JSONArray(json);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject o = arr.getJSONObject(i);
                ToolItem t = new ToolItem();
                t.id = o.optString("id", "tool_" + i);
                t.name = o.optString("name", "Tool");
                t.category = o.optString("category", "General");
                t.description = o.optString("description", "");
                t.icon = o.optString("icon", "settings");
                t.controlType = o.optString("controlType", "button");
                t.color = parseColor(o.optString("color", "accent"));
                t.toggleValue = o.optBoolean("toggleValue", false);
                t.sliderValue = (float) o.optDouble("sliderValue", 0);
                t.sliderMin = (float) o.optDouble("sliderMin", 0);
                t.sliderMax = (float) o.optDouble("sliderMax", 1);
                allTools.add(t);
            }
        } catch (Exception e) {
            android.util.Log.w("BugpunchTools", "parse failed", e);
        }
    }

    private int parseColor(String c) {
        switch (c) {
            case "danger": return COL_DANGER;
            case "warn":   return COL_WARN;
            case "accent": return COL_ACCENT;
            default:
                try { return Color.parseColor(c); }
                catch (Exception e) { return COL_ACCENT; }
        }
    }

    // ── Category chips ──

    private void rebuildCategories() {
        categoryLayout.removeAllViews();
        LinkedHashSet<String> cats = new LinkedHashSet<>();
        cats.add("All");
        for (ToolItem t : allTools) cats.add(t.category);

        for (String cat : cats) {
            TextView chip = new TextView(this);
            chip.setText(cat);
            chip.setTextColor(cat.equals(activeCategory) ? COL_TEXT : COL_DIM);
            chip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
            chip.setPadding(dp(14), dp(8), dp(14), dp(8));
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(cat.equals(activeCategory) ? COL_CAT_SEL : COL_CAT);
            bg.setCornerRadius(dp(16));
            chip.setBackground(bg);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.rightMargin = dp(6);
            chip.setOnClickListener(v -> {
                activeCategory = cat;
                rebuildCategories();
                rebuildToolList();
            });
            categoryLayout.addView(chip, lp);
        }
    }

    // ── Tool list ──

    private void rebuildToolList() {
        toolListLayout.removeAllViews();
        String lastCategory = "";

        for (ToolItem t : allTools) {
            if (!"All".equals(activeCategory) && !t.category.equals(activeCategory)) continue;
            if (!searchFilter.isEmpty() &&
                !t.name.toLowerCase().contains(searchFilter.toLowerCase()) &&
                !t.description.toLowerCase().contains(searchFilter.toLowerCase())) continue;

            // Category header
            if ("All".equals(activeCategory) && !t.category.equals(lastCategory)) {
                lastCategory = t.category;
                TextView catHeader = new TextView(this);
                catHeader.setText(t.category);
                catHeader.setTextColor(COL_DIM);
                catHeader.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
                catHeader.setTypeface(Typeface.DEFAULT_BOLD);
                catHeader.setPadding(dp(4), dp(16), 0, dp(6));
                catHeader.setAllCaps(true);
                catHeader.setLetterSpacing(0.1f);
                toolListLayout.addView(catHeader);
            }

            toolListLayout.addView(makeToolRow(t));
        }
    }

    private View makeToolRow(ToolItem tool) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(12), dp(10), dp(12), dp(10));
        GradientDrawable rowBg = new GradientDrawable();
        rowBg.setColor(COL_ITEM);
        rowBg.setCornerRadius(dp(10));
        row.setBackground(rowBg);
        LinearLayout.LayoutParams rowLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rowLp.bottomMargin = dp(4);
        row.setLayoutParams(rowLp);

        // Icon
        ImageView icon = new ImageView(this);
        icon.setImageDrawable(new FeatherIcon(this, tool.icon, tool.color));
        LinearLayout.LayoutParams iconLp = new LinearLayout.LayoutParams(dp(28), dp(28));
        iconLp.rightMargin = dp(12);
        row.addView(icon, iconLp);

        // Name + description
        LinearLayout textCol = new LinearLayout(this);
        textCol.setOrientation(LinearLayout.VERTICAL);
        TextView name = new TextView(this);
        name.setText(tool.name);
        name.setTextColor(COL_TEXT);
        name.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        textCol.addView(name);
        if (tool.description != null && !tool.description.isEmpty()) {
            TextView desc = new TextView(this);
            desc.setText(tool.description);
            desc.setTextColor(COL_DIM);
            desc.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            desc.setMaxLines(2);
            desc.setEllipsize(android.text.TextUtils.TruncateAt.END);
            textCol.addView(desc);
        }
        row.addView(textCol, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));

        // Control
        switch (tool.controlType) {
            case "button":
                row.addView(makeButton(tool));
                break;
            case "toggle":
                row.addView(makeToggle(tool));
                break;
            case "slider":
                row.addView(makeSlider(tool));
                break;
        }

        return row;
    }

    private View makeButton(ToolItem tool) {
        TextView btn = new TextView(this);
        btn.setText("Run");
        btn.setTextColor(Color.WHITE);
        btn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        btn.setGravity(Gravity.CENTER);
        btn.setPadding(dp(16), dp(6), dp(16), dp(6));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(tool.color);
        bg.setCornerRadius(dp(8));
        btn.setBackground(bg);
        btn.setOnClickListener(v -> sendCallback(tool.id, "click", ""));
        return btn;
    }

    private View makeToggle(ToolItem tool) {
        FrameLayout track = new FrameLayout(this);
        GradientDrawable trackBg = new GradientDrawable();
        trackBg.setCornerRadius(dp(14));
        trackBg.setColor(tool.toggleValue ? COL_TOG_ON : COL_TOG_OFF);
        track.setBackground(trackBg);
        int w = dp(48), h = dp(28);
        track.setLayoutParams(new LinearLayout.LayoutParams(w, h));

        View knob = new View(this);
        GradientDrawable knobBg = new GradientDrawable();
        knobBg.setCornerRadius(dp(10));
        knobBg.setColor(Color.WHITE);
        knob.setBackground(knobBg);
        int knobSize = dp(20);
        FrameLayout.LayoutParams knobLp = new FrameLayout.LayoutParams(knobSize, knobSize);
        knobLp.gravity = Gravity.CENTER_VERTICAL;
        knobLp.leftMargin = tool.toggleValue ? w - knobSize - dp(4) : dp(4);
        track.addView(knob, knobLp);

        track.setOnClickListener(v -> {
            tool.toggleValue = !tool.toggleValue;
            trackBg.setColor(tool.toggleValue ? COL_TOG_ON : COL_TOG_OFF);
            knobLp.leftMargin = tool.toggleValue ? w - knobSize - dp(4) : dp(4);
            knob.setLayoutParams(knobLp);
            sendCallback(tool.id, "toggle", tool.toggleValue ? "true" : "false");
        });
        return track;
    }

    private View makeSlider(ToolItem tool) {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setGravity(Gravity.END);

        TextView val = new TextView(this);
        val.setText(String.format("%.1f", tool.sliderValue));
        val.setTextColor(COL_DIM);
        val.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        val.setGravity(Gravity.END);
        col.addView(val);

        SeekBar seek = new SeekBar(this);
        seek.setMax(1000);
        seek.setProgress(Math.round((tool.sliderValue - tool.sliderMin) / (tool.sliderMax - tool.sliderMin) * 1000));
        seek.getThumb().setTint(COL_ACCENT);
        seek.getProgressDrawable().setTint(COL_ACCENT);
        LinearLayout.LayoutParams seekLp = new LinearLayout.LayoutParams(dp(140), ViewGroup.LayoutParams.WRAP_CONTENT);
        col.addView(seek, seekLp);

        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar s, int progress, boolean fromUser) {
                float v = tool.sliderMin + (tool.sliderMax - tool.sliderMin) * progress / 1000f;
                tool.sliderValue = v;
                val.setText(String.format("%.1f", v));
                if (fromUser) sendCallback(tool.id, "slider", String.valueOf(v));
            }
            @Override public void onStartTrackingTouch(SeekBar s) {}
            @Override public void onStopTrackingTouch(SeekBar s) {}
        });
        return col;
    }

    private void sendCallback(String toolId, String action, String value) {
        BugpunchUnity.sendMessage("BugpunchToolsBridge", "OnToolAction",
            toolId + "|" + action + "|" + value);
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics());
    }

    // ── Feather icon renderer ──
    // Draws stroke-based icons on a Canvas from hardcoded path data.
    // Covers the ~30 most useful icons; unknown names fall back to a circle.

    static class FeatherIcon extends Drawable {
        private final Paint paint;
        private final String name;
        private final int size;

        FeatherIcon(Context ctx, String name, int color) {
            this.name = name;
            this.size = (int) TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP, 24, ctx.getResources().getDisplayMetrics());
            paint = new Paint(Paint.ANTI_ALIAS_FLAG);
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(size / 12f);
            paint.setStrokeCap(Paint.Cap.ROUND);
            paint.setStrokeJoin(Paint.Join.ROUND);
            paint.setColor(color);
        }

        @Override public void draw(Canvas c) {
            float s = getBounds().width();
            float u = s / 24f;  // scale unit (icons are 24x24 viewBox)
            float cx = s / 2, cy = s / 2;

            switch (name) {
                case "play":
                    Path play = new Path();
                    play.moveTo(5 * u, 3 * u); play.lineTo(19 * u, 12 * u); play.lineTo(5 * u, 21 * u); play.close();
                    c.drawPath(play, paint);
                    break;
                case "pause":
                    c.drawRect(6 * u, 4 * u, 10 * u, 20 * u, paint);
                    c.drawRect(14 * u, 4 * u, 18 * u, 20 * u, paint);
                    break;
                case "settings":
                case "gear":
                    c.drawCircle(cx, cy, 3 * u, paint);
                    // Simplified gear: outer circle with ticks
                    c.drawCircle(cx, cy, 8 * u, paint);
                    for (int i = 0; i < 8; i++) {
                        float a = (float) (i * Math.PI / 4);
                        c.drawLine(cx + (float)Math.cos(a) * 7 * u, cy + (float)Math.sin(a) * 7 * u,
                                   cx + (float)Math.cos(a) * 10 * u, cy + (float)Math.sin(a) * 10 * u, paint);
                    }
                    break;
                case "search":
                    c.drawCircle(11 * u, 11 * u, 8 * u, paint);
                    c.drawLine(16.65f * u, 16.65f * u, 21 * u, 21 * u, paint);
                    break;
                case "x":
                case "close":
                    c.drawLine(6 * u, 6 * u, 18 * u, 18 * u, paint);
                    c.drawLine(18 * u, 6 * u, 6 * u, 18 * u, paint);
                    break;
                case "alert-triangle":
                case "warning":
                    Path tri = new Path();
                    tri.moveTo(12 * u, 2 * u); tri.lineTo(2 * u, 22 * u); tri.lineTo(22 * u, 22 * u); tri.close();
                    c.drawPath(tri, paint);
                    c.drawLine(12 * u, 9 * u, 12 * u, 13 * u, paint);
                    c.drawPoint(12 * u, 17 * u, paint);
                    break;
                case "zap":
                case "bolt":
                    Path zap = new Path();
                    zap.moveTo(13 * u, 2 * u); zap.lineTo(3 * u, 14 * u); zap.lineTo(12 * u, 14 * u);
                    zap.lineTo(11 * u, 22 * u); zap.lineTo(21 * u, 10 * u); zap.lineTo(12 * u, 10 * u); zap.close();
                    c.drawPath(zap, paint);
                    break;
                case "eye":
                    c.drawCircle(cx, cy, 3 * u, paint);
                    Path eye = new Path();
                    eye.moveTo(1 * u, 12 * u);
                    eye.quadTo(12 * u, 3 * u, 23 * u, 12 * u);
                    eye.quadTo(12 * u, 21 * u, 1 * u, 12 * u);
                    c.drawPath(eye, paint);
                    break;
                case "activity":
                case "chart":
                    Path act = new Path();
                    act.moveTo(2 * u, 12 * u); act.lineTo(6 * u, 12 * u); act.lineTo(9 * u, 4 * u);
                    act.lineTo(13 * u, 20 * u); act.lineTo(16 * u, 12 * u); act.lineTo(22 * u, 12 * u);
                    c.drawPath(act, paint);
                    break;
                case "sliders":
                case "tune":
                    c.drawLine(4 * u, 6 * u, 20 * u, 6 * u, paint);
                    c.drawLine(4 * u, 12 * u, 20 * u, 12 * u, paint);
                    c.drawLine(4 * u, 18 * u, 20 * u, 18 * u, paint);
                    c.drawCircle(8 * u, 6 * u, 2 * u, paint);
                    c.drawCircle(16 * u, 12 * u, 2 * u, paint);
                    c.drawCircle(10 * u, 18 * u, 2 * u, paint);
                    break;
                case "send":
                    Path send = new Path();
                    send.moveTo(22 * u, 2 * u); send.lineTo(15 * u, 22 * u);
                    send.lineTo(11 * u, 13 * u); send.lineTo(2 * u, 9 * u); send.close();
                    c.drawPath(send, paint);
                    break;
                case "flag":
                    c.drawLine(4 * u, 15 * u, 4 * u, 22 * u, paint);
                    Path flag = new Path();
                    flag.moveTo(4 * u, 15 * u); flag.lineTo(4 * u, 2 * u);
                    flag.lineTo(14 * u, 5 * u); flag.lineTo(20 * u, 2 * u);
                    flag.lineTo(20 * u, 15 * u); flag.lineTo(14 * u, 12 * u); flag.close();
                    c.drawPath(flag, paint);
                    break;
                case "clock":
                    c.drawCircle(cx, cy, 10 * u, paint);
                    c.drawLine(cx, 6 * u, cx, cy, paint);
                    c.drawLine(cx, cy, 16 * u, cy, paint);
                    break;
                case "cpu":
                    c.drawRect(5 * u, 5 * u, 19 * u, 19 * u, paint);
                    c.drawRect(9 * u, 9 * u, 15 * u, 15 * u, paint);
                    for (int i = 0; i < 3; i++) {
                        float p = (9 + i * 3) * u;
                        c.drawLine(p, 2 * u, p, 5 * u, paint);
                        c.drawLine(p, 19 * u, p, 22 * u, paint);
                        c.drawLine(2 * u, p, 5 * u, p, paint);
                        c.drawLine(19 * u, p, 22 * u, p, paint);
                    }
                    break;
                case "target":
                case "crosshair":
                    c.drawCircle(cx, cy, 10 * u, paint);
                    c.drawCircle(cx, cy, 6 * u, paint);
                    c.drawCircle(cx, cy, 2 * u, paint);
                    c.drawLine(cx, 2 * u, cx, 6 * u, paint);
                    c.drawLine(cx, 18 * u, cx, 22 * u, paint);
                    c.drawLine(2 * u, cy, 6 * u, cy, paint);
                    c.drawLine(18 * u, cy, 22 * u, cy, paint);
                    break;
                case "terminal":
                    c.drawRect(2 * u, 3 * u, 22 * u, 21 * u, paint);
                    c.drawLine(5 * u, 10 * u, 9 * u, 14 * u, paint);
                    c.drawLine(9 * u, 14 * u, 5 * u, 18 * u, paint);
                    c.drawLine(12 * u, 18 * u, 18 * u, 18 * u, paint);
                    break;
                case "toggle-left":
                    c.drawRoundRect(new RectF(1 * u, 5 * u, 23 * u, 19 * u), 7 * u, 7 * u, paint);
                    Paint fill = new Paint(paint); fill.setStyle(Paint.Style.FILL);
                    c.drawCircle(8 * u, 12 * u, 4 * u, fill);
                    break;
                case "toggle-right":
                    c.drawRoundRect(new RectF(1 * u, 5 * u, 23 * u, 19 * u), 7 * u, 7 * u, paint);
                    Paint fill2 = new Paint(paint); fill2.setStyle(Paint.Style.FILL);
                    c.drawCircle(16 * u, 12 * u, 4 * u, fill2);
                    break;
                case "info":
                    c.drawCircle(cx, cy, 10 * u, paint);
                    c.drawLine(cx, 11 * u, cx, 17 * u, paint);
                    c.drawPoint(cx, 8 * u, paint);
                    break;
                case "check":
                    c.drawLine(4 * u, 12 * u, 9 * u, 17 * u, paint);
                    c.drawLine(9 * u, 17 * u, 20 * u, 6 * u, paint);
                    break;
                case "refresh-cw":
                case "refresh":
                    Path arc = new Path();
                    arc.addArc(new RectF(3 * u, 3 * u, 21 * u, 21 * u), -60, 240);
                    c.drawPath(arc, paint);
                    c.drawLine(20 * u, 3 * u, 20 * u, 9 * u, paint);
                    c.drawLine(20 * u, 9 * u, 14 * u, 9 * u, paint);
                    break;
                case "trash-2":
                case "trash":
                    c.drawLine(3 * u, 6 * u, 21 * u, 6 * u, paint);
                    c.drawRect(5 * u, 6 * u, 19 * u, 21 * u, paint);
                    c.drawLine(8 * u, 3 * u, 16 * u, 3 * u, paint);
                    c.drawLine(10 * u, 10 * u, 10 * u, 17 * u, paint);
                    c.drawLine(14 * u, 10 * u, 14 * u, 17 * u, paint);
                    break;
                case "shield":
                    Path sh = new Path();
                    sh.moveTo(12 * u, 2 * u); sh.lineTo(2 * u, 6 * u);
                    sh.lineTo(2 * u, 13 * u); sh.quadTo(2 * u, 22 * u, 12 * u, 22 * u);
                    sh.quadTo(22 * u, 22 * u, 22 * u, 13 * u);
                    sh.lineTo(22 * u, 6 * u); sh.close();
                    c.drawPath(sh, paint);
                    break;
                case "layers":
                    Path l1 = new Path();
                    l1.moveTo(12 * u, 2 * u); l1.lineTo(2 * u, 7 * u); l1.lineTo(12 * u, 12 * u);
                    l1.lineTo(22 * u, 7 * u); l1.close();
                    c.drawPath(l1, paint);
                    c.drawLine(2 * u, 12 * u, 12 * u, 17 * u, paint);
                    c.drawLine(22 * u, 12 * u, 12 * u, 17 * u, paint);
                    c.drawLine(2 * u, 17 * u, 12 * u, 22 * u, paint);
                    c.drawLine(22 * u, 17 * u, 12 * u, 22 * u, paint);
                    break;
                default:
                    // Fallback: simple circle
                    c.drawCircle(cx, cy, 8 * u, paint);
                    break;
            }
        }

        @Override public void setAlpha(int a) { paint.setAlpha(a); }
        @Override public void setColorFilter(android.graphics.ColorFilter cf) { paint.setColorFilter(cf); }
        @Override public int getOpacity() { return android.graphics.PixelFormat.TRANSLUCENT; }
        @Override public int getIntrinsicWidth() { return size; }
        @Override public int getIntrinsicHeight() { return size; }
    }
}
