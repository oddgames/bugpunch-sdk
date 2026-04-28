/*
 * BugpunchCrashHandler.mm — Native crash handler for iOS
 *
 * Catches POSIX signals (SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL) and
 * Mach exceptions. Writes a crash report to disk with a stack trace.
 *
 * On next launch, the C# bridge detects pending .crash files and uploads them.
 *
 * Design principles:
 * - Signal handlers are async-signal-safe (no ObjC, no malloc, only write/open/close)
 * - Mach exception handler runs on a dedicated thread (can use more APIs)
 * - Previous signal handlers are chained so we don't conflict with Crashlytics/Sentry
 * - Stack traces captured via backtrace() in the Mach handler, manual frame walk in signal handler
 */

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import "BugpunchLogReader.h"
#include <signal.h>
#include <unistd.h>
#include <fcntl.h>
#include <string.h>
#include <time.h>
#include <execinfo.h>
#include <sys/types.h>
#include <sys/sysctl.h>
#include <mach/mach.h>
#include <pthread.h>
#include <mach-o/dyld.h>
#include <mach-o/loader.h>

#define MAX_PATH 512
#define MAX_FRAMES 64
#define MAX_METADATA 256
// Mirrors the Android module table. Each entry is a loaded image with its
// Mach-O LC_UUID build-id — the same identifier the server's symbol store
// uses as primary key (see /api/symbols/check).
#define MAX_MODULES 128
#define MAX_UUID_HEX 33  /* 16-byte Mach-O UUID → 32 hex + null */

struct bp_module {
    uintptr_t load_addr;        /* header address in memory, matches the frame PCs */
    char uuid[MAX_UUID_HEX];    /* hex-encoded LC_UUID, or "" if none */
    char path[MAX_PATH];        /* dyld image name */
};
static struct bp_module s_modules[MAX_MODULES];
static int s_module_count = 0;

#pragma mark - Static state

static char s_crash_dir[MAX_PATH] = {0};
static char s_app_version[MAX_METADATA] = {0};
static char s_bundle_id[MAX_METADATA] = {0};
static char s_unity_version[MAX_METADATA] = {0};
static char s_device_model[MAX_METADATA] = {0};
static char s_os_version[MAX_METADATA] = {0};
static char s_gpu_name[MAX_METADATA] = {0};

// Active log_session id at crash time. Pushed in by BugpunchTunnel whenever
// the runtime rotates sSessionId. The next-launch drain sends the dumped log
// ring's tail to /api/v1/log-sessions/<id>/append-crash-tail so the Logs page
// shows everything up to the moment of death.
static char s_session_id[MAX_METADATA] = {0};

// Previous signal handlers for chaining
static struct sigaction s_old_handlers[32];
static const int s_signals[] = { SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL };
static const int s_num_signals = sizeof(s_signals) / sizeof(s_signals[0]);

static volatile int s_handling_crash = 0;

// Mach exception handler
static mach_port_t s_exception_port = MACH_PORT_NULL;
static pthread_t s_mach_thread;
static volatile bool s_mach_running = false;

#pragma mark - Async-signal-safe write helpers

static void safe_write_str(int fd, const char* s) {
    if (!s) return;
    int len = 0;
    while (s[len] && len < 4096) len++;
    write(fd, s, len);
}

static void safe_write_u64(int fd, unsigned long long val) {
    char buf[21];
    int i = 20;
    buf[i] = '\0';
    if (val == 0) { write(fd, "0", 1); return; }
    while (val > 0 && i > 0) {
        buf[--i] = '0' + (char)(val % 10);
        val /= 10;
    }
    write(fd, buf + i, 20 - i);
}

static void safe_write_hex(int fd, uintptr_t val) {
    static const char hex[] = "0123456789abcdef";
    char buf[18];
    buf[0] = '0'; buf[1] = 'x';
    for (int i = 0; i < 16; i++) {
        buf[17 - i] = hex[val & 0xf];
        val >>= 4;
    }
    int start = 2;
    while (start < 17 && buf[start] == '0') start++;
    write(fd, buf, 2);
    write(fd, buf + start, 18 - start);
}

static const char* signal_name(int sig) {
    switch (sig) {
        case SIGABRT: return "SIGABRT";
        case SIGSEGV: return "SIGSEGV";
        case SIGBUS:  return "SIGBUS";
        case SIGFPE:  return "SIGFPE";
        case SIGILL:  return "SIGILL";
        default:      return "UNKNOWN";
    }
}

static int build_crash_path(char* path, int maxlen, const char* prefix) {
    int pi = 0;
    const char* d = s_crash_dir;
    while (*d && pi < maxlen - 64) path[pi++] = *d++;
    path[pi++] = '/';
    while (*prefix) path[pi++] = *prefix++;

    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    unsigned long long ms = (unsigned long long)ts.tv_sec * 1000ULL +
                            (unsigned long long)ts.tv_nsec / 1000000ULL;
    char numbuf[21];
    int ni = 20; numbuf[ni] = '\0';
    if (ms == 0) numbuf[--ni] = '0';
    while (ms > 0 && ni > 0) { numbuf[--ni] = '0' + (char)(ms % 10); ms /= 10; }
    while (ni <= 20 && numbuf[ni]) path[pi++] = numbuf[ni++];

    const char* ext = ".crash";
    while (*ext) path[pi++] = *ext++;
    path[pi] = '\0';
    return pi;
}

#pragma mark - Module table capture (LC_UUID per dyld image)

