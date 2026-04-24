package au.com.oddgames.bugpunch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
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

            // Route the screen into the encoder's input Surface
            mVirtualDisplay = mProjection.createVirtualDisplay(
                "BugpunchCapture", mWidth, mHeight, dpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                mInputSurface, null, mEncoderHandler);

            mRunning = true;
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

        // Find the oldest keyframe within the last windowSeconds
        long latestPtsUs = snapshot[snapshot.length - 1].ptsUs;
        long cutoffPtsUs = latestPtsUs - (long) mWindowSeconds * 1_000_000L;
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
                        Sample s = new Sample(copy, info.presentationTimeUs, info.flags);
                        long cutoffPtsUs = info.presentationTimeUs - (long) mWindowSeconds * 1_000_000L;
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
        }
    };

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
    }
}
