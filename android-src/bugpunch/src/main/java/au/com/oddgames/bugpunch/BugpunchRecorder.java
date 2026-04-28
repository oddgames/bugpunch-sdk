package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Canvas;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.view.Surface;

import java.nio.ByteBuffer;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;

/**
 * Native Android screen recorder with a rolling-window ring buffer.
 *
 * Continuously encodes the screen with MediaCodec and keeps the most recent
 * {@code windowSeconds} of encoded samples in memory. On {@link #dump(String)},
 * writes the oldest keyframe-aligned segment within the window to an MP4 file.
 *
 * Requires the caller to have obtained a MediaProjection consent token via
 * {@link MediaProjectionManager#createScreenCaptureIntent()} and pass the
 * result back to {@link #start(Activity, int, Intent, int)}.
 */
public class BugpunchRecorder {
    private static final String TAG = "[Bugpunch.Recorder]";
    private static final String MIME_TYPE = "video/avc";
    private static final int I_FRAME_INTERVAL = 1; // seconds — must be small so we can trim cleanly

    private static BugpunchRecorder sInstance;

    public static synchronized BugpunchRecorder getInstance() {
        if (sInstance == null) sInstance = new BugpunchRecorder();
        return sInstance;
    }

    // Config
    private int mWidth;
    private int mHeight;
    private int mBitrate;
    private int mFps;
    private int mWindowSeconds;

    // Runtime
    private MediaProjection mProjection;
    private VirtualDisplay mVirtualDisplay;
    private MediaCodec mEncoder;
    private Surface mInputSurface;
    private HandlerThread mEncoderThread;
    private Handler mEncoderHandler;
    private volatile boolean mRunning;
    private MediaFormat mOutputFormat; // captured from INFO_OUTPUT_FORMAT_CHANGED

    // Populated on every successful dump(). MediaCodec Surface-input PTS is
    // System.nanoTime()-derived (exposed as microseconds by MediaCodec), so
    // multiplying back by 1000 yields nanos comparable to MotionEvent event
    // time nanos — which is what the touch recorder needs to align overlays.
    private volatile long mLastDumpStartNanos;
    private volatile long mLastDumpEndNanos;

    public long getLastDumpStartNanos() { return mLastDumpStartNanos; }
    public long getLastDumpEndNanos()   { return mLastDumpEndNanos; }
    public int  getWidth()  { return mWidth;  }
    public int  getHeight() { return mHeight; }

    /** Returns the encoder's input Surface if recording is active, else null. Used by BugpunchStreamer to grab frames for WebRTC. */
    public Surface getInputSurface() {
        return mRunning ? mInputSurface : null;
    }

    /** Returns true if MediaProjection recording is currently active. */
    public boolean isRunning() { return mRunning; }

    /** True if we're in buffer-input mode (Unity feeds frames via queueFrame). */
    public boolean isBufferMode() { return mBufferMode; }

    /** True while one or more callers have paused the ring. Encoded samples
     *  arriving while paused are dropped (not added to the in-RAM ring or
     *  the crash-survivable native ring). */
    public boolean isRingPaused() { return mPauseCount.get() > 0; }

    /**
     * Stop appending encoded samples to the rolling rings while a Bugpunch
     * UI is on screen, so the user typing into our own forms doesn't push
     * out the pre-incident gameplay footage.
     *
     * Counter-based — every call must be paired with {@link #resumeRing()}.
     * Pre-pause samples are retained; the first sample after the count
     * returns to zero folds the PTS gap into a paused-time accumulator so
     * they aren't immediately evicted by the now much-later PTS cutoff.
     *
     * Safe to call from any thread. Safe to call before/after the recorder
     * is running — pause state persists and applies once samples flow.
     */
    public void pauseRing() {
        int n = mPauseCount.incrementAndGet();
        if (n == 1) {
            mPauseStartPtsUs = mLastSamplePtsUs;
            Log.i(TAG, "ring paused (lastPts=" + mPauseStartPtsUs + "us)");
        }
    }

    /** Pair to {@link #pauseRing()}. The ring resumes when the count hits zero. */
    public void resumeRing() {
        int n = mPauseCount.updateAndGet(v -> v > 0 ? v - 1 : 0);
        if (n == 0) {
            Log.i(TAG, "ring resumed (accumPaused=" + mAccumPausedUs + "us)");
        }
    }

    // Buffer-input mode — used when MediaProjection consent was denied and
    // Unity feeds NV12 frames directly from a mirror RenderTexture.
    private volatile boolean mBufferMode;
    // Bounded queue; if full (Unity producing faster than codec drains) we
    // drop the oldest frame rather than block the render thread.
    private static final int PENDING_FRAMES_MAX = 4;
    private final BlockingQueue<QueuedFrame> mPendingFrames = new ArrayBlockingQueue<>(PENDING_FRAMES_MAX);
    private long mBufferModeStartNanos;
    private static class QueuedFrame {
        final byte[] nv12;
        final long ptsUs;
        QueuedFrame(byte[] nv12, long ptsUs) { this.nv12 = nv12; this.ptsUs = ptsUs; }
    }

