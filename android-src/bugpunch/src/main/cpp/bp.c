/*
 * bp.c — Native signal handler for Android (NDK)
 *
 * Catches SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL and writes a crash report
 * to disk. All operations inside the signal handler are async-signal-safe:
 * only write(), open(), close(), _exit() — no malloc, no printf, no JNI.
 *
 * Built as libbugpunch_crash.so (OUTPUT_NAME override in CMakeLists.txt —
 * the short target/source names keep the CMake object-path under the 250-char
 * limit on deep Jenkins workspaces).
 */

#include <jni.h>
#include <signal.h>
#include <unistd.h>
#include <fcntl.h>
#include <string.h>
#include <time.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <android/log.h>
#include <link.h>       /* dl_iterate_phdr, ElfW, PT_NOTE */
#include <elf.h>        /* NT_GNU_BUILD_ID */

/* Optional: use <unwind.h> for stack trace on ARM/ARM64. */
#include <unwind.h>

#include "bp_video.h"

#define TAG "BugpunchCrash"
#define MAX_PATH 512
#define MAX_FRAMES 64
#define MAX_METADATA 256
/* Build-ID table — populated at init and re-snapshotted at crash time.
 * Real Unity processes on Android 14/15 load 150+ native libs before any
 * app code runs (system AIDL shims, a/v codecs, vendor HALs, Vulkan
 * loaders, etc.) plus Unity's own libunity/libil2cpp/libmain + AAR-side
 * .so + game native plugins. dl_iterate_phdr visits in load order, so a
 * small cap silently truncates the APP libs — exactly the ones we need
 * for symbolication. Bump generously; each entry is ~MAX_PATH+41 bytes. */
#define MAX_MODULES 512
#define MAX_BUILDID_HEX 41  /* 20-byte GNU build-id → 40 hex + null */

/* ── Static state (pre-allocated, no malloc in handler) ── */

static char s_crash_dir[MAX_PATH] = {0};

/* Metadata set by C# */
static char s_app_version[MAX_METADATA] = {0};
static char s_bundle_id[MAX_METADATA] = {0};
static char s_unity_version[MAX_METADATA] = {0};
static char s_device_model[MAX_METADATA] = {0};
static char s_os_version[MAX_METADATA] = {0};
static char s_gpu_name[MAX_METADATA] = {0};

/* Active log_session id at crash time. Pushed in by BugpunchTunnel whenever
 * BugpunchRuntime.sSessionId rotates (process start + each foreground after
 * a long background). The drain on the next launch sends the dumped log
 * ring's tail to /api/v1/log-sessions/<id>/append-crash-tail so the Logs
 * page shows everything up to the moment of death. */
static char s_session_id[MAX_METADATA] = {0};

/* Log snapshot rescue — native-memory pattern shared with the storyboard ring.
 * Java's logcat reader serializes the current ring into `s_logs_buf` each
 * flush, then sets `s_logs_len` to the valid-byte count. At crash time we
 * write [0..s_logs_len) to `s_logs_path`. No disk I/O during gameplay. */
static void* s_logs_buf = NULL;
static volatile int s_logs_len = 0;
static char s_logs_path[MAX_PATH] = {0};

/* Video ring path — recorded once at init via Java. The signal handler
 * doesn't touch video bytes (they're already in the kernel page cache from
 * normal operation); we just emit the path so drain knows where to look,
 * and call bp_video_finalize() to msync the ring header. */
static char s_video_path[MAX_PATH] = {0};

/* Input breadcrumb ring — captures what the user was pressing in the seconds
 * before the crash. Each entry is a fixed-size record so the signal handler
 * can dump the whole slab without parsing. Java owns allocation (direct
 * ByteBuffer) and writes entries in the C# Update() pipeline; bp.c reads
 * them via GetDirectBufferAddress.
 *
 * Wire format per entry (must match InputEvent struct in BugpunchInput.java):
 *   int64  timestampMs     // epoch millis
 *   int32  type            // 0=touchDown,1=touchUp,2=touchMove,3=keyDown,4=keyUp,5=sceneChange
 *   float  x, y            // screen coords (ignored for key/scene)
 *   int32  keyCode         // key events
 *   char   path[192]       // UI hierarchy "Canvas/Shop/Row[3]/BuyButton"
 *   char   scene[32]       // active Unity scene
 *   = 248 bytes per entry.
 *
 * Ring capacity set by Java (typically 128 entries → ~31 KB). We track the
 * write head in `s_input_head` and total valid entries in `s_input_count`
 * (capped at capacity — signals "ring has wrapped"). */
static void* s_input_buf = NULL;
static volatile int s_input_head = 0;
static volatile int s_input_count = 0;
static volatile int s_input_capacity = 0;
static volatile int s_input_entry_size = 0;
static char s_input_path[MAX_PATH] = {0};

