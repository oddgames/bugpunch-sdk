// BugpunchMemoryGovernor.h
//
// Mirror of BugpunchMemoryGovernor.java / BugpunchMemoryGovernor.cs — the one
// process-wide memory-pressure level every SDK subsystem consults before it
// allocates, and reacts to when it changes. The crash the SDK exists to record
// is often an out-of-memory one, so the SDK must never be the extra tens of MB
// that tip a starved device over; this is where "can we use memory right now"
// is answered, once, for the whole lane.
//
// Levels (identical meaning on every lane):
//   0 Normal    — nothing to shed.
//   1 Elevated  — the OS signalled pressure (dispatch WARN) or per-app headroom
//                 is under 256 MB: stop optional work that allocates (engine
//                 asset sweeps, full log snapshots, context renders), shrink
//                 capture sizes.
//   2 Critical  — a UIKit memory warning, dispatch CRITICAL, or headroom under
//                 96 MB (the band jetsam fires from): release everything the SDK
//                 can rebuild later (crash-frame retain pool, readback textures,
//                 the start-of-session log buffer), pause GPU video capture, and
//                 flush dirty ring pages so the kernel can reclaim them.
//
// Native derives the level from signals native already has (the perf monitor
// feeds its 1 Hz headroom read; the governor registers the OS notifications
// itself so it works before/without the monitor). C# reads it through
// Bugpunch_GetMemoryPressure (like the thermal tier) and receives a push on
// every transition (BugpunchClient.OnMemoryPressure) so it can drop its own
// buffers. A signal pins its level for a hold window, recovery needs headroom
// well back above the band (hysteresis), and levels never flap faster than
// once per 10 s. Every transition and every shed is announced in the captured
// log ring and folded into the memMap (os.governor), so a report shows what the
// SDK itself gave up and when.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#ifdef __OBJC__
#import <Foundation/Foundation.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum { BPMemoryPressureNormal = 0, BPMemoryPressureElevated = 1, BPMemoryPressureCritical = 2 };

/// Idempotent. Registers the UIKit memory-warning observer and the dispatch
/// memory-pressure source, and arms the recovery poll. Called at SDK boot.
void BugpunchMemoryGovernor_Start(void);

/// Current level (0/1/2). Cheap, any thread. This is the C# read-through.
int Bugpunch_GetMemoryPressure(void);

/// Feed the latest per-app headroom (os_proc_available_memory, MB; < 0 =
/// unknown). The perf monitor calls this from its 1 Hz sampler.
void BugpunchMemoryGovernor_OnHeadroom(float headroomMB);

/// Record something the SDK released in response to pressure, for the memMap
/// and the log ring. `what` is a short noun phrase; `mb` may be 0 when unknown.
void BugpunchMemoryGovernor_NoteShed(const char* what, float mb);

#ifdef __OBJC__
/// Fold the governor's state + history into a memMap `os` block as `governor`.
void BugpunchMemoryGovernor_AppendMemMap(NSMutableDictionary* os);
#endif

#ifdef __cplusplus
}
#endif
