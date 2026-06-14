// =============================================================================
// LANE: iOS (Obj-C++)
//
// Bug-bounty leaderboard fetch + model. Backs two surfaces:
//   - the "Leaderboard" tab in BugpunchInboxViewController (full ranked list),
//   - the compact ranked panel in BugpunchBountyDialog's celebration popup
//     (which is rendered from notification params, NOT a fetch — see
//     BugpunchPush's maybeHandleBountyType:deepLink:).
//
// GETs {serverBase}/api/v1/projects/{projectId}/bounty-leaderboard?userId={me}
// with the lane's standard API-key + device headers (the same X-Api-Key /
// X-Device-Id / X-Player-Email set the poll / chat / feedback paths use), off
// the main thread. Parses
//   {entries:[{rank,name,count,totalAmount,isMe}], me, totalFinders, rewardCode}
// with NSJSONSerialization, completion on the main thread.
//
// Lane mirror (PINNED — class + shape align across every lane):
//   - Android: android-src/.../BugpunchBountyLeaderboard.java
//   - iOS:     this file + BugpunchBountyLeaderboard.mm
//   - C#:      Bugpunch/Sources/RemoteIDE/BountyLeaderboard.cs (pending)
// =============================================================================

#pragma once

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

/// One ranked finder row. Mirrors the server `LeaderboardEntry`.
@interface BPBountyEntry : NSObject
@property (nonatomic, assign) NSInteger rank;
@property (nonatomic, copy)   NSString*  name;
@property (nonatomic, assign) NSInteger count;
@property (nonatomic, assign) NSInteger totalAmount;
@property (nonatomic, assign) BOOL       isMe;
@end

/// The whole leaderboard payload. Mirrors the server `Leaderboard`.
@interface BPBountyBoard : NSObject
@property (nonatomic, strong) NSArray<BPBountyEntry*>* entries;
/// The requesting player's own standing, or nil if they have no bounties yet
/// (or fell outside the returned slice with no `me`).
@property (nonatomic, strong, nullable) BPBountyEntry* me;
@property (nonatomic, assign) NSInteger totalFinders;
@property (nonatomic, copy)   NSString*  rewardCode;
- (BOOL)isEmpty;
@end

@interface BugpunchBountyLeaderboard : NSObject

/// Parse a full leaderboard JSON object (server response or the `leaderboard`
/// notification param). Null-safe — returns an empty board for nil/non-dict.
+ (BPBountyBoard*)parse:(nullable NSDictionary*)root;

/// Build a board from the bounty-notification params, with no network call.
/// The popup gets `rank` (int|null) + `leaderboard` (the top-5 board, same
/// shape) embedded in the deep-link so it can render its compact panel without
/// a fetch. When the embedded board has no `me` but `rank` is present we
/// synthesise a minimal `me` so "You're #{rank}" still renders.
+ (BPBountyBoard*)fromParams:(nullable NSDictionary*)params;

/// GET the leaderboard off the main thread and hand the parsed board to
/// `completion` ON THE MAIN THREAD. On any failure (no config, HTTP error, bad
/// JSON) the completion still fires with an empty board so the UI can show its
/// empty state rather than hang. Auth: X-Api-Key + X-Device-Id +
/// X-Player-Email — exactly the way the chat / feedback / poll paths build
/// their requests.
+ (void)fetch:(void (^)(BPBountyBoard* board))completion;

@end

NS_ASSUME_NONNULL_END
