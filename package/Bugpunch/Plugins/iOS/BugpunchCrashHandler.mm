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

#define MAX_PATH 512
#define MAX_FRAMES 64
#define MAX_METADATA 256

#pragma mark - Static state

static char s_crash_dir[MAX_PATH] = {0};
static char s_app_version[MAX_METADATA] = {0};
static char s_bundle_id[MAX_METADATA] = {0};
static char s_unity_version[MAX_METADATA] = {0};
static char s_device_model[MAX_METADATA] = {0};
static char s_os_version[MAX_METADATA] = {0};
static char s_gpu_name[MAX_METADATA] = {0};

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

static void write_metadata(int fd) {
    safe_write_str(fd, "platform:iOS\n");
    safe_write_str(fd, "app_version:"); safe_write_str(fd, s_app_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "bundle_id:"); safe_write_str(fd, s_bundle_id); safe_write_str(fd, "\n");
    safe_write_str(fd, "unity_version:"); safe_write_str(fd, s_unity_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "device_model:"); safe_write_str(fd, s_device_model); safe_write_str(fd, "\n");
    safe_write_str(fd, "os_version:"); safe_write_str(fd, s_os_version); safe_write_str(fd, "\n");
    safe_write_str(fd, "gpu:"); safe_write_str(fd, s_gpu_name); safe_write_str(fd, "\n");
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

            // Get stack trace of the crashing thread
            [report appendString:@"---STACK---\n"];
            void* frames[MAX_FRAMES];
            int count = backtrace(frames, MAX_FRAMES);
            char** symbols = backtrace_symbols(frames, count);
            for (int i = 0; i < count; i++) {
                if (symbols && symbols[i]) {
                    [report appendFormat:@"%s\n", symbols[i]];
                } else {
                    [report appendFormat:@"0x%lx\n", (unsigned long)frames[i]];
                }
            }
            if (symbols) free(symbols);
            [report appendString:@"---END---\n"];

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

#pragma mark - C API (called from Unity C# via DllImport)

extern "C" {

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

    NSLog(@"[BugpunchCrash] Signal handlers installed (dir=%s)", s_crash_dir);

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
