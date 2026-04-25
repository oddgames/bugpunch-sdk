package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Native image picker for the chat composer.
 *
 * Flow:
 *   C# calls BugpunchImagePicker.pick(activity)
 *     → starts the transparent PickerActivity
 *     → PickerActivity fires Intent.ACTION_PICK with image/* MIME filter
 *     → onActivityResult handles the URI, copies bytes into the app's
 *       cache dir, then finishes the activity
 *     → UnitySendMessage("BugpunchReportCallback", "OnImagePicked", path)
 *       is sent (empty path string on cancel)
 *
 * The file is copied into cache (not read directly from the content URI)
 * so the C# side gets a real absolute filesystem path it can feed to
 * File.ReadAllBytes without worrying about content provider permissions.
 */
public class BugpunchImagePicker {
    private static final String TAG = "[Bugpunch.ImagePicker]";
    private static final String CALLBACK_OBJECT = "BugpunchReportCallback";

    /**
     * Launch the system picker. Safe to call multiple times — the transparent
     * activity is single-top so re-entrance just replaces the previous pick.
     */
    public static void pick(Activity activity) {
        try {
            Intent i = new Intent(activity, PickerActivity.class);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(i);
        } catch (Exception e) {
            Log.e(TAG, "pick(): failed to start PickerActivity", e);
            sendResult(""); // treat as cancel
        }
    }

    /** UnitySendMessage via reflection (same pattern as BugpunchReportOverlay). */
    static void sendResult(String path) {
        try {
            Class<?> playerClass = Class.forName("com.unity3d.player.UnityPlayer");
            playerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                    .invoke(null, CALLBACK_OBJECT, "OnImagePicked", path == null ? "" : path);
        } catch (Exception e) {
            Log.e(TAG, "UnitySendMessage failed", e);
        }
    }

    /**
     * Transparent activity that owns the picker Intent. Needed because
     * Intent.ACTION_PICK must be launched via startActivityForResult from
     * a real Activity, and we don't want to reach into the Unity player
     * activity itself (it doesn't expose its result handler).
     *
     * No layout — theme is Translucent.NoTitleBar, the user only ever sees
     * the system picker chrome.
     */
    public static class PickerActivity extends Activity {
        private static final int REQUEST_PICK = 0xBEEF;

        @Override
        protected void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            try {
                Intent pick = new Intent(Intent.ACTION_PICK);
                pick.setType("image/*");
                startActivityForResult(pick, REQUEST_PICK);
            } catch (Exception e) {
                Log.e(TAG, "Failed to open image picker", e);
                finishWith("");
            }
        }

        @Override
        protected void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            if (requestCode != REQUEST_PICK) { finishWith(""); return; }
            if (resultCode != RESULT_OK || data == null || data.getData() == null) {
                // User cancelled / nothing selected.
                finishWith("");
                return;
            }
            Uri uri = data.getData();
            try {
                String path = copyUriToCache(this, uri);
                finishWith(path);
            } catch (Exception e) {
                Log.e(TAG, "copy to cache failed", e);
                finishWith("");
            }
        }

        private void finishWith(String path) {
            sendResult(path);
            finish();
        }
    }

    /**
     * Copy the content URI's bytes into a fresh file under the app's cache
     * directory and return the absolute path. Extension is derived from the
     * URI or the MIME type so the receiver can content-sniff via suffix.
     */
    static String copyUriToCache(Context ctx, Uri uri) throws Exception {
        String ext = guessExtension(ctx, uri);
        File dir = new File(ctx.getCacheDir(), "bugpunch_picks");
        if (!dir.exists()) dir.mkdirs();
        File out = new File(dir, "pick_" + System.currentTimeMillis() + ext);

        try (InputStream in = ctx.getContentResolver().openInputStream(uri);
             OutputStream os = new FileOutputStream(out)) {
            if (in == null) throw new Exception("content resolver returned null stream");
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
        }
        return out.getAbsolutePath();
    }

    static String guessExtension(Context ctx, Uri uri) {
        String last = uri.getLastPathSegment();
        if (last != null && last.contains(".")) {
            String e = last.substring(last.lastIndexOf('.'));
            if (e.length() <= 6) return e;
        }
        String mime = ctx.getContentResolver().getType(uri);
        if (mime == null) return ".jpg";
        switch (mime) {
            case "image/png": return ".png";
            case "image/gif": return ".gif";
            case "image/webp": return ".webp";
            case "image/bmp": return ".bmp";
            case "image/jpeg":
            default: return ".jpg";
        }
    }
}
