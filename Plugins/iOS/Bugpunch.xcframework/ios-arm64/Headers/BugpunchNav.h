// BugpunchNav.h
//
// Single navigation host for ALL Bugpunch UI on iOS — the standard path every
// player-facing screen shows through (mirrors C# BugpunchNav and the Android
// BugpunchHostActivity stack).
//
// Owns a DEDICATED UIWindow that sits just above the Unity window. Screens are
// pushed/popped on one UINavigationController (system bar hidden — screens keep
// their own chrome; pushed screens add a back chevron via `depth`). Because all
// Bugpunch UI lives on this one window:
//   • "hide everything for a screenshot" = hide the window (state preserved
//     perfectly — no dismiss/re-present) → see BugpunchUI.h
//   • "what's on top" / back routing / teardown are single operations that
//     automatically include any future screen.
//
// The ANR / render-freeze watchdog is suspended while the window is visible
// (Unity stops presenting frames underneath) and resumed when it hides —
// owned HERE so individual screens don't need balanced appear/disappear pairs.

#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchNav : NSObject

/// Show the window with `root` as the only screen on the stack. Replaces the
/// old "present a fullScreen modal on the Unity window" idiom.
+ (void)presentRoot:(UIViewController*)root;

/// Push a screen. If the window isn't up yet, behaves like presentRoot:.
+ (void)push:(UIViewController*)vc;

/// Pop one screen (popping the last one dismisses the window).
+ (void)pop;

+ (void)popToRoot;

/// Hide + empty the window, hand key status back to the game window.
+ (void)dismissAll;

/// Present a modal over the current stack (consent sheets, pickers, players).
/// Lands on the Bugpunch window, so it stays inside the capture suspend
/// envelope.
+ (void)presentOverlay:(UIViewController*)vc;

/// True while the window is visible with at least one screen.
+ (BOOL)isVisible;

/// Stack depth (0 when the window is down). Screens use `depth > 1` to decide
/// whether their header shows a back chevron ‹ (pop) next to the close ✕
/// (dismissAll).
+ (NSUInteger)depth;

/// Deepest visible VC on the Bugpunch window (top of stack, following any
/// presented modal). Replaces the per-file topPresentingVC scene-walks.
+ (nullable UIViewController*)topViewController;

/// Capture plumbing (BugpunchUI): hide/unhide WITHOUT touching the stack.
+ (void)setWindowHiddenForCapture:(BOOL)hidden;

@end

NS_ASSUME_NONNULL_END
