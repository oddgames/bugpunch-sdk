package au.com.oddgames.bugpunch;

import android.graphics.Bitmap;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.view.PixelCopy;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.webrtc.DataChannel;
import org.webrtc.DefaultVideoDecoderFactory;
import org.webrtc.HardwareVideoEncoderFactory;
import org.webrtc.IceCandidate;
import org.webrtc.JavaI420Buffer;
import org.webrtc.MediaConstraints;
import org.webrtc.MediaStream;
import org.webrtc.PeerConnection;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.RtpReceiver;
import org.webrtc.SdpObserver;
import org.webrtc.SessionDescription;
import org.webrtc.SurfaceTextureHelper;
import org.webrtc.VideoFrame;
import org.webrtc.VideoSource;
import org.webrtc.VideoTrack;

import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/**
 * Native WebRTC streaming peer — N7 of the native migration plan.
 *
 * Handles webrtc-offer / webrtc-ice-candidates / webrtc-quality / webrtc-ice
 * requests that arrive on the native BugpunchTunnel, bypassing the C# round-trip.
 *
 * Video pipeline:
 *   PixelCopy on Unity SurfaceView → ARGB Bitmap → I420 ByteBuffer
 *   → VideoFrame → VideoSource → VideoTrack → PeerConnection → browser
 *
 * HardwareVideoEncoderFactory is created with null sharedContext so it uses
 * ByteBuffer (non-texture) input — no Unity EGL context sharing required.
 */
public class BugpunchStreamer {
    private static final String TAG = "[Bugpunch.Streamer]";

    private static final int DEFAULT_WIDTH  = 960;
    private static final int DEFAULT_HEIGHT = 540;
    private static final int DEFAULT_FPS    = 20;

    // One factory per process — initialize once.
    private static volatile boolean sFactoryInitialized;
    private static PeerConnectionFactory sFactory;
    private static final Object sFactoryLock = new Object();

    // Singleton — one active peer at a time.
    private static volatile BugpunchStreamer sInstance;
    public static BugpunchStreamer getInstance() {
        BugpunchStreamer i = sInstance;
        if (i == null) { synchronized (BugpunchStreamer.class) { i = sInstance; if (i == null) { sInstance = i = new BugpunchStreamer(); } } }
        return i;
    }

    // ── WebRTC state ──
    private PeerConnection mPc;
    private VideoSource mVideoSource;
    private VideoTrack mVideoTrack;
    private volatile boolean mStreaming;
    private final Object mPcLock = new Object();

    // ICE candidates queued for the browser to poll
    private final List<String> mIceCandidates = new ArrayList<>();
    private final Object mIceLock = new Object();

    // ── Stream settings ──
    private int mWidth  = DEFAULT_WIDTH;
    private int mHeight = DEFAULT_HEIGHT;
    private int mFps    = DEFAULT_FPS;

    // ── Capture ──
    private HandlerThread mCaptureThread;
    private Handler mCaptureHandler;
    private Bitmap mCaptureBitmap;     // reused to avoid heap churn
    private SurfaceView mCachedSurface;

    // MediaProjection surface for WebRTC when debug mode is active
    private android.view.Surface mMediaProjectionSurface;
    private SurfaceTextureHelper mSurfaceTextureHelper;
    private android.graphics.SurfaceTexture mSurfaceTexture;

    // App context for creating WebRTC resources
    private static android.content.Context sAppContext;

    private BugpunchStreamer() {}

    // ──────────────────────────────────────────────────────────────────────────
    // Public dispatch — called by BugpunchTunnel from the tunnel worker thread
    // ──────────────────────────────────────────────────────────────────────────