// Scan a Mach-O header's load commands for LC_UUID, hex-encode the 16-byte
// UUID into `out`. Returns 1 on success.
static int read_macho_uuid(const struct mach_header* hdr, char* out, int out_size) {
    if (!hdr) { out[0] = '\0'; return 0; }
    int is64 = (hdr->magic == MH_MAGIC_64 || hdr->magic == MH_CIGAM_64);
    if (!is64 && hdr->magic != MH_MAGIC && hdr->magic != MH_CIGAM) {
        out[0] = '\0'; return 0;
    }
    const uint8_t* cmds = (const uint8_t*)hdr +
        (is64 ? sizeof(struct mach_header_64) : sizeof(struct mach_header));
    uint32_t ncmds = hdr->ncmds;
    for (uint32_t i = 0; i < ncmds; i++) {
        const struct load_command* lc = (const struct load_command*)cmds;
        if (lc->cmd == LC_UUID && out_size >= 33) {
            const struct uuid_command* uc = (const struct uuid_command*)lc;
            static const char hex[] = "0123456789abcdef";
            for (int b = 0; b < 16; b++) {
                out[b * 2]     = hex[(uc->uuid[b] >> 4) & 0xf];
                out[b * 2 + 1] = hex[uc->uuid[b] & 0xf];
            }
            out[32] = '\0';
            return 1;
        }
        cmds += lc->cmdsize;
    }
    out[0] = '\0';
    return 0;
}

// Populate the module table from dyld. Called once at install time —
// _dyld_* is not strictly async-signal-safe, so we never call it from the
// signal handler. Late-loaded images (after Bugpunch init) are missed;
// same tradeoff as Android.
static void capture_modules(void) {
    uint32_t n = _dyld_image_count();
    s_module_count = 0;
    for (uint32_t i = 0; i < n && s_module_count < MAX_MODULES; i++) {
        const struct mach_header* hdr = _dyld_get_image_header(i);
        const char* name = _dyld_get_image_name(i);
        if (!hdr) continue;

        struct bp_module* m = &s_modules[s_module_count];
        m->load_addr = (uintptr_t)hdr;
        const char* p = name ? name : "";
        int j = 0;
        while (p[j] && j < MAX_PATH - 1) { m->path[j] = p[j]; j++; }
        m->path[j] = '\0';
        read_macho_uuid(hdr, m->uuid, sizeof(m->uuid));
        s_module_count++;
    }
}

// Write the ---BUILD_IDS--- section consumed by server/crashSymbolicator.
// Format matches bp.c exactly: "<load_addr_hex> <uuid|-> <path|->".
static void write_build_ids(int fd) {
    safe_write_str(fd, "---BUILD_IDS---\n");
    for (int i = 0; i < s_module_count; i++) {
        safe_write_hex(fd, s_modules[i].load_addr);
        safe_write_str(fd, " ");
        safe_write_str(fd, s_modules[i].uuid[0] ? s_modules[i].uuid : "-");
        safe_write_str(fd, " ");
        safe_write_str(fd, s_modules[i].path[0] ? s_modules[i].path : "-");
        safe_write_str(fd, "\n");
    }
    safe_write_str(fd, "---END_BUILD_IDS---\n");
}

static void write_metadata(int fd) {
    safe_write_str(fd, "platform:iOS\n");
    safe_write_str(fd, "app_version:"); safe_write_str(fd, s_app_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "bundle_id:"); safe_write_str(fd, s_bundle_id); safe_write_str(fd, "\n");
    safe_write_str(fd, "unity_version:"); safe_write_str(fd, s_unity_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "device_model:"); safe_write_str(fd, s_device_model); safe_write_str(fd, "\n");
    safe_write_str(fd, "os_version:"); safe_write_str(fd, s_os_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "gpu:"); safe_write_str(fd, s_gpu_name); safe_write_str(fd, "\n");
    safe_write_str(fd, "session_id:"); safe_write_str(fd, s_session_id); safe_write_str(fd, "\n");
}

#pragma mark - Storyboard signal-handler dump

// Storyboard ring base path — same convention as Android: dump files land at
// `<base>_<i>.rgba` per slot, `<base>_at_crash.rgba` for the newest, and
// `<base>.bin` for the packed headers. Set by Bugpunch_SetStoryboardPathBase
// after the runtime resolves s_crash_dir at init.
static char s_sb_path_base[MAX_PATH] = {0};

// Async-signal-safe accessors implemented in BugpunchStoryboard.mm.
extern "C" int  bp_storyboard_capacity(void);
extern "C" int  bp_storyboard_header_bytes(void);
extern "C" int  bp_storyboard_count(void);
extern "C" int  bp_storyboard_newest(void);
extern "C" const unsigned char* bp_storyboard_header_ptr(int slot);
extern "C" const unsigned char* bp_storyboard_pixels_ptr(int slot, int* len_out);

// Append a small unsigned int (0..99) as decimal to dst. Async-signal-safe.
static int append_decimal(char* dst, int pos, int max, int v) {
    if (v >= 10 && pos < max - 1) {
        dst[pos++] = (char)('0' + v / 10);
        v %= 10;
    }
    if (pos < max - 1) dst[pos++] = (char)('0' + v);
    return pos;
}

// Append a literal C string. Async-signal-safe.
static int append_str(char* dst, int pos, int max, const char* s) {
    while (*s && pos < max - 1) dst[pos++] = *s++;
    return pos;
}

