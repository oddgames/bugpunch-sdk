// =============================================================================
// BugpunchStoryboard.mm — iOS storyboard input-press capture ring.
//
// Mirrors the Android implementation (BugpunchStoryboard.java + bp.c). C# pushes
// a downscaled RGBA frame + press metadata on every UI press via
// Bugpunch_PushButtonPressFrame. The newest slot is the rescue path for
// `screenshot_at_crash` when a POSIX signal / Mach exception fires; the whole
// ring is the storyboard rail's input timeline.
//
// Memory layout per slot — must stay byte-identical with the Java reader:
//   off  size  field
//   0    8     long   tsMs                        (little-endian)
//   8    4     float  x                           (Unity screen pixels, BL origin)
//   12   4     float  y
//   16   4     int    screenW
//   20   4     int    screenH
//   24   4     int    w                           (pixel-buffer width)
//   28   4     int    h                           (pixel-buffer height)
//   32   4     int    pixelsLen                   (= w * h * 4)
//   36  192    char   path[192]                   UTF-8 NUL-padded
//  228   96    char   label[96]
//  324   32    char   scene[32]
//   = 356 bytes header.
//
// Pixel slabs are malloc'd lazily on first push (so an idle session pays zero
// pixel-buffer memory). The signal handler reads pre-stored pointers via the
// async-signal-safe getters below — no locks, no malloc, no Obj-C runtime.
// =============================================================================

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#define BP_SB_CAPACITY      10
#define BP_SB_PATH_LEN      192
#define BP_SB_LABEL_LEN     96
#define BP_SB_SCENE_LEN     32
#define BP_SB_HEADER_BYTES  (8 + 4*7 + BP_SB_PATH_LEN + BP_SB_LABEL_LEN + BP_SB_SCENE_LEN)  // 356

static unsigned char s_sb_header[BP_SB_CAPACITY][BP_SB_HEADER_BYTES];
static unsigned char* s_sb_pixels[BP_SB_CAPACITY] = {0};
static int   s_sb_pixels_cap[BP_SB_CAPACITY] = {0};
static int   s_sb_pixels_len[BP_SB_CAPACITY] = {0};
static volatile int s_sb_newest = -1;
static volatile int s_sb_count = 0;
static int s_sb_next = 0;
static int s_sb_locked_w = 0;
static int s_sb_locked_h = 0;
static pthread_mutex_t s_sb_mutex = PTHREAD_MUTEX_INITIALIZER;

static inline void bp_sb_write_le_u32(unsigned char* dst, uint32_t v) {
    dst[0] = (unsigned char)(v & 0xff);
    dst[1] = (unsigned char)((v >> 8) & 0xff);
    dst[2] = (unsigned char)((v >> 16) & 0xff);
    dst[3] = (unsigned char)((v >> 24) & 0xff);
}
static inline void bp_sb_write_le_u64(unsigned char* dst, uint64_t v) {
    bp_sb_write_le_u32(dst,     (uint32_t)(v & 0xffffffffu));
    bp_sb_write_le_u32(dst + 4, (uint32_t)((v >> 32) & 0xffffffffu));
}
static inline void bp_sb_write_le_float(unsigned char* dst, float f) {
    uint32_t u; memcpy(&u, &f, 4);
    bp_sb_write_le_u32(dst, u);
}
static inline void bp_sb_write_fixed_utf8(unsigned char* dst, const char* s, int maxBytes) {
    if (!s) { memset(dst, 0, maxBytes); return; }
    int n = (int)strlen(s);
    if (n > maxBytes) n = maxBytes;
    memcpy(dst, s, n);
    if (n < maxBytes) memset(dst + n, 0, maxBytes - n);
}

#pragma mark - C# entry point

