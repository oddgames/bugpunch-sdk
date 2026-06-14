// =============================================================================
// LANE: iOS (Obj-C++)
//
// Bug-bounty reward popup. Shown to a player when the Bugpunch server awards a
// bounty for finding a NEW, previously-undiscovered bug. Delivered via the
// identity-targeted notification platform (issue oddgames/bugpunch-server#308)
// as a {type:"bounty"} envelope; BugpunchPush detects it (maybeHandleBounty:),
// shows this dialog, and forwards the bounty payload to C#
// (BugpunchClient.OnNativeBountyEarned) so the game can grant a reward.
//
// Lane mirror (PINNED — class + copy align across every lane):
//   - Android: android-src/.../BugpunchBountyDialog.java
//   - iOS:     this file + BugpunchBountyDialog.mm
//   - C#:      Bugpunch/Sources/RemoteIDE/BountyOverlay.cs (UIToolkit)
//
// Modeled on the branded BPAlert card (single dismiss button "Nice!"), same
// product visual language as the consent / crash dialogs.
// =============================================================================

#pragma once

#import <Foundation/Foundation.h>

@class BPBountyBoard;

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchBountyDialog : NSObject

/// YES while the bounty popup is on screen — used to suppress a duplicate.
+ (BOOL)isShowing;

/// Show the bounty reward popup. `displayText` is the formatted reward string
/// the server resolved (e.g. "100 gems" / "$5.00") and is substituted into the
/// pinned body copy. Hops to the main thread internally (via BPAlert); safe to
/// call from any thread. No-op if a bounty popup is already showing.
+ (void)present:(nullable NSString*)displayText;

/// Show the bounty reward popup with an optional compact leaderboard panel.
/// `board` is the top-N standing parsed from the notification params (no fetch
/// — see BugpunchPush's maybeHandleBountyType:deepLink:); pass nil to omit the
/// panel. Rendered between the reward body and the "Nice!" button.
+ (void)present:(nullable NSString*)displayText leaderboard:(nullable BPBountyBoard*)board;

/// As above, plus "where to test next" `hints` parsed from the bounty params —
/// an array of `{ area, freshness, churn }` dictionaries (the coldest areas of
/// the build). Rendered above the leaderboard. Pass nil/empty to omit.
+ (void)present:(nullable NSString*)displayText
     leaderboard:(nullable BPBountyBoard*)board
          hints:(nullable NSArray*)hints;

@end

NS_ASSUME_NONNULL_END