    // Ring buffer of encoded samples
    private final Deque<Sample> mBuffer = new ArrayDeque<>();
    private final Object mBufferLock = new Object();

    // ─── Pause-while-our-UI-is-up ────────────────────────────────────
    //
    // While a Bugpunch UI (bug report form, feedback, etc.) is on screen,
    // the user is typing into our forms — feeding that footage into the
    // ring would push out the pre-incident gameplay we actually care about.
    // Pause by dropping incoming samples (both in-RAM ring and native
    // crash ring). Counter-based so overlapping UIs balance correctly.
    //
    // We compensate the trim cutoff with mAccumPausedUs so that, once
    // recording resumes, pre-pause samples aren't immediately evicted by
    // the now much-later PTS values. mPauseStartPtsUs is the PTS of the
    // last sample seen before pausing; the first post-resume sample SETS
    // mAccumPausedUs to (its PTS - mPauseStartPtsUs) — overwriting any
    // previous pause cycle, NOT accumulating. Capped at mWindowSeconds so
    // the dump never exceeds 2× the configured window even after a single
    // long pause. (Accumulating across cycles caused dumps to grow without
    // bound — bug forms / chat / annotate / tools each pause once, summing
    // to multi-minute compensations and producing many-minute uploads.)
    private final java.util.concurrent.atomic.AtomicInteger mPauseCount =
        new java.util.concurrent.atomic.AtomicInteger(0);
    private volatile long mLastSamplePtsUs;
    private volatile long mPauseStartPtsUs;
    private volatile long mAccumPausedUs;

    // ─── Segment-mode (chat video — issue #30) ───────────────────────
    //
    // When the chat composer attaches a video, we want a single MP4 starting
    // when the user tapped "Record" and ending when they tapped "Stop". The
    // ring buffer doesn't help here (it'd cap the recording at windowSeconds)
    // so we additively pipe every encoded sample straight into a MediaMuxer
    // alongside the ring-buffer behaviour. This way the legacy bug-report
    // dump path is untouched — it still reads the ring buffer.
    //
    // The muxer is created lazily once we know the output format (after the
    // first INFO_OUTPUT_FORMAT_CHANGED). That callback fires on the encoder
    // thread, which is also where {@code onOutputBufferAvailable} runs, so
    // we don't need extra locking around mSegmentMuxer.
    private volatile boolean mSegmentMode;
    private volatile String mSegmentOutputPath;
    private android.media.MediaMuxer mSegmentMuxer;
    private int mSegmentTrackIdx = -1;
    private boolean mSegmentMuxerStarted;
    private long mSegmentBasePtsUs = -1;
    private int mSegmentSamplesWritten;
    /** Set true on a successful finalize — read by the chat caller to know
     *  whether the file at mSegmentOutputPath is actually playable. */
    private volatile boolean mLastSegmentValid;
    private final Object mSegmentLock = new Object();

    // ─── Crash-survivable video ring (bp_video.c) ─────────────────────
    //
    // Additive to the in-memory mBuffer: every encoded sample goes BOTH
    // into the existing in-RAM ring (used by the live dump/chat paths)
    // AND into a native mmap'd file the kernel persists across process
    // death. On crash, bp.c's signal handler msyncs the header; on next
    // launch BugpunchCrashDrain remuxes the ring into an .mp4. Capacity
    // is sized from mWindowSeconds × bitrate, clamped to [30, 90] s.
    private static final int VIDEO_WINDOW_MIN_SEC = 30;
    private static final int VIDEO_WINDOW_MAX_SEC = 90;
    /** Headroom multiplier on top of nominal bitrate × window — encoders
     *  burst above the target bitrate around scene cuts and the first GOP. */
    private static final double VIDEO_RING_HEADROOM = 1.5;
    private volatile boolean mVideoRingActive;
    private volatile boolean mVideoRingFormatPublished;

    private static class Sample {
        final byte[] data;
        final long ptsUs;       // presentation timestamp from the codec (monotonically increasing)
        final int flags;        // MediaCodec.BufferInfo.flags (BUFFER_FLAG_KEY_FRAME, CODEC_CONFIG, etc.)
        Sample(byte[] data, long ptsUs, int flags) {
            this.data = data; this.ptsUs = ptsUs; this.flags = flags;
        }
    }

    // ─── Public API called from Unity ────────────────────────────────

    /** Initialize config. Call before {@link #start}. */
    public void configure(int width, int height, int bitrate, int fps, int windowSeconds) {
        this.mWidth = width;
        this.mHeight = height;
        this.mBitrate = bitrate;
        this.mFps = fps;
        this.mWindowSeconds = windowSeconds;
    }

    // Retained Context used by stop() to stop the foreground service.
    private Context mAppContext;

    /**
     * Start recording from a foreground service context. Android 14+ requires
     * the caller to already be a foreground service of type mediaProjection
     * before {@code getMediaProjection()} is invoked.
     */
    public synchronized boolean startFromService(Context serviceContext, int resultCode, Intent resultData, int dpi) {
        return startInternal(serviceContext, resultCode, resultData, dpi);
    }