// Walk the storyboard ring and dump per-slot raw RGBA + packed headers, plus
// the newest slot duplicated as `_at_crash.rgba` (used as `screenshot:`). Same
// shape Android writes — drain is shared by both platforms.
static void write_storyboard(int fd) {
    int count = bp_storyboard_count();
    int newest = bp_storyboard_newest();
    int cap = bp_storyboard_capacity();
    int hdr_bytes = bp_storyboard_header_bytes();
    if (count <= 0 || newest < 0 || cap <= 0 || hdr_bytes <= 0
        || s_sb_path_base[0] == 0) return;
    if (count > cap) count = cap;
    int start = (newest - count + 1 + cap) % cap;

    char hdr_path[MAX_PATH];
    int hp = append_str(hdr_path, 0, MAX_PATH, s_sb_path_base);
    hp = append_str(hdr_path, hp, MAX_PATH, ".bin");
    hdr_path[hp] = '\0';
    int hfd = open(hdr_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);

    for (int i = 0; i < count; i++) {
        int slot = (start + i) % cap;
        const unsigned char* hdr_addr = bp_storyboard_header_ptr(slot);
        int pix_len = 0;
        const unsigned char* pix_addr = bp_storyboard_pixels_ptr(slot, &pix_len);
        if (!hdr_addr || !pix_addr || pix_len <= 0) continue;

        // Per-slot RGBA dump file: `<base>_<i>.rgba`.
        char p_path[MAX_PATH];
        int pp = append_str(p_path, 0, MAX_PATH, s_sb_path_base);
        if (pp < MAX_PATH - 1) p_path[pp++] = '_';
        pp = append_decimal(p_path, pp, MAX_PATH, i);
        pp = append_str(p_path, pp, MAX_PATH, ".rgba");
        p_path[pp] = '\0';

        int pfd = open(p_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (pfd >= 0) {
            const char* p = (const char*)pix_addr;
            size_t left = (size_t)pix_len;
            while (left > 0) {
                ssize_t n = write(pfd, p, left);
                if (n <= 0) break;
                p += n; left -= (size_t)n;
            }
            close(pfd);

            if (slot == newest) {
                // Duplicate the newest slot as `<base>_at_crash.rgba` so the
                // existing `screenshot:` consumer can encode it without
                // racing the storyboard parser (which deletes its sources).
                int w = (int)((uint32_t)hdr_addr[24] | ((uint32_t)hdr_addr[25] << 8)
                          | ((uint32_t)hdr_addr[26] << 16) | ((uint32_t)hdr_addr[27] << 24));
                int hi = (int)((uint32_t)hdr_addr[28] | ((uint32_t)hdr_addr[29] << 8)
                          | ((uint32_t)hdr_addr[30] << 16) | ((uint32_t)hdr_addr[31] << 24));

                char at_path[MAX_PATH];
                int ap = append_str(at_path, 0, MAX_PATH, s_sb_path_base);
                ap = append_str(at_path, ap, MAX_PATH, "_at_crash.rgba");
                at_path[ap] = '\0';
                int afd = open(at_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
                if (afd >= 0) {
                    const char* p2 = (const char*)pix_addr;
                    size_t left2 = (size_t)pix_len;
                    while (left2 > 0) {
                        ssize_t n = write(afd, p2, left2);
                        if (n <= 0) break;
                        p2 += n; left2 -= (size_t)n;
                    }
                    close(afd);
                    safe_write_str(fd, "screenshot:"); safe_write_str(fd, at_path); safe_write_str(fd, "\n");
                    safe_write_str(fd, "frame_w:"); safe_write_u64(fd, (unsigned long long)w); safe_write_str(fd, "\n");
                    safe_write_str(fd, "frame_h:"); safe_write_u64(fd, (unsigned long long)hi); safe_write_str(fd, "\n");
                    safe_write_str(fd, "frame_format:rgba8888\n");
                }
            }
        }

        if (hfd >= 0) {
            const char* hp2 = (const char*)hdr_addr;
            size_t left = (size_t)hdr_bytes;
            while (left > 0) {
                ssize_t n = write(hfd, hp2, left);
                if (n <= 0) break;
                hp2 += n; left -= (size_t)n;
            }
        }
    }
    if (hfd >= 0) close(hfd);

    safe_write_str(fd, "storyboard_path_base:"); safe_write_str(fd, s_sb_path_base); safe_write_str(fd, "\n");
    safe_write_str(fd, "storyboard_count:"); safe_write_u64(fd, (unsigned long long)count); safe_write_str(fd, "\n");
    safe_write_str(fd, "storyboard_header_bytes:"); safe_write_u64(fd, (unsigned long long)hdr_bytes); safe_write_str(fd, "\n");
}

extern "C" void Bugpunch_SetStoryboardPathBase(const char* pathBase) {
    if (!pathBase) { s_sb_path_base[0] = '\0'; return; }
    int i; for (i = 0; pathBase[i] && i < MAX_PATH - 1; i++) s_sb_path_base[i] = pathBase[i];
    s_sb_path_base[i] = '\0';
}

#pragma mark - Signal handler (async-signal-safe)

static void crash_signal_handler(int sig, siginfo_t* info, void* ucontext) {
    if (__sync_val_compare_and_swap(&s_handling_crash, 0, 1) != 0) {
        // Re-entry — chain to previous
        if (s_old_handlers[sig].sa_sigaction) {
            s_old_handlers[sig].sa_sigaction(sig, info, ucontext);
        }
        _exit(128 + sig);
        return;
    }

    char path[MAX_PATH + 64];
    build_crash_path(path, sizeof(path), "signal_");

    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) goto chain;

    safe_write_str(fd, "BUGPUNCH_CRASH_V1\n");
    safe_write_str(fd, "type:NATIVE_SIGNAL\n");
    safe_write_str(fd, "signal:"); safe_write_str(fd, signal_name(sig)); safe_write_str(fd, "\n");
    safe_write_str(fd, "signal_code:"); safe_write_u64(fd, info ? info->si_code : 0); safe_write_str(fd, "\n");
    safe_write_str(fd, "fault_addr:"); safe_write_hex(fd, (uintptr_t)(info ? info->si_addr : 0)); safe_write_str(fd, "\n");
    safe_write_str(fd, "pid:"); safe_write_u64(fd, getpid()); safe_write_str(fd, "\n");

    {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        safe_write_str(fd, "timestamp:");
        safe_write_u64(fd, (unsigned long long)ts.tv_sec);
        safe_write_str(fd, "\n");
    }

    write_metadata(fd);
    write_storyboard(fd);

    // Stack trace — backtrace() is technically not async-signal-safe on all
    // platforms, but works reliably on iOS/ARM64 in practice
    safe_write_str(fd, "---STACK---\n");
    {
        void* frames[MAX_FRAMES];
        int count = backtrace(frames, MAX_FRAMES);
        for (int i = 0; i < count; i++) {
            safe_write_hex(fd, (uintptr_t)frames[i]);
            safe_write_str(fd, "\n");
        }
    }
    safe_write_str(fd, "---END---\n");

    write_build_ids(fd);

    close(fd);

chain:
    // Chain to previous handler
    {
        struct sigaction* old = &s_old_handlers[sig];
        if (old->sa_flags & SA_SIGINFO) {
            if (old->sa_sigaction) old->sa_sigaction(sig, info, ucontext);
        } else {
            if (old->sa_handler && old->sa_handler != SIG_DFL && old->sa_handler != SIG_IGN) {
                old->sa_handler(sig);
            }
        }
    }

    // Re-raise with default handler
    {
        struct sigaction sa;
        memset(&sa, 0, sizeof(sa));
        sa.sa_handler = SIG_DFL;
        sigaction(sig, &sa, NULL);
        raise(sig);
    }
}

#pragma mark - Mach exception handler

static void* mach_exception_thread(void* arg) {
    while (s_mach_running) {
        // Receive exception message
        struct {
            mach_msg_header_t head;
            mach_msg_body_t body;
            mach_msg_port_descriptor_t thread;
            mach_msg_port_descriptor_t task;
            NDR_record_t ndr;
            exception_type_t exception;
            mach_msg_type_number_t code_count;
            mach_exception_data_type_t code[2];
            mach_msg_trailer_t trailer;
        } msg;

        kern_return_t kr = mach_msg(&msg.head, MACH_RCV_MSG | MACH_RCV_LARGE,
                                     0, sizeof(msg), s_exception_port,
                                     MACH_MSG_TIMEOUT_NONE, MACH_PORT_NULL);
        if (kr != KERN_SUCCESS) {
            if (s_mach_running) {
                NSLog(@"[BugpunchCrash] mach_msg receive failed: %d", kr);
            }
            continue;
        }

        if (!s_mach_running) break;

        // We're on a dedicated thread (not in a signal handler), so we can use
        // higher-level APIs here: NSString, backtrace_symbols, etc.
        NSLog(@"[BugpunchCrash] Mach exception: type=%d code[0]=%lld code[1]=%lld",
              msg.exception, (long long)msg.code[0], (long long)msg.code[1]);

        @autoreleasepool {
            NSString* crashDir = [NSString stringWithUTF8String:s_crash_dir];
            NSString* filename = [NSString stringWithFormat:@"mach_%llu.crash",
                (unsigned long long)([[NSDate date] timeIntervalSince1970] * 1000)];
            NSString* path = [crashDir stringByAppendingPathComponent:filename];

            NSMutableString* report = [NSMutableString string];
            [report appendString:@"BUGPUNCH_CRASH_V1\n"];
            [report appendFormat:@"type:MACH_EXCEPTION\n"];
            [report appendFormat:@"exception_type:%d\n", msg.exception];
            [report appendFormat:@"exception_code:%lld\n", (long long)msg.code[0]];
            [report appendFormat:@"exception_subcode:%lld\n", (long long)msg.code[1]];
            [report appendFormat:@"timestamp:%llu\n",
                (unsigned long long)[[NSDate date] timeIntervalSince1970]];

            [report appendFormat:@"platform:iOS\n"];
            [report appendFormat:@"app_version:%s\n", s_app_version];
            [report appendFormat:@"bundle_id:%s\n", s_bundle_id];
            [report appendFormat:@"unity_version:%s\n", s_unity_version];
            [report appendFormat:@"device_model:%s\n", s_device_model];
            [report appendFormat:@"os_version:%s\n", s_os_version];
            [report appendFormat:@"gpu:%s\n", s_gpu_name];
            [report appendFormat:@"session_id:%s\n", s_session_id];

            // Storyboard ring rescue. Mach thread isn't a signal handler so we
            // could use Obj-C, but the file dumps are pure POSIX in both paths
            // — reuse the signal-handler helper by writing into a tmp FD then
            // splicing the output into the report before the ---STACK--- block.
            // Cheaper to just call the helper twice (file dumps + emit lines)
            // through a memfd-style buffer; iOS lacks memfd_create so we open
            // a small temp file, write metadata to it, read it back, then
            // unlink. Acceptable cost — Mach crashes are rare.
            if (s_sb_path_base[0] && bp_storyboard_count() > 0) {
                NSString* tmpMetaPath = [NSTemporaryDirectory()
                    stringByAppendingPathComponent:@"bp_sb_mach_meta.tmp"];
                int mfd = open([tmpMetaPath fileSystemRepresentation],
                    O_WRONLY | O_CREAT | O_TRUNC, 0644);
                if (mfd >= 0) {
                    write_storyboard(mfd);
                    close(mfd);
                    NSData* metaData = [NSData dataWithContentsOfFile:tmpMetaPath];
                    if (metaData.length > 0) {
                        NSString* meta = [[NSString alloc] initWithData:metaData
                            encoding:NSUTF8StringEncoding];
                        if (meta) [report appendString:meta];
                    }
                    [[NSFileManager defaultManager] removeItemAtPath:tmpMetaPath error:nil];
                }
            }

            // Get stack trace of the crashing thread
            // Hex-only frames — the server's crashSymbolicator regex matches
            // `^0x[0-9a-f]+$` per line, so a backtrace_symbols-formatted
            // line would be silently skipped. dSYM resolution on the server
            // produces better symbols than backtrace_symbols anyway.
            [report appendString:@"---STACK---\n"];
            void* frames[MAX_FRAMES];
            int count = backtrace(frames, MAX_FRAMES);
            for (int i = 0; i < count; i++) {
                [report appendFormat:@"0x%lx\n", (unsigned long)frames[i]];
            }
            [report appendString:@"---END---\n"];

            // Build-ID table — identical format to the signal-handler path,
            // identical format to Android's bp.c. Server symbolicator parses
            // one set of rules for all three.
            [report appendString:@"---BUILD_IDS---\n"];
            for (int i = 0; i < s_module_count; i++) {
                [report appendFormat:@"0x%lx %s %s\n",
                    (unsigned long)s_modules[i].load_addr,
                    s_modules[i].uuid[0] ? s_modules[i].uuid : "-",
                    s_modules[i].path[0] ? s_modules[i].path : "-"];
            }
            [report appendString:@"---END_BUILD_IDS---\n"];

            // Write to disk
            NSError* error = nil;
            [report writeToFile:path atomically:YES encoding:NSUTF8StringEncoding error:&error];
            if (error) {
                NSLog(@"[BugpunchCrash] Failed to write Mach crash report: %@", error);
            } else {
                NSLog(@"[BugpunchCrash] Mach crash report written: %@", path);
            }
        }

        // Don't forward — let the default behavior (signal) handle the actual crash.
        // The signal handler will also fire and chain properly.
    }
    return NULL;
}

#pragma mark - ANR Watchdog

// ANR detection thread — mirrors Android's AnrWatchdog. The main thread
// calls Bugpunch_TickAnrWatchdog() every ~1 second (via the CADisplayLink
// frame callback); if that stops arriving within the timeout, an ANR is
// declared and a crash report is written.
//
// iOS screenshot strategy: drawViewHierarchyInRect requires the main thread,
// and during ANR the main thread is stuck. iOS has no equivalent to Android's
// PixelCopy (which reads the GPU compositor buffer from any thread). We use
// a pre-capture rolling buffer: every ~1s the main thread captures a
// half-resolution screenshot and stores it. When the ANR fires, the watchdog
// thread reads the latest pre-captured frame. It's at most ~1s stale, but
// during ANR the screen is frozen anyway so it's accurate.

// ── Pre-capture screenshot ring buffer ──
#define ANR_SHOT_BUFFER_SIZE 4
static NSData* s_anr_shot_buffer[ANR_SHOT_BUFFER_SIZE];
static int     s_anr_shot_index = 0;
static NSObject* s_anr_shot_lock;
static dispatch_once_t s_anr_shot_once;

static void bp_anr_shot_init(void) {
    dispatch_once(&s_anr_shot_once, ^{
        s_anr_shot_lock = [NSObject new];
    });
}

// Called from the main thread (CADisplayLink tick). Captures a half-resolution
// screenshot and stores it in the ring buffer. ~2-4ms on modern iPhones.
static void bp_anr_precapture(void) {
    bp_anr_shot_init();
    @autoreleasepool {
        UIWindow* window = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
                if (scene.activationState != UISceneActivationStateForegroundActive) continue;
                if (![scene isKindOfClass:[UIWindowScene class]]) continue;
                UIWindowScene* ws = (UIWindowScene*)scene;
                for (UIWindow* w in ws.windows) {
                    if (w.isKeyWindow) { window = w; break; }
                }
                if (window) break;
                if (ws.windows.count > 0) window = ws.windows.firstObject;
            }
        }
        if (!window) {
            #pragma clang diagnostic push
            #pragma clang diagnostic ignored "-Wdeprecated-declarations"
            window = UIApplication.sharedApplication.keyWindow;
            #pragma clang diagnostic pop
        }
        if (!window || window.bounds.size.width <= 0) return;

        // Half scale for performance — ANR screenshots are diagnostic, not pixel-perfect.
        UIGraphicsImageRendererFormat* fmt = [UIGraphicsImageRendererFormat defaultFormat];
        fmt.opaque = YES;
        fmt.scale = window.screen.scale * 0.5;
        UIGraphicsImageRenderer* renderer =
            [[UIGraphicsImageRenderer alloc] initWithSize:window.bounds.size format:fmt];

        UIImage* img = [renderer imageWithActions:^(UIGraphicsImageRendererContext* ctx) {
            [window drawViewHierarchyInRect:window.bounds afterScreenUpdates:NO];
        }];
        NSData* jpeg = UIImageJPEGRepresentation(img, 0.5);
        if (!jpeg || jpeg.length == 0) return;

        @synchronized (s_anr_shot_lock) {
            s_anr_shot_buffer[s_anr_shot_index] = jpeg;
            s_anr_shot_index = (s_anr_shot_index + 1) % ANR_SHOT_BUFFER_SIZE;
        }
    }
}

