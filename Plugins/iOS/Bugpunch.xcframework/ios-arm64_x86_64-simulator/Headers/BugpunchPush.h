// =============================================================================
// LANE: iOS (Obj-C++)
//
// BugpunchPush — APNs push lane coordinator. Mirrors `BugpunchPush.java`
// (Android FCM lane). NO C# sibling: push is mobile-only (the C# Editor +
// Standalone lane has no remote-push transport — see THREE_LANE_MIRRORS).
//
// Owns:
//   - Remote-notification registration (UIApplication + UNUserNotificationCenter
//     authorization), main-thread only.
//   - APNs device-token capture via an app-delegate swizzle of
//     `didRegisterForRemoteNotificationsWithDeviceToken` / `…DidFailToRegister`
//     (Unity owns the real app delegate; we can't subclass it).
//   - Token register POST to `{serverUrl}/api/devices/{deviceId}/push-token`.
//   - UNUserNotificationCenterDelegate (foreground present + tap routing).
//   - Deep-link dispatch via `+openDeepLinkScreen:params:`.
//   - Poll-tier `pendingNotifications` → local notifications + ack (driven
//     from BugpunchPoller's bootstrap dispatcher).
//
// Cross-lane rule: reads all shared state (serverUrl, deviceId, device token,
// userId, email) from BPRuntime / NSUserDefaults — never from a "client" type.
//
// MANUAL CAPABILITY NOTE: APNs needs no SDK config, but the *app target* must
// have the "Push Notifications" capability enabled (adds `aps-environment` to
// the entitlements). The SDK cannot add an entitlement to the consumer's app
// — the game dev does this once in Xcode (Signing & Capabilities → +Capability
// → Push Notifications). Without it, `registerForRemoteNotifications` calls
// `didFailToRegisterForRemoteNotificationsWithError`; we log and no-op.
// =============================================================================

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BPPush : NSObject

/// Start the push lane: install the app-delegate swizzle + UN delegate, then
/// request authorization and register for remote notifications (main thread).
/// Idempotent — a second call is a no-op. Best-effort; never throws.
+ (void)start;

/// Route a tapped / local notification to a screen. Single guarded hook so
/// every deep-link payload funnels through one place.
///   - "chat"  → TODO(#29) native chat board (logs + foregrounds for now).
///   - other   → foreground the app (default).
+ (void)openDeepLinkScreen:(nullable NSString*)screen
                    params:(nullable NSDictionary*)params;

@end

// Bridge from BugpunchPoller's bootstrap dispatcher: surface poll-tier
// `pendingNotifications` as local notifications, then ack them. Defined in
// BugpunchPush.mm. `jsonC` is the JSON array
// `[{id,type,title,body,deeplink}, ...]`.
#ifdef __cplusplus
extern "C" {
#endif
void BugpunchPush_OnPendingNotifications(const char* jsonC);
#ifdef __cplusplus
}
#endif

NS_ASSUME_NONNULL_END
