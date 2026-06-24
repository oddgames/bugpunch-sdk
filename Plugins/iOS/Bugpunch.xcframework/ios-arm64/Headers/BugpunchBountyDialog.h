// =============================================================================
// LANE: iOS (Obj-C++)
//
// Bug-bounty CLAIM popup. Shown to a player when the Bugpunch server awards a
// bounty for finding a NEW, previously-undiscovered bug (and re-pushed when the
// issue is later resolved). Delivered via the identity-targeted notification
// platform (issue oddgames/bugpunch-server#308) as a {type:"bounty"} envelope;
// BugpunchPush detects it (maybeHandleBountyType:deepLink:), parses the params
// into a BPBountyClaim, shows this dialog, and forwards the payload to C#
// (BugpunchClient.OnNativeBountyEarned) so the game can grant a reward.
//
// Lane mirror (PINNED — class + copy align across every lane):
//   - Android: android-src/.../BugpunchBountyDialog.java
//   - iOS:     this file + BugpunchBountyDialog.mm
//   - C#:      Bugpunch/Sources/UI/BugpunchBountyDialog.cs (UIToolkit)
//
// Layout — a 3-panel claim screen (responsive), mirroring the C# reference:
//   - LANDSCAPE / WIDE (width >= height and >= ~900pt): three columns
//       TOP BUG HUNTERS (left) | claim card (center) | LOW TEST COVERAGE AREAS (right)
//   - PORTRAIT / NARROW: the same three blocks STACK (card, then leaderboard,
//       then hints) inside a scroll view.
//   - Neither side panel present → a lone center claim card.
// The center card NEVER exceeds the screen height: it's capped to ~94% of the
// view and its content scrolls while the CLAIM REWARD button stays pinned at the
// bottom. Side-panel row lists + the stacked layout scroll the same way.
//
// Built on the branded BPGradientView card / blur backdrop / themed colours from
// BugpunchPretty + BugpunchAlert so it reads as one product with the consent /
// crash dialogs. The celebration treatment (CAEmitterLayer fireworks + card
// entrance) is preserved. Title + body copy are PINNED — keep verbatim in sync
// across lanes.
// =============================================================================

#pragma once

#import <Foundation/Foundation.h>

@class BPBountyBoard;

NS_ASSUME_NONNULL_BEGIN

/// Parsed bounty-award payload for the claim popup — the iOS peer of the C#
/// `BountyInfo`. Field names + defaults mirror the server's
/// `deepLink.params` shape ({ amount, baseAmount, bonusAmount, rewardMin,
/// rewardMax, code, displayText, resolved, issue:{ id, status, fixedInVersion,
/// reportedAt }, rank, leaderboard, hints }). Built by BugpunchPush from the
/// notification params (see maybeHandleBountyType:deepLink:).
@interface BPBountyClaim : NSObject

/// Reward hero amount (the actual earned bounty, minor units).
@property (nonatomic, assign) NSInteger amount;
/// Base portion before any bonus. Defaults to `amount`.
@property (nonatomic, assign) NSInteger baseAmount;
/// Bonus portion on top of `baseAmount`. Defaults to 0. The BASE+BONUS=TOTAL
/// breakdown row only renders when this is > 0.
@property (nonatomic, assign) NSInteger bonusAmount;
/// Configured reward RANGE. Default rewardMin = rewardMax = `amount`. The muted
/// "Bounty range" line only renders when rewardMax > rewardMin.
@property (nonatomic, assign) NSInteger rewardMin;
@property (nonatomic, assign) NSInteger rewardMax;

/// Redeemable code (sub-label under the hero when present).
@property (nonatomic, copy, nullable) NSString* code;
/// Human reward string (e.g. "$5.00" / "PISTON CREDITS"). Used as the hero
/// sub-label fallback + the range unit + the pinned body sentence.
@property (nonatomic, copy, nullable) NSString* displayText;

/// Named reward preset id (empty for the legacy default reward).
@property (nonatomic, copy, nullable) NSString* typeId;
/// Reward preset display name (shown as a small caption above the title).
@property (nonatomic, copy, nullable) NSString* typeName;
/// Optional hero image URL for the claim card (empty = none).
@property (nonatomic, copy, nullable) NSString* imageUrl;
/// YES = grant is authoritative server-side (webhook); NO = the game grants it
/// directly in OnBountyEarned. Defaults NO.
@property (nonatomic, assign) BOOL requireWebhook;

/// True on the RESOLVE re-push — flips the card from "EARNED / pending" to
/// "COMPLETED / VERIFIED · FIXED · REWARDED". Defaults NO.
@property (nonatomic, assign) BOOL resolved;
/// Underlying issue id (shortened to 8 chars in the status block).
@property (nonatomic, copy, nullable) NSString* issueId;
/// Issue lifecycle status ("pending" / "resolved"). Defaults "pending".
@property (nonatomic, copy, nullable) NSString* issueStatus;
/// Version the issue was fixed in (resolved only).
@property (nonatomic, copy, nullable) NSString* fixedInVersion;
/// ISO timestamp the report was filed (shown date-only).
@property (nonatomic, copy, nullable) NSString* reportedAt;

/// Optional compact leaderboard (parsed from params, no fetch).
@property (nonatomic, strong, nullable) BPBountyBoard* board;
/// Optional "where to test next" hints — NSArray<NSDictionary*> of
/// { area, freshness, churn }.
@property (nonatomic, strong, nullable) NSArray* hints;

@end

@interface BugpunchBountyDialog : NSObject

/// YES while the bounty popup is on screen — used to suppress a duplicate.
+ (BOOL)isShowing;

/// Show the bounty CLAIM popup for `claim`. Hops to the main thread internally;
/// safe to call from any thread. No-op if a bounty popup is already showing.
+ (void)presentClaim:(BPBountyClaim*)claim;

/// Back-compat convenience: build a minimal claim (amount 0, pending) from just
/// the reward string + optional leaderboard + hints, then present it. The push
/// path uses +presentClaim: directly with the full parsed payload.
+ (void)present:(nullable NSString*)displayText;
+ (void)present:(nullable NSString*)displayText leaderboard:(nullable BPBountyBoard*)board;
+ (void)present:(nullable NSString*)displayText
     leaderboard:(nullable BPBountyBoard*)board
          hints:(nullable NSArray*)hints;

@end

NS_ASSUME_NONNULL_END