// Called from the ANR watchdog background thread. Returns the latest
// pre-captured JPEG data, or nil if none available.
static NSData* bp_anr_get_latest_shot(void) {
    bp_anr_shot_init();
    NSData* latest = nil;
    @synchronized (s_anr_shot_lock) {
        int idx = (s_anr_shot_index - 1 + ANR_SHOT_BUFFER_SIZE) % ANR_SHOT_BUFFER_SIZE;
        latest = s_anr_shot_buffer[idx];
    }
    return latest;
}

static volatile uint64_t s_anr_last_tick_ms = 0;
static volatile bool s_anr_running = false;
static pthread_t s_anr_thread;
static int s_anr_timeout_ms = 5000;
static const int64_t ANR_COOLDOWN_MS = 60000;
static volatile int64_t s_anr_last_fired_ms = 0;

static uint64_t bp_now_ms(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000ULL + (uint64_t)ts.tv_nsec / 1000000ULL;
}

static void write_anr_report(uint64_t elapsed_ms) {
    @autoreleasepool {
        NSString* crashDir = [NSString stringWithUTF8String:s_crash_dir];
        uint64_t tsMs = (uint64_t)([[NSDate date] timeIntervalSince1970] * 1000);
        NSString* filename = [NSString stringWithFormat:@"anr_%llu.crash", (unsigned long long)tsMs];
        NSString* path = [crashDir stringByAppendingPathComponent:filename];

        // 1 frame BEFORE (from Metal backbuffer — last rendered frame, ~0-16ms old)
        // + 2 frames AFTER (re-read backbuffer at 500ms intervals to detect
        // screen movement). Same pattern as Android.
        extern bool Bugpunch_WriteBackbufferJPEG(const char*, float);

        NSMutableArray<NSString*>* shotPaths = [NSMutableArray array];
        NSMutableArray<NSNumber*>* shotTimestamps = [NSMutableArray array];

        // Before-frame — the last frame rendered before the ANR.
        NSString* beforePath = [crashDir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"anr_%llu_before.jpg", (unsigned long long)tsMs]];
        if (Bugpunch_WriteBackbufferJPEG([beforePath UTF8String], 0.75f)) {
            [shotPaths addObject:beforePath];
            [shotTimestamps addObject:@(tsMs)];
        }

        // 2 after-frames — re-read backbuffer at 500ms intervals.
        for (int i = 0; i < 2; i++) {
            usleep(500000); // 500ms
            uint64_t afterTs = (uint64_t)([[NSDate date] timeIntervalSince1970] * 1000);
            NSString* afterPath = [crashDir stringByAppendingPathComponent:
                [NSString stringWithFormat:@"anr_%llu_after%d.jpg", (unsigned long long)afterTs, i]];
            if (Bugpunch_WriteBackbufferJPEG([afterPath UTF8String], 0.75f)) {
                [shotPaths addObject:afterPath];
                [shotTimestamps addObject:@(afterTs)];
            }
        }

        NSString* shotPath = shotPaths.count > 0 ? shotPaths[0] : nil;

        NSMutableString* report = [NSMutableString string];
        [report appendString:@"BUGPUNCH_CRASH_V1\n"];
        [report appendString:@"type:ANR\n"];
        [report appendFormat:@"timestamp:%llu\n", (unsigned long long)tsMs];
        [report appendFormat:@"elapsed_ms:%llu\n", (unsigned long long)elapsed_ms];
        [report appendString:@"thread:main\n"];
        if (shotPath) {
            [report appendFormat:@"screenshot:%@\n", shotPath];
        }
        for (NSUInteger i = 0; i < shotPaths.count; i++) {
            [report appendFormat:@"anr_screenshot_%lu:%@\n", (unsigned long)i, shotPaths[i]];
            [report appendFormat:@"anr_screenshot_ts_%lu:%@\n", (unsigned long)i, shotTimestamps[i]];
        }
        [report appendFormat:@"screenshot_count:%lu\n", (unsigned long)shotPaths.count];

        // Log snapshot. The signal handler can't touch BPLogReader (uses
        // @synchronized — not async-signal-safe), but the ANR watchdog runs
        // on a regular pthread with the process still alive, so we can pull
        // the ring directly. Crash drain reads `logs:` to attach the file.
        @try {
            NSString* logsText = [BPLogReader snapshotText];
            if (logsText.length > 0) {
                NSString* logsPath = [crashDir stringByAppendingPathComponent:
                    [NSString stringWithFormat:@"anr_%llu_logs.log", (unsigned long long)tsMs]];
                NSError* logErr = nil;
                if ([logsText writeToFile:logsPath atomically:YES
                        encoding:NSUTF8StringEncoding error:&logErr]) {
                    [report appendFormat:@"logs:%@\n", logsPath];
                } else {
                    NSLog(@"[BugpunchCrash] ANR log snapshot write failed: %@", logErr);
                }
            }
        } @catch (NSException* ex) {
            NSLog(@"[BugpunchCrash] ANR log snapshot exception: %@", ex);
        }

        [report appendFormat:@"platform:iOS\n"];
        [report appendFormat:@"app_version:%s\n", s_app_version];
        [report appendFormat:@"bundle_id:%s\n", s_bundle_id];
        [report appendFormat:@"unity_version:%s\n", s_unity_version];
        [report appendFormat:@"device_model:%s\n", s_device_model];
        [report appendFormat:@"os_version:%s\n", s_os_version];
        [report appendFormat:@"gpu:%s\n", s_gpu_name];
        [report appendFormat:@"session_id:%s\n", s_session_id];

        // Capture thread stacks via Mach thread API (works from any thread).
        [report appendString:@"---STACK---\n"];
        thread_act_array_t threads;
        mach_msg_type_number_t threadCount;
        if (task_threads(mach_task_self(), &threads, &threadCount) == KERN_SUCCESS) {
            for (mach_msg_type_number_t i = 0; i < threadCount; i++) {
                char name[64] = {0};
                pthread_t pt = pthread_from_mach_thread_np(threads[i]);
                if (pt) {
                    pthread_getname_np(pt, name, sizeof(name));
                }
                [report appendFormat:@"thread:%s id=%u state=",
                    name[0] ? name : "?", (unsigned)i];

                // Get thread basic info for run state
                thread_basic_info_data_t binfo;
                mach_msg_type_number_t binfoCount = THREAD_BASIC_INFO_COUNT;
                if (thread_info(threads[i], THREAD_BASIC_INFO,
                    (thread_info_t)&binfo, &binfoCount) == KERN_SUCCESS) {
                    switch (binfo.run_state) {
                        case TH_STATE_RUNNING:    [report appendString:@"RUNNING\n"]; break;
                        case TH_STATE_STOPPED:    [report appendString:@"STOPPED\n"]; break;
                        case TH_STATE_WAITING:    [report appendString:@"WAITING\n"]; break;
                        case TH_STATE_UNINTERRUPTIBLE: [report appendString:@"UNINTERRUPTIBLE\n"]; break;
                        case TH_STATE_HALTED:     [report appendString:@"HALTED\n"]; break;
                        default:                  [report appendFormat:@"%d\n", binfo.run_state]; break;
                    }
                } else {
                    [report appendString:@"?\n"];
                }
                mach_port_deallocate(mach_task_self(), threads[i]);
            }
            vm_deallocate(mach_task_self(), (vm_address_t)threads,
                threadCount * sizeof(thread_act_t));
        }

        // backtrace() on this watchdog thread — shows where the ANR logic ran.
        // (Can't get the main thread's backtrace portably without register walking.)
        [report appendString:@"\n--- ANR watchdog thread stack ---\n"];
        void* frames[MAX_FRAMES];
        int count = backtrace(frames, MAX_FRAMES);
        for (int i = 0; i < count; i++) {
            [report appendFormat:@"0x%lx\n", (unsigned long)frames[i]];
        }
        [report appendString:@"---END---\n"];

        NSError* error = nil;
        [report writeToFile:path atomically:YES encoding:NSUTF8StringEncoding error:&error];
        if (error) {
            NSLog(@"[BugpunchCrash] Failed to write ANR report: %@", error);
        } else {
            NSLog(@"[BugpunchCrash] ANR report written: %@ (elapsed %llums, screenshot=%@)",
                path, (unsigned long long)elapsed_ms, shotPath ? @"yes" : @"no");
        }
    }
}

