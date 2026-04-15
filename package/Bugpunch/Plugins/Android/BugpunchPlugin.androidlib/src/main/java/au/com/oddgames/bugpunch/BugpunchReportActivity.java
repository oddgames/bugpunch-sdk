package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.drawable.BitmapDrawable;
import android.os.Bundle;
import android.text.InputType;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup.LayoutParams;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

/**
 * Bug report form. Shows a screenshot preview (tap to annotate), email +
 * description + severity inputs, Send/Cancel. On Send, calls
 * {@link BugpunchDebugMode#submitReport} which builds the manifest and hands
 * to the uploader.
 *
 * Launched via {@link #launch(String, String)} with the pre-captured
 * screenshot path; lives entirely native (no Unity involvement).
 */
public class BugpunchReportActivity extends Activity {
    private static final String TAG = "BugpunchReport";
    private static final int REQ_ANNOTATE = 4201;

    private static final String EX_SHOT = "bp_shot";
    private static final String EX_TITLE = "bp_title";
    private static final String EX_DESC = "bp_desc";

    static String sAnnotationsPath;        // filled by annotate activity on return

    private String mShotPath;
    private String mAnnotationsPath;
    private ImageView mPreview;
    private EditText mEmail, mDescription;
    private Spinner mSeverity;
    private String mInitialDescription;
    private String mInitialTitle;

    public static void launch(String screenshotPath, String title, String description) {
        Activity host = BugpunchUnity.currentActivity();
        if (host == null) { Log.w(TAG, "no activity"); return; }
        Intent i = new Intent(host, BugpunchReportActivity.class);
        i.putExtra(EX_SHOT, screenshotPath);
        i.putExtra(EX_TITLE, title == null ? "" : title);
        i.putExtra(EX_DESC, description == null ? "" : description);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host.startActivity(i);
    }

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        mShotPath = getIntent().getStringExtra(EX_SHOT);
        mInitialTitle = getIntent().getStringExtra(EX_TITLE);
        mInitialDescription = getIntent().getStringExtra(EX_DESC);

        buildUi();
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_ANNOTATE && resultCode == RESULT_OK) {
            mAnnotationsPath = sAnnotationsPath;
            sAnnotationsPath = null;
            refreshPreview();
        }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        // Release the "report in progress" guard so the next Send Report works.
        BugpunchDebugMode.clearReportInProgress();
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(20);
        root.setPadding(pad, pad, pad, pad);
        root.setBackgroundColor(0xFF101418);

        TextView header = new TextView(this);
        header.setText("Report a bug");
        header.setTextColor(Color.WHITE);
        header.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        header.setPadding(0, 0, 0, dp(16));
        root.addView(header);

        // Screenshot preview — tap to annotate
        mPreview = new ImageView(this);
        mPreview.setAdjustViewBounds(true);
        mPreview.setMaxHeight(dp(260));
        mPreview.setScaleType(ImageView.ScaleType.FIT_CENTER);
        mPreview.setBackgroundColor(0xFF1A1F25);
        mPreview.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { openAnnotate(); }
        });
        LinearLayout.LayoutParams lpShot = new LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, dp(260));
        lpShot.bottomMargin = dp(4);
        root.addView(mPreview, lpShot);

        TextView hint = new TextView(this);
        hint.setText("Tap screenshot to annotate");
        hint.setTextColor(0xFF7A8899);
        hint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        hint.setPadding(0, 0, 0, dp(16));
        root.addView(hint);

        root.addView(label("Your email"));
        mEmail = input(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);
        root.addView(mEmail);

        root.addView(label("Description"));
        mDescription = input(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE);
        mDescription.setMinLines(4);
        mDescription.setGravity(Gravity.TOP | Gravity.START);
        if (mInitialDescription != null) mDescription.setText(mInitialDescription);
        root.addView(mDescription);

        root.addView(label("Severity"));
        mSeverity = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
            android.R.layout.simple_spinner_item,
            new String[] { "Low", "Medium", "High", "Critical" });
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        mSeverity.setAdapter(adapter);
        mSeverity.setSelection(1);
        root.addView(mSeverity);

        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        buttons.setGravity(Gravity.END);
        buttons.setPadding(0, dp(24), 0, 0);
        Button cancel = button("Cancel", 0xFF394048, Color.WHITE);
        cancel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { finish(); }
        });
        LinearLayout.LayoutParams lpBtn = new LinearLayout.LayoutParams(
            LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT);
        lpBtn.rightMargin = dp(12);
        buttons.addView(cancel, lpBtn);
        Button send = button("Send", 0xFF2A7BE0, Color.WHITE);
        send.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { onSend(); }
        });
        buttons.addView(send);
        root.addView(buttons);

        scroll.addView(root);
        setContentView(scroll);

        refreshPreview();
    }

    private void refreshPreview() {
        try {
            if (mShotPath == null) return;
            android.graphics.Bitmap shot = BitmapFactory.decodeFile(mShotPath);
            if (shot == null) return;
            if (mAnnotationsPath != null) {
                // Composite annotation overlay on top of screenshot for preview.
                android.graphics.Bitmap overlay = BitmapFactory.decodeFile(mAnnotationsPath);
                if (overlay != null) {
                    android.graphics.Bitmap combined = shot.copy(
                        android.graphics.Bitmap.Config.ARGB_8888, true);
                    android.graphics.Canvas c = new android.graphics.Canvas(combined);
                    android.graphics.Rect dst = new android.graphics.Rect(
                        0, 0, combined.getWidth(), combined.getHeight());
                    c.drawBitmap(overlay, null, dst, null);
                    overlay.recycle();
                    mPreview.setImageBitmap(combined);
                    return;
                }
            }
            mPreview.setImageBitmap(shot);
        } catch (Throwable t) {
            Log.w(TAG, "preview failed", t);
        }
    }

    private void openAnnotate() {
        if (mShotPath == null) return;
        Intent i = new Intent(this, BugpunchAnnotateActivity.class);
        i.putExtra(BugpunchAnnotateActivity.EX_SHOT, mShotPath);
        startActivityForResult(i, REQ_ANNOTATE);
    }

    private void onSend() {
        String email = mEmail.getText().toString().trim();
        String desc = mDescription.getText().toString().trim();
        String severity = (String) mSeverity.getSelectedItem();
        if (desc.isEmpty()) {
            Toast.makeText(this, "Please add a description", Toast.LENGTH_SHORT).show();
            return;
        }
        try {
            BugpunchDebugMode.submitReport(
                mInitialTitle != null ? mInitialTitle : "Bug report",
                desc, email, severity, mShotPath, mAnnotationsPath);
            Toast.makeText(this, "Report sent", Toast.LENGTH_SHORT).show();
        } catch (Throwable t) {
            Log.w(TAG, "submit failed", t);
            Toast.makeText(this, "Failed to send report", Toast.LENGTH_SHORT).show();
        }
        finish();
    }

    // ── Small builders to keep this file terse ──

    private TextView label(String text) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextColor(0xFFB0BAC5);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        t.setPadding(0, dp(12), 0, dp(4));
        return t;
    }

    private EditText input(int inputType) {
        EditText e = new EditText(this);
        e.setInputType(inputType);
        e.setBackgroundColor(0xFF1A1F25);
        e.setTextColor(Color.WHITE);
        e.setPadding(dp(12), dp(10), dp(12), dp(10));
        return e;
    }

    private Button button(String text, int bg, int fg) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextColor(fg);
        b.setBackgroundColor(bg);
        b.setAllCaps(false);
        return b;
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
            getResources().getDisplayMetrics());
    }
}