    /**
     * Entry point for all /webrtc-* requests coming in from the native tunnel.
     * Synchronous paths respond immediately; the offer path is async and calls
     * BugpunchTunnel.sendResponse() when the answer is ready.
     */
    public void handleRequest(String requestId, String path, String method, String body) {
        String basePath = path.split("\\?")[0];
        Log.d(TAG, "request " + method + " " + basePath + " bodyLen=" + (body == null ? 0 : body.length()));
        switch (basePath) {
            case "/webrtc-offer":
                ensureFactory();
                handleOffer(requestId, body);
                break;
            case "/webrtc-ice-candidates":
                BugpunchTunnel.sendResponse(
                    buildResponse(requestId, 200, drainCandidatesJson(), "application/json"));
                break;
            case "/webrtc-ice":
                handleIce(requestId, body);
                break;
            case "/webrtc-quality":
                if ("POST".equalsIgnoreCase(method)) handleQuality(requestId, body);
                else BugpunchTunnel.sendResponse(buildResponse(requestId, 200, getQualityJson(), "application/json"));
                break;
            default:
                BugpunchTunnel.sendResponse(
                    buildResponse(requestId, 404, "{\"error\":\"not found\"}", "application/json"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Offer / Answer
    // ──────────────────────────────────────────────────────────────────────────

    private void handleOffer(final String requestId, final String body) {
        Log.i(TAG, "handleOffer START requestId=" + requestId + " bodyLen=" + (body != null ? body.length() : 0));
        if (mStreaming) {
            Log.i(TAG, "handleOffer: was streaming, stopping first requestId=" + requestId);
            stopStreaming();
        }

        String sdp;
        List<PeerConnection.IceServer> iceServers = new ArrayList<>();
        try {
            JSONObject j = new JSONObject(body);
            sdp = j.optString("sdp", "");
            JSONArray arr = j.optJSONArray("iceServers");
            if (arr != null) {
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject s = arr.optJSONObject(i);
                    if (s == null) continue;
                    PeerConnection.IceServer.Builder b = PeerConnection.IceServer.builder(s.optString("urls", ""));
                    String u = s.optString("username", ""), c = s.optString("credential", "");
                    if (!u.isEmpty()) b.setUsername(u);
                    if (!c.isEmpty()) b.setPassword(c);
                    iceServers.add(b.createIceServer());
                }
            }
        } catch (JSONException e) {
            Log.w(TAG, "offer rejected — bad JSON: " + e.getMessage());
            BugpunchTunnel.sendResponse(buildResponse(requestId, 400, "{\"error\":\"bad offer JSON\"}", "application/json"));
            return;
        }
        if (sdp.isEmpty()) {
            Log.w(TAG, "offer rejected — empty SDP (iceServers=" + iceServers.size() + ")");
            BugpunchTunnel.sendResponse(buildResponse(requestId, 400, "{\"error\":\"empty SDP\"}", "application/json"));
            return;
        }
        Log.i(TAG, "offer received — sdpLen=" + sdp.length() + " iceServers=" + iceServers.size());
        if (iceServers.isEmpty())
            iceServers.add(PeerConnection.IceServer.builder("stun:stun.l.google.com:19302").createIceServer());

        synchronized (mPcLock) {
            cleanupPc();

            if (sFactory == null) {
                Log.w(TAG, "offer aborted — PeerConnectionFactory not initialized (no app context?)");
                BugpunchTunnel.sendResponse(buildResponse(requestId, 503, "{\"error\":\"factory not ready\"}", "application/json"));
                return;
            }

            mVideoSource = sFactory.createVideoSource(false);
            mVideoTrack = sFactory.createVideoTrack("bp-video", mVideoSource);

            PeerConnection.RTCConfiguration cfg = new PeerConnection.RTCConfiguration(iceServers);
            cfg.sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN;
            mPc = sFactory.createPeerConnection(cfg, new PcObserver());
            if (mPc == null) {
                Log.w(TAG, "offer aborted — createPeerConnection returned null");
                BugpunchTunnel.sendResponse(buildResponse(requestId, 500, "{\"error\":\"createPeerConnection failed\"}", "application/json"));
                return;
            }

            mPc.addTrack(mVideoTrack, Collections.singletonList("stream"));

            // Apply bitrate ceiling
            setVideoMaxBitrate(2_500_000L);
        }

        // Set remote description (the offer), then create answer
        SessionDescription offer = new SessionDescription(SessionDescription.Type.OFFER, sdp);
        final CountDownLatch answerReady = new CountDownLatch(1);
        final String[] answerHolder = {null};
        final String[] errorHolder = {null};

        synchronized (mPcLock) {
            if (mPc == null) return;
            mPc.setRemoteDescription(new SdpObserver() {
                @Override public void onSetSuccess() {
                    synchronized (mPcLock) {
                        Log.i(TAG, "setRemoteDescription SUCCESS requestId=" + requestId);
                        if (mPc == null) { errorHolder[0] = "pc gone after setRemote"; answerReady.countDown(); return; }
                        Log.i(TAG, "createAnswer START requestId=" + requestId);
                        mPc.createAnswer(new SdpObserver() {
                            @Override public void onCreateSuccess(SessionDescription answer) {
                                synchronized (mPcLock) {
                                    Log.i(TAG, "createAnswer SUCCESS sdpLen=" + answer.description.length() + " requestId=" + requestId);
                                    if (mPc == null) { errorHolder[0] = "pc gone after create"; answerReady.countDown(); return; }
                                    Log.i(TAG, "setLocalDescription START requestId=" + requestId);
                                    mPc.setLocalDescription(new SdpObserver() {
                                        @Override public void onSetSuccess() {
                                            answerHolder[0] = answer.description;
                                            Log.i(TAG, "setLocalDescription SUCCESS sdpLen=" + answer.description.length() + " requestId=" + requestId);
                                            answerReady.countDown();
                                        }
                                        @Override public void onSetFailure(String s) { Log.e(TAG, "setLocalDescription FAILED: " + s + " requestId=" + requestId); errorHolder[0] = s; answerReady.countDown(); }
                                        @Override public void onCreateSuccess(SessionDescription sd) {}
                                        @Override public void onCreateFailure(String s) {}
                                    }, answer);
                                }
                            }
                            @Override public void onCreateFailure(String s) { Log.e(TAG, "createAnswer FAILED: " + s + " requestId=" + requestId); errorHolder[0] = s; answerReady.countDown(); }
                            @Override public void onSetSuccess() {}
                            @Override public void onSetFailure(String s) {}
                        }, new MediaConstraints());
                    }
                }
                @Override public void onSetFailure(String s) { Log.e(TAG, "setRemoteDescription FAILED: " + s + " requestId=" + requestId); errorHolder[0] = s; answerReady.countDown(); }
                @Override public void onCreateSuccess(SessionDescription sd) {}
                @Override public void onCreateFailure(String s) {}
            }, offer);
        }

        try {
            answerReady.await(15, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }

        if (errorHolder[0] != null || answerHolder[0] == null) {
            String err = errorHolder[0] != null ? errorHolder[0] : "timeout";
            Log.i(TAG, "offer handling failed: " + err + " requestId=" + requestId);
            BugpunchTunnel.sendResponse(buildResponse(requestId, 500, "{\"error\":\"" + err + "\"}", "application/json"));
            return;
        }

        mStreaming = true;
        mVideoSource.getCapturerObserver().onCapturerStarted(true);
        startCaptureLoop();
        Log.i(TAG, "WebRTC streaming started (" + mWidth + "x" + mHeight + "@" + mFps + "fps) requestId=" + requestId);

        try {
            JSONObject resp = new JSONObject();
            resp.put("type", "answer");
            resp.put("sdp", answerHolder[0]);
            String respJson = resp.toString();
            Log.i(TAG, "WebRTC answer sdpLen=" + answerHolder[0].length() + " requestId=" + requestId);
            BugpunchTunnel.sendResponse(buildResponse(requestId, 200, respJson, "application/json"));
            Log.i(TAG, "WebRTC answer sent to tunnel requestId=" + requestId);
        } catch (JSONException e) {
            Log.e(TAG, "answer serialize failed: " + e.getMessage() + " requestId=" + requestId);
            BugpunchTunnel.sendResponse(buildResponse(requestId, 500, "{\"error\":\"answer serialize\"}", "application/json"));
        }
    }

    private void handleIce(String requestId, String body) {
        try {
            JSONObject j = new JSONObject(body);
            String candidate = j.optString("candidate", "");
            String sdpMid = j.optString("sdpMid", "");
            int sdpMLineIndex = j.optInt("sdpMLineIndex", 0);
            if (candidate.isEmpty()) {
                Log.d(TAG, "remote ICE: empty candidate (end-of-candidates marker)");
            } else {
                synchronized (mPcLock) {
                    if (mPc == null) {
                        Log.w(TAG, "remote ICE dropped — no peer connection");
                    } else {
                        mPc.addIceCandidate(new IceCandidate(sdpMid, sdpMLineIndex, candidate));
                        Log.d(TAG, "remote ICE added: mid=" + sdpMid + " idx=" + sdpMLineIndex);
                    }
                }
            }
        } catch (JSONException e) {
            Log.w(TAG, "remote ICE rejected — bad JSON: " + e.getMessage());
        }
        BugpunchTunnel.sendResponse(buildResponse(requestId, 200, "{\"ok\":true}", "application/json"));
    }

    private void handleQuality(String requestId, String body) {
        try {
            JSONObject j = new JSONObject(body);
            int w = j.optInt("width", mWidth);
            int h = j.optInt("height", mHeight);
            int f = j.optInt("fps", mFps);
            mWidth  = Math.max(160, Math.min(w, 3840));
            mHeight = Math.max(120, Math.min(h, 2160));
            mFps    = Math.max(1, Math.min(f, 60));
            if (mStreaming) { stopStreaming(); Log.i(TAG, "quality changed — reconnect required"); }
        } catch (JSONException ignore) {}
        BugpunchTunnel.sendResponse(buildResponse(requestId, 200, getQualityJson(), "application/json"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ICE candidate drain (browser polls GET /webrtc-ice-candidates)
    // ──────────────────────────────────────────────────────────────────────────

    private String drainCandidatesJson() {
        synchronized (mIceLock) {
            if (mIceCandidates.isEmpty()) return "[]";
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < mIceCandidates.size(); i++) {
                if (i > 0) sb.append(',');
                sb.append(mIceCandidates.get(i));
            }
            sb.append(']');
            mIceCandidates.clear();
            return sb.toString();
        }
    }

    private String getQualityJson() {
        return "{\"width\":" + mWidth + ",\"height\":" + mHeight + ",\"fps\":" + mFps
               + ",\"streaming\":" + mStreaming + "}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Capture loop — uses MediaProjection (BugpunchRecorder) when debug mode
    // is active, otherwise falls back to PixelCopy on the Unity SurfaceView.
    // ──────────────────────────────────────────────────────────────────────────

    private void startCaptureLoop() {
        stopCaptureLoop();

        // Check if BugpunchRecorder has an active MediaProjection surface
        // (available when user has entered debug mode)
        android.view.Surface recorderSurface = BugpunchRecorder.getInstance().getInputSurface();
        if (recorderSurface != null && BugpunchRecorder.getInstance().isRunning()) {
            Log.i(TAG, "using MediaProjection surface for WebRTC (debug mode active)");
            startMediaProjectionCapture(recorderSurface);
        } else {
            Log.i(TAG, "using PixelCopy capture (non-debug mode)");
            startPixelCopyCapture();
        }
    }

    /**
     * MediaProjection-based capture using SurfaceTextureHelper.
     * This path is used when the user has consented to debug recording via BugpunchDebugMode.
     * The frames come from the MediaCodec encoder's input surface which receives the screen content.
     */
    private void startMediaProjectionCapture(android.view.Surface encoderSurface) {
        // Create a SurfaceTexture for receiving frames
        mSurfaceTexture = new android.graphics.SurfaceTexture(mWidth);

        // Create SurfaceTextureHelper for thread-safe texture handling
        mCaptureThread = new HandlerThread("bugpunch-webrtc");
        mCaptureThread.start();
        mCaptureHandler = new Handler(mCaptureThread.getLooper());

        mSurfaceTextureHelper = SurfaceTextureHelper.create("BugpunchVideoCapture", null, false);
        mSurfaceTextureHelper.startListening(frame -> {
            if (!mStreaming) {
                frame.release();
                return;
            }
            // Convert the VideoFrame and push to VideoSource
            pushFrameFromTexture(frame);
            frame.release();
        });

        // Connect the SurfaceTexture to the video source via capture adapter
        mVideoSource.getCapturerObserver().onCapturerStarted(true);

        // Store the encoder surface for later (for copying frames to the encoder)
        mMediaProjectionSurface = encoderSurface;

        // Start a loop to capture frames and feed them to the SurfaceTexture
        mCaptureHandler.post(mMediaProjectionCaptureRunnable);

        Log.i(TAG, "MediaProjection capture started");
    }

    private final Runnable mMediaProjectionCaptureRunnable = new Runnable() {
        @Override public void run() {
            if (!mStreaming) return;

            // Update the SurfaceTexture with the latest frame
            if (mSurfaceTexture != null) {
                mSurfaceTexture.updateTexImage();
            }

            // Calculate delay for target FPS
            long frameIntervalMs = 1000L / mFps;
            if (mCaptureHandler != null && mStreaming) {
                mCaptureHandler.postDelayed(this, frameIntervalMs);
            }
        }
    };

    /**
     * Push a frame from the SurfaceTexture to the WebRTC pipeline.
     * Uses the CapturerObserver's onFrameCaptured method.
     */
    private void pushFrameFromTexture(VideoFrame frame) {
        VideoSource vs = mVideoSource;
        if (vs == null) return;
        try {
            vs.getCapturerObserver().onFrameCaptured(frame);
        } catch (Exception e) {
            Log.w(TAG, "pushFrameFromTexture failed: " + e.getMessage());
        }
    }

    /**
     * PixelCopy-based capture for non-debug mode.
     * Captures from the Unity SurfaceView directly.
     */
    private void startPixelCopyCapture() {
        mCaptureThread = new HandlerThread("bugpunch-capture");
        mCaptureThread.start();
        mCaptureHandler = new Handler(mCaptureThread.getLooper());
        mCaptureHandler.post(mCaptureRunnable);
    }

    private void stopCaptureLoop() {
        mStreaming = false;
        if (mCaptureHandler != null) {
            mCaptureHandler.removeCallbacksAndMessages(null);
            mCaptureHandler = null;
        }
        if (mCaptureThread != null) {
            mCaptureThread.quitSafely();
            mCaptureThread = null;
        }
        mCaptureBitmap = null;
        mCachedSurface = null;

        // Clean up MediaProjection capture resources
        if (mSurfaceTextureHelper != null) {
            mSurfaceTextureHelper.stopListening();
            mSurfaceTextureHelper.dispose();
            mSurfaceTextureHelper = null;
        }
        if (mSurfaceTexture != null) {
            mSurfaceTexture.release();
            mSurfaceTexture = null;
        }
        mMediaProjectionSurface = null;
    }

    private final Runnable mCaptureRunnable = new Runnable() {
        @Override public void run() {
            if (!mStreaming) return;
            long frameIntervalMs = 1000L / mFps;
            long t0 = System.currentTimeMillis();

            SurfaceView sv = getOrFindSurface();
            if (sv == null || !sv.getHolder().getSurface().isValid()) {
                // Surface not ready — retry shortly
                if (mCaptureHandler != null) mCaptureHandler.postDelayed(this, 100);
                return;
            }

            ensureBitmap(sv.getWidth(), sv.getHeight());
            final Bitmap bmp = mCaptureBitmap;

            PixelCopy.request(sv, bmp, rc -> {
                if (rc == PixelCopy.SUCCESS && mStreaming) {
                    pushFrame(bmp);
                } else if (rc != PixelCopy.SUCCESS) {
                    Log.w(TAG, "PixelCopy failed rc=" + rc);
                }
                long elapsed = System.currentTimeMillis() - t0;
                long delay = Math.max(0, frameIntervalMs - elapsed);
                if (mStreaming && mCaptureHandler != null)
                    mCaptureHandler.postDelayed(mCaptureRunnable, delay);
            }, mCaptureHandler);
        }
    };

    private void ensureBitmap(int w, int h) {
        // Scale down to stream resolution
        int sw = mWidth, sh = mHeight;
        if (sw == 0 || sh == 0) { sw = w; sh = h; }
        if (mCaptureBitmap == null
                || mCaptureBitmap.getWidth() != sw
                || mCaptureBitmap.getHeight() != sh) {
            mCaptureBitmap = Bitmap.createBitmap(sw, sh, Bitmap.Config.ARGB_8888);
        }
    }

    private void pushFrame(Bitmap bmp) {
        VideoSource vs = mVideoSource;
        if (vs == null) return;
        int w = bmp.getWidth(), h = bmp.getHeight();
        byte[] i420 = argbToI420(bmp, w, h);
        int uvW = (w + 1) >> 1, uvH = (h + 1) >> 1;
        JavaI420Buffer buf = JavaI420Buffer.wrap(
            w, h,
            ByteBuffer.wrap(i420, 0, w * h), w,
            ByteBuffer.wrap(i420, w * h, uvW * uvH), uvW,
            ByteBuffer.wrap(i420, w * h + uvW * uvH, uvW * uvH), uvW,
            null);
        VideoFrame frame = new VideoFrame(buf, 0, System.nanoTime());
        try {
            vs.getCapturerObserver().onFrameCaptured(frame);
        } finally {
            frame.release();
        }
    }

    // ARGB_8888 → I420 (BT.601 full-range)
    private static byte[] argbToI420(Bitmap bmp, int w, int h) {
        int[] px = new int[w * h];
        bmp.getPixels(px, 0, w, 0, 0, w, h);
        int uvW = (w + 1) >> 1, uvH = (h + 1) >> 1;
        byte[] out = new byte[w * h + 2 * uvW * uvH];
        int yBase = 0, uBase = w * h, vBase = uBase + uvW * uvH;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int p = px[y * w + x];
                int r = (p >> 16) & 0xff, g = (p >> 8) & 0xff, b = p & 0xff;
                out[yBase++] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                if ((y & 1) == 0 && (x & 1) == 0) {
                    out[uBase + (y >> 1) * uvW + (x >> 1)] = (byte)(((-38*r - 74*g + 112*b + 128) >> 8) + 128);
                    out[vBase + (y >> 1) * uvW + (x >> 1)] = (byte)(((112*r - 94*g - 18*b + 128) >> 8) + 128);
                }
            }
        }
        return out;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Surface lookup
    // ──────────────────────────────────────────────────────────────────────────

    private SurfaceView getOrFindSurface() {
        if (mCachedSurface != null && mCachedSurface.isAttachedToWindow()) return mCachedSurface;
        try {
            android.app.Activity a = BugpunchUnity.currentActivity();
            if (a == null) return null;
            final SurfaceView[] found = {null};
            final CountDownLatch latch = new CountDownLatch(1);
            a.runOnUiThread(() -> {
                found[0] = findSurfaceView(a.getWindow().getDecorView());
                latch.countDown();
            });
            latch.await(1, TimeUnit.SECONDS);
            mCachedSurface = found[0];
        } catch (Throwable t) {
            Log.w(TAG, "findSurface: " + t.getMessage());
        }
        return mCachedSurface;
    }

    private static SurfaceView findSurfaceView(View v) {
        if (v instanceof SurfaceView) return (SurfaceView) v;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) {
                SurfaceView sv = findSurfaceView(g.getChildAt(i));
                if (sv != null) return sv;
            }
        }
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Bitrate cap
    // ──────────────────────────────────────────────────────────────────────────

    private void setVideoMaxBitrate(long bps) {
        synchronized (mPcLock) {
            if (mPc == null) return;
            for (org.webrtc.RtpSender sender : mPc.getSenders()) {
                if (sender.track() == null || !"video".equals(sender.track().kind())) continue;
                org.webrtc.RtpParameters params = sender.getParameters();
                if (params == null || params.encodings.isEmpty()) continue;
                for (org.webrtc.RtpParameters.Encoding enc : params.encodings) enc.maxBitrateBps = (int) bps;
                sender.setParameters(params);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cleanup
    // ──────────────────────────────────────────────────────────────────────────

    public void stopStreaming() {
        stopCaptureLoop();
        synchronized (mPcLock) {
            if (mVideoSource != null) {
                mVideoSource.getCapturerObserver().onCapturerStopped();
                mVideoSource.dispose();
                mVideoSource = null;
            }
            cleanupPc();
        }
        synchronized (mIceLock) { mIceCandidates.clear(); }
        Log.i(TAG, "streaming stopped");
    }

    private void cleanupPc() {
        if (mVideoTrack != null) { mVideoTrack.dispose(); mVideoTrack = null; }
        if (mPc != null) { mPc.close(); mPc.dispose(); mPc = null; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PeerConnectionFactory — initialized once per process
    // ──────────────────────────────────────────────────────────────────────────

    private static void ensureFactory() {
        if (sFactoryInitialized) return;
        synchronized (sFactoryLock) {
            if (sFactoryInitialized) return;
            sAppContext = BugpunchRuntime.getAppContext();
            if (sAppContext == null) { Log.w(TAG, "no app context — factory not created"); return; }
            PeerConnectionFactory.initialize(
                PeerConnectionFactory.InitializationOptions.builder(sAppContext)
                    .createInitializationOptions());
            // null sharedContext → ByteBuffer input mode (no Unity EGL sharing needed)
            PeerConnectionFactory.Builder builder = PeerConnectionFactory.builder()
                .setVideoEncoderFactory(new HardwareVideoEncoderFactory(null, false, false))
                .setVideoDecoderFactory(new DefaultVideoDecoderFactory(null));
            sFactory = builder.createPeerConnectionFactory();
            sFactoryInitialized = true;
            Log.i(TAG, "PeerConnectionFactory initialized");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Response envelope helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static String buildResponse(String requestId, int status, String body, String contentType) {
        try {
            JSONObject r = new JSONObject();
            r.put("type", "response");
            r.put("requestId", requestId);
            r.put("status", status);
            r.put("body", body);
            r.put("contentType", contentType);
            return r.toString();
        } catch (JSONException e) {
            return "{\"type\":\"response\",\"requestId\":\"" + requestId + "\",\"status\":500}";
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PeerConnection observer — verbose logging for debugging connection issues
    // ──────────────────────────────────────────────────────────────────────────

    private final class PcObserver implements PeerConnection.Observer {
        @Override public void onIceCandidate(IceCandidate candidate) {
            if (candidate == null) return;
            try {
                JSONObject j = new JSONObject();
                j.put("candidate", candidate.sdp);
                j.put("sdpMid", candidate.sdpMid);
                j.put("sdpMLineIndex", candidate.sdpMLineIndex);
                int queued;
                synchronized (mIceLock) { mIceCandidates.add(j.toString()); queued = mIceCandidates.size(); }
                Log.i(TAG, ">>> ICE candidate: mid=" + candidate.sdpMid + " idx=" + candidate.sdpMLineIndex + " sdpLen=" + (candidate.sdp != null ? candidate.sdp.length() : 0) + " queued=" + queued);
            } catch (JSONException e) {
                Log.w(TAG, "local ICE serialize failed: " + e.getMessage());
            }
        }
        @Override public void onConnectionChange(PeerConnection.PeerConnectionState state) {
            Log.i(TAG, ">>> connection state change: " + state);
            switch (state) {
                case NEW:           Log.i(TAG, "    [NEW] connection created, waiting for ICE"); break;
                case CONNECTING:    Log.i(TAG, "    [CONNECTING] establishing connection..."); break;
                case CONNECTED:     Log.i(TAG, "    [CONNECTED] WebRTC connection established!"); break;
                case DISCONNECTED:  Log.w(TAG, "    [DISCONNECTED] transient - ICE may recover"); break;
                case FAILED:        Log.e(TAG, "    [FAILED] connection failed - stopping stream"); break;
                case CLOSED:        Log.i(TAG, "    [CLOSED] connection closed"); break;
            }
            if (state == PeerConnection.PeerConnectionState.FAILED
                    || state == PeerConnection.PeerConnectionState.CLOSED) {
                Log.w(TAG, ">>> stopping stream due to connection state: " + state);
                stopStreaming();
            }
        }
        @Override public void onSignalingChange(PeerConnection.SignalingState s) {
            Log.i(TAG, ">>> signaling change: " + s);
            switch (s) {
                case STABLE:           Log.d(TAG, "    [STABLE] no SDP transaction in progress"); break;
                case HAVE_LOCAL_OFFER: Log.d(TAG, "    [HAVE_LOCAL_OFFER] waiting for answer"); break;
                case HAVE_LOCAL_PRANSWER: Log.d(TAG, "    [HAVE_LOCAL_PRANSWER] provisional answer sent"); break;
                case HAVE_REMOTE_OFFER: Log.d(TAG, "    [HAVE_REMOTE_OFFER] remote offer received"); break;
                case HAVE_REMOTE_PRANSWER: Log.d(TAG, "    [HAVE_REMOTE_PRANSWER] remote provisional answer"); break;
                case CLOSED:           Log.d(TAG, "    [CLOSED] peer connection closed"); break;
            }
        }
        @Override public void onIceConnectionChange(PeerConnection.IceConnectionState s) {
            Log.i(TAG, ">>> ICE connection change: " + s);
            switch (s) {
                case NEW:              Log.d(TAG, "    [NEW] ICE not started"); break;
                case CHECKING:         Log.d(TAG, "    [CHECKING] checking connectivity..."); break;
                case CONNECTED:        Log.i(TAG, "    [CONNECTED] ICE connected - can stream!"); break;
                case COMPLETED:        Log.i(TAG, "    [COMPLETED] ICE completed - fully connected"); break;
                case FAILED:           Log.e(TAG, "    [FAILED] ICE failed - checking network/firewall"); break;
                case DISCONNECTED:     Log.w(TAG, "    [DISCONNECTED] ICE disconnected - may recover"); break;
                case CLOSED:           Log.d(TAG, "    [CLOSED] ICE connection closed"); break;
            }
        }
        @Override public void onIceConnectionReceivingChange(boolean receiving) {
            Log.i(TAG, ">>> ICE receiving: " + receiving);
        }
        @Override public void onIceGatheringChange(PeerConnection.IceGatheringState s) {
            Log.i(TAG, ">>> ICE gathering: " + s);
            switch (s) {
                case NEW:      Log.d(TAG, "    [NEW] not gathering candidates"); break;
                case GATHERING: Log.d(TAG, "    [GATHERING] collecting candidates..."); break;
                case COMPLETE: Log.i(TAG, "    [COMPLETE] all candidates gathered"); break;
            }
        }
        @Override public void onIceCandidatesRemoved(IceCandidate[] candidates) {
            Log.d(TAG, ">>> ICE candidates removed: " + (candidates != null ? candidates.length : 0));
        }
        @Override public void onAddStream(MediaStream stream) {
            Log.i(TAG, ">>> add stream received (id=" + (stream != null ? stream.getId() : "null") + ")");
        }
        @Override public void onRemoveStream(MediaStream stream) {
            Log.i(TAG, ">>> remove stream");
        }
        @Override public void onDataChannel(DataChannel dc) {
            Log.i(TAG, ">>> data channel: label=" + (dc != null ? dc.label() : "null"));
        }
        @Override public void onRenegotiationNeeded() {
            Log.i(TAG, ">>> renegotiation needed");
        }
        @Override public void onAddTrack(RtpReceiver r, MediaStream[] streams) {
            Log.i(TAG, ">>> add track: receiver=" + (r != null ? r.id() : "null") + " streams=" + (streams != null ? streams.length : 0));
        }
        @Override public void onTrack(org.webrtc.RtpTransceiver transceiver) {
            String trackInfo = "unknown";
            if (transceiver != null && transceiver.getReceiver() != null && transceiver.getReceiver().track() != null) {
                trackInfo = transceiver.getReceiver().track().kind() + " track";
            }
            Log.i(TAG, ">>> onTrack: " + trackInfo);
        }
    }
}
