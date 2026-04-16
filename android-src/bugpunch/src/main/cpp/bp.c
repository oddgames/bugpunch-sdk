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

#define TAG "BugpunchCrash"
#define MAX_PATH 512
#define MAX_FRAMES 64
#define MAX_METADATA 256
/* Build-ID table — populated at init, read at crash. Sized for a typical
 * Unity IL2CPP process (libunity, libil2cpp, libmain, libc, libart, a
 * handful of AAR-side .so, and game native plugins) with headroom. */
#define MAX_MODULES 64
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
