// BugpunchImagePicker.mm
// Native image picker for the chat composer (iOS).
//
// Bridge:
//   C# BugpunchNative.PickImage(callback)
//     → Bugpunch_PickImage()
//     → PHPickerViewController in single-image mode
//     → user picks / cancels
//     → on pick: load UIImage → write JPEG to NSTemporaryDirectory
//     → on cancel: empty path
//     → UnitySendMessage("BugpunchReportCallback", "OnImagePicked", path)
//
// Routed through UnitySendMessage instead of a C function pointer so Android
// and iOS land on the same C# dispatch path (ReportOverlayCallback MonoBehaviour
// → BugpunchNative.DispatchImagePicked).
//
// Copyright (c) ODDGames. All rights reserved.

#import <UIKit/UIKit.h>
#import <PhotosUI/PhotosUI.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static UIViewController *GetRootViewController(void) {
    UIApplication *app = [UIApplication sharedApplication];
    for (UIWindowScene *scene in app.connectedScenes) {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        for (UIWindow *w in scene.windows) {
            if (w.isKeyWindow && w.rootViewController) return w.rootViewController;
        }
    }
    // Fallback — some builds don't flag a key window until after first touch.
    for (UIWindowScene *scene in app.connectedScenes) {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        for (UIWindow *w in scene.windows) {
            if (w.rootViewController) return w.rootViewController;
        }
    }
    return nil;
}

// Callback target — uses UnitySendMessage so both platforms share a single
// C# dispatch (the BugpunchReportCallback MonoBehaviour).
static void BPSendPickResult(NSString *path) {
    const char *cPath = path ? [path UTF8String] : "";
    UnitySendMessage("BugpunchReportCallback", "OnImagePicked", cPath ? cPath : "");
}

API_AVAILABLE(ios(14.0))
@interface BPImagePickerDelegate : NSObject <PHPickerViewControllerDelegate>
@end

@implementation BPImagePickerDelegate

- (void)picker:(PHPickerViewController *)picker didFinishPicking:(NSArray<PHPickerResult *> *)results {
    // Dismiss first, then process — keeps the UI responsive even if the
    // image load takes a moment.
    __weak BPImagePickerDelegate *weakSelf = self;
    [picker dismissViewControllerAnimated:YES completion:^{
        (void)weakSelf;
        if (results.count == 0) {
            BPSendPickResult(@"");
            return;
        }
        PHPickerResult *r = results.firstObject;
        NSItemProvider *p = r.itemProvider;
        if ([p canLoadObjectOfClass:[UIImage class]]) {
            [p loadObjectOfClass:[UIImage class]
                completionHandler:^(__kindof id<NSItemProviderReading> object, NSError *error) {
                    dispatch_async(dispatch_get_main_queue(), ^{
                        if (![object isKindOfClass:[UIImage class]] || error) {
                            BPSendPickResult(@"");
                            return;
                        }
                        UIImage *img = (UIImage *)object;
                        NSData *data = UIImageJPEGRepresentation(img, 0.9);
                        if (!data) { BPSendPickResult(@""); return; }
                        NSString *dir = NSTemporaryDirectory();
                        NSString *filename = [NSString stringWithFormat:@"bugpunch_pick_%lld.jpg",
                                              (long long)[NSDate timeIntervalSinceReferenceDate]];
                        NSString *path = [dir stringByAppendingPathComponent:filename];
                        NSError *writeErr = nil;
                        if ([data writeToFile:path options:NSDataWritingAtomic error:&writeErr]) {
                            BPSendPickResult(path);
                        } else {
                            NSLog(@"[Bugpunch.ImagePicker] write failed: %@", writeErr);
                            BPSendPickResult(@"");
                        }
                    });
                }];
        } else {
            BPSendPickResult(@"");
        }
    }];
}

@end

// Keep the delegate alive — PHPickerViewController holds it weakly.
static id sPickerDelegate = nil;

extern "C" {

void Bugpunch_PickImage(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController *root = GetRootViewController();
        if (!root) {
            BPSendPickResult(@"");
            return;
        }

        if (@available(iOS 14.0, *)) {
            PHPickerConfiguration *cfg = [[PHPickerConfiguration alloc] init];
            cfg.selectionLimit = 1;
            cfg.filter = [PHPickerFilter imagesFilter];

            PHPickerViewController *picker = [[PHPickerViewController alloc] initWithConfiguration:cfg];
            BPImagePickerDelegate *del = [[BPImagePickerDelegate alloc] init];
            sPickerDelegate = del;
            picker.delegate = del;
            picker.modalPresentationStyle = UIModalPresentationFormSheet;
            [root presentViewController:picker animated:YES completion:nil];
        } else {
            // iOS 13 and below — PHPickerViewController isn't available.
            // Bugpunch ships against iOS 14+ per package metadata, but the
            // runtime check lets us fail cleanly instead of crashing if a
            // consumer drops the deployment target.
            NSLog(@"[Bugpunch.ImagePicker] PHPickerViewController requires iOS 14+");
            BPSendPickResult(@"");
        }
    });
}

} // extern "C"
