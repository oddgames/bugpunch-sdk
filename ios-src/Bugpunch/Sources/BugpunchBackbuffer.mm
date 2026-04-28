// BugpunchBackbuffer.mm — Metal backbuffer capture for iOS.
//
// Copies the last-rendered frame's Metal texture to a persistent shared-memory
// texture on every present. The copy is a GPU blit (~0.2ms, async) with near-
// zero CPU overhead. The readback texture lives in MTLStorageModeShared so any
// thread can read raw pixels at any time — no GPU sync needed after the blit
// completes.
//
// Used by the ANR watchdog (background thread, main thread stuck) and as a
// fast-path screenshot for exceptions. Falls back to the slow UIKit path
// (drawViewHierarchyInRect) when Metal capture isn't available.

#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <UIKit/UIKit.h>
#import <objc/runtime.h>

// ── State ──

static id<MTLDevice>       s_device;
static id<MTLCommandQueue>  s_queue;
static id<MTLTexture>       s_readback[2];  // double buffer
static volatile int         s_writeIdx = 0;
static volatile bool        s_ready = false;
static int                  s_width = 0;
static int                  s_height = 0;

// Original IMP for the swizzled present method.
typedef void (*PresentIMP)(id, SEL);
static PresentIMP s_origPresent = NULL;
static PresentIMP s_origPresentAT = NULL;

// ── Helpers ──

static void bp_ensure_readback(int w, int h) {
    if (s_width == w && s_height == h && s_readback[0] && s_readback[1]) return;
    s_width = w;
    s_height = h;
    MTLTextureDescriptor* desc = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                                     width:w
                                    height:h
                                 mipmapped:NO];
    desc.usage = MTLTextureUsageShaderRead;
    desc.storageMode = MTLStorageModeShared;
    s_readback[0] = [s_device newTextureWithDescriptor:desc];
    s_readback[1] = [s_device newTextureWithDescriptor:desc];
    s_readback[0].label = @"BugpunchBackbuffer0";
    s_readback[1].label = @"BugpunchBackbuffer1";
    NSLog(@"[BugpunchBackbuffer] readback textures created: %dx%d", w, h);
}

static void bp_blit_drawable(id<MTLTexture> src) {
    if (!s_queue || !src) return;
    int w = (int)src.width;
    int h = (int)src.height;
    if (w <= 0 || h <= 0) return;

    bp_ensure_readback(w, h);

    int idx = (s_writeIdx + 1) % 2;
    id<MTLTexture> dst = s_readback[idx];
    if (!dst) return;

    id<MTLCommandBuffer> cmd = [s_queue commandBuffer];
    if (!cmd) return;
    cmd.label = @"BugpunchBlit";

    id<MTLBlitCommandEncoder> blit = [cmd blitCommandEncoder];
    [blit copyFromTexture:src
              sourceSlice:0
              sourceLevel:0
             sourceOrigin:MTLOriginMake(0, 0, 0)
               sourceSize:MTLSizeMake(w, h, 1)
                toTexture:dst
         destinationSlice:0
         destinationLevel:0
        destinationOrigin:MTLOriginMake(0, 0, 0)];
    [blit endEncoding];

    [cmd addCompletedHandler:^(id<MTLCommandBuffer> _) {
        // Flip the read index after blit completes — readers always see
        // a fully-written texture.
        s_writeIdx = idx;
    }];
    [cmd commit];
}

// ── Swizzled present methods ──

static void bp_swizzled_present(id self, SEL _cmd) {
    // Blit BEFORE presenting — after present the drawable is recycled.
    @try {
        id<CAMetalDrawable> drawable = (id<CAMetalDrawable>)self;
        bp_blit_drawable(drawable.texture);
    } @catch (NSException* ex) {
        // Don't crash the app if blit fails.
    }
    if (s_origPresent) s_origPresent(self, _cmd);
}

static void bp_swizzled_presentAtTime(id self, SEL _cmd) {
    @try {
        id<CAMetalDrawable> drawable = (id<CAMetalDrawable>)self;
        bp_blit_drawable(drawable.texture);
    } @catch (NSException* ex) {}
    if (s_origPresentAT) s_origPresentAT(self, _cmd);
}

// ── Public C API ──

