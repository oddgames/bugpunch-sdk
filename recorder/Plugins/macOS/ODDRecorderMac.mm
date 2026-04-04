// ODDRecorderMac.mm
// macOS native recording plugin for ODDGames.Recorder
//
// This file includes the shared implementation from the iOS plugin source.
// On macOS, ReplayKit capture is disabled; only the manual frame/audio
// input path (AppendVideoFrame / AppendAudioSamples) is active.
//
// When building as a macOS .bundle, compile THIS file (not the iOS original).
// The #if TARGET_OS_IOS guards in ODDRecorder.mm automatically exclude
// ReplayKit code paths on macOS.
//
// Copyright (c) ODDGames. All rights reserved.

// Include the shared implementation
// Both platforms share the same AVAssetWriter encoding code;
// the iOS source uses TARGET_OS_IOS to conditionally enable ReplayKit.
#include "../iOS/ODDRecorder.mm"