    /**
     * Chat-video segment mode (issue #30). Same plumbing as
     * {@link #startFromService} but additionally muxes every encoded sample
     * straight into an MP4 at {@code outputPath}. On {@link #stop()} the
     * muxer is finalised and the file becomes playable.
     *
     * Additive — the legacy ring-buffer path used by bug-report video is
     * untouched. The first finished segment can be read at the supplied path
     * on stop; the existing {@link #dump(String)} keeps working alongside
     * for any caller that still wants the rolling window.
     */
    public synchronized boolean startSegmentToPath(Context serviceContext,
                                                   int resultCode, Intent resultData,
                                                   int dpi, String outputPath) {
        if (outputPath == null || outputPath.isEmpty()) {
            Log.e(TAG, "startSegmentToPath: outputPath required");
            return false;
        }
        synchronized (mSegmentLock) {
            mSegmentMode = true;
            mSegmentOutputPath = outputPath;
            mSegmentMuxer = null;
            mSegmentTrackIdx = -1;
            mSegmentMuxerStarted = false;
            mSegmentBasePtsUs = -1;
            mSegmentSamplesWritten = 0;
            mLastSegmentValid = false;
        }
        boolean ok = startInternal(serviceContext, resultCode, resultData, dpi);
        if (!ok) {
            synchronized (mSegmentLock) { mSegmentMode = false; mSegmentOutputPath = null; }
        }
        return ok;
    }

    /** True once {@link #stop()} successfully finalised the segment MP4 at the
     *  path passed to {@link #startSegmentToPath}. False if recording never
     *  produced enough media samples to make a playable file. */
    public boolean isLastSegmentValid() { return mLastSegmentValid; }

    /**
     * Start recording directly from an Activity — only safe on Android 13 and
     * below. Android 14+ will throw SecurityException unless called from a
     * running foreground service. Prefer {@link #startFromService}.
     */
    public synchronized boolean start(Activity activity, int resultCode, Intent resultData, int dpi) {
        return startInternal(activity, resultCode, resultData, dpi);
    }

    /**
     * Start recording in buffer-input mode — no MediaProjection, no VirtualDisplay.
     * Unity pushes NV12 frames via {@link #queueFrame(byte[], long)}.
     * Used as a fallback when the user denies the OS MediaProjection consent
     * dialog: we lose system-UI capture but still record the game surface.
     *
     * @return true if the encoder started successfully
     */
    public synchronized boolean startBufferMode(Context ctx) {
        if (mRunning) { Log.w(TAG, "startBufferMode: already running"); return true; }
        try {
            mAppContext = ctx != null ? ctx.getApplicationContext() : null;

            mEncoderThread = new HandlerThread("BugpunchEncoder");
            mEncoderThread.start();
            mEncoderHandler = new Handler(mEncoderThread.getLooper());

            MediaFormat format = MediaFormat.createVideoFormat(MIME_TYPE, mWidth, mHeight);
            // NV12 (Y plane + interleaved UV). Unity-side converter must match.
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420SemiPlanar);
            format.setInteger(MediaFormat.KEY_BIT_RATE, mBitrate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, mFps);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, I_FRAME_INTERVAL);

            mEncoder = MediaCodec.createEncoderByType(MIME_TYPE);
            mEncoder.setCallback(mCodecCallback, mEncoderHandler);
            mEncoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            // No createInputSurface — we're in buffer mode.
            mEncoder.start();

