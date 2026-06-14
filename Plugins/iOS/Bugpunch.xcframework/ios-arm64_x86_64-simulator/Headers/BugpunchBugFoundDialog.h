// =============================================================================
// LANE: iOS (Obj-C++)
//
// Internal-tester triage prompt. Shown to an INTERNAL tester right after their
// report uploads, when the server has decided the issue is a NEW (or regressed)
// bug worth claiming. Delivered via the identity-targeted notification platform
// as a {type:"bug_found"} envelope (server bountyService.notifyInternalFound);
// BugpunchPush detects it (maybeHandleBugFoundType:deepLink:) and shows this
// dialog.
//
// The card states the report uploaded, echoes the exception title, offers a
// multiline description field, and a "Mark as found" button that POSTs to
//   /api/v1/issues/{issueId}/bounty-claim
// (NSURLSession, off the main thread) with the X-Device-Id header (the stable
// device id) + {description,userId,name}. The server resolves the tester ROLE
// from the device — the body carries NO role. On a 2xx the card swaps to
// "Marked as found" and dismisses.
//
// Lane mirror (PINNED — class + copy align across every lane):
//   - Android: android-src/.../BugpunchBugFoundDialog.java
//   - iOS:     this file + BugpunchBugFoundDialog.mm
//   - C#:      Bugpunch/Sources/RemoteIDE/BugFoundOverlay.cs (pending)
//
// Modeled on the branded BugpunchBountyDialog card (same product visual
// language), with a workmanlike (celebration-free) treatment.
// =============================================================================

#pragma once

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchBugFoundDialog : NSObject

/// YES while the triage card is on screen — used to suppress a duplicate.
+ (BOOL)isShowing;

/// Show the triage card for `issueId`. `title` is the exception title to echo;
/// `isRegression` switches the header copy between a fresh find and a
/// regression. Hops to the main thread internally; no-op when `issueId` is
/// empty or a card is already showing.
+ (void)present:(nullable NSString*)issueId
          title:(nullable NSString*)title
           type:(nullable NSString*)type
   isRegression:(BOOL)isRegression;

@end

NS_ASSUME_NONNULL_END
