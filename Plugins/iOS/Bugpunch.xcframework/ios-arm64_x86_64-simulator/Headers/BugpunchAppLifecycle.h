// BugpunchAppLifecycle.h
//
// Mirror of BugpunchAppLifecycle.java — process-wide foreground/background
// tracker. Capture pipelines (BugpunchRingRecorder, future native WebRTC
// streamer) subscribe so they can pause heavy capture work when the user
// isn't watching the screen.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#import <Foundation/Foundation.h>

typedef void(^BPLifecycleCallback)(void);

@interface BPAppLifecycle : NSObject

+ (instancetype)shared;

/// Install UIApplication background/foreground observers. Idempotent.
- (void)install;

/// True while the app is in the foreground (active or inactive — only
/// didEnterBackground flips this off).
- (BOOL)isForeground;

/// True only while the app is foreground-ACTIVE (UIApplicationStateActive).
/// Flips off on willResignActive — i.e. while a system modal / Control Center /
/// app-switcher peek / incoming-call banner is up, and before didEnterBackground.
/// Distinct from -isForeground, which stays YES through the inactive window.
/// The render-freeze / GPU-hang classifier gates on THIS: CADisplayLink (and
/// therefore present) legitimately pauses the instant the app goes inactive, so
/// a stale present while merely inactive is not a hang. (Android's
/// isForeground() is already resumed-count based and needs no separate flag.)
- (BOOL)isActive;

/// Subscribe to lifecycle transitions. Both callbacks are fired on the main
/// queue. The current state is delivered immediately on subscribe so
/// consumers don't have to peek.
///
/// Each subscription is identified by an opaque token returned from this
/// method — pass it to -removeListener: to drop the pair.
- (id)addOnForeground:(BPLifecycleCallback)onFg
         onBackground:(BPLifecycleCallback)onBg;

- (void)removeListener:(id)token;

@end