/* Storyboard ring — last N UI presses, each with a fixed-layout header (per
 * BugpunchStoryboard.HEADER_BYTES) and a variable RGBA pixel buffer. C# pushes
 * a frame after every UI press via AsyncGPUReadback; the slot dimensions lock
 * to the first frame's size (orientation change drops the ring).
 *
 * Header layout per slot — must match Java BugpunchStoryboard:
 *   off  size  field
 *   0    8     long   tsMs
 *   8    4     float  x
 *   12   4     float  y
 *   16   4     int    screenW
 *   20   4     int    screenH
 *   24   4     int    w
 *   28   4     int    h
 *   32   4     int    pixelsLen
 *   36  192    char   path[192]
 *  228   96    char   label[96]
 *  324   32    char   scene[32]
 *   = 356 bytes.
 *
 * Newest slot's pixels are written as `screenshot_at_crash.rgba`; the whole
 * ring is dumped as `<base>_<i>.rgba` (oldest-to-newest) + `<base>.bin`
 * (concatenated headers) so drain on the next launch can JPEG-encode each
 * frame and compose a per-event `storyboard_frames` JSON array. */
#define BP_STORYBOARD_MAX_SLOTS 16
static void* s_sb_hdr[BP_STORYBOARD_MAX_SLOTS] = {0};
static void* s_sb_pix[BP_STORYBOARD_MAX_SLOTS] = {0};
static int   s_sb_pix_cap[BP_STORYBOARD_MAX_SLOTS] = {0};
static int   s_sb_hdr_bytes = 0;
static volatile int s_sb_capacity = 0;
static volatile int s_sb_newest = -1;
static volatile int s_sb_count = 0;
static char s_sb_path_base[MAX_PATH] = {0};

/* Previous signal handlers (for chaining) */
static struct sigaction s_old_handlers[32]; /* indexed by signal number */
static const int s_signals[] = { SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL };
static const int s_num_signals = sizeof(s_signals) / sizeof(s_signals[0]);

static volatile int s_handling_crash = 0; /* re-entry guard */

/* Stack trace frames (pre-allocated) */
static uintptr_t s_frames[MAX_FRAMES];
static int s_frame_count = 0;

/* Loaded-module table. Populated once at init via dl_iterate_phdr (which
 * acquires the loader lock — NOT async-signal-safe). Read at crash time
 * from pre-filled static buffers — no dl* calls in the signal path. */
struct bp_module {
    uintptr_t load_addr;                 /* dlpi_addr — base to subtract from frame PCs */
    char path[MAX_PATH];                 /* dlpi_name (may be "[vdso]" or "") */
    char build_id[MAX_BUILDID_HEX];      /* hex-encoded NT_GNU_BUILD_ID, or "" if none */
};
static struct bp_module s_modules[MAX_MODULES];
static int s_module_count = 0;

/* ── Async-signal-safe helpers ── */

/* Write a string to an fd (no printf, no strlen that could fault) */
static void safe_write_str(int fd, const char* s) {
    if (!s) return;
    int len = 0;
    while (s[len] && len < 4096) len++;
    write(fd, s, len);
}

/* Write an unsigned 64-bit integer as decimal */
static void safe_write_u64(int fd, unsigned long long val) {
    char buf[21]; /* max 20 digits + null */
    int i = 20;
    buf[i] = '\0';
    if (val == 0) {
        write(fd, "0", 1);
        return;
    }
    while (val > 0 && i > 0) {
        buf[--i] = '0' + (char)(val % 10);
        val /= 10;
    }
    write(fd, buf + i, 20 - i);
}

/* Write a pointer as hex */
static void safe_write_hex(int fd, uintptr_t val) {
    static const char hex[] = "0123456789abcdef";
    char buf[18]; /* 0x + 16 hex digits */
    buf[0] = '0';
    buf[1] = 'x';
    int i;
    for (i = 0; i < 16; i++) {
        buf[17 - i] = hex[val & 0xf];
        val >>= 4;
    }
    /* Skip leading zeros but keep at least one digit */
    int start = 2;
    while (start < 17 && buf[start] == '0') start++;
    write(fd, buf, 2); /* "0x" */
    write(fd, buf + start, 18 - start);
}

/* Convert signal number to name */
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

/* ── Build-ID capture (runs at init, NOT in signal handler) ── */

/* Hex-encode `n` bytes of `src` into `dst` (dst must have >= 2*n+1 bytes).
 * Signal-safe: only indexes a small static table. */
static void hex_encode(char* dst, const unsigned char* src, int n) {
    static const char hex[] = "0123456789abcdef";
    int i;
    for (i = 0; i < n; i++) {
        dst[i * 2]     = hex[(src[i] >> 4) & 0xf];
        dst[i * 2 + 1] = hex[src[i] & 0xf];
    }
    dst[n * 2] = '\0';
}

