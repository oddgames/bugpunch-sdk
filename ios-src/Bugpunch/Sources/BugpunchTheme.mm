// BugpunchTheme.mm — implementation, see BugpunchTheme.h for rationale.
//
// All surfaces read their colours / card radius / font sizes through this
// class so a single native palette drives Welcome card, Request-Help picker,
// Recording overlay, Consent sheet, and Crash report overlay.
//
// Copyright (c) ODDGames. All rights reserved.

#import "BugpunchTheme.h"

// ── Schema defaults ──
// Kept in sync with BugpunchTheme.java and the shared C# theme schema so the
// three sides can't drift apart silently.
static UIColor* DefColor(NSString* k) {
    if ([k isEqualToString:@"cardBackground"])  return [UIColor colorWithRed:0x21/255.0 green:0x21/255.0 blue:0x21/255.0 alpha:1.0];
    if ([k isEqualToString:@"cardBorder"])      return [UIColor colorWithRed:0x47/255.0 green:0x47/255.0 blue:0x47/255.0 alpha:1.0];
    if ([k isEqualToString:@"backdrop"])        return [[UIColor blackColor] colorWithAlphaComponent:0x99/255.0];
    if ([k isEqualToString:@"textPrimary"])     return [UIColor whiteColor];
    if ([k isEqualToString:@"textSecondary"])   return [UIColor colorWithRed:0xB8/255.0 green:0xB8/255.0 blue:0xB8/255.0 alpha:1.0];
    if ([k isEqualToString:@"textMuted"])       return [UIColor colorWithRed:0x8C/255.0 green:0x8C/255.0 blue:0x8C/255.0 alpha:1.0];
    if ([k isEqualToString:@"accentPrimary"])   return [UIColor colorWithRed:0x40/255.0 green:0x7D/255.0 blue:0x4C/255.0 alpha:1.0];
    if ([k isEqualToString:@"accentRecord"])    return [UIColor colorWithRed:0xD2/255.0 green:0x2E/255.0 blue:0x2E/255.0 alpha:1.0];
    if ([k isEqualToString:@"accentChat"])      return [UIColor colorWithRed:0x33/255.0 green:0x61/255.0 blue:0x99/255.0 alpha:1.0];
    if ([k isEqualToString:@"accentFeedback"])  return [UIColor colorWithRed:0x40/255.0 green:0x7D/255.0 blue:0x4C/255.0 alpha:1.0];
    if ([k isEqualToString:@"accentBug"])       return [UIColor colorWithRed:0x94/255.0 green:0x38/255.0 blue:0x38/255.0 alpha:1.0];
    return nil;
}

static CGFloat DefRadius(NSString* k) {
    if ([k isEqualToString:@"cardRadius"]) return 12.0f;
    return 0.0f;
}

static CGFloat DefFont(NSString* k) {
    if ([k isEqualToString:@"fontSizeTitle"])   return 20.0f;
    if ([k isEqualToString:@"fontSizeBody"])    return 14.0f;
    if ([k isEqualToString:@"fontSizeCaption"]) return 12.0f;
    return 0.0f;
}

static NSMutableDictionary<NSString*, UIColor*>* sColors;
static NSMutableDictionary<NSString*, NSNumber*>* sRadii;
static NSMutableDictionary<NSString*, NSNumber*>* sFonts;
static BOOL sApplied = NO;

static void EnsureInit(void) {
    if (sColors) return;
    sColors = [NSMutableDictionary new];
    sRadii  = [NSMutableDictionary new];
    sFonts  = [NSMutableDictionary new];
    // Pre-seed defaults so pre-apply lookups work — Bugpunch_ShowRequestHelp
    // could get called before the config parse if something's out of order.
    NSArray* colorKeys = @[@"cardBackground", @"cardBorder", @"backdrop",
                           @"textPrimary", @"textSecondary", @"textMuted",
                           @"accentPrimary", @"accentRecord", @"accentChat",
                           @"accentFeedback", @"accentBug"];
    for (NSString* k in colorKeys) sColors[k] = DefColor(k);
    sRadii[@"cardRadius"] = @(DefRadius(@"cardRadius"));
    sFonts[@"fontSizeTitle"]   = @(DefFont(@"fontSizeTitle"));
    sFonts[@"fontSizeBody"]    = @(DefFont(@"fontSizeBody"));
    sFonts[@"fontSizeCaption"] = @(DefFont(@"fontSizeCaption"));
}

