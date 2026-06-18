// BugpunchPostSsoSignIn.h — common SSO-finalisation entry point shared by
// the iOS Apple + Google sign-in helpers. POSTs the provider's id_token to
// the server's verifier endpoint and, on success, hydrates BPRuntime and
// persists the resolved identity under the existing kBPPickerPrefsAuth*
// NSUserDefaults keys (so a cold launch restores the same identity that
// the picker last applied).
//
// Endpoint (see SDK_PATHS): POST /api/v1/projects/{projectId}/profiles/sso-sign-in
// Request body:  { "provider": "google"|"apple", "idToken": "...", "deviceId": "..." }
// Response body: { "provider": "...", "sub": "...", "email": "...",
//                  "name": "...", "avatarUrl": "..." }
//
// Mirrors `BugpunchPostSsoSignIn.java` on the Android lane. (Minor lane
// difference: Android leaves hydration to the picker via applyIdentity;
// iOS hydrates inside this helper since the same NSUserDefaults keys
// and BPRuntime fields are reachable from any file — saves the picker
// from re-implementing the persist+UnitySendMessage hop.)
//
// Threading: the network round-trip runs on a background queue; the
// completion block is invoked on the main queue so callers can update UI
// directly without bouncing through dispatch_async again.

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchPostSsoSignIn : NSObject

/// Finalise a third-party sign-in by exchanging the provider id_token for
/// a verified identity. On success, BPRuntime is hydrated, NSUserDefaults
/// is updated under the kBPPickerPrefsAuth* keys, OnProfilePicked is sent
/// to the Unity BugpunchClient game object, and completion fires with
/// `success=YES` + the response dict. On failure, completion fires with
/// `success=NO` + a non-nil `errorMessage` suitable for inline display.
///
/// `provider` is "google" or "apple".
/// `idToken` is the raw JWT / identity token returned by the provider.
/// `nonce` is the raw (unhashed) per-flow nonce for Apple sign-in (nil for
/// other providers). The server verifies sha256(nonce) == jwt.nonce claim.
/// `hintName` / `hintEmail` are UNTRUSTED client-supplied hints — they are
/// NOT sent to the server and must NEVER be used server-side. They exist
/// solely as display fallbacks when the server response omits the field
/// (Apple only returns name + email on the very first sign-in per Apple ID).
+ (void)postWithProvider:(NSString*)provider
                 idToken:(NSString*)idToken
                   nonce:(nullable NSString*)nonce
                hintName:(nullable NSString*)hintName
               hintEmail:(nullable NSString*)hintEmail
              completion:(void (^)(BOOL success,
                                   NSDictionary* _Nullable identity,
                                   NSString* _Nullable errorMessage))completion;

/// Custom auth: exchange a token from the host game's own backend for a
/// verified identity. Same endpoint + response shape as the SSO path, but the
/// body is `{ "provider":"custom", "customToken":"...", "deviceId":"..." }`;
/// the server validates the token via the project's custom-auth webhook.
/// completion fires on the main queue (success + identity dict, or NO + error).
+ (void)postCustomWithToken:(NSString*)token
                 completion:(void (^)(BOOL success,
                                      NSDictionary* _Nullable identity,
                                      NSString* _Nullable errorMessage))completion;

@end

NS_ASSUME_NONNULL_END