/* Search a library's PT_NOTE segments for NT_GNU_BUILD_ID, hex-encode into
 * out. Returns 1 if found. */
static int extract_build_id(struct dl_phdr_info* info, char* out, int out_size) {
    int i;
    for (i = 0; i < info->dlpi_phnum; i++) {
        const ElfW(Phdr)* phdr = &info->dlpi_phdr[i];
        if (phdr->p_type != PT_NOTE) continue;

        const char* notes = (const char*)(info->dlpi_addr + phdr->p_vaddr);
        const char* end = notes + phdr->p_memsz;
        while (notes + sizeof(ElfW(Nhdr)) <= end) {
            const ElfW(Nhdr)* nhdr = (const ElfW(Nhdr)*)notes;
            /* Note payload is 4-byte aligned after name and after desc. */
            int name_pad = (nhdr->n_namesz + 3) & ~3;
            int desc_pad = (nhdr->n_descsz + 3) & ~3;
            if (nhdr->n_type == NT_GNU_BUILD_ID &&
                nhdr->n_descsz > 0 &&
                nhdr->n_descsz * 2 + 1 <= out_size) {
                const unsigned char* desc = (const unsigned char*)(
                    notes + sizeof(*nhdr) + name_pad);
                hex_encode(out, desc, nhdr->n_descsz);
                return 1;
            }
            notes += sizeof(*nhdr) + name_pad + desc_pad;
        }
    }
    out[0] = '\0';
    return 0;
}

static int module_callback(struct dl_phdr_info* info, size_t size, void* data) {
    (void)size; (void)data;
    if (s_module_count >= MAX_MODULES) return 0;
    struct bp_module* m = &s_modules[s_module_count];
    m->load_addr = (uintptr_t)info->dlpi_addr;

    /* Copy path (truncate if longer than buffer). */
    const char* p = info->dlpi_name ? info->dlpi_name : "";
    int i = 0;
    while (p[i] && i < MAX_PATH - 1) { m->path[i] = p[i]; i++; }
    m->path[i] = '\0';

    extract_build_id(info, m->build_id, sizeof(m->build_id));
    s_module_count++;
    return 0;
}

/* Snapshot the current module map. Called once from init (post-System.loadLibrary)
 * and ideally again if the game dlopens more native plugins — but we
 * currently don't observe such events, so late-loaded libs won't be in the
 * table. Acceptable MVP tradeoff since the key targets (libil2cpp/libunity/
 * game plugins) load before Bugpunch init. */
static void capture_modules(void) {
    s_module_count = 0;
    dl_iterate_phdr(module_callback, NULL);
}

/* ── Stack walking via _Unwind_Backtrace ── */

struct unwind_state {
    uintptr_t* frames;
    int count;
    int max;
};

static _Unwind_Reason_Code unwind_callback(struct _Unwind_Context* ctx, void* arg) {
    struct unwind_state* state = (struct unwind_state*)arg;
    uintptr_t pc = _Unwind_GetIP(ctx);
    if (pc == 0) return _URC_END_OF_STACK;
    if (state->count < state->max) {
        state->frames[state->count++] = pc;
    }
    return _URC_NO_REASON;
}

static int capture_backtrace(uintptr_t* frames, int max_frames) {
    struct unwind_state state = { frames, 0, max_frames };
    _Unwind_Backtrace(unwind_callback, &state);
    return state.count;
}

/* ── Signal handler (async-signal-safe!) ── */

