// BugpunchScreenshot.mm — native iOS screenshot capture via UIGraphicsImageRenderer.
//
// Reads the app's own UIWindow — no permission, no user prompt, no ReplayKit.
// Writes a JPEG to the given path; callback invoked on main thread.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>

// Block-typed (not C function pointer) so call sites can capture local
// state — chat / feedback both need `^{ ... }` closures over `self`,
// the message id, the file path. Existing C-only callers (perf monitor,
// trace) wrap their static functions in a one-liner block.
typedef void (^BugpunchScreenshotCallback)(const char* requestId, int success, const char* payload);

static UIWindow* BPFindKeyWindow(void) {
    if (@available(iOS 13.0, *)) {
        for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
            if (scene.activationState != UISceneActivationStateForegroundActive) continue;
            if (![scene isKindOfClass:[UIWindowScene class]]) continue;
            UIWindowScene* ws = (UIWindowScene*)scene;
            for (UIWindow* w in ws.windows) {
                if (w.isKeyWindow) return w;
            }
            if (ws.windows.count > 0) return ws.windows.firstObject;
        }
    }
    #pragma clang diagnostic push
    #pragma clang diagnostic ignored "-Wdeprecated-declarations"
    UIWindow* kw = UIApplication.sharedApplication.keyWindow;
    #pragma clang diagnostic pop
    if (kw) return kw;
    NSArray<UIWindow*>* windows = UIApplication.sharedApplication.windows;
    return windows.count > 0 ? windows.firstObject : nil;
}

static void BPCaptureOnMain(NSString* requestId, NSString* outputPath, int quality,
                            BugpunchScreenshotCallback cb) {
    @try {
        UIWindow* window = BPFindKeyWindow();
        if (!window || window.bounds.size.width <= 0 || window.bounds.size.height <= 0) {
            if (cb) cb([requestId UTF8String], 0, "no key window");
            return;
        }

        UIGraphicsImageRendererFormat* fmt = [UIGraphicsImageRendererFormat defaultFormat];
        fmt.opaque = YES;
        UIGraphicsImageRenderer* renderer =
            [[UIGraphicsImageRenderer alloc] initWithSize:window.bounds.size format:fmt];

        UIImage* img = [renderer imageWithActions:^(UIGraphicsImageRendererContext * _Nonnull ctx) {
            // drawViewHierarchyInRect:afterScreenUpdates: captures the actual presented
            // contents, including Metal/GL layers (which renderInContext: misses).
            [window drawViewHierarchyInRect:window.bounds afterScreenUpdates:NO];
        }];

        CGFloat q = MAX(0.01, MIN(1.0, quality / 100.0));
        NSData* jpeg = UIImageJPEGRepresentation(img, q);
        if (!jpeg) {
            if (cb) cb([requestId UTF8String], 0, "encode failed");
            return;
        }

        NSString* dir = [outputPath stringByDeletingLastPathComponent];
        [[NSFileManager defaultManager] createDirectoryAtPath:dir
                                  withIntermediateDirectories:YES
                                                   attributes:nil
                                                        error:nil];
        NSError* err = nil;
        if (![jpeg writeToFile:outputPath options:NSDataWritingAtomic error:&err]) {
            if (cb) cb([requestId UTF8String], 0,
                [[err localizedDescription] ?: @"write failed" UTF8String]);
            return;
        }
        if (cb) cb([requestId UTF8String], 1, [outputPath UTF8String]);
    } @catch (NSException* ex) {
        if (cb) cb([requestId UTF8String], 0, [[ex reason] ?: @"exception" UTF8String]);
    }
}

/// Capture the key UIWindow to a JPEG. Async — callback invoked on main thread
/// once the file is written (or on failure). Callback may be nil for fire-and-
/// forget captures (e.g. trace pre-shots that are read off disk later).
///
/// Not wrapped in `extern "C"` because `BugpunchScreenshotCallback` is an
/// Objective-C block type, which doesn't have a stable C-only ABI. All
/// callers are iOS Obj-C++ siblings forward-declaring the block typedef +
/// function, so C-linkage isn't actually buying us anything here.
void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
                                int quality, BugpunchScreenshotCallback cb) {
    if (!requestId || !outputPath) {
        if (cb) cb(requestId ? requestId : "", 0, "null args");
        return;
    }
    NSString* rid = [NSString stringWithUTF8String:requestId];
    NSString* path = [NSString stringWithUTF8String:outputPath];

    dispatch_async(dispatch_get_main_queue(), ^{
        BPCaptureOnMain(rid, path, quality, cb);
    });
}
