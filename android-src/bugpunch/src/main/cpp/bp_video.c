/*
 * bp_video.c — crash-survivable H.264 sample ring backed by an mmap'd file.
 *
 * Built into libbugpunch_crash.so alongside bp.c. The hot path (per encoded
 * sample) is two memcpys into mmap'd pages and two atomic stores; no syscalls,
 * no locks, no allocations. The kernel's page-cache writeback is what makes
 * the data crash-survivable — by the time SIGSEGV fires, every sample we've
 * written is already in pages the kernel will persist whether or not the
 * process exits cleanly.
 *
 * On a crash, bp.c's signal handler calls bp_video_finalize() which msyncs
 * the header page so the cursors are durable. That's it — no muxer state to
 * unwind, no Java to call into, no malloc. The .mp4 itself is produced on the
 * next launch by BugpunchCrashDrain via a normal MediaMuxer pass over the
 * recovered ring.
 *
 * File layout (single file, fixed total size set at init):
 *   [0       .. 4096          ]  HEADER         (header struct, page-aligned)
 *   [4096    .. 4096 + idx_cap*24]  INDEX RING  (24-byte entries: pts/off/len/flags)
 *   [...     .. EOF           ]  PAYLOAD RING   (raw H.264 NAL bytes)
 *
 * Both rings advance via monotonically-increasing 64-bit cursors; physical
 * offset = absolute % capacity. Reader checks `payload_offset >= payload_head -
 * payload_cap` to filter entries whose payload bytes have been overwritten.
 */

#include "bp_video.h"

#include <jni.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdint.h>
#include <string.h>
#include <stdatomic.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <android/log.h>

#define BPV_TAG "BugpunchCrash.video"
#define BPV_MAGIC   0x52565042u   /* 'BPVR' */
#define BPV_VERSION 1u
#define BPV_HEADER_SIZE 4096u
#define BPV_MAX_CSD 256u
#define BPV_FLAG_KEYFRAME 0x1u

/* On-disk header (kept under 4096 bytes so it lives in a single page —
 * msync of one page is the entire crash-time work). All multi-byte fields
 * are written in native (little-endian on every device we ship to). */
typedef struct {
    uint32_t magic;
    uint32_t version;
    uint64_t payload_off;     /* file offset of payload region start */
    uint64_t payload_cap;     /* payload region size in bytes */
    uint64_t payload_head;    /* absolute monotonic byte cursor */
    uint64_t idx_off;         /* file offset of index ring start */
    uint64_t idx_cap;         /* index ring capacity (entries) */
    uint64_t idx_head;        /* absolute monotonic entry cursor */
    uint32_t width;
    uint32_t height;
    uint32_t fps;
    uint32_t format_set;      /* 1 once SPS/PPS populated */
    uint32_t sps_len;
    uint8_t  sps[BPV_MAX_CSD];
    uint32_t pps_len;
    uint8_t  pps[BPV_MAX_CSD];
    uint64_t total_samples;   /* lifetime counter, for diagnostics */
    /* implicit pad to BPV_HEADER_SIZE via mmap region size */
} bp_video_hdr;

typedef struct {
    int64_t  pts_us;
    uint64_t payload_offset;  /* absolute, mod payload_cap → physical */
    uint32_t len;
    uint32_t flags;
} bp_video_idx;

_Static_assert(sizeof(bp_video_idx) == 24, "index entry must be 24 bytes");
_Static_assert(sizeof(bp_video_hdr) <= BPV_HEADER_SIZE, "header too large");

/* ── State ── */

/* All set during init; cleared on close. The signal handler reads s_base /
 * s_size to decide whether to msync; everything else is set up beforehand
 * so the crash path is read-only. */
static void*    s_base = NULL;        /* mmap base (header lives here) */
static size_t   s_size = 0;           /* full mmap size */
static int      s_fd   = -1;
static uint64_t s_payload_off = 0;
static uint64_t s_payload_cap = 0;
static uint64_t s_idx_off     = 0;
static uint64_t s_idx_cap     = 0;

/* Convenience pointers into the mapping (no syscalls to compute). */
static uint8_t*       s_payload = NULL;
static bp_video_idx*  s_idx     = NULL;
static bp_video_hdr*  s_hdr     = NULL;

/* ── Internal helpers ── */

static void bpv_log(const char* msg) {
    __android_log_write(ANDROID_LOG_INFO, BPV_TAG, msg);
}

static void bpv_warn(const char* msg) {
    __android_log_write(ANDROID_LOG_WARN, BPV_TAG, msg);
}