            mBufferMode = true;
            mBufferModeStartNanos = System.nanoTime();
            mPendingFrames.clear();
            mRunning = true;
            startVideoRing(ctx);
            // Prime the encoder with a single black NV12 frame so it emits CSD
            // + a first IDR within ~150 ms, before any real Unity frames arrive.
            // Without this, a crash within the first ~1 s of EnterDebugMode
            // produces a ring with no SPS/PPS and the remuxer rejects it
            // (`no_csd_yet`). The black frame is overwritten in the rolling
            // window as soon as real content flows; only matters for very
            // early crashes.
            primeEncoderWithBlackFrame();
            Log.i(TAG, "started buffer mode " + mWidth + "x" + mHeight + " @ " + mFps
                + "fps, window=" + mWindowSeconds + "s");
            return true;
        } catch (Exception e) {
            Log.e(TAG, "startBufferMode failed", e);
            teardown();
            return false;
        }
    }

    /**
     * Queue an NV12-encoded frame for the encoder. Non-blocking — if the
     * pending queue is full (encoder falling behind) the oldest queued frame
     * is dropped. Safe to call from any thread. No-op if the recorder isn't
     * running in buffer mode.
     *
     * @param nv12   Y plane (w*h bytes) followed by interleaved UV (w*h/2)
     * @param ptsUs  presentation timestamp in microseconds, monotonically increasing
     * @return true if the frame was queued (or dropped-to-requeue); false if not in buffer mode
     */
    public boolean queueFrame(byte[] nv12, long ptsUs) {
        if (!mRunning || !mBufferMode) return false;
        if (nv12 == null) return false;
        QueuedFrame f = new QueuedFrame(nv12, ptsUs);
        // Non-blocking: drop oldest on full so the render thread never waits.
        if (!mPendingFrames.offer(f)) {
            mPendingFrames.poll();
            mPendingFrames.offer(f);
        }
        return true;
    }

    /**
     * Time base helper for Unity-side PTS computation. Unity passes a nanos
     * timestamp; we convert to microseconds relative to mBufferModeStartNanos
     * so codec PTS values stay monotonic across sessions.
     */
    public long getBufferModeStartNanos() { return mBufferModeStartNanos; }

    /**
     * Push one synthetic black NV12 frame at PTS=0 so the encoder produces
     * its first output (CSD + IDR keyframe) without waiting for Unity to
     * deliver real content. Cuts the warmup window for crash-survivable video
     * from "first Unity frame + encoder roundtrip" (often >1 s on slow startup
     * paths) to roughly the encoder's own dequeue latency (~100-200 ms).
     *
     * NV12 black: Y plane all 0x10 (BT.601 limited-range black), interleaved
     * UV plane all 0x80 (chroma neutral). Size = w*h + w*h/2 bytes; computed
     * once and cached so repeated EnterDebugMode calls don't reallocate.
     */
    /**
     * Projection-mode equivalent of {@link #primeEncoderWithBlackFrame()}.
     * Draws one solid-black frame onto {@link #mInputSurface} via
     * {@code lockHardwareCanvas} → fill → {@code unlockCanvasAndPost}, which
     * the encoder dequeues like any other input. Runs after {@code start()}
     * but before {@code createVirtualDisplay} attaches the screen mirror, so
     * the primer frame is guaranteed to be the encoder's first input —
     * subsequent mirrored frames arrive on top of it. Best-effort: if the
     * canvas lock throws (some vendors disallow it on encoder-input Surfaces),
     * we just skip and accept the slightly longer warmup.
     */
    private void primeProjectionSurface() {
        if (mInputSurface == null) return;
        Canvas canvas = null;
        try {
            canvas = mInputSurface.lockHardwareCanvas();
            if (canvas == null) canvas = mInputSurface.lockCanvas(null);
            if (canvas == null) return;
            canvas.drawColor(android.graphics.Color.BLACK);
        } catch (Throwable t) {
            Log.w(TAG, "primeProjectionSurface lock failed", t);
        } finally {
            if (canvas != null) {
                try { mInputSurface.unlockCanvasAndPost(canvas); }
                catch (Throwable ignored) {}
            }
        }
    }

    private byte[] mBlackFrame;
    private void primeEncoderWithBlackFrame() {
        try {
            int ySize  = mWidth * mHeight;
            int uvSize = ySize / 2;
            int total  = ySize + uvSize;
            if (mBlackFrame == null || mBlackFrame.length != total) {
                mBlackFrame = new byte[total];
                java.util.Arrays.fill(mBlackFrame, 0, ySize, (byte) 0x10);
                java.util.Arrays.fill(mBlackFrame, ySize, total, (byte) 0x80);
            }
            queueFrame(mBlackFrame, 0L);
        } catch (Throwable t) {
            Log.w(TAG, "primeEncoderWithBlackFrame failed", t);
        }
    }

    private boolean startInternal(Context ctx, int resultCode, Intent resultData, int dpi) {
        if (mRunning) { Log.w(TAG, "already running"); return true; }
        try {
            mAppContext = ctx.getApplicationContext();

            // Acquire MediaProjection
            MediaProjectionManager mgr = (MediaProjectionManager)
                ctx.getSystemService(Context.MEDIA_PROJECTION_SERVICE);
            mProjection = mgr.getMediaProjection(resultCode, resultData);
            if (mProjection == null) {
                Log.e(TAG, "getMediaProjection returned null");
                return false;
            }

            // Set up encoder thread
            mEncoderThread = new HandlerThread("BugpunchEncoder");
            mEncoderThread.start();
            mEncoderHandler = new Handler(mEncoderThread.getLooper());

            // Android 14+ requires a callback registered BEFORE createVirtualDisplay
            // or the call throws IllegalStateException. The callback also lets us
            // clean up if the user revokes projection via the system shade.
            mProjection.registerCallback(new android.media.projection.MediaProjection.Callback() {
                @Override public void onStop() {
                    Log.i(TAG, "MediaProjection.onStop — tearing down");
                    stop();
                }
            }, mEncoderHandler);

            // Configure MediaCodec for H.264 encoding from a Surface
            MediaFormat format = MediaFormat.createVideoFormat(MIME_TYPE, mWidth, mHeight);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            format.setInteger(MediaFormat.KEY_BIT_RATE, mBitrate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, mFps);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, I_FRAME_INTERVAL);

            mEncoder = MediaCodec.createEncoderByType(MIME_TYPE);
            mEncoder.setCallback(mCodecCallback, mEncoderHandler);
            mEncoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            mInputSurface = mEncoder.createInputSurface();
            mEncoder.start();

            mRunning = true;
            startVideoRing(ctx);
            // Prime the encoder with a single black frame drawn directly onto
            // the input Surface, BEFORE VirtualDisplay attaches and starts
            // mirroring. This forces CSD + first IDR to land in ~100-200 ms
            // even if the OS render loop hasn't pushed a real frame yet —
            // covers crashes that fire within the first second of
            // EnterDebugMode (otherwise the ring rejects with `no_csd_yet`).
            // The frame slides out of the rolling window as soon as real
            // mirrored content arrives.
            primeProjectionSurface();

            // Route the screen into the encoder's input Surface
            mVirtualDisplay = mProjection.createVirtualDisplay(
                "BugpunchCapture", mWidth, mHeight, dpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                mInputSurface, null, mEncoderHandler);
            Log.i(TAG, "started " + mWidth + "x" + mHeight + " @ " + mFps + "fps, window=" + mWindowSeconds + "s");
            return true;
        } catch (Exception e) {
            Log.e(TAG, "start failed", e);
            teardown();
            return false;
        }
    }

    /** Stop recording and release resources. Also stops the foreground service. */
    public synchronized void stop() {
        if (!mRunning) return;
        mRunning = false;
        // Finalize the chat-video segment muxer (if active) BEFORE teardown
        // so the encoder thread is still alive while we close the muxer —
        // any final pending writeSampleData calls have already drained
        // through the encoder callback by the time mRunning flips false.
        finalizeSegmentMuxer();
        teardown();
        // Best-effort: stop the foreground service if it's running. We use a
        // guard inside the service to avoid infinite recursion when the
        // service's onDestroy calls stop() again.
        if (mAppContext != null) {
            try { BugpunchProjectionService.stopIfRunning(mAppContext); } catch (Exception ignored) {}
        }
        mAppContext = null;
        Log.i(TAG, "stopped");
    }

    /** Returns true if the ring buffer has any keyframe in the current window. */
    public boolean hasFootage() {
        synchronized (mBufferLock) {
            for (Sample s : mBuffer) {
                if ((s.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0) return true;
            }
            return false;
        }
    }

    /**
     * Write the most recent {@code windowSeconds} of footage to an MP4 at the
     * given path, trimmed to start on the oldest keyframe within the window.
     *
     * @return true on success
     */
    public boolean dump(String outputPath) {
        if (mOutputFormat == null) {
            Log.w(TAG, "dump: no output format yet (no frames encoded)");
            return false;
        }

        // Snapshot the buffer so the encoder thread can keep writing
        Sample[] snapshot;
        synchronized (mBufferLock) {
            snapshot = mBuffer.toArray(new Sample[0]);
        }
        if (snapshot.length == 0) {
            Log.w(TAG, "dump: buffer empty");
            return false;
        }

        // Find the oldest keyframe within the last windowSeconds. Subtract
        // mAccumPausedUs so dumps performed after a pause/resume cycle still
        // include the pre-pause footage rather than evicting it on the now
        // much-later PTS values.
        long latestPtsUs = snapshot[snapshot.length - 1].ptsUs;
        long cutoffPtsUs = latestPtsUs - (long) mWindowSeconds * 1_000_000L - mAccumPausedUs;
        int firstKeyframeIdx = -1;
        for (int i = 0; i < snapshot.length; i++) {
            Sample s = snapshot[i];
            if ((s.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) == 0) continue;
            if (s.ptsUs >= cutoffPtsUs) { firstKeyframeIdx = i; break; }
            // Otherwise remember this one — it's a candidate for "most recent keyframe before cutoff"
            firstKeyframeIdx = i;
        }
        if (firstKeyframeIdx < 0) {
            Log.w(TAG, "dump: no keyframe found");
            return false;
        }

        // Pre-count real (non-codec-config) samples. If zero, don't even open
        // the muxer — otherwise Android's MPEG4Writer logs "Stop() called but
        // track is not started" and produces a 0-byte file.
        int realSamples = 0;
        for (int i = firstKeyframeIdx; i < snapshot.length; i++) {
            if ((snapshot[i].flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) realSamples++;
        }
        if (realSamples == 0) {
            Log.w(TAG, "dump: no media samples yet (only codec-config). "
                + "Wait ~2s after recording starts before dumping.");
            return false;
        }

        // Write trimmed samples to MP4 via MediaMuxer
        MediaMuxer muxer = null;
        try {
            muxer = new MediaMuxer(outputPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            int trackIdx = muxer.addTrack(mOutputFormat);
            muxer.start();

            long baseUs = snapshot[firstKeyframeIdx].ptsUs;
            long lastPtsUs = baseUs;
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            int written = 0;
            for (int i = firstKeyframeIdx; i < snapshot.length; i++) {
                Sample s = snapshot[i];
                // Skip codec-config samples (already embedded in mOutputFormat's csd-0/csd-1)
                if ((s.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) continue;

                ByteBuffer bb = ByteBuffer.wrap(s.data);
                info.offset = 0;
                info.size = s.data.length;
                info.presentationTimeUs = s.ptsUs - baseUs;
                info.flags = s.flags;
                muxer.writeSampleData(trackIdx, bb, info);
                lastPtsUs = s.ptsUs;
                written++;
            }
            if (written == 0) {
                // No real frames to write — calling muxer.stop() now would log
                // "Stop() called but track is not started" and produce a 0-byte MP4.
                Log.w(TAG, "dump: no media samples (only codec-config) — recorder hasn't run long enough");
                muxer.release();
                muxer = null;
                new java.io.File(outputPath).delete();
                return false;
            }
            muxer.stop();
            mLastDumpStartNanos = baseUs * 1000L;
            mLastDumpEndNanos   = lastPtsUs * 1000L;
            Log.i(TAG, "dump: wrote " + written + " samples to " + outputPath);
            return true;
        } catch (Exception e) {
            Log.e(TAG, "dump failed", e);
            return false;
        } finally {
            if (muxer != null) {
                try { muxer.release(); } catch (Exception ignored) {}
            }
        }
    }

    // ─── Encoder callback ────────────────────────────────────────────

    private final MediaCodec.Callback mCodecCallback = new MediaCodec.Callback() {
        @Override
        public void onInputBufferAvailable(MediaCodec codec, int index) {
            if (!mBufferMode) return; // Surface-input path — no manual buffers
            QueuedFrame f = mPendingFrames.poll();
            if (f == null) {
                // No frame ready — give the buffer back empty so the codec can
                // recycle it. Queueing size=0 keeps the encoder from stalling
                // indefinitely waiting for input.
                try { codec.queueInputBuffer(index, 0, 0, 0, 0); } catch (Exception ignored) {}
                return;
            }
            try {
                ByteBuffer buf = codec.getInputBuffer(index);
                int size = Math.min(f.nv12.length, buf != null ? buf.capacity() : f.nv12.length);
                if (buf != null) { buf.clear(); buf.put(f.nv12, 0, size); }
                codec.queueInputBuffer(index, 0, size, f.ptsUs, 0);
            } catch (Exception e) {
                Log.w(TAG, "queueInputBuffer failed", e);
                try { codec.queueInputBuffer(index, 0, 0, 0, 0); } catch (Exception ignored) {}
            }
        }

        @Override
        public void onOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info) {
            try {
                ByteBuffer buf = codec.getOutputBuffer(index);
                if (buf != null && info.size > 0) {
                    buf.position(info.offset);
                    buf.limit(info.offset + info.size);
                    byte[] copy = new byte[info.size];
                    buf.get(copy);

                    if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
                        // Drop incoming samples while a Bugpunch UI is up so
                        // typing into our forms doesn't push out the
                        // pre-incident gameplay we want to capture. CSD
                        // samples above bypass this gate — they're config,
                        // not content. Segment-mode (chat video) deliberately
                        // ignores pause: that flow is an explicit
                        // user-initiated recording, not the rolling ring.
                        boolean paused = mPauseCount.get() > 0;
                        if (!paused) {
                            // First post-resume sample: fold the PTS gap into
                            // the paused-time accumulator so the cutoff
                            // doesn't immediately evict pre-pause samples.
                            if (mPauseStartPtsUs > 0) {
                                long gapUs = info.presentationTimeUs - mPauseStartPtsUs;
                                long capUs = (long) mWindowSeconds * 1_000_000L;
                                mAccumPausedUs = Math.min(gapUs, capUs);
                                mPauseStartPtsUs = 0;
                            }
                            mLastSamplePtsUs = info.presentationTimeUs;
                            // Crash-survivable ring write — happens BEFORE the
                            // RAM-ring/segment-muxer paths so a crash mid-callback
                            // still preserves the sample in the mmap'd ring.
                            if (mVideoRingActive && mVideoRingFormatPublished) {
                                boolean keyframe = (info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0;
                                try {
                                    BugpunchCrashHandler.videoWriteSample(
                                        copy, 0, copy.length, info.presentationTimeUs, keyframe);
                                } catch (Throwable t) {
                                    Log.w(TAG, "videoWriteSample failed (disabling ring)", t);
                                    mVideoRingActive = false;
                                }
                            }
                            Sample s = new Sample(copy, info.presentationTimeUs, info.flags);
                            long cutoffPtsUs = info.presentationTimeUs
                                - (long) mWindowSeconds * 1_000_000L
                                - mAccumPausedUs;
                            synchronized (mBufferLock) {
                                mBuffer.addLast(s);
                                // Trim samples older than the window, but keep the first keyframe so we have a valid start
                                while (mBuffer.size() > 2 && mBuffer.peekFirst().ptsUs < cutoffPtsUs) {
                                    // Only trim if there's a later keyframe we can restart from
                                    if (!hasKeyframeAfter(cutoffPtsUs)) break;
                                    mBuffer.pollFirst();
                                }
                            }
                        }

                        // Segment-mode (#30) — also pipe to the live MP4 muxer
                        // for the chat video flow. Skip until we've seen the
                        // first keyframe so the file always plays from t=0.
                        // Runs regardless of pauseRing(): it's an explicit
                        // user-driven recording, not the background ring.
                        if (mSegmentMode) {
                            writeSegmentSample(copy, info);
                        }
                    }
                    // CODEC_CONFIG samples (SPS/PPS) are already embedded in the output MediaFormat
                    // once onOutputFormatChanged fires, so we don't need to stash them separately.
                }
            } catch (Exception e) {
                Log.e(TAG, "onOutputBufferAvailable error", e);
            } finally {
                try { codec.releaseOutputBuffer(index, false); } catch (Exception ignored) {}
            }
        }

        @Override
        public void onError(MediaCodec codec, MediaCodec.CodecException e) {
            Log.e(TAG, "codec error", e);
        }

        @Override
        public void onOutputFormatChanged(MediaCodec codec, MediaFormat format) {
            mOutputFormat = format;
            Log.i(TAG, "output format: " + format);
            if (mVideoRingActive && !mVideoRingFormatPublished) {
                publishVideoFormat(format);
            }
            // Segment-mode: build the muxer now that we know the output
            // format (codec-config samples are embedded in csd-0/csd-1 of
            // this MediaFormat, so we don't need to write them manually).
            if (mSegmentMode) {
                openSegmentMuxer(format);
            }
        }
    };

    // ─── Crash-survivable video ring helpers ────────────────────────

    /** Allocate and mmap the native ring. Sized from {@link #mWindowSeconds}
     *  (clamped to [30, 90] s) and {@link #mBitrate} with a headroom factor
     *  for encoder bursting. Best-effort — any failure just leaves the
     *  ring inactive; the in-RAM path keeps working untouched. */
    private void startVideoRing(Context ctx) {
        try {
            int windowSec = Math.max(VIDEO_WINDOW_MIN_SEC,
                Math.min(VIDEO_WINDOW_MAX_SEC, mWindowSeconds));
            // bytes ≈ bitrate(bps) * window(s) / 8 * headroom
            long totalBytes = (long) ((double) mBitrate * windowSec / 8.0 * VIDEO_RING_HEADROOM);
            // Hard floor + ceiling so we never produce a degenerate file.
            if (totalBytes < 2L * 1024 * 1024) totalBytes = 2L * 1024 * 1024;     // 2 MB
            if (totalBytes > 64L * 1024 * 1024) totalBytes = 64L * 1024 * 1024;   // 64 MB
            // Index sized for ~3× the expected sample count at fps × window —
            // covers B-frames + IDR splits comfortably without bloating the file.
            int idxCapacity = Math.max(512, mFps * windowSec * 3);

            String path = new java.io.File(ctx.getCacheDir(), "bp_video.dat")
                .getAbsolutePath();
            boolean ok = BugpunchCrashHandler.videoInit(
                path, totalBytes, idxCapacity, mWidth, mHeight, mFps);
            mVideoRingActive = ok;
            mVideoRingFormatPublished = false;
            if (ok) {
                Log.i(TAG, "video ring opened: path=" + path
                    + " bytes=" + totalBytes + " idx=" + idxCapacity);
            } else {
                Log.w(TAG, "video ring init failed — crash-survivable video disabled");
            }
        } catch (Throwable t) {
            Log.w(TAG, "startVideoRing threw", t);
            mVideoRingActive = false;
            mVideoRingFormatPublished = false;
        }
    }

    /** Pull csd-0 (SPS) and csd-1 (PPS) out of the encoder's output format
     *  and publish them into the ring header. The remuxer on next launch
     *  needs these to build a {@code MediaFormat} for {@code MediaMuxer}. */
    private void publishVideoFormat(MediaFormat format) {
        try {
            ByteBuffer sps = format.containsKey("csd-0") ? format.getByteBuffer("csd-0") : null;
            ByteBuffer pps = format.containsKey("csd-1") ? format.getByteBuffer("csd-1") : null;
            if (sps == null || pps == null) {
                Log.w(TAG, "publishVideoFormat: missing csd-0/csd-1");
                return;
            }
            byte[] spsArr = new byte[sps.remaining()];
            sps.duplicate().get(spsArr);
            byte[] ppsArr = new byte[pps.remaining()];
            pps.duplicate().get(ppsArr);
            BugpunchCrashHandler.videoSetFormat(spsArr, ppsArr);
            mVideoRingFormatPublished = true;
            Log.i(TAG, "video ring CSD published: sps=" + spsArr.length
                + "B pps=" + ppsArr.length + "B");
        } catch (Throwable t) {
            Log.w(TAG, "publishVideoFormat failed", t);
        }
    }

    /** Close the native ring on a clean shutdown. The signal-handler path
     *  uses bp_video_finalize() instead — closing here would race. */
    private void closeVideoRing() {
        if (!mVideoRingActive) return;
        try {
            BugpunchCrashHandler.videoFinalize();
            BugpunchCrashHandler.videoClose();
        } catch (Throwable t) {
            Log.w(TAG, "closeVideoRing failed", t);
        }
        mVideoRingActive = false;
        mVideoRingFormatPublished = false;
    }

    // ─── Segment-mode helpers (#30) ──────────────────────────────────

    private void openSegmentMuxer(MediaFormat format) {
        synchronized (mSegmentLock) {
            if (mSegmentMuxer != null) return;
            String path = mSegmentOutputPath;
            if (path == null || path.isEmpty()) return;
            try {
                mSegmentMuxer = new android.media.MediaMuxer(
                    path, android.media.MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
                mSegmentTrackIdx = mSegmentMuxer.addTrack(format);
                mSegmentMuxer.start();
                mSegmentMuxerStarted = true;
                Log.i(TAG, "segment muxer opened: " + path);
            } catch (Exception e) {
                Log.e(TAG, "openSegmentMuxer failed", e);
                try { if (mSegmentMuxer != null) mSegmentMuxer.release(); } catch (Exception ignored) {}
                mSegmentMuxer = null;
                mSegmentTrackIdx = -1;
                mSegmentMuxerStarted = false;
            }
        }
    }

    private void writeSegmentSample(byte[] data, MediaCodec.BufferInfo info) {
        synchronized (mSegmentLock) {
            if (!mSegmentMuxerStarted || mSegmentMuxer == null || mSegmentTrackIdx < 0) return;
            // Wait for the first keyframe so the file is decodable from t=0.
            if (mSegmentBasePtsUs < 0) {
                if ((info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) == 0) return;
                mSegmentBasePtsUs = info.presentationTimeUs;
            }
            try {
                MediaCodec.BufferInfo out = new MediaCodec.BufferInfo();
                out.offset = 0;
                out.size = data.length;
                out.presentationTimeUs = info.presentationTimeUs - mSegmentBasePtsUs;
                out.flags = info.flags;
                mSegmentMuxer.writeSampleData(mSegmentTrackIdx,
                    java.nio.ByteBuffer.wrap(data), out);
                mSegmentSamplesWritten++;
            } catch (Exception e) {
                Log.w(TAG, "segment writeSampleData failed", e);
            }
        }
    }

    private void finalizeSegmentMuxer() {
        synchronized (mSegmentLock) {
            if (mSegmentMuxer == null) {
                mLastSegmentValid = false;
                mSegmentMode = false;
                mSegmentOutputPath = null;
                return;
            }
            boolean ok = false;
            try {
                if (mSegmentMuxerStarted && mSegmentSamplesWritten > 0) {
                    mSegmentMuxer.stop();
                    ok = true;
                }
            } catch (Exception e) {
                Log.w(TAG, "segment muxer.stop failed", e);
            }
            try { mSegmentMuxer.release(); } catch (Exception ignored) {}
            mSegmentMuxer = null;
            mSegmentTrackIdx = -1;
            mSegmentMuxerStarted = false;
            mLastSegmentValid = ok;
            // Drop a 0-byte / unfinishable file so the chat caller can detect
            // the failure cleanly via File.exists() / length() > 0 checks.
            if (!ok && mSegmentOutputPath != null) {
                try { new java.io.File(mSegmentOutputPath).delete(); } catch (Exception ignored) {}
            }
            Log.i(TAG, "segment muxer finalized: ok=" + ok
                + " samples=" + mSegmentSamplesWritten
                + " path=" + mSegmentOutputPath);
            mSegmentMode = false;
            mSegmentOutputPath = null;
            mSegmentBasePtsUs = -1;
            mSegmentSamplesWritten = 0;
        }
    }

    /** Must be called with mBufferLock held. */
    private boolean hasKeyframeAfter(long ptsUs) {
        for (Sample s : mBuffer) {
            if (s.ptsUs > ptsUs && (s.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0) return true;
        }
        return false;
    }

    // ─── Teardown ────────────────────────────────────────────────────

    private void teardown() {
        try { if (mVirtualDisplay != null) mVirtualDisplay.release(); } catch (Exception e) { Log.w(TAG, "vd release", e); }
        mVirtualDisplay = null;

        try {
            if (mEncoder != null) {
                try { mEncoder.stop(); } catch (Exception ignored) {}
                mEncoder.release();
            }
        } catch (Exception e) { Log.w(TAG, "encoder release", e); }
        mEncoder = null;

        try { if (mInputSurface != null) mInputSurface.release(); } catch (Exception e) { Log.w(TAG, "surface release", e); }
        mInputSurface = null;

        try { if (mProjection != null) mProjection.stop(); } catch (Exception e) { Log.w(TAG, "projection stop", e); }
        mProjection = null;

        if (mEncoderThread != null) {
            try { mEncoderThread.quitSafely(); } catch (Exception ignored) {}
            mEncoderThread = null;
            mEncoderHandler = null;
        }

        synchronized (mBufferLock) { mBuffer.clear(); }
        mOutputFormat = null;
        mBufferMode = false;
        mPendingFrames.clear();
        // Reset pause state — counter persists across stop/restart only by
        // accident; a fresh recorder session starts unpaused with a zero
        // accumulator so the cutoff math doesn't carry stale offsets.
        mPauseCount.set(0);
        mLastSamplePtsUs = 0;
        mPauseStartPtsUs = 0;
        mAccumPausedUs = 0;
        closeVideoRing();
    }
}
