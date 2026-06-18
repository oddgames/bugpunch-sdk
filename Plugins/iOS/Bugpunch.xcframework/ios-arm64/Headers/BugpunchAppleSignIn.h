// BugpunchAppleSignIn.h — Sign in with Apple wrapper.
//
// Thin Obj-C++ adapter over `ASAuthorizationAppleIDProvider` /
// `ASAuthorizationController` (AuthenticationServices.framework, iOS 13+).
// Requests the user's full name + email scope. The Apple flow shows
// Apple's native modal — no URL scheme required, no entitlement
// configuration needed beyond enabling "Sign in with Apple" on the App
// ID in the consumer game's provisioning profile.
//
// Returns the raw `.identityToken` JWT (base64-decoded → UTF-8 string)
// alongside best-effort email/name. Apple only returns name + email on
// the FIRST sign-in per app/Apple ID pair — subsequent signs leave them
// nil and the SDK relies on the server's stored `sub`→identity mapping.
//
// Mirrors `BugpunchAppleSignIn.java` on the Android lane.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchAppleSignIn : NSObject

/// Launch the native Sign in with Apple flow. `presentingWindow` anchors
/// the Apple sheet (required by ASAuthorizationController on iOS 13+).
/// The completion block is invoked on the main queue.
///
/// On success: success=YES, idToken is the JWT string and nonce is the raw
/// (unhashed) nonce embedded in the request — pass both to the server so it
/// can verify the nonce claim inside the JWT. email + name may be empty after
/// the first sign-in (Apple only returns them once per Apple ID per app).
/// On failure / user cancellation: success=NO, errorMessage non-nil
/// (or nil if the user simply backed out — treat nil as silent dismiss).
+ (void)signInWithPresentingWindow:(nullable UIWindow*)presentingWindow
                        completion:(void (^)(BOOL success,
                                             NSString* _Nullable idToken,
                                             NSString* _Nullable nonce,
                                             NSString* _Nullable email,
                                             NSString* _Nullable name,
                                             NSString* _Nullable errorMessage))completion;

@end

NS_ASSUME_NONNULL_END