static void* anr_watchdog_thread(void* arg) {
    pthread_setname_np("BugpunchANR");
    while (s_anr_running) {
        usleep((useconds_t)(s_anr_timeout_ms * 500));  // sleep half the timeout
        if (!s_anr_running) break;

        uint64_t now = bp_now_ms();
        uint64_t last = s_anr_last_tick_ms;
        if (last == 0) continue;  // not started yet

        uint64_t elapsed = now - last;
        if (elapsed > (uint64_t)s_anr_timeout_ms) {
            // Cooldown: one ANR per 60s
            if (now - (uint64_t)s_anr_last_fired_ms < ANR_COOLDOWN_MS) {
                s_anr_last_tick_ms = now;
                continue;
            }
            NSLog(@"[BugpunchCrash] ANR detected! Main thread unresponsive for %llums",
                (unsigned long long)elapsed);
            s_anr_last_fired_ms = (int64_t)now;
            write_anr_report(elapsed);
            s_anr_last_tick_ms = now;  // reset so we don't fire again immediately
        }
    }
    return NULL;
}

#pragma mark - C API (called from Unity C# via DllImport)

extern "C" {

void Bugpunch_StartAnrWatchdog(int timeoutMs) {
    if (s_anr_running) return;
    s_anr_timeout_ms = timeoutMs > 0 ? timeoutMs : 5000;
    s_anr_last_tick_ms = bp_now_ms();
    s_anr_running = true;
    pthread_create(&s_anr_thread, NULL, anr_watchdog_thread, NULL);
    NSLog(@"[BugpunchCrash] ANR watchdog started (timeout=%dms)", s_anr_timeout_ms);
}

void Bugpunch_StopAnrWatchdog(void) {
    if (!s_anr_running) return;
    s_anr_running = false;
    pthread_join(s_anr_thread, NULL);
    NSLog(@"[BugpunchCrash] ANR watchdog stopped");
}

void Bugpunch_TickAnrWatchdog(void) {
    s_anr_last_tick_ms = bp_now_ms();
    // The Metal backbuffer blit happens automatically on every presentDrawable
    // (swizzled in BugpunchBackbuffer.mm) — no explicit pre-capture needed here.
}

bool Bugpunch_InstallCrashHandlers(const char* crashDir) {
    if (!crashDir) return false;

    strncpy(s_crash_dir, crashDir, MAX_PATH - 1);
    s_crash_dir[MAX_PATH - 1] = '\0';

    // Create directory if needed
    NSString* dir = [NSString stringWithUTF8String:s_crash_dir];
    NSFileManager* fm = [NSFileManager defaultManager];
    if (![fm fileExistsAtPath:dir]) {
        NSError* error = nil;
        [fm createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:&error];
        if (error) {
            NSLog(@"[BugpunchCrash] Failed to create crash dir: %@", error);
            return false;
        }
    }

    // Install signal handlers (chain previous ones)
    struct sigaction sa;
    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = crash_signal_handler;
    sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&sa.sa_mask);

    bool ok = true;
    for (int i = 0; i < s_num_signals; i++) {
        if (sigaction(s_signals[i], &sa, &s_old_handlers[s_signals[i]]) != 0) {
            NSLog(@"[BugpunchCrash] Failed to install handler for signal %d", s_signals[i]);
            ok = false;
        }
    }

    // Set up alternate signal stack for stack overflow handling
    static char alt_stack[SIGSTKSZ * 2];
    stack_t ss;
    ss.ss_sp = alt_stack;
    ss.ss_size = sizeof(alt_stack);
    ss.ss_flags = 0;
    sigaltstack(&ss, NULL);

    // Install Mach exception handler
    kern_return_t kr = mach_port_allocate(mach_task_self(), MACH_PORT_RIGHT_RECEIVE, &s_exception_port);
    if (kr == KERN_SUCCESS) {
        kr = mach_port_insert_right(mach_task_self(), s_exception_port, s_exception_port,
                                     MACH_MSG_TYPE_MAKE_SEND);
        if (kr == KERN_SUCCESS) {
            // EXC_BAD_ACCESS covers SIGSEGV/SIGBUS, EXC_BAD_INSTRUCTION covers SIGILL,
            // EXC_ARITHMETIC covers SIGFPE, EXC_CRASH covers SIGABRT
            kr = task_set_exception_ports(mach_task_self(),
                EXC_MASK_BAD_ACCESS | EXC_MASK_BAD_INSTRUCTION |
                EXC_MASK_ARITHMETIC | EXC_MASK_CRASH,
                s_exception_port,
                EXCEPTION_DEFAULT | MACH_EXCEPTION_CODES,
                THREAD_STATE_NONE);

            if (kr == KERN_SUCCESS) {
                s_mach_running = true;
                pthread_create(&s_mach_thread, NULL, mach_exception_thread, NULL);
                NSLog(@"[BugpunchCrash] Mach exception handler installed");
            } else {
                NSLog(@"[BugpunchCrash] task_set_exception_ports failed: %d", kr);
            }
        }
    }

    // Snapshot loaded images so the signal handler can write a build-id
     // table without calling the non-signal-safe _dyld_* APIs.
    capture_modules();

    NSLog(@"[BugpunchCrash] Signal handlers installed (dir=%s, modules=%d)",
        s_crash_dir, s_module_count);

    // Drain anything left in the upload queue from previous launches.
    extern void Bugpunch_DrainUploadQueue(void);
    Bugpunch_DrainUploadQueue();

    return ok;
}

