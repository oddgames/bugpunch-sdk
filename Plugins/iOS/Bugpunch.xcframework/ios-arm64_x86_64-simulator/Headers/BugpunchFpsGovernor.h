// BugpunchFpsGovernor.h
//
// Mirror of BugpunchFpsGovernor.java — dynamic FPS + bitrate governor
// driven by thermal state. Consumers (BugpunchRingRecorder, future
// native WebRTC streamer) subscribe and receive callbacks each time
// the thermal tier moves.
//
// Tier map:
//   NSProcessInfoThermalStateNominal  → target (default 30)
//   NSProcessInfoThermalStateFair     → target
//   NSProcessInfoThermalStateSerious  → 15, bitrate × 0.5
//   NSProcessInfoThermalStateCritical → 10, bitrate × 0.33
//
// Resolution selection: -maxLongEdge returns a tier-aware ceiling
// (1080 high / 720 mid / 540 low) resolved eagerly from physical
// memory + processor count. Streamer + recorder + on-demand segment
// configure paths clamp to this value to keep the encoder budget
// realistic — encode cost scales with pixels × fps.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#import <Foundation/Foundation.h>

typedef void(^BPFpsChange)(int newFps, NSString *reason);
typedef void(^BPBitrateScale)(float scale, NSString *reason);

@interface BPFpsGovernor : NSObject

+ (instancetype)shared;

/// Idempotent — first call starts the thermal observer and seeds tier.
- (void)startWithTargetFps:(int)targetFps;

- (int)currentFps;
- (float)currentBitrateScale;
- (int)targetFps;
- (int)maxLongEdge;
- (NSString *)deviceTier;

/// Subscribe — both callbacks fire immediately with the current state.
/// Returns an opaque token; pass it to -removeListener:.
- (id)addOnFpsChange:(BPFpsChange)onFps
       onBitrateScale:(BPBitrateScale)onScale;

- (void)removeListener:(id)token;

@end
