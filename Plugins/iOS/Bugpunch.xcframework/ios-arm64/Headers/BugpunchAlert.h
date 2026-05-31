// BugpunchAlert.h — reusable branded alert / menu presenter for Bugpunch
// iOS surfaces. The single replacement for stock UIAlertController across
// every player-facing native dialog.
//
// Visual language is lifted verbatim from BugpunchConsentSheet.mm
// (BPConsentViewController): a BPGradientView card over a blurred dark
// backdrop, BPTheme colours + BugpunchStrings copy, rounded buttons. One,
// two, or three stacked buttons; an optional destructive (red) primary.
//
// Two ways in:
//   - Obj-C++ in-process: +[BPAlert presentTitle:message:actions:] with
//     BPAlertAction objects (title + style + block). Used for both
//     confirm-style alerts and choice "action sheet" menus.
//   - extern "C" file-scope: Bugpunch_PresentAlert(...) — C-ABI entry for
//     symmetry with the other Bugpunch_Present* presenters.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#import <UIKit/UIKit.h>

typedef NS_ENUM(NSInteger, BPAlertActionStyle) {
    BPAlertActionStyleDefault   = 0, // outlined / neutral
    BPAlertActionStylePrimary   = 1, // accent fill (the affirmative action)
    BPAlertActionStyleDestructive = 2, // red fill (irreversible action)
    BPAlertActionStyleCancel    = 3, // text-only, dismisses
};

@interface BPAlertAction : NSObject
@property (nonatomic, copy)   NSString* title;
@property (nonatomic, assign) BPAlertActionStyle style;
@property (nonatomic, copy)   void (^handler)(void); // nil == just dismiss
+ (instancetype)actionWithTitle:(NSString*)title
                          style:(BPAlertActionStyle)style
                        handler:(void (^)(void))handler;
@end

@interface BPAlert : NSObject

/// Present a branded alert/menu. Buttons render top-to-bottom in array
/// order. A Cancel-style action is always rendered as the trailing
/// text-only button (and mirrored by the top-right close ×). Safe to call
/// from any thread — hops to the main queue. Presents over the current
/// top-most view controller.
+ (void)presentTitle:(NSString*)title
             message:(NSString*)message
             actions:(NSArray<BPAlertAction*>*)actions;

@end

#ifdef __cplusplus
extern "C" {
#endif

/// Plain C callback fired when a branded-alert button is tapped.
typedef void (*BPAlertCHandler)(void);

/// C-ABI entry: present a branded alert with up to three buttons. `titles`
/// is a C-string array of length `count`; `styles` is a parallel array of
/// BPAlertActionStyle ints; `handlers` is a parallel array of C callbacks
/// (any may be NULL). Title / message may be NULL. Provided for symmetry
/// with the other Bugpunch_Present* C entries; in-process Obj-C++ callers
/// should prefer +[BPAlert presentTitle:message:actions:].
void Bugpunch_PresentAlert(const char* title,
                           const char* message,
                           const char* const* titles,
                           const int* styles,
                           const BPAlertCHandler* handlers,
                           int count);

#ifdef __cplusplus
}
#endif