void Bugpunch_UninstallCrashHandlers(void) {
    // Restore signal handlers
    for (int i = 0; i < s_num_signals; i++) {
        sigaction(s_signals[i], &s_old_handlers[s_signals[i]], NULL);
    }

    // Stop Mach exception thread
    if (s_mach_running) {
        s_mach_running = false;
        // Send a dummy message to unblock the receive
        if (s_exception_port != MACH_PORT_NULL) {
            mach_msg_header_t msg;
            msg.msgh_bits = MACH_MSGH_BITS(MACH_MSG_TYPE_MAKE_SEND, 0);
            msg.msgh_size = sizeof(msg);
            msg.msgh_remote_port = s_exception_port;
            msg.msgh_local_port = MACH_PORT_NULL;
            msg.msgh_id = 0;
            mach_msg(&msg, MACH_SEND_MSG, sizeof(msg), 0, MACH_PORT_NULL,
                     MACH_MSG_TIMEOUT_NONE, MACH_PORT_NULL);
            pthread_join(s_mach_thread, NULL);
            mach_port_deallocate(mach_task_self(), s_exception_port);
            s_exception_port = MACH_PORT_NULL;
        }
    }

    NSLog(@"[BugpunchCrash] Crash handlers uninstalled");
}

void Bugpunch_SetCrashMetadata(const char* appVersion, const char* bundleId,
    const char* unityVersion, const char* deviceModel, const char* osVersion,
    const char* gpuName)
{
    if (appVersion) strncpy(s_app_version, appVersion, MAX_METADATA - 1);
    if (bundleId) strncpy(s_bundle_id, bundleId, MAX_METADATA - 1);
    if (unityVersion) strncpy(s_unity_version, unityVersion, MAX_METADATA - 1);
    if (deviceModel) strncpy(s_device_model, deviceModel, MAX_METADATA - 1);
    if (osVersion) strncpy(s_os_version, osVersion, MAX_METADATA - 1);
    if (gpuName) strncpy(s_gpu_name, gpuName, MAX_METADATA - 1);
}

