// BugpunchGoogleSignIn.h — Google Sign-In via Apple's ASWebAuthenticationSession.
//
// Deliberately does NOT depend on the GoogleSignIn iOS SDK — adding it
// would bloat the xcframework and force consumers to add the
// GoogleSignIn Pod / SwiftPM dependency. ASWebAuthenticationSession
// (iOS 12+) is the system-blessed in-app browser for OAuth flows; it
// shares Safari cookies (so the user is usually one-tap signed in) and
// handles redirect interception WITHOUT the consuming game needing to
// register a custom URL scheme — that's the whole reason to use it
// instead of SFSafariViewController + the legacy openURL handshake.
//
// Flow:
//   1. Build the Google OAuth 2.0 authorize URL (response_type=id_token,
//      scope=openid email profile, nonce=random).
//   2. Open it in ASWebAuthenticationSession with `callbackURLScheme`
//      set to the reverse-DNS form of the iOS OAuth client ID (Google's
//      convention: client `1234-abc.apps.googleusercontent.com` →
//      scheme `com.googleusercontent.apps.1234-abc`).
//   3. Parse `id_token` from the redirect URL fragment.
//
// Config: requires the iOS OAuth client ID, sourced from the
// BugpunchConfig at startup (`googleClientIdIos`, surfaced into the
// native config dict by C# BugpunchNative.cs). Empty / missing →
// caller MUST hide the Google button. The picker checks this before
// invoking the helper.
//
// Mirrors `BugpunchGoogleSignIn.java` on the Android lane (Custom Tabs
// or Credential Manager there).

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchGoogleSignIn : NSObject

/// Returns the iOS OAuth client ID configured via BugpunchConfig (read
/// from BPRuntime.config[@"googleClientIdIos"]). Empty string when not
/// configured — callers must check before showing a Google button.
+ (NSString*)configuredClientId;

/// Launch the Google web sign-in flow. `presentingWindow` anchors the
/// system web view sheet. Completion runs on the main queue.
///
/// On success: success=YES, idToken is Google's id_token JWT (post to
/// the server's SSO verifier endpoint).
/// On user cancel: success=NO, errorMessage=nil (silent dismiss).
/// On error: success=NO, errorMessage non-nil.
+ (void)signInWithPresentingWindow:(nullable UIWindow*)presentingWindow
                        completion:(void (^)(BOOL success,
                                             NSString* _Nullable idToken,
                                             NSString* _Nullable errorMessage))completion;

@end

NS_ASSUME_NONNULL_END