@implementation BPTheme

+ (void)applyFromJson:(NSDictionary*)themeDict {
    EnsureInit();
    if (![themeDict isKindOfClass:[NSDictionary class]]) {
        sApplied = YES;
        return;
    }
    for (NSString* k in themeDict) {
        id v = themeDict[k];
        if (!v || v == [NSNull null]) continue;

        if (sColors[k] != nil) {
            UIColor* parsed = [BPTheme colorFromHex:[v isKindOfClass:[NSString class]] ? v : nil
                                           fallback:nil];
            if (parsed) sColors[k] = parsed;
        } else if ([k hasSuffix:@"Radius"]) {
            if ([v isKindOfClass:[NSNumber class]])      sRadii[k] = v;
            else if ([v isKindOfClass:[NSString class]]) sRadii[k] = @([(NSString*)v doubleValue]);
        } else if ([k hasPrefix:@"fontSize"]) {
            if ([v isKindOfClass:[NSNumber class]])      sFonts[k] = v;
            else if ([v isKindOfClass:[NSString class]]) sFonts[k] = @([(NSString*)v doubleValue]);
        }
    }
    sApplied = YES;
    NSLog(@"[Bugpunch.Theme] applied: cardBg=%@ accentPrimary=%@ radius=%@ titleFont=%@",
          sColors[@"cardBackground"], sColors[@"accentPrimary"],
          sRadii[@"cardRadius"], sFonts[@"fontSizeTitle"]);
}

+ (BOOL)isApplied { return sApplied; }

+ (UIColor*)color:(NSString*)name fallback:(UIColor*)fb {
    EnsureInit();
    UIColor* c = sColors[name];
    return c ?: fb;
}

+ (CGFloat)radius:(NSString*)name fallback:(CGFloat)fb {
    EnsureInit();
    NSNumber* n = sRadii[name];
    return n ? n.doubleValue : fb;
}

+ (CGFloat)font:(NSString*)name fallback:(CGFloat)fb {
    EnsureInit();
    NSNumber* n = sFonts[name];
    return n ? n.doubleValue : fb;
}

+ (UIColor*)colorFromHex:(NSString*)hex fallback:(UIColor*)fb {
    if (![hex isKindOfClass:[NSString class]] || hex.length == 0) return fb;
    NSString* s = [hex stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
    if ([s hasPrefix:@"#"]) s = [s substringFromIndex:1];

    unsigned int hexValue = 0;
    NSScanner* scanner = [NSScanner scannerWithString:s];
    if (![scanner scanHexInt:&hexValue]) return fb;

    CGFloat r, g, b, a = 1.0f;
    if (s.length == 6) {
        // #RRGGBB
        r = ((hexValue >> 16) & 0xFF) / 255.0f;
        g = ((hexValue >> 8)  & 0xFF) / 255.0f;
        b = ( hexValue        & 0xFF) / 255.0f;
    } else if (s.length == 8) {
        // #RRGGBBAA — matches the shared theme schema (Android also uses this).
        r = ((hexValue >> 24) & 0xFF) / 255.0f;
        g = ((hexValue >> 16) & 0xFF) / 255.0f;
        b = ((hexValue >> 8)  & 0xFF) / 255.0f;
        a = ( hexValue        & 0xFF) / 255.0f;
    } else {
        return fb;
    }
    return [UIColor colorWithRed:r green:g blue:b alpha:a];
}

@end
