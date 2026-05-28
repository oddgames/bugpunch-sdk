// BugpunchCredentialSignIn.h — Bugpunch email + password sign-in via a
// custom modal view controller. Mirror of
// android-src/.../BugpunchCredentialSignIn.java.
//
// Unlike the third-party SSO helpers (which delegate UI to
// ASWebAuthenticationSession / ASAuthorizationController), this helper
// owns its own form: two UITextFields (email + password) and a Continue
// pill button. On Continue it POSTs to
//     /api/v1/projects/:projectId/profiles/sso-sign-in
//     body: { provider: "bugpunch", email, password, deviceId }
// and on 2xx returns the identity envelope { provider, sub, email, name,
// avatarUrl } to the caller so the picker can run the same
// BPPickerOnSsoSignedIn path used by Google + Apple.
//
// Always available — no per-project OAuth client config required.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchCredentialSignIn : NSObject

/// Present the credential entry form modally on top of the picker. On
/// success completion fires with success=YES + the identity dict. On user
/// cancel completion fires with success=NO + errorMessage=nil (silent
/// dismiss). On error completion fires with success=NO + a non-nil
/// errorMessage; the form's own inline error label has already been shown,
/// so the caller typically ignores the message and just re-enables its
/// button. Completion runs on the main queue.
+ (void)presentFromViewController:(UIViewController*)presenter
                       completion:(void (^)(BOOL success,
                                            NSDictionary* _Nullable identity,
                                            NSString* _Nullable errorMessage))completion;

@end

NS_ASSUME_NONNULL_END
