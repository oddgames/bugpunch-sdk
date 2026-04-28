// BugpunchStrings.h — central string-table lookup for Bugpunch iOS surfaces.
//
// Applied once at startup from Bugpunch_StartDebugMode with the parsed
// `strings` dictionary the C# side serialised. Each overlay asks for keys
// by name; missing keys fall back to the caller's English fallback. Locale
// override → default → fallback.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#import <Foundation/Foundation.h>

@interface BPStrings : NSObject

/** Apply the strings dictionary from the startup config. Nil / empty is a
 *  no-op (defaults stay in place). Safe to call multiple times — a later
 *  call replaces the previously loaded values. Shape:
 *  {locale, defaults: {...}, translations: {"es": {...}, ...}}. */
+ (void)applyFromJson:(NSDictionary*)stringsDict;

/** Returns true once {@link applyFromJson:} has been called at least once. */
+ (BOOL)isApplied;

/** Resolve a string by key. Active-locale override → default → fallback.
 *  Always non-nil. */
+ (NSString*)text:(NSString*)key fallback:(NSString*)fallback;

/** Active locale code: explicit setting if non-"auto", otherwise the
 *  device's primary preferred language ([NSLocale preferredLanguages][0]).
 *  Empty string if nothing's available. */
+ (NSString*)activeLocale;

@end
