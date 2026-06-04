// =============================================================================
// LANE: iOS (Obj-C++)
//
// BugpunchStreamer — native live WebRTC streamer for the Remote IDE feed.
// Mirror class of `BugpunchStreamer.java` (Android). One PeerConnection per
// dashboard viewer (keyed by sessionId), all fed by a single RTCVideoSource
// that consumes raw ReplayKit frames teed off `BugpunchRingRecorder` — so one
// screen-capture session powers both the crash video ring AND every live
// stream, exactly like Android shares one MediaProjection.
//
// Signalling rides the native report tunnel (`BugpunchTunnel`) as bare
// streamAttach / streamOffer / iceCandidate / streamDetach frames; the streamer
// answers with streamReady / streamAnswer / iceCandidate. This replaces the C#
// Unity.WebRTC path that previously handled iOS `/webrtc-*` as `request` frames.
//
// The Obj-C WebRTC SDK (`<WebRTC/WebRTC.h>`, from the vendored
// WebRTC.xcframework) is imported only in the .mm — this header stays
// framework-free so non-WebRTC translation units can call the entry points.
// =============================================================================

#pragma once

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Spin up (or refresh) the peer for `sessionId` and start the shared capture
/// pipeline if it isn't already running. Idempotent across concurrent
/// streamAttach frames. Refuses on role=public, emitting a streamReady frame
/// with ready=false,reason="role-public". Called from BugpunchTunnel on the
/// streamAttach frame.
void Bugpunch_Streamer_Start(const char* sessionId);

/// Apply the dashboard's SDP offer for `sessionId`, create + send the answer.
/// Called from BugpunchTunnel on the streamOffer frame.
void Bugpunch_Streamer_OnRemoteOffer(const char* sessionId, const char* sdp);

/// Add a remote ICE candidate for `sessionId` (queued until the remote
/// description is set). Called from BugpunchTunnel on the iceCandidate frame.
void Bugpunch_Streamer_OnRemoteIce(const char* sessionId,
                                   const char* sdpMid,
                                   int sdpMLineIndex,
                                   const char* candidate);

/// Tear down a single peer; other viewers keep streaming. The shared capture
/// pipeline lingers until the last peer closes. Empty/NULL sessionId tears
/// down everything (legacy streamDetach without a sessionId).
void Bugpunch_Streamer_Stop(const char* sessionId);

/// Tear down every peer + the shared capture pipeline. Called when the report
/// tunnel disconnects mid-stream.
void Bugpunch_Streamer_StopAll(void);

/// True while at least one peer / the shared capture pipeline is active.
bool Bugpunch_Streamer_IsActive(void);

#ifdef __cplusplus
}
#endif
