// BugpunchStrings.mm — implementation, see BugpunchStrings.h for rationale.
//
// Resolution mirrors BugpunchStrings.java:
//   1. Active locale's translation override (locale = explicit or
//      [NSLocale preferredLanguages][0]).
//   2. Two-letter region-stripped fallback ("es" if "es-MX" missed).
//   3. Default English value supplied by the C# table.
//   4. Caller's hardcoded fallback.
//
// Copyright (c) ODDGames. All rights reserved.

#import "BugpunchStrings.h"

static NSMutableDictionary<NSString*, NSString*>* sDefaults;
static NSMutableDictionary<NSString*, NSDictionary<NSString*, NSString*>*>* sTranslations;
static NSString* sLocale = @"auto";
static BOOL sApplied = NO;

static void EnsureInit(void) {
    if (sDefaults) return;
    sDefaults     = [NSMutableDictionary new];
    sTranslations = [NSMutableDictionary new];
}

@implementation BPStrings

+ (void)applyFromJson:(NSDictionary*)stringsDict {
    EnsureInit();
    [sDefaults removeAllObjects];
    [sTranslations removeAllObjects];

    if (![stringsDict isKindOfClass:[NSDictionary class]]) {
        sApplied = YES;
        return;
    }

    NSString* loc = stringsDict[@"locale"];
    sLocale = (loc.length > 0) ? loc : @"auto";

    NSDictionary* defaults = stringsDict[@"defaults"];
    if ([defaults isKindOfClass:[NSDictionary class]]) {
        for (NSString* k in defaults) {
            id v = defaults[k];
            if ([v isKindOfClass:[NSString class]]) sDefaults[k] = v;
        }
    }

    NSDictionary* trs = stringsDict[@"translations"];
    if ([trs isKindOfClass:[NSDictionary class]]) {
        for (NSString* code in trs) {
            id set = trs[code];
            if (![set isKindOfClass:[NSDictionary class]]) continue;
            NSMutableDictionary* bucket = [NSMutableDictionary new];
            for (NSString* k in (NSDictionary*)set) {
                id v = ((NSDictionary*)set)[k];
                if ([v isKindOfClass:[NSString class]]) bucket[k] = v;
            }
            sTranslations[code] = bucket;
        }
    }

    sApplied = YES;
    NSLog(@"[Bugpunch.Strings] applied: defaults=%lu translations=%lu locale=%@",
          (unsigned long)sDefaults.count,
          (unsigned long)sTranslations.count, sLocale);
}

+ (BOOL)isApplied { return sApplied; }

+ (NSString*)text:(NSString*)key fallback:(NSString*)fallback {
    EnsureInit();
    if (key.length == 0) return fallback ?: @"";

    NSString* active = [BPStrings activeLocale];
    if (active.length > 0) {
        NSDictionary* bucket = sTranslations[active];
        NSString* hit = bucket[key];
        if (hit.length > 0) return hit;

        // Region-stripped fallback: try "es" if "es-MX" wasn't an exact hit.
        NSRange dash = [active rangeOfString:@"-"];
        if (dash.location != NSNotFound) {
            NSString* shortCode = [active substringToIndex:dash.location];
            NSDictionary* shortBucket = sTranslations[shortCode];
            NSString* shortHit = shortBucket[key];
            if (shortHit.length > 0) return shortHit;
        }
    }

    NSString* d = sDefaults[key];
    if (d.length > 0) return d;
    return fallback ?: @"";
}

+ (NSString*)activeLocale {
    if (sLocale.length > 0 && ![sLocale isEqualToString:@"auto"]) return sLocale;
    NSArray* prefs = [NSLocale preferredLanguages];
    if (prefs.count > 0) {
        // [NSLocale preferredLanguages] returns BCP 47 codes ("en", "es-MX").
        NSString* primary = prefs[0];
        return primary ?: @"";
    }
    return @"";
}

@end