extern "C" void Bugpunch_PushButtonPressFrame(long long tsMs,
                                              const char* path,
                                              const char* label,
                                              const char* scene,
                                              float x,
                                              float y,
                                              int screenW,
                                              int screenH,
                                              int w,
                                              int h,
                                              const unsigned char* bytes,
                                              int byteLen) {
    if (!bytes || byteLen <= 0 || w <= 0 || h <= 0) return;
    int expected = w * h * 4;
    if (byteLen < expected) return;

    pthread_mutex_lock(&s_sb_mutex);

    // Aspect / size lock. First push wins. Orientation change drops the entire
    // ring rather than mixing slot dimensions — the signal handler needs each
    // slot's pixel size to be predictable from its header w/h alone.
    if (s_sb_locked_w == 0 && s_sb_locked_h == 0) {
        s_sb_locked_w = w; s_sb_locked_h = h;
    } else if (s_sb_locked_w != w || s_sb_locked_h != h) {
        for (int i = 0; i < BP_SB_CAPACITY; i++) {
            free(s_sb_pixels[i]);
            s_sb_pixels[i] = NULL;
            s_sb_pixels_cap[i] = 0;
            s_sb_pixels_len[i] = 0;
            memset(s_sb_header[i], 0, BP_SB_HEADER_BYTES);
        }
        s_sb_newest = -1;
        s_sb_count = 0;
        s_sb_next = 0;
        s_sb_locked_w = w; s_sb_locked_h = h;
    }

    int slot = s_sb_next;
    if (s_sb_pixels[slot] == NULL || s_sb_pixels_cap[slot] < expected) {
        free(s_sb_pixels[slot]);
        s_sb_pixels[slot] = (unsigned char*)malloc((size_t)expected);
        s_sb_pixels_cap[slot] = s_sb_pixels[slot] ? expected : 0;
    }
    if (s_sb_pixels[slot]) {
        memcpy(s_sb_pixels[slot], bytes, (size_t)expected);
        s_sb_pixels_len[slot] = expected;

        unsigned char* hdr = s_sb_header[slot];
        bp_sb_write_le_u64(hdr + 0,  (uint64_t)tsMs);
        bp_sb_write_le_float(hdr + 8,  x);
        bp_sb_write_le_float(hdr + 12, y);
        bp_sb_write_le_u32(hdr + 16, (uint32_t)screenW);
        bp_sb_write_le_u32(hdr + 20, (uint32_t)screenH);
        bp_sb_write_le_u32(hdr + 24, (uint32_t)w);
        bp_sb_write_le_u32(hdr + 28, (uint32_t)h);
        bp_sb_write_le_u32(hdr + 32, (uint32_t)expected);
        bp_sb_write_fixed_utf8(hdr + 36,                                     path,  BP_SB_PATH_LEN);
        bp_sb_write_fixed_utf8(hdr + 36 + BP_SB_PATH_LEN,                    label, BP_SB_LABEL_LEN);
        bp_sb_write_fixed_utf8(hdr + 36 + BP_SB_PATH_LEN + BP_SB_LABEL_LEN,  scene, BP_SB_SCENE_LEN);

        s_sb_newest = slot;
        s_sb_next = (slot + 1) % BP_SB_CAPACITY;
        if (s_sb_count < BP_SB_CAPACITY) s_sb_count++;
    }

    pthread_mutex_unlock(&s_sb_mutex);
}

#pragma mark - Async-signal-safe getters (read-only access from crash handler)

extern "C" int  bp_storyboard_capacity(void) { return BP_SB_CAPACITY; }
extern "C" int  bp_storyboard_header_bytes(void) { return BP_SB_HEADER_BYTES; }
extern "C" int  bp_storyboard_count(void)   { return s_sb_count; }
extern "C" int  bp_storyboard_newest(void)  { return s_sb_newest; }

extern "C" const unsigned char* bp_storyboard_header_ptr(int slot) {
    if (slot < 0 || slot >= BP_SB_CAPACITY) return NULL;
    return s_sb_header[slot];
}