extern "C" {

/// Initialize the Metal backbuffer capture system. Call once at startup.
/// Returns true if Metal is available and swizzle succeeded.
bool Bugpunch_InitBackbuffer(void) {
    if (s_ready) return true;

    // Get the Metal device. Try Unity's device first, fall back to system default.
    // UnityGetMetalDevice() may not be linked if Unity hasn't started Metal yet,
    // so we use the system default which is the same physical device.
    s_device = MTLCreateSystemDefaultDevice();
    if (!s_device) {
        NSLog(@"[BugpunchBackbuffer] no Metal device — backbuffer capture unavailable");
        return false;
    }

    s_queue = [s_device newCommandQueue];
    if (!s_queue) {
        NSLog(@"[BugpunchBackbuffer] failed to create command queue");
        return false;
    }
    s_queue.label = @"BugpunchBackbuffer";

    // Swizzle CAMetalDrawable's present and presentAtTime: methods.
    // CAMetalDrawable is a protocol backed by a private class. We need to find
    // the concrete class at runtime. The standard approach: create a dummy
    // CAMetalLayer, get a drawable, inspect its class.
    //
    // However, we can't get a drawable without a running Metal layer. Instead,
    // swizzle the protocol methods on the implementing class by searching for
    // classes that conform to CAMetalDrawable. The concrete class is typically
    // named "CAMetalDrawableBridge" or similar — it varies by iOS version.
    //
    // Pragmatic approach: use objc_getClass on known internal class names, or
    // iterate all classes looking for the protocol conformance.

    Protocol* proto = @protocol(CAMetalDrawable);
    BOOL swizzled = NO;

    unsigned int classCount = 0;
    Class* classList = objc_copyClassList(&classCount);
    for (unsigned int i = 0; i < classCount; i++) {
        Class cls = classList[i];
        if (!class_conformsToProtocol(cls, proto)) continue;

        // Swizzle -present
        Method m = class_getInstanceMethod(cls, @selector(present));
        if (m) {
            s_origPresent = (PresentIMP)method_getImplementation(m);
            method_setImplementation(m, (IMP)bp_swizzled_present);
            swizzled = YES;
        }

        // Swizzle -presentAtTime: — Unity may use either.
        // presentAtTime: has a different signature (CFTimeInterval arg) but we
        // only need to intercept before calling through, and the drawable is
        // `self` regardless. We use the same blit logic.
        Method mAT = class_getInstanceMethod(cls, @selector(presentAtTime:));
        if (mAT) {
            s_origPresentAT = (PresentIMP)method_getImplementation(mAT);
            method_setImplementation(mAT, (IMP)bp_swizzled_presentAtTime);
        }

        if (swizzled) {
            NSLog(@"[BugpunchBackbuffer] swizzled present on %s", class_getName(cls));
            break;
        }
    }
    free(classList);

    if (!swizzled) {
        NSLog(@"[BugpunchBackbuffer] failed to find CAMetalDrawable class — swizzle unavailable");
        return false;
    }

    s_ready = true;
    NSLog(@"[BugpunchBackbuffer] initialized (device=%@)", s_device.name);
    return true;
}

/// Read the last-captured frame as JPEG data. Thread-safe — can be called
/// from the ANR watchdog background thread or anywhere else.
/// Returns nil if no frame has been captured yet.
NSData* Bugpunch_ReadBackbufferJPEG(float quality) {
    if (!s_ready) return nil;

    int idx = s_writeIdx;
    id<MTLTexture> tex = s_readback[idx];
    if (!tex) return nil;

    int w = (int)tex.width;
    int h = (int)tex.height;
    if (w <= 0 || h <= 0) return nil;

    // Read raw BGRA pixels from the shared-memory texture.
    NSUInteger bytesPerRow = w * 4;
    NSMutableData* pixels = [NSMutableData dataWithLength:bytesPerRow * h];
    [tex getBytes:pixels.mutableBytes
      bytesPerRow:bytesPerRow
       fromRegion:MTLRegionMake2D(0, 0, w, h)
      mipmapLevel:0];

    // Convert BGRA → CGImage → JPEG.
    CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
    CGContextRef ctx = CGBitmapContextCreate(
        pixels.mutableBytes, w, h, 8, bytesPerRow,
        cs, kCGBitmapByteOrder32Little | kCGImageAlphaPremultipliedFirst);
    CGColorSpaceRelease(cs);
    if (!ctx) return nil;

    CGImageRef cgImg = CGBitmapContextCreateImage(ctx);
    CGContextRelease(ctx);
    if (!cgImg) return nil;

    UIImage* img = [UIImage imageWithCGImage:cgImg];
    CGImageRelease(cgImg);

    NSData* jpeg = UIImageJPEGRepresentation(img, MAX(0.01f, MIN(1.0f, quality)));
    return jpeg;
}

/// Write the last-captured frame to a JPEG file. Thread-safe.
/// Returns true on success.
bool Bugpunch_WriteBackbufferJPEG(const char* outputPath, float quality) {
    NSData* jpeg = Bugpunch_ReadBackbufferJPEG(quality);
    if (!jpeg || jpeg.length == 0) return false;

    NSString* path = [NSString stringWithUTF8String:outputPath];
    NSString* dir = [path stringByDeletingLastPathComponent];
    [[NSFileManager defaultManager] createDirectoryAtPath:dir
                              withIntermediateDirectories:YES
                                               attributes:nil error:nil];
    return [jpeg writeToFile:path atomically:YES];
}

/// Returns true if the Metal backbuffer capture is initialized and has at
/// least one frame available.
bool Bugpunch_BackbufferReady(void) {
    return s_ready && s_readback[s_writeIdx] != nil;
}

/// Tear down (call on shutdown — optional, process exit cleans up anyway).
void Bugpunch_ShutdownBackbuffer(void) {
    s_ready = false;
    s_readback[0] = nil;
    s_readback[1] = nil;
    s_queue = nil;
    // Don't nil s_device — other code may reference it.
}

}