/* ── Public C API (called from JNI exports below + bp.c signal handler) ── */

void bp_video_finalize(void) {
    /* Async-signal-safe. Only msync the header page — payload pages have
     * been getting touched continuously during normal operation, so the
     * kernel's writeback already has them queued. We just need the cursors
     * (payload_head / idx_head) to be durable so the reader knows where
     * the valid window ends. */
    if (s_base == NULL) return;
    /* MS_ASYNC is signal-safe and non-blocking; the kernel commits the
     * dirty pages on its own schedule. MS_SYNC would block on disk I/O,
     * which we never want from a signal handler. */
    msync(s_base, BPV_HEADER_SIZE, MS_ASYNC);
}

/* ── JNI exports ── */

JNIEXPORT jboolean JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeVideoInit(
    JNIEnv* env, jclass cls,
    jstring path, jlong totalBytes, jint idxCapacity, jint width, jint height, jint fps)
{
    (void)cls;
    if (s_base != NULL) {
        bpv_warn("init: already initialized");
        return JNI_TRUE;
    }
    if (totalBytes < (jlong)(BPV_HEADER_SIZE + 1024) || idxCapacity < 32) {
        bpv_warn("init: invalid sizing");
        return JNI_FALSE;
    }

    const char* cpath = (*env)->GetStringUTFChars(env, path, NULL);
    if (!cpath) return JNI_FALSE;

    int fd = open(cpath, O_RDWR | O_CREAT | O_TRUNC, 0600);
    (*env)->ReleaseStringUTFChars(env, path, cpath);
    if (fd < 0) {
        bpv_warn("init: open failed");
        return JNI_FALSE;
    }
    if (ftruncate(fd, totalBytes) != 0) {
        close(fd);
        bpv_warn("init: ftruncate failed");
        return JNI_FALSE;
    }

    void* base = mmap(NULL, (size_t)totalBytes, PROT_READ | PROT_WRITE,
                      MAP_SHARED, fd, 0);
    if (base == MAP_FAILED) {
        close(fd);
        bpv_warn("init: mmap failed");
        return JNI_FALSE;
    }

    /* Layout: [header][idx ring][payload ring] */
    uint64_t idx_off  = BPV_HEADER_SIZE;
    uint64_t idx_size = (uint64_t)idxCapacity * sizeof(bp_video_idx);
    /* Round payload start up to 4 KiB so payload writes hit aligned pages. */
    uint64_t payload_off = (idx_off + idx_size + 4095u) & ~((uint64_t)4095u);
    if (payload_off + 4096u > (uint64_t)totalBytes) {
        munmap(base, (size_t)totalBytes);
        close(fd);
        bpv_warn("init: idx capacity leaves no room for payload");
        return JNI_FALSE;
    }
    uint64_t payload_cap = (uint64_t)totalBytes - payload_off;

    /* Zero the header page; payload/idx are untouched (saves init time on
     * a 30 MB file, and kernel zero-fills on first read either way). */
    memset(base, 0, BPV_HEADER_SIZE);
    bp_video_hdr* h = (bp_video_hdr*)base;
    h->magic        = BPV_MAGIC;
    h->version      = BPV_VERSION;
    h->payload_off  = payload_off;
    h->payload_cap  = payload_cap;
    h->payload_head = 0;
    h->idx_off      = idx_off;
    h->idx_cap      = (uint64_t)idxCapacity;
    h->idx_head     = 0;
    h->width        = (uint32_t)width;
    h->height       = (uint32_t)height;
    h->fps          = (uint32_t)fps;
    h->format_set   = 0;
    h->total_samples = 0;

    s_base    = base;
    s_size    = (size_t)totalBytes;
    s_fd      = fd;
    s_hdr     = h;
    s_payload_off = payload_off;
    s_payload_cap = payload_cap;
    s_idx_off     = idx_off;
    s_idx_cap     = (uint64_t)idxCapacity;
    s_payload = (uint8_t*)base + payload_off;
    s_idx     = (bp_video_idx*)((uint8_t*)base + idx_off);

    /* Persist the freshly-laid-out header right away. From this point on
     * the file is recoverable even if we crash before writing any sample. */
    msync(base, BPV_HEADER_SIZE, MS_ASYNC);

    __android_log_print(ANDROID_LOG_INFO, BPV_TAG,
        "init ok: total=%lld payload=%llu idx=%lld %dx%d@%dfps",
        (long long)totalBytes, (unsigned long long)payload_cap,
        (long long)idxCapacity, width, height, fps);
    return JNI_TRUE;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeVideoSetFormat(
    JNIEnv* env, jclass cls,
    jbyteArray sps, jbyteArray pps)
{
    (void)cls;
    if (s_hdr == NULL) return;
    if (sps == NULL || pps == NULL) return;

    jsize sl = (*env)->GetArrayLength(env, sps);
    jsize pl = (*env)->GetArrayLength(env, pps);
    if (sl <= 0 || sl > (jsize)BPV_MAX_CSD) { bpv_warn("setFormat: SPS oversized"); return; }
    if (pl <= 0 || pl > (jsize)BPV_MAX_CSD) { bpv_warn("setFormat: PPS oversized"); return; }

    (*env)->GetByteArrayRegion(env, sps, 0, sl, (jbyte*)s_hdr->sps);
    (*env)->GetByteArrayRegion(env, pps, 0, pl, (jbyte*)s_hdr->pps);
    s_hdr->sps_len = (uint32_t)sl;
    s_hdr->pps_len = (uint32_t)pl;
    /* Publish format_set last so a reader observing format_set==1 is
     * guaranteed to see the corresponding SPS/PPS bytes. */
    atomic_thread_fence(memory_order_release);
    s_hdr->format_set = 1;
    msync(s_base, BPV_HEADER_SIZE, MS_ASYNC);
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeVideoWriteSample(
    JNIEnv* env, jclass cls,
    jbyteArray data, jint offset, jint length, jlong ptsUs, jboolean keyframe)
{
    (void)cls;
    if (s_hdr == NULL || s_payload == NULL || s_idx == NULL) return;
    if (length <= 0) return;
    if ((uint64_t)length > s_payload_cap) {
        /* A single sample bigger than the ring would overwrite itself. Drop. */
        bpv_warn("writeSample: oversized sample dropped");
        return;
    }

    /* Reserve space at the current head. Single-writer (encoder thread), so
     * we don't need atomics for ordering — just plain reads/writes. The
     * signal handler is a *reader* of head, not a writer; any read it does
     * sees a head value that's at worst one sample stale, which is fine. */
    uint64_t pay_head = s_hdr->payload_head;
    uint64_t phys = pay_head % s_payload_cap;

    jbyte* src = (*env)->GetPrimitiveArrayCritical(env, data, NULL);
    if (!src) return;

    /* Copy payload, splitting at the wrap boundary if needed. */
    if (phys + (uint64_t)length <= s_payload_cap) {
        memcpy(s_payload + phys, src + offset, (size_t)length);
    } else {
        size_t first = (size_t)(s_payload_cap - phys);
        memcpy(s_payload + phys, src + offset, first);
        memcpy(s_payload, src + offset + first, (size_t)length - first);
    }
    (*env)->ReleasePrimitiveArrayCritical(env, data, src, JNI_ABORT);

    /* Append the index entry. */
    uint64_t idx_head = s_hdr->idx_head;
    uint64_t idx_phys = idx_head % s_idx_cap;
    bp_video_idx* e = &s_idx[idx_phys];
    e->pts_us         = (int64_t)ptsUs;
    e->payload_offset = pay_head;
    e->len            = (uint32_t)length;
    e->flags          = keyframe ? BPV_FLAG_KEYFRAME : 0u;

    /* Publish heads. atomic_thread_fence(release) ensures the index entry
     * fields above are visible before the head bumps — required for the
     * signal-handler reader to never see a torn entry. */
    atomic_thread_fence(memory_order_release);
    s_hdr->payload_head = pay_head + (uint64_t)length;
    s_hdr->idx_head     = idx_head + 1;
    s_hdr->total_samples++;
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeVideoFinalize(
    JNIEnv* env, jclass cls)
{
    (void)env; (void)cls;
    bp_video_finalize();
}

JNIEXPORT void JNICALL
Java_au_com_oddgames_bugpunch_BugpunchCrashHandler_nativeVideoClose(
    JNIEnv* env, jclass cls)
{
    (void)env; (void)cls;
    if (s_base == NULL) return;
    /* Final sync so a clean shutdown leaves a fully-flushed file behind. */
    msync(s_base, BPV_HEADER_SIZE, MS_ASYNC);
    munmap(s_base, s_size);
    if (s_fd >= 0) close(s_fd);
    s_base = NULL;
    s_size = 0;
    s_fd   = -1;
    s_hdr  = NULL;
    s_idx  = NULL;
    s_payload = NULL;
    s_payload_off = 0;
    s_payload_cap = 0;
    s_idx_off = 0;
    s_idx_cap = 0;
    bpv_log("closed");
}
