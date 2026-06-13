// BugpunchUI.h
//
// The single suspend/restore primitive for "hide ALL Bugpunch UI, capture the
// live game, then return exactly where the player was". Mirrors Android
// BugpunchUI.suspendForCapture/restoreAfterCapture per the three-lane rule.
//
// Implementation: all Bugpunch screens live on the dedicated BugpunchNav
// window, so suspending is just hiding that window — the stack (inbox tab,
// pushed issue-detail, scroll positions, composer drafts) is untouched and
// restore is an unhide. Works for ANY current or future screen with no
// per-screen code.

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchUI : NSObject

/// Hide the Bugpunch window (reveals the live game, state preserved).
/// No-op (returns NO) when no Bugpunch UI is up.
+ (BOOL)suspendForCapture;

/// Unhide after capture — the player lands on the exact screen they left.
+ (void)restoreAfterCapture;

@end

NS_ASSUME_NONNULL_END