extern "C" const unsigned char* bp_storyboard_pixels_ptr(int slot, int* len_out) {
    if (slot < 0 || slot >= BP_SB_CAPACITY) {
        if (len_out) *len_out = 0;
        return NULL;
    }
    if (len_out) *len_out = s_sb_pixels_len[slot];
    return s_sb_pixels[slot];
}

#pragma mark - Live-Obj-C helpers (ANR / report paths)

/// True when the ring has at least one pushed frame. Live-Obj-C only — signal
/// handlers should read s_sb_count directly via bp_storyboard_count().
extern "C" bool BPStoryboard_HasNewest(void) {
    return s_sb_newest >= 0 && s_sb_count > 0;
}

/// Epoch-ms timestamp of the newest pushed frame, or 0 if the ring is empty.
extern "C" long long BPStoryboard_NewestTimestampMs(void) {
    int slot = s_sb_newest;
    if (slot < 0 || s_sb_count == 0) return 0;
    const unsigned char* h = s_sb_header[slot];
    uint64_t v =  ((uint64_t)h[0])
               | ((uint64_t)h[1] << 8)
               | ((uint64_t)h[2] << 16)
               | ((uint64_t)h[3] << 24)
               | ((uint64_t)h[4] << 32)
               | ((uint64_t)h[5] << 40)
               | ((uint64_t)h[6] << 48)
               | ((uint64_t)h[7] << 56);
    return (long long)v;
}

/// Encode the newest frame to a JPEG at outputPath. Used by the ANR watchdog
/// and the live-Obj-C report path in lieu of the old 1 Hz rolling buffer.
/// Returns true on success.
extern "C" bool BPStoryboard_WriteNewestJpegTo(const char* outputPathC, int quality) {
    if (!outputPathC || !*outputPathC) return false;

    pthread_mutex_lock(&s_sb_mutex);
    int slot = s_sb_newest;
    if (slot < 0 || s_sb_count == 0 || !s_sb_pixels[slot]) {
        pthread_mutex_unlock(&s_sb_mutex);
        return false;
    }
    const unsigned char* h = s_sb_header[slot];
    int w = (int)((uint32_t)h[24] | ((uint32_t)h[25] << 8)
              | ((uint32_t)h[26] << 16) | ((uint32_t)h[27] << 24));
    int hi = (int)((uint32_t)h[28] | ((uint32_t)h[29] << 8)
              | ((uint32_t)h[30] << 16) | ((uint32_t)h[31] << 24));
    int len = s_sb_pixels_len[slot];
    if (w <= 0 || hi <= 0 || len < w * hi * 4) {
        pthread_mutex_unlock(&s_sb_mutex);
        return false;
    }
    NSData* pixelData = [NSData dataWithBytes:s_sb_pixels[slot] length:len];
    pthread_mutex_unlock(&s_sb_mutex);

    @autoreleasepool {
        // CGBitmapContext expects RGBA byte order on little-endian + premultiplied
        // alpha. Unity AsyncGPUReadback delivers straight RGBA8888.
        CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
        CGBitmapInfo info = (CGBitmapInfo)(kCGImageAlphaPremultipliedLast
            | kCGBitmapByteOrder32Big);
        CGContextRef ctx = CGBitmapContextCreate((void*)pixelData.bytes, w, hi, 8,
            (size_t)w * 4, cs, info);
        CGColorSpaceRelease(cs);
        if (!ctx) return false;
        CGImageRef img = CGBitmapContextCreateImage(ctx);
        CGContextRelease(ctx);
        if (!img) return false;
        UIImage* ui = [UIImage imageWithCGImage:img];
        CGImageRelease(img);

        CGFloat q = (CGFloat)(quality < 1 ? 1 : (quality > 100 ? 100 : quality)) / 100.0;
        NSData* jpeg = UIImageJPEGRepresentation(ui, q);
        if (!jpeg) return false;
        NSString* path = [NSString stringWithUTF8String:outputPathC];
        return [jpeg writeToFile:path atomically:YES];
    }
}
