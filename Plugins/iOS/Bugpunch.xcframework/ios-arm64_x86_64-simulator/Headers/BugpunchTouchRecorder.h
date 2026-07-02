// BugpunchTouchRecorder.h
// Always-on ring buffer of UITouch events for Bugpunch SDK (iOS)
//
// Pairs with BugpunchRingRecorder: captures every touch phase via a swizzle
// on UIApplication.sendEvent: and keeps them in a bounded ring. On report,
// Snapshot() filters to the video's host-time window and returns a JSON
// array already rebased to video t=0.
//
// UITouch.timestamp is seconds-since-boot (mach_absolute_time-derived), the
// same clock used by ReplayKit sample buffer PTS — so (touch.timestamp -
// videoStartHostTime) * 1000 is the ms offset into the dumped MP4.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#include <stdint.h>
#include <stdbool.h>

#ifdef __OBJC__
#import <Foundation/Foundation.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// Configure ring capacity. Call before Start.
/// @param maxEvents  Max events held (older dropped). ~10000 is safe (~500KB).
void BugpunchTouch_Configure(int maxEvents);

/// Start capturing. Installs the UIApplication sendEvent: swizzle on first
/// call; subsequent Start/Stop toggles a flag without re-swizzling.
bool BugpunchTouch_Start(void);

/// Stop capturing. Events already in the ring are preserved until next Start.
void BugpunchTouch_Stop(void);

bool BugpunchTouch_IsRunning(void);

/// Snapshot events in [startHostTime, endHostTime] (seconds since boot) and
/// return them as a JSON array, already rebased so t=0 is startHostTime.
/// Format: [{"t":<ms>,"id":<int>,"phase":"began|moved|stationary|ended|cancelled","x":<px>,"y":<px>}, ...]
/// @return malloc'd UTF-8 JSON string. Caller frees with BugpunchTouch_FreeJson.
///         Returns NULL if no events in range.
const char* BugpunchTouch_SnapshotJson(double startHostTime, double endHostTime);

/// Free a string returned by BugpunchTouch_SnapshotJson.
void BugpunchTouch_FreeJson(const char* json);

/// Reference screen dimensions at capture time (points * scale = pixels).
/// Dashboard uses these to map touches onto the rendered video. Populated on
/// first Start; safe to call after.
void BugpunchTouch_GetCaptureSize(int* outWidth, int* outHeight);

/// Return recent touch events (last trailMs) wrapped with screen dimensions.
/// JSON: {"events":[...],"w":<px>,"h":<px>}
/// Caller frees with BugpunchTouch_FreeJson.
const char* BugpunchTouch_GetLiveTouches(int trailMs);

#ifdef __OBJC__
/// Absolute-stamped touch slice for the crash-survivable perf/touch sidecar,
/// as a ready-to-serialize object graph (the sidecar writer folds it into its
/// own JSON — no intermediate string, no parse round-trip). Unlike SnapshotJson
/// this does NOT rebase — every event carries its absolute host-time stamp
/// (seconds since boot, the same clock as the video ring PTS) so the
/// next-launch crash drain can rebase against the recovered video's window.
/// Only events at or after `sinceHostTime` are included (pass the ring window's
/// start — older events can never align with recovered footage). Shape:
/// {"w":<px>,"h":<px>,"events":[[hostSec,id,"phase",x,y],...]}
/// nil when no events are in range.
NSDictionary* BugpunchTouch_SidecarSnapshot(double sinceHostTime);
#endif

#ifdef __cplusplus
}
#endif
