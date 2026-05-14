// BugpunchTheme.h — central theme lookup for Bugpunch iOS surfaces.
//
// Applied once at startup from Bugpunch_StartDebugMode with the parsed
// `theme` dictionary. Each overlay builder asks for named colours / radii /
// font sizes; missing keys fall back to the schema defaults.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#import <UIKit/UIKit.h>

@interface BPTheme : NSObject

/** Apply the theme dictionary from the startup config. Nil / empty is a
 *  no-op (defaults stay in place). Safe to call multiple times — a later
 *  call replaces the previously loaded values. */
+ (void)applyFromJson:(NSDictionary*)themeDict;

/** Returns true once {@link applyFromJson:} has been called at least once. */
+ (BOOL)isApplied;

/** Colour for the given key, or {@code fallback} if missing / unparseable. */
+ (UIColor*)color:(NSString*)name fallback:(UIColor*)fb;

/** Points value for a radius key (iOS doesn't distinguish dp/pt). */
+ (CGFloat)radius:(NSString*)name fallback:(CGFloat)fb;

/** Font size in pt for a font-size key. */
+ (CGFloat)font:(NSString*)name fallback:(CGFloat)fb;

/** Hex parsing exposed so any native surface with its own colour code can
 *  reuse the same decoder. Accepts {@code #RRGGBB} and {@code #RRGGBBAA}. */
+ (UIColor*)colorFromHex:(NSString*)hex fallback:(UIColor*)fb;

@end