// Push the active log_session id so the signal / Mach exception handler
// stamps it into the crash header. The next-launch drain reads it and POSTs
// the recovered log tail to /api/v1/log-sessions/<id>/append-crash-tail.
void Bugpunch_SetCrashSessionId(const char* sessionId)
{
    if (!sessionId) { s_session_id[0] = '\0'; return; }
    strncpy(s_session_id, sessionId, MAX_METADATA - 1);
    s_session_id[MAX_METADATA - 1] = '\0';
}

/// Returns a newline-separated list of pending crash file paths, or empty string.
const char* Bugpunch_GetPendingCrashFiles(void) {
    static char result[8192];
    result[0] = '\0';

    NSString* dir = [NSString stringWithUTF8String:s_crash_dir];
    NSFileManager* fm = [NSFileManager defaultManager];
    NSArray* files = [fm contentsOfDirectoryAtPath:dir error:nil];

    int offset = 0;
    for (NSString* file in files) {
        if (![file hasSuffix:@".crash"]) continue;
        NSString* fullPath = [dir stringByAppendingPathComponent:file];
        const char* cstr = [fullPath UTF8String];
        int len = (int)strlen(cstr);
        if (offset + len + 2 >= sizeof(result)) break;
        if (offset > 0) result[offset++] = '\n';
        memcpy(result + offset, cstr, len);
        offset += len;
    }
    result[offset] = '\0';
    return result;
}

/// Read a crash file and return its contents. Caller must not free.
const char* Bugpunch_ReadCrashFile(const char* path) {
    static char* s_buffer = NULL;
    if (s_buffer) { free(s_buffer); s_buffer = NULL; }

    NSString* nsPath = [NSString stringWithUTF8String:path];
    NSError* error = nil;
    NSString* contents = [NSString stringWithContentsOfFile:nsPath
                                                  encoding:NSUTF8StringEncoding
                                                     error:&error];
    if (!contents) return "";

    const char* cstr = [contents UTF8String];
    size_t len = strlen(cstr);
    s_buffer = (char*)malloc(len + 1);
    memcpy(s_buffer, cstr, len + 1);
    return s_buffer;
}

/// Delete a crash file after upload.
bool Bugpunch_DeleteCrashFile(const char* path) {
    NSString* nsPath = [NSString stringWithUTF8String:path];
    return [[NSFileManager defaultManager] removeItemAtPath:nsPath error:nil];
}

} // extern "C"
