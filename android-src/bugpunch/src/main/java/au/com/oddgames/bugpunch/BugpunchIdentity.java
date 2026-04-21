package au.com.oddgames.bugpunch;

import android.content.Context;
import android.provider.Settings;

/**
 * Stable, reinstall-surviving device identity for QA device pins.
 *
 * <p>Android exposes {@code Settings.Secure.ANDROID_ID} — a 64-bit hex string
 * scoped per (app-signing-key, user, device) since Android 8. It persists
 * across app uninstall/reinstall as long as the signing key stays the same,
 * and resets on factory reset or signing-key rotation. Since QA builds are
 * distributed with a stable internal signing key, this is effectively
 * permanent for a QA device.
 *
 * <p>No permission required. No user prompt. No dependency on our own
 * persistent storage (which would be wiped on uninstall).
 */
public final class BugpunchIdentity {
    private BugpunchIdentity() {}

    /**
     * @return ANDROID_ID for this device + app signer, or empty string if
     * the context is null (very early boot) or the system returns null.
     */
    public static String getStableDeviceId(Context context) {
        if (context == null) return "";
        try {
            String id = Settings.Secure.getString(
                context.getContentResolver(), Settings.Secure.ANDROID_ID);
            return id != null ? id : "";
        } catch (Throwable t) {
            return "";
        }
    }
}