static void crash_signal_handler(int sig, siginfo_t* info, void* ucontext) {
    /* Re-entry guard — if we crash inside the handler, chain to previous */
    if (__sync_val_compare_and_swap(&s_handling_crash, 0, 1) != 0) {
        /* Already handling — chain to previous handler */
        if (s_old_handlers[sig].sa_sigaction) {
            s_old_handlers[sig].sa_sigaction(sig, info, ucontext);
        }
        _exit(128 + sig);
        return; /* unreachable */
    }

    /* Build crash file path: {crash_dir}/native_{timestamp}.crash */
    char path[MAX_PATH + 64];
    int pi = 0;
    {
        const char* d = s_crash_dir;
        while (*d && pi < MAX_PATH) path[pi++] = *d++;
        path[pi++] = '/';
        const char* prefix = "native_";
        while (*prefix) path[pi++] = *prefix++;

        /* Timestamp: use monotonic clock (async-signal-safe on Linux/Android) */
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        unsigned long long ms = (unsigned long long)ts.tv_sec * 1000ULL +
                                (unsigned long long)ts.tv_nsec / 1000000ULL;
        /* Write ms as decimal into path */
        char numbuf[21];
        int ni = 20;
        numbuf[ni] = '\0';
        unsigned long long tmp = ms;
        if (tmp == 0) numbuf[--ni] = '0';
        while (tmp > 0 && ni > 0) { numbuf[--ni] = '0' + (char)(tmp % 10); tmp /= 10; }
        while (ni <= 20 && numbuf[ni]) path[pi++] = numbuf[ni++];

        const char* ext = ".crash";
        while (*ext) path[pi++] = *ext++;
        path[pi] = '\0';
    }

    /* Open crash file */
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) {
        /* Can't write crash file — chain to previous handler */
        goto chain;
    }

    /* Write header */
    safe_write_str(fd, "BUGPUNCH_CRASH_V1\n");
    safe_write_str(fd, "type:NATIVE_SIGNAL\n");
    safe_write_str(fd, "signal:");
    safe_write_str(fd, signal_name(sig));
    safe_write_str(fd, "\n");
    safe_write_str(fd, "signal_code:");
    safe_write_u64(fd, (unsigned long long)(info ? info->si_code : 0));
    safe_write_str(fd, "\n");
    safe_write_str(fd, "fault_addr:");
    safe_write_hex(fd, (uintptr_t)(info ? info->si_addr : 0));
    safe_write_str(fd, "\n");
    safe_write_str(fd, "pid:");
    safe_write_u64(fd, (unsigned long long)getpid());
    safe_write_str(fd, "\n");
    safe_write_str(fd, "tid:");
    safe_write_u64(fd, (unsigned long long)gettid());
    safe_write_str(fd, "\n");

    /* Timestamp */
    {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        safe_write_str(fd, "timestamp:");
        safe_write_u64(fd, (unsigned long long)ts.tv_sec);
        safe_write_str(fd, "\n");
    }

    /* Platform */
    safe_write_str(fd, "platform:Android\n");

    /* Metadata */
    safe_write_str(fd, "app_version:"); safe_write_str(fd, s_app_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "bundle_id:"); safe_write_str(fd, s_bundle_id); safe_write_str(fd, "\n");
    safe_write_str(fd, "unity_version:"); safe_write_str(fd, s_unity_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "device_model:"); safe_write_str(fd, s_device_model); safe_write_str(fd, "\n");
    safe_write_str(fd, "os_version:"); safe_write_str(fd, s_os_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "gpu:"); safe_write_str(fd, s_gpu_name); safe_write_str(fd, "\n");
    safe_write_str(fd, "session_id:"); safe_write_str(fd, s_session_id); safe_write_str(fd, "\n");

    /* Storyboard ring rescue. Same async-signal-safe pattern as the screenshot
     * ring: pixels live in native-memory direct ByteBuffers registered by
     * BugpunchStoryboard, so we just open files + write() the bytes. We dump
     * each valid slot in oldest-to-newest order to `<base>_<i>.rgba` plus a
     * `<base>.bin` containing every slot's header. The newest slot doubles as
     * `screenshot_at_crash` so the existing dashboard "frame at crash" path
     * keeps working without code change. */
    if (s_sb_count > 0 && s_sb_newest >= 0 && s_sb_capacity > 0
        && s_sb_path_base[0] && s_sb_hdr_bytes > 0) {
        int cap = s_sb_capacity;
        int count = s_sb_count;
        if (count > cap) count = cap;
        int newest = s_sb_newest;
        int start = (newest - count + 1 + cap) % cap;

        /* Concatenated headers go into <base>.bin so drain can map slot
         * indices to metadata without parsing the .crash file format. */
        char hdr_path[MAX_PATH];
        int bp = 0;
        for (int k = 0; s_sb_path_base[k] && bp < MAX_PATH - 5; k++) hdr_path[bp++] = s_sb_path_base[k];
        const char* binSuffix = ".bin";
        for (int k = 0; binSuffix[k] && bp < MAX_PATH - 1; k++) hdr_path[bp++] = binSuffix[k];
        hdr_path[bp] = '\0';
        int hfd = open(hdr_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);

        int written_pixels_for_newest = 0;
        for (int i = 0; i < count; i++) {
            int slot = (start + i) % cap;
            void* hdr_addr = s_sb_hdr[slot];
            void* pix_addr = s_sb_pix[slot];
            if (!hdr_addr || !pix_addr) continue;

            /* Pixel buffer: read pixelsLen from header (offset 32 in our layout). */
            const unsigned char* h = (const unsigned char*)hdr_addr;
            int pixels_len = (int)((unsigned)h[32] | ((unsigned)h[33] << 8)
                                | ((unsigned)h[34] << 16) | ((unsigned)h[35] << 24));
            if (pixels_len <= 0 || pixels_len > s_sb_pix_cap[slot]) continue;

            /* Per-slot rgba file — <base>_<i>.rgba. Build path with manual
             * int decimal write: i is 0..15 so up to 2 digits. */
            char p_path[MAX_PATH];
            int pp = 0;
            for (int k = 0; s_sb_path_base[k] && pp < MAX_PATH - 16; k++) p_path[pp++] = s_sb_path_base[k];
            if (pp < MAX_PATH - 1) p_path[pp++] = '_';
            int idx_val = i;
            if (idx_val >= 10) {
                if (pp < MAX_PATH - 1) p_path[pp++] = (char)('0' + idx_val / 10);
                idx_val %= 10;
            }
            if (pp < MAX_PATH - 1) p_path[pp++] = (char)('0' + idx_val);
            const char* rawSuffix = ".rgba";
            for (int k = 0; rawSuffix[k] && pp < MAX_PATH - 1; k++) p_path[pp++] = rawSuffix[k];
            p_path[pp] = '\0';

            int pfd = open(p_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
            if (pfd >= 0) {
                const char* p = (const char*)pix_addr;
                size_t left = (size_t)pixels_len;
                while (left > 0) {
                    ssize_t n = write(pfd, p, left);
                    if (n <= 0) break;
                    p += n;
                    left -= (size_t)n;
                }
                close(pfd);
                if (slot == newest) {
                    /* Also surface the newest frame as the at-crash screenshot.
                     * Write a separate `_at_crash.rgba` so the storyboard parser
                     * can still consume the per-slot file (encodeRawFrame deletes
                     * its source). 1× extra ring-of-pixels write at signal time
                     * is cheap; avoids a fragile ordering contract in the drain. */
                    int w = (int)((unsigned)h[24] | ((unsigned)h[25] << 8)
                               | ((unsigned)h[26] << 16) | ((unsigned)h[27] << 24));
                    int hi = (int)((unsigned)h[28] | ((unsigned)h[29] << 8)
                               | ((unsigned)h[30] << 16) | ((unsigned)h[31] << 24));

                    char at_path[MAX_PATH];
                    int ap = 0;
                    for (int k = 0; s_sb_path_base[k] && ap < MAX_PATH - 16; k++) at_path[ap++] = s_sb_path_base[k];
                    const char* atSuffix = "_at_crash.rgba";
                    for (int k = 0; atSuffix[k] && ap < MAX_PATH - 1; k++) at_path[ap++] = atSuffix[k];
                    at_path[ap] = '\0';
                    int afd = open(at_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
                    if (afd >= 0) {
                        const char* p = (const char*)pix_addr;
                        size_t left = (size_t)pixels_len;
                        while (left > 0) {
                            ssize_t n = write(afd, p, left);
                            if (n <= 0) break;
                            p += n;
                            left -= (size_t)n;
                        }
                        close(afd);
                        safe_write_str(fd, "screenshot:"); safe_write_str(fd, at_path); safe_write_str(fd, "\n");
                        safe_write_str(fd, "frame_w:"); safe_write_u64(fd, (unsigned long long)w); safe_write_str(fd, "\n");
                        safe_write_str(fd, "frame_h:"); safe_write_u64(fd, (unsigned long long)hi); safe_write_str(fd, "\n");
                        safe_write_str(fd, "frame_format:rgba8888\n");
                        written_pixels_for_newest = 1;
                    }
                }
            }

            if (hfd >= 0) {
                const char* hp = (const char*)hdr_addr;
                size_t left = (size_t)s_sb_hdr_bytes;
                while (left > 0) {
                    ssize_t n = write(hfd, hp, left);
                    if (n <= 0) break;
                    hp += n;
                    left -= (size_t)n;
                }
            }
        }
        if (hfd >= 0) close(hfd);

        safe_write_str(fd, "storyboard_path_base:"); safe_write_str(fd, s_sb_path_base); safe_write_str(fd, "\n");
        safe_write_str(fd, "storyboard_count:"); safe_write_u64(fd, (unsigned long long)count); safe_write_str(fd, "\n");
        safe_write_str(fd, "storyboard_header_bytes:"); safe_write_u64(fd, (unsigned long long)s_sb_hdr_bytes); safe_write_str(fd, "\n");
        (void)written_pixels_for_newest; /* used in extended logging if added later */
    }
    /* Log snapshot: Java keeps the ring serialized into a native ByteBuffer.
     * Dump [0..s_logs_len) to the configured path now. If either is unset or
     * length is zero, the file simply doesn't exist and drain skips it. */
    if (s_logs_buf && s_logs_len > 0 && s_logs_path[0]) {
        int lfd = open(s_logs_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (lfd >= 0) {
            const char* p = (const char*)s_logs_buf;
            size_t left = (size_t)s_logs_len;
            while (left > 0) {
                ssize_t n = write(lfd, p, left);
                if (n <= 0) break;
                p += n;
                left -= (size_t)n;
            }
            close(lfd);
            safe_write_str(fd, "logs:"); safe_write_str(fd, s_logs_path); safe_write_str(fd, "\n");
        }
    }

    /* Input breadcrumb ring: dump the valid entries only, in chronological
     * order. The ring is circular; entries [head-count..head) mod capacity
     * are valid (oldest to newest). Emit them into a single raw file and
     * record the entry stride + count so drain can parse without guessing.
     *
     * Drain on next launch walks this file as `count` fixed-size records
     * and converts to the JSON `breadcrumbs` metadata field. */
    if (s_input_buf && s_input_count > 0 && s_input_path[0]
        && s_input_capacity > 0 && s_input_entry_size > 0) {
        int count = s_input_count;
        int capacity = s_input_capacity;
        int entrySize = s_input_entry_size;
        int head = s_input_head;   /* next-write index; oldest = (head - count + cap) % cap */
        if (count > capacity) count = capacity;
        int start = (head - count + capacity) % capacity;
        int bfd = open(s_input_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (bfd >= 0) {
            /* Write the tail segment first (start..end-of-ring) then the
             * head wrap (0..start) so output is chronological regardless
             * of ring position. */
            const char* base = (const char*)s_input_buf;
            int tail = capacity - start;
            if (tail > count) tail = count;
            if (tail > 0) {
                const char* p = base + (size_t)start * entrySize;
                size_t left = (size_t)tail * entrySize;
                while (left > 0) {
                    ssize_t n = write(bfd, p, left);
                    if (n <= 0) break;
                    p += n;
                    left -= (size_t)n;
                }
            }
            int head_chunk = count - tail;
            if (head_chunk > 0) {
                const char* p = base;
                size_t left = (size_t)head_chunk * entrySize;
                while (left > 0) {
                    ssize_t n = write(bfd, p, left);
                    if (n <= 0) break;
                    p += n;
                    left -= (size_t)n;
                }
            }
            close(bfd);
            safe_write_str(fd, "breadcrumbs:");   safe_write_str(fd, s_input_path); safe_write_str(fd, "\n");
            safe_write_str(fd, "breadcrumbs_count:"); safe_write_u64(fd, (unsigned long long)count); safe_write_str(fd, "\n");
            safe_write_str(fd, "breadcrumbs_stride:"); safe_write_u64(fd, (unsigned long long)entrySize); safe_write_str(fd, "\n");
        }
    }

    /* Video ring: msync the header page so the cursors are durable, then
     * emit the path so drain on next launch can locate and remux it. The
     * payload pages are already on their way to disk via normal page-cache
     * writeback — nothing for us to flush here.
     *
     * Skipped silently if the recorder was never started (s_video_path empty)
     * or the ring is otherwise unconfigured (bp_video_finalize is a no-op). */
    if (s_video_path[0]) {
        bp_video_finalize();
        safe_write_str(fd, "video:"); safe_write_str(fd, s_video_path); safe_write_str(fd, "\n");
    }

    /* Stack trace */
    safe_write_str(fd, "---STACK---\n");
    s_frame_count = capture_backtrace(s_frames, MAX_FRAMES);
    {
        int i;
        for (i = 0; i < s_frame_count; i++) {
            safe_write_hex(fd, s_frames[i]);
            safe_write_str(fd, "\n");
        }
    }
    safe_write_str(fd, "---END---\n");

    /* Re-snapshot the module table so libs loaded AFTER Bugpunch init
     * (libil2cpp/libunity/libgame/AAR-bundled .so's) show up with proper
     * load_addr + build-id. The init-time snapshot only catches what's
     * loaded during JNI_OnLoad, which on Unity is just the system libs.
     *
     * dl_iterate_phdr isn't formally async-signal-safe — it takes the
     * bionic loader lock — but the lock is only held during dlopen/dlclose,
     * and crashing inside those is rare enough that Crashlytics/Breakpad
     * historically accept the same tradeoff. If this ever deadlocks in the
     * wild we'll switch to parsing /proc/self/maps + reading ELF NOTE
     * sections from mapped memory, both of which are truly signal-safe. */
    capture_modules();

    /* Loaded modules (name | load_addr | GNU build-id). Paired with the
     * server-side symbol store: server matches the build-id to the
     * corresponding unstripped .so, subtracts load_addr from each frame
     * PC to get an offset, runs llvm-symbolizer. No need to parse /maps
     * for the frames this table covers. */
    safe_write_str(fd, "---BUILD_IDS---\n");
    {
        int i;
        for (i = 0; i < s_module_count; i++) {
            safe_write_hex(fd, s_modules[i].load_addr);
            safe_write_str(fd, " ");
            safe_write_str(fd, s_modules[i].build_id[0] ? s_modules[i].build_id : "-");
            safe_write_str(fd, " ");
            safe_write_str(fd, s_modules[i].path[0] ? s_modules[i].path : "-");
            safe_write_str(fd, "\n");
        }
    }
    safe_write_str(fd, "---END_BUILD_IDS---\n");

    /* Read /proc/self/maps for symbolication (truncated to 64KB) */
    safe_write_str(fd, "---MAPS---\n");
    {
        int maps_fd = open("/proc/self/maps", O_RDONLY);
        if (maps_fd >= 0) {
            char buf[4096];
            int total = 0;
            int n;
            while (total < 65536 && (n = read(maps_fd, buf, sizeof(buf))) > 0) {
                write(fd, buf, n);
                total += n;
            }
            close(maps_fd);
        }
    }
    safe_write_str(fd, "---END_MAPS---\n");

    close(fd);

chain:
    /* Chain to previous handler (e.g., Firebase Crashlytics, Sentry) */
    {
        struct sigaction* old = &s_old_handlers[sig];
        if (old->sa_flags & SA_SIGINFO) {
            if (old->sa_sigaction) {
                old->sa_sigaction(sig, info, ucontext);
            }
        } else {
            if (old->sa_handler && old->sa_handler != SIG_DFL && old->sa_handler != SIG_IGN) {
                old->sa_handler(sig);
            }
        }
    }

    /* Re-raise with default handler to get the expected exit behavior */
    {
        struct sigaction sa;
        memset(&sa, 0, sizeof(sa));
        sa.sa_handler = SIG_DFL;
        sigaction(sig, &sa, NULL);
        raise(sig);
    }
}

/* ── JNI exports ── */

JNIEXPORT jboolean JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeInstallSignalHandlers(
    JNIEnv* env, jclass cls, jstring crashDir)
{
    const char* dir = (*env)->GetStringUTFChars(env, crashDir, NULL);
    if (!dir) return JNI_FALSE;

    /* Copy crash dir to static buffer */
    int i;
    for (i = 0; dir[i] && i < MAX_PATH - 1; i++) {
        s_crash_dir[i] = dir[i];
    }
    s_crash_dir[i] = '\0';
    (*env)->ReleaseStringUTFChars(env, crashDir, dir);

    /* Install signal handlers, chaining previous ones */
    struct sigaction sa;
    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = crash_signal_handler;
    sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&sa.sa_mask);

    int ok = 1;
    for (i = 0; i < s_num_signals; i++) {
        int sig = s_signals[i];
        if (sigaction(sig, &sa, &s_old_handlers[sig]) != 0) {
            __android_log_print(ANDROID_LOG_ERROR, TAG,
                "Failed to install handler for signal %d", sig);
            ok = 0;
        }
    }

    /* Set up alternate signal stack so we can handle stack overflows */
    {
        static char alt_stack[SIGSTKSZ * 2];
        stack_t ss;
        ss.ss_sp = alt_stack;
        ss.ss_size = sizeof(alt_stack);
        ss.ss_flags = 0;
        sigaltstack(&ss, NULL);
    }

    /* Snapshot loaded modules so the signal handler can write a build-id
     * table without calling the non-signal-safe loader APIs. */
    capture_modules();

    __android_log_print(ANDROID_LOG_INFO, TAG,
        "Signal handlers installed (dir=%s, modules=%d)",
        s_crash_dir, s_module_count);
    return ok ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeUninstallSignalHandlers(
    JNIEnv* env, jclass cls)
{
    int i;
    for (i = 0; i < s_num_signals; i++) {
        int sig = s_signals[i];
        sigaction(sig, &s_old_handlers[sig], NULL);
    }
    __android_log_print(ANDROID_LOG_INFO, TAG, "Signal handlers uninstalled");
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetMetadata(
    JNIEnv* env, jclass cls,
    jstring appVersion, jstring bundleId, jstring unityVersion,
    jstring deviceModel, jstring osVersion, jstring gpuName)
{
    const char* s;

    #define COPY_FIELD(field, jstr) \
        s = (*env)->GetStringUTFChars(env, jstr, NULL); \
        if (s) { \
            int _i; \
            for (_i = 0; s[_i] && _i < MAX_METADATA - 1; _i++) field[_i] = s[_i]; \
            field[_i] = '\0'; \
            (*env)->ReleaseStringUTFChars(env, jstr, s); \
        }

    COPY_FIELD(s_app_version, appVersion)
    COPY_FIELD(s_bundle_id, bundleId)
    COPY_FIELD(s_unity_version, unityVersion)
    COPY_FIELD(s_device_model, deviceModel)
    COPY_FIELD(s_os_version, osVersion)
    COPY_FIELD(s_gpu_name, gpuName)

    #undef COPY_FIELD
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetSessionId(
    JNIEnv* env, jclass cls, jstring sessionId)
{
    (void)cls;
    if (!sessionId) { s_session_id[0] = '\0'; return; }
    const char* s = (*env)->GetStringUTFChars(env, sessionId, NULL);
    if (!s) { s_session_id[0] = '\0'; return; }
    int i;
    for (i = 0; s[i] && i < MAX_METADATA - 1; i++) s_session_id[i] = s[i];
    s_session_id[i] = '\0';
    (*env)->ReleaseStringUTFChars(env, sessionId, s);
}

/* Copy a jstring into a fixed-size char buffer; tolerates NULL jstring
 * (clears the buffer). Used by the attachment-path setters below. */
static void copy_jstring_to(JNIEnv* env, jstring src, char* dst, int max) {
    if (!src) { dst[0] = '\0'; return; }
    const char* s = (*env)->GetStringUTFChars(env, src, NULL);
    if (!s) { dst[0] = '\0'; return; }
    int i;
    for (i = 0; s[i] && i < max - 1; i++) dst[i] = s[i];
    dst[i] = '\0';
    (*env)->ReleaseStringUTFChars(env, src, s);
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetLogsPath(
    JNIEnv* env, jclass cls, jstring path)
{
    (void)cls;
    copy_jstring_to(env, path, s_logs_path, MAX_PATH);
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetLogsBuffer(
    JNIEnv* env, jclass cls, jobject buf)
{
    (void)cls;
    s_logs_buf = buf ? (*env)->GetDirectBufferAddress(env, buf) : NULL;
    if (!buf) s_logs_len = 0;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetLogsLength(
    JNIEnv* env, jclass cls, jint length)
{
    (void)env; (void)cls;
    s_logs_len = (int)length;
}

/* Input breadcrumb ring — set once at startup. `buf` is a direct ByteBuffer
 * owned by Java (allocated by BugpunchInput); we cache its native address
 * so the signal handler can read entries without any JNI call. */
JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetInputBuffer(
    JNIEnv* env, jclass cls, jobject buf, jint capacity, jint entrySize)
{
    (void)cls;
    s_input_buf = buf ? (*env)->GetDirectBufferAddress(env, buf) : NULL;
    s_input_capacity = (int)capacity;
    s_input_entry_size = (int)entrySize;
    if (!buf) {
        s_input_head = 0;
        s_input_count = 0;
    }
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetInputHead(
    JNIEnv* env, jclass cls, jint head, jint count)
{
    (void)env; (void)cls;
    s_input_head = (int)head;
    s_input_count = (int)count;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetInputPath(
    JNIEnv* env, jclass cls, jstring path)
{
    (void)cls;
    copy_jstring_to(env, path, s_input_path, MAX_PATH);
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetVideoPath(
    JNIEnv* env, jclass cls, jstring path)
{
    (void)cls;
    copy_jstring_to(env, path, s_video_path, MAX_PATH);
}

/* ── Storyboard ring JNI bridges ──
 * Header slots are pre-allocated direct ByteBuffers (one per ring slot) and
 * registered once at init. Pixel slots are lazily allocated by Java on first
 * write per slot, so register-on-first-write is the norm. The signal handler
 * walks both tables to dump the ring; there's no JNI involvement at crash time. */

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetStoryboardCapacity(
    JNIEnv* env, jclass cls, jint capacity)
{
    (void)env; (void)cls;
    int c = (int)capacity;
    if (c < 0) c = 0;
    if (c > BP_STORYBOARD_MAX_SLOTS) c = BP_STORYBOARD_MAX_SLOTS;
    s_sb_capacity = c;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetStoryboardSlotHeader(
    JNIEnv* env, jclass cls, jint slot, jobject header, jint headerBytes)
{
    (void)cls;
    int s = (int)slot;
    if (s < 0 || s >= BP_STORYBOARD_MAX_SLOTS) return;
    s_sb_hdr[s] = header ? (*env)->GetDirectBufferAddress(env, header) : NULL;
    /* All slots share the same header byte length — last writer wins, but the
     * Java side passes the same constant for every slot. */
    if (headerBytes > 0) s_sb_hdr_bytes = (int)headerBytes;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetStoryboardSlotPixels(
    JNIEnv* env, jclass cls, jint slot, jobject pixels, jint pixelBytes)
{
    (void)cls;
    int s = (int)slot;
    if (s < 0 || s >= BP_STORYBOARD_MAX_SLOTS) return;
    s_sb_pix[s] = pixels ? (*env)->GetDirectBufferAddress(env, pixels) : NULL;
    s_sb_pix_cap[s] = (int)pixelBytes;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetStoryboardNewest(
    JNIEnv* env, jclass cls, jint slot, jint count)
{
    (void)env; (void)cls;
    s_sb_newest = (int)slot;
    s_sb_count  = (int)count;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeSetStoryboardPathBase(
    JNIEnv* env, jclass cls, jstring pathBase)
{
    (void)cls;
    copy_jstring_to(env, pathBase, s_sb_path_base, MAX_PATH);
}
