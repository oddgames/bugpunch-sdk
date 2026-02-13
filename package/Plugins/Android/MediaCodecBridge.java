package au.com.oddgames.recorder;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLExt;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.view.Surface;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;

/**
 * MediaCodecBridge — Hardware-accelerated H.264 video encoder for Unity.
 *
 * Uses MediaCodec with createInputSurface() for zero-copy GPU encoding.
 * Creates a shared EGL context with Unity's GL context to blit Unity's
 * framebuffer texture onto the encoder's input Surface.
 *
 * Called from Unity C# via AndroidJavaObject.
 */
public class MediaCodecBridge {

    private static final String TAG = "MediaCodecBridge";

    private static final String VIDEO_MIME_TYPE = "video/avc";
    private static final String AUDIO_MIME_TYPE = "audio/mp4a-latm";
    private static final int I_FRAME_INTERVAL = 2;
    private static final int AUDIO_SAMPLE_RATE = 44100;
    private static final int AUDIO_CHANNEL_COUNT = 2;
    private static final int AUDIO_BITRATE = 128000;
    private static final long DRAIN_TIMEOUT_US = 0; // non-blocking

    // Encoder state
    private MediaCodec videoEncoder;
    private MediaCodec audioEncoder;
    private MediaMuxer muxer;
    private Surface inputSurface;

    // EGL state for shared context rendering
    private EGLDisplay eglDisplay = EGL14.EGL_NO_DISPLAY;
    private EGLContext eglContext = EGL14.EGL_NO_CONTEXT;
    private EGLSurface eglSurface = EGL14.EGL_NO_SURFACE;
    private EGLContext unitySavedContext = EGL14.EGL_NO_CONTEXT;
    private EGLSurface unitySavedDrawSurface = EGL14.EGL_NO_SURFACE;
    private EGLSurface unitySavedReadSurface = EGL14.EGL_NO_SURFACE;

    // GL blit program
    private int glProgram = 0;
    private int aPositionLoc;
    private int aTexCoordLoc;
    private int uTextureLoc;
    private FloatBuffer vertexBuffer;

    // Track indices
    private int videoTrackIndex = -1;
    private int audioTrackIndex = -1;
    private int totalTracksExpected = 1; // 1 for video only, 2 if audio included
    private int tracksAdded = 0;

    // State flags
    private volatile boolean isRecording = false;
    private volatile boolean muxerStarted = false;
    private volatile boolean initialized = false;
    private boolean includeAudio = false;
    private final Object muxerLock = new Object();

    // Recording parameters
    private String outputPath;
    private int width;
    private int height;
    private int fps;
    private long startTimeNs;

    // Encoder drain thread
    private HandlerThread encoderThread;
    private Handler encoderHandler;

    // Fullscreen quad vertices: position (x, y) + texcoord (u, v)
    private static final float[] FULLSCREEN_QUAD = {
        // position      // texcoord
        -1.0f, -1.0f,    0.0f, 0.0f,
         1.0f, -1.0f,    1.0f, 0.0f,
        -1.0f,  1.0f,    0.0f, 1.0f,
         1.0f,  1.0f,    1.0f, 1.0f,
    };

    // Vertex shader source
    private static final String VERTEX_SHADER_SOURCE =
        "attribute vec4 aPosition;\n" +
        "attribute vec2 aTexCoord;\n" +
        "varying vec2 vTexCoord;\n" +
        "void main() {\n" +
        "    gl_Position = aPosition;\n" +
        "    vTexCoord = aTexCoord;\n" +
        "}\n";

    // Fragment shader source
    private static final String FRAGMENT_SHADER_SOURCE =
        "precision mediump float;\n" +
        "varying vec2 vTexCoord;\n" +
        "uniform sampler2D uTexture;\n" +
        "void main() {\n" +
        "    gl_FragColor = texture2D(uTexture, vTexCoord);\n" +
        "}\n";

    /**
     * Initialize the encoder pipeline.
     *
     * @param outputPath  Full path for the output MP4 file.
     * @param width       Video width in pixels (must be even).
     * @param height      Video height in pixels (must be even).
     * @param fps         Target frames per second.
     * @param bitrate     Video bitrate in bits/sec (e.g. 8_000_000 for 8 Mbps).
     * @param includeAudio Whether to set up an audio encoder track.
     * @return true on success, false on failure.
     */
    public boolean init(String outputPath, int width, int height, int fps, int bitrate, boolean includeAudio) {
        Log.i(TAG, "init: " + width + "x" + height + " @ " + fps + "fps, bitrate=" + bitrate
                + ", audio=" + includeAudio + ", output=" + outputPath);

        if (initialized) {
            Log.w(TAG, "init: Already initialized, call stopRecording first.");
            return false;
        }

        this.outputPath = outputPath;
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.includeAudio = includeAudio;
        this.totalTracksExpected = includeAudio ? 2 : 1;
        this.tracksAdded = 0;
        this.videoTrackIndex = -1;
        this.audioTrackIndex = -1;
        this.muxerStarted = false;

        try {
            // ---- Video encoder ----
            if (!initVideoEncoder(bitrate)) {
                release();
                return false;
            }

            // ---- EGL shared context ----
            if (!initEglContext()) {
                Log.w(TAG, "init: EGL shared context creation failed. "
                        + "captureFrame() will not work; use captureFrameFromPixels() instead.");
                // Do NOT fail entirely — the pixel-copy fallback path is still usable.
            }

            // ---- GL blit shader ----
            if (eglContext != EGL14.EGL_NO_CONTEXT) {
                if (!initGlProgram()) {
                    Log.w(TAG, "init: GL program creation failed. Shared-context path unavailable.");
                    destroyEglContext();
                }
            }

            // ---- Audio encoder (optional) ----
            if (includeAudio) {
                if (!initAudioEncoder()) {
                    Log.w(TAG, "init: Audio encoder creation failed. Continuing without audio.");
                    this.includeAudio = false;
                    this.totalTracksExpected = 1;
                }
            }

            // ---- MediaMuxer ----
            muxer = new MediaMuxer(outputPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

            // ---- Encoder drain thread ----
            encoderThread = new HandlerThread("MediaCodecBridge-Encoder");
            encoderThread.start();
            encoderHandler = new Handler(encoderThread.getLooper());

            initialized = true;
            Log.i(TAG, "init: Success.");
            return true;

        } catch (IOException e) {
            Log.e(TAG, "init: IOException during setup.", e);
            release();
            return false;
        } catch (Exception e) {
            Log.e(TAG, "init: Unexpected exception during setup.", e);
            release();
            return false;
        }
    }

    /**
     * Begin recording. Must be called after init().
     */
    public void startRecording() {
        if (!initialized) {
            Log.e(TAG, "startRecording: Not initialized.");
            return;
        }
        if (isRecording) {
            Log.w(TAG, "startRecording: Already recording.");
            return;
        }

        isRecording = true;
        startTimeNs = System.nanoTime();
        Log.i(TAG, "startRecording: Recording started.");
    }

    /**
     * Capture a frame from a Unity GL texture via the shared EGL context.
     * Must be called on Unity's render thread (GL context must be current).
     *
     * @param textureId The Unity texture GL name (e.g. from RenderTexture.GetNativeTexturePtr()).
     */
    public void captureFrame(int textureId) {
        if (!isRecording) {
            return;
        }
        if (eglContext == EGL14.EGL_NO_CONTEXT) {
            Log.w(TAG, "captureFrame: No shared EGL context available. Use captureFrameFromPixels().");
            return;
        }

        long timestampNs = System.nanoTime() - startTimeNs;

        try {
            // Save Unity's current EGL state
            unitySavedContext = EGL14.eglGetCurrentContext();
            unitySavedDrawSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_DRAW);
            unitySavedReadSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_READ);

            // Make the shared encoder context current
            if (!EGL14.eglMakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext)) {
                Log.e(TAG, "captureFrame: eglMakeCurrent failed for encoder context.");
                restoreUnityEglContext();
                return;
            }

            // Set viewport to match encoder dimensions
            GLES20.glViewport(0, 0, width, height);

            // Clear
            GLES20.glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);

            // Use blit program
            GLES20.glUseProgram(glProgram);

            // Bind the Unity texture
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, textureId);
            GLES20.glUniform1i(uTextureLoc, 0);

            // Set up vertex attributes
            vertexBuffer.position(0);
            GLES20.glEnableVertexAttribArray(aPositionLoc);
            GLES20.glVertexAttribPointer(aPositionLoc, 2, GLES20.GL_FLOAT, false, 16, vertexBuffer);

            vertexBuffer.position(2);
            GLES20.glEnableVertexAttribArray(aTexCoordLoc);
            GLES20.glVertexAttribPointer(aTexCoordLoc, 2, GLES20.GL_FLOAT, false, 16, vertexBuffer);

            // Draw fullscreen quad
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);

            // Disable attributes
            GLES20.glDisableVertexAttribArray(aPositionLoc);
            GLES20.glDisableVertexAttribArray(aTexCoordLoc);

            // Set presentation timestamp
            EGLExt.eglPresentationTimeANDROID(eglDisplay, eglSurface, timestampNs);

            // Swap — submits the frame to the encoder
            EGL14.eglSwapBuffers(eglDisplay, eglSurface);

            // Restore Unity's EGL context
            restoreUnityEglContext();

            // Drain encoder output (non-blocking)
            drainEncoder(videoEncoder, true);

        } catch (Exception e) {
            Log.e(TAG, "captureFrame: Exception during frame capture.", e);
            restoreUnityEglContext();
        }
    }

    /**
     * Fallback frame capture using raw RGBA pixel data.
     * Use this when the shared EGL context path is unavailable (e.g. Vulkan renderer).
     * Data is provided from C# via AsyncGPUReadback.
     *
     * @param rgbaData    Raw RGBA pixel data (width * height * 4 bytes).
     * @param width       Frame width.
     * @param height      Frame height.
     * @param timestampNs Presentation timestamp relative to recording start, in nanoseconds.
     */
    public void captureFrameFromPixels(byte[] rgbaData, int width, int height, long timestampNs) {
        if (!isRecording) {
            return;
        }
        if (videoEncoder == null) {
            Log.e(TAG, "captureFrameFromPixels: Video encoder is null.");
            return;
        }
        if (rgbaData == null || rgbaData.length < width * height * 4) {
            Log.e(TAG, "captureFrameFromPixels: Invalid rgbaData.");
            return;
        }

        try {
            // For the pixel-copy path, we need to render the pixel data onto the
            // input surface using EGL. If EGL is available, upload as texture and blit.
            // If not, this path cannot work either — log an error.
            if (eglContext == EGL14.EGL_NO_CONTEXT) {
                Log.e(TAG, "captureFrameFromPixels: No EGL context available. Cannot encode.");
                return;
            }

            // Save and switch EGL context
            unitySavedContext = EGL14.eglGetCurrentContext();
            unitySavedDrawSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_DRAW);
            unitySavedReadSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_READ);

            if (!EGL14.eglMakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext)) {
                Log.e(TAG, "captureFrameFromPixels: eglMakeCurrent failed.");
                restoreUnityEglContext();
                return;
            }

            // Create a temporary texture and upload the pixel data
            int[] texId = new int[1];
            GLES20.glGenTextures(1, texId, 0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, texId[0]);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);

            ByteBuffer pixelBuf = ByteBuffer.allocateDirect(rgbaData.length);
            pixelBuf.put(rgbaData);
            pixelBuf.position(0);

            GLES20.glTexImage2D(GLES20.GL_TEXTURE_2D, 0, GLES20.GL_RGBA,
                    width, height, 0, GLES20.GL_RGBA, GLES20.GL_UNSIGNED_BYTE, pixelBuf);

            // Blit to encoder surface
            GLES20.glViewport(0, 0, this.width, this.height);
            GLES20.glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);

            GLES20.glUseProgram(glProgram);
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, texId[0]);
            GLES20.glUniform1i(uTextureLoc, 0);

            vertexBuffer.position(0);
            GLES20.glEnableVertexAttribArray(aPositionLoc);
            GLES20.glVertexAttribPointer(aPositionLoc, 2, GLES20.GL_FLOAT, false, 16, vertexBuffer);

            vertexBuffer.position(2);
            GLES20.glEnableVertexAttribArray(aTexCoordLoc);
            GLES20.glVertexAttribPointer(aTexCoordLoc, 2, GLES20.GL_FLOAT, false, 16, vertexBuffer);

            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);

            GLES20.glDisableVertexAttribArray(aPositionLoc);
            GLES20.glDisableVertexAttribArray(aTexCoordLoc);

            // Clean up temporary texture
            GLES20.glDeleteTextures(1, texId, 0);

            // Set presentation timestamp and swap
            EGLExt.eglPresentationTimeANDROID(eglDisplay, eglSurface, timestampNs);
            EGL14.eglSwapBuffers(eglDisplay, eglSurface);

            // Restore Unity context
            restoreUnityEglContext();

            // Drain encoder output
            drainEncoder(videoEncoder, true);

        } catch (Exception e) {
            Log.e(TAG, "captureFrameFromPixels: Exception.", e);
            restoreUnityEglContext();
        }
    }

    /**
     * Encode a chunk of PCM audio data.
     * Called from C# with data from OnAudioFilterRead.
     *
     * @param pcmData     Raw PCM audio samples (16-bit interleaved stereo).
     * @param timestampNs Presentation timestamp relative to recording start, in nanoseconds.
     */
    public void encodeAudioFrame(byte[] pcmData, long timestampNs) {
        if (!isRecording || audioEncoder == null) {
            return;
        }
        if (pcmData == null || pcmData.length == 0) {
            return;
        }

        try {
            int inputIndex = audioEncoder.dequeueInputBuffer(10000); // 10ms timeout
            if (inputIndex < 0) {
                Log.w(TAG, "encodeAudioFrame: No input buffer available, dropping audio frame.");
                return;
            }

            ByteBuffer inputBuffer = audioEncoder.getInputBuffer(inputIndex);
            if (inputBuffer == null) {
                Log.e(TAG, "encodeAudioFrame: getInputBuffer returned null for index " + inputIndex);
                return;
            }

            inputBuffer.clear();
            int bytesToCopy = Math.min(pcmData.length, inputBuffer.capacity());
            inputBuffer.put(pcmData, 0, bytesToCopy);

            audioEncoder.queueInputBuffer(inputIndex, 0, bytesToCopy, timestampNs / 1000, 0);

            drainEncoder(audioEncoder, false);

        } catch (MediaCodec.CodecException e) {
            Log.e(TAG, "encodeAudioFrame: CodecException.", e);
        } catch (Exception e) {
            Log.e(TAG, "encodeAudioFrame: Exception.", e);
        }
    }

    /**
     * Stop recording, finalize the MP4 file, and release all resources.
     *
     * @return The output file path on success, or empty string on failure.
     */
    public String stopRecording() {
        Log.i(TAG, "stopRecording: Stopping...");

        if (!initialized) {
            Log.w(TAG, "stopRecording: Not initialized.");
            return "";
        }

        isRecording = false;

        String result = outputPath != null ? outputPath : "";

        try {
            // Signal EOS to video encoder
            if (videoEncoder != null) {
                Log.d(TAG, "stopRecording: Signaling video EOS.");
                videoEncoder.signalEndOfInputStream();
                drainEncoderToEos(videoEncoder, true);
            }

            // Signal EOS to audio encoder
            if (audioEncoder != null) {
                Log.d(TAG, "stopRecording: Signaling audio EOS.");
                int inputIndex = audioEncoder.dequeueInputBuffer(10000);
                if (inputIndex >= 0) {
                    audioEncoder.queueInputBuffer(inputIndex, 0, 0, 0,
                            MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                }
                drainEncoderToEos(audioEncoder, false);
            }

        } catch (Exception e) {
            Log.e(TAG, "stopRecording: Exception during EOS signaling.", e);
        }

        // Release everything
        release();

        Log.i(TAG, "stopRecording: Complete. Output: " + result);
        return result;
    }

    /**
     * Query whether recording is currently active.
     */
    public boolean isRecording() {
        return isRecording;
    }

    /**
     * Check whether the shared EGL context path is available.
     * If false, callers must use captureFrameFromPixels() instead of captureFrame().
     */
    public boolean isEglContextAvailable() {
        return eglContext != EGL14.EGL_NO_CONTEXT && glProgram != 0;
    }

    // =========================================================================
    // Private: Initialization helpers
    // =========================================================================

    private boolean initVideoEncoder(int bitrate) {
        Log.d(TAG, "initVideoEncoder: Creating H.264 encoder.");

        // Verify that a hardware H.264 encoder is available
        MediaCodecList codecList = new MediaCodecList(MediaCodecList.REGULAR_CODECS);
        String encoderName = null;
        for (MediaCodecInfo info : codecList.getCodecInfos()) {
            if (!info.isEncoder()) continue;
            for (String type : info.getSupportedTypes()) {
                if (type.equalsIgnoreCase(VIDEO_MIME_TYPE)) {
                    encoderName = info.getName();
                    break;
                }
            }
            if (encoderName != null) break;
        }

        if (encoderName == null) {
            Log.e(TAG, "initVideoEncoder: No H.264 encoder found on this device.");
            return false;
        }

        Log.d(TAG, "initVideoEncoder: Using encoder: " + encoderName);

        try {
            MediaFormat format = MediaFormat.createVideoFormat(VIDEO_MIME_TYPE, width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT,
                    MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, I_FRAME_INTERVAL);

            videoEncoder = MediaCodec.createEncoderByType(VIDEO_MIME_TYPE);
            videoEncoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);

            // Create the input surface BEFORE starting the encoder
            inputSurface = videoEncoder.createInputSurface();
            if (inputSurface == null) {
                Log.e(TAG, "initVideoEncoder: createInputSurface() returned null.");
                return false;
            }

            videoEncoder.start();
            Log.d(TAG, "initVideoEncoder: Video encoder started.");
            return true;

        } catch (IOException e) {
            Log.e(TAG, "initVideoEncoder: Failed to create encoder.", e);
            return false;
        } catch (MediaCodec.CodecException e) {
            Log.e(TAG, "initVideoEncoder: CodecException during configure/start.", e);
            return false;
        } catch (Exception e) {
            Log.e(TAG, "initVideoEncoder: Unexpected exception.", e);
            return false;
        }
    }

    private boolean initAudioEncoder() {
        Log.d(TAG, "initAudioEncoder: Creating AAC encoder.");

        try {
            MediaFormat format = MediaFormat.createAudioFormat(AUDIO_MIME_TYPE,
                    AUDIO_SAMPLE_RATE, AUDIO_CHANNEL_COUNT);
            format.setInteger(MediaFormat.KEY_BIT_RATE, AUDIO_BITRATE);
            format.setInteger(MediaFormat.KEY_AAC_PROFILE,
                    MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);

            audioEncoder = MediaCodec.createEncoderByType(AUDIO_MIME_TYPE);
            audioEncoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            audioEncoder.start();

            Log.d(TAG, "initAudioEncoder: Audio encoder started.");
            return true;

        } catch (IOException e) {
            Log.e(TAG, "initAudioEncoder: Failed to create encoder.", e);
            return false;
        } catch (MediaCodec.CodecException e) {
            Log.e(TAG, "initAudioEncoder: CodecException.", e);
            return false;
        } catch (Exception e) {
            Log.e(TAG, "initAudioEncoder: Unexpected exception.", e);
            return false;
        }
    }

    private boolean initEglContext() {
        Log.d(TAG, "initEglContext: Creating shared EGL context.");

        try {
            // Get Unity's current EGL state
            eglDisplay = EGL14.eglGetCurrentDisplay();
            if (eglDisplay == EGL14.EGL_NO_DISPLAY) {
                Log.e(TAG, "initEglContext: eglGetCurrentDisplay returned EGL_NO_DISPLAY. "
                        + "Likely running with Vulkan renderer.");
                return false;
            }

            EGLContext unityContext = EGL14.eglGetCurrentContext();
            if (unityContext == EGL14.EGL_NO_CONTEXT) {
                Log.e(TAG, "initEglContext: eglGetCurrentContext returned EGL_NO_CONTEXT.");
                return false;
            }

            // Choose an EGL config
            int[] configAttribs = {
                EGL14.EGL_RED_SIZE, 8,
                EGL14.EGL_GREEN_SIZE, 8,
                EGL14.EGL_BLUE_SIZE, 8,
                EGL14.EGL_ALPHA_SIZE, 8,
                EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
                EGL14.EGL_NONE
            };

            EGLConfig[] configs = new EGLConfig[1];
            int[] numConfigs = new int[1];
            if (!EGL14.eglChooseConfig(eglDisplay, configAttribs, 0, configs, 0, 1, numConfigs, 0)) {
                Log.e(TAG, "initEglContext: eglChooseConfig failed.");
                return false;
            }
            if (numConfigs[0] == 0) {
                Log.e(TAG, "initEglContext: No suitable EGL config found.");
                return false;
            }

            EGLConfig eglConfig = configs[0];

            // Create a new EGL context sharing with Unity's context
            int[] contextAttribs = {
                EGL14.EGL_CONTEXT_CLIENT_VERSION, 2,
                EGL14.EGL_NONE
            };

            eglContext = EGL14.eglCreateContext(eglDisplay, eglConfig, unityContext,
                    contextAttribs, 0);
            if (eglContext == EGL14.EGL_NO_CONTEXT) {
                Log.e(TAG, "initEglContext: eglCreateContext failed. EGL error: "
                        + EGL14.eglGetError());
                return false;
            }

            // Create an EGL window surface from the encoder's input Surface
            int[] surfaceAttribs = { EGL14.EGL_NONE };
            eglSurface = EGL14.eglCreateWindowSurface(eglDisplay, eglConfig,
                    inputSurface, surfaceAttribs, 0);
            if (eglSurface == EGL14.EGL_NO_SURFACE) {
                Log.e(TAG, "initEglContext: eglCreateWindowSurface failed. EGL error: "
                        + EGL14.eglGetError());
                EGL14.eglDestroyContext(eglDisplay, eglContext);
                eglContext = EGL14.EGL_NO_CONTEXT;
                return false;
            }

            Log.d(TAG, "initEglContext: Shared EGL context created successfully.");
            return true;

        } catch (Exception e) {
            Log.e(TAG, "initEglContext: Exception.", e);
            return false;
        }
    }

    private boolean initGlProgram() {
        Log.d(TAG, "initGlProgram: Compiling blit shaders.");

        try {
            // Temporarily make the shared context current
            EGLContext prevContext = EGL14.eglGetCurrentContext();
            EGLSurface prevDrawSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_DRAW);
            EGLSurface prevReadSurface = EGL14.eglGetCurrentSurface(EGL14.EGL_READ);

            if (!EGL14.eglMakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext)) {
                Log.e(TAG, "initGlProgram: eglMakeCurrent failed.");
                return false;
            }

            // Compile vertex shader
            int vertexShader = compileShader(GLES20.GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE);
            if (vertexShader == 0) {
                restoreEglContext(prevContext, prevDrawSurface, prevReadSurface);
                return false;
            }

            // Compile fragment shader
            int fragmentShader = compileShader(GLES20.GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE);
            if (fragmentShader == 0) {
                GLES20.glDeleteShader(vertexShader);
                restoreEglContext(prevContext, prevDrawSurface, prevReadSurface);
                return false;
            }

            // Link program
            glProgram = GLES20.glCreateProgram();
            GLES20.glAttachShader(glProgram, vertexShader);
            GLES20.glAttachShader(glProgram, fragmentShader);
            GLES20.glLinkProgram(glProgram);

            int[] linkStatus = new int[1];
            GLES20.glGetProgramiv(glProgram, GLES20.GL_LINK_STATUS, linkStatus, 0);
            if (linkStatus[0] != GLES20.GL_TRUE) {
                String log = GLES20.glGetProgramInfoLog(glProgram);
                Log.e(TAG, "initGlProgram: Program link failed: " + log);
                GLES20.glDeleteProgram(glProgram);
                glProgram = 0;
                GLES20.glDeleteShader(vertexShader);
                GLES20.glDeleteShader(fragmentShader);
                restoreEglContext(prevContext, prevDrawSurface, prevReadSurface);
                return false;
            }

            // Shaders are linked into the program; we can release them
            GLES20.glDeleteShader(vertexShader);
            GLES20.glDeleteShader(fragmentShader);

            // Get attribute/uniform locations
            aPositionLoc = GLES20.glGetAttribLocation(glProgram, "aPosition");
            aTexCoordLoc = GLES20.glGetAttribLocation(glProgram, "aTexCoord");
            uTextureLoc = GLES20.glGetUniformLocation(glProgram, "uTexture");

            // Create vertex buffer
            ByteBuffer bb = ByteBuffer.allocateDirect(FULLSCREEN_QUAD.length * 4);
            bb.order(ByteOrder.nativeOrder());
            vertexBuffer = bb.asFloatBuffer();
            vertexBuffer.put(FULLSCREEN_QUAD);
            vertexBuffer.position(0);

            // Restore previous EGL context
            restoreEglContext(prevContext, prevDrawSurface, prevReadSurface);

            Log.d(TAG, "initGlProgram: Blit shader compiled and linked.");
            return true;

        } catch (Exception e) {
            Log.e(TAG, "initGlProgram: Exception.", e);
            return false;
        }
    }

    private int compileShader(int type, String source) {
        int shader = GLES20.glCreateShader(type);
        if (shader == 0) {
            Log.e(TAG, "compileShader: glCreateShader failed for type " + type);
            return 0;
        }

        GLES20.glShaderSource(shader, source);
        GLES20.glCompileShader(shader);

        int[] compileStatus = new int[1];
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, compileStatus, 0);
        if (compileStatus[0] != GLES20.GL_TRUE) {
            String log = GLES20.glGetShaderInfoLog(shader);
            Log.e(TAG, "compileShader: Compilation failed: " + log);
            GLES20.glDeleteShader(shader);
            return 0;
        }

        return shader;
    }

    // =========================================================================
    // Private: Encoder drain
    // =========================================================================

    /**
     * Drain available output from an encoder (non-blocking).
     * Writes encoded data to the muxer when ready.
     *
     * @param codec   The MediaCodec to drain.
     * @param isVideo true for video track, false for audio track.
     */
    private void drainEncoder(MediaCodec codec, boolean isVideo) {
        if (codec == null) return;

        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

        while (true) {
            int outputIndex;
            try {
                outputIndex = codec.dequeueOutputBuffer(bufferInfo, DRAIN_TIMEOUT_US);
            } catch (MediaCodec.CodecException e) {
                Log.e(TAG, "drainEncoder: CodecException during dequeueOutputBuffer.", e);
                break;
            } catch (IllegalStateException e) {
                Log.e(TAG, "drainEncoder: IllegalStateException (codec may be released).", e);
                break;
            }

            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                // No output available right now
                break;

            } else if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                handleFormatChanged(codec, isVideo);

            } else if (outputIndex >= 0) {
                ByteBuffer outputBuffer = codec.getOutputBuffer(outputIndex);
                if (outputBuffer == null) {
                    Log.w(TAG, "drainEncoder: getOutputBuffer returned null for index " + outputIndex);
                    codec.releaseOutputBuffer(outputIndex, false);
                    continue;
                }

                // Skip codec-specific data (SPS/PPS) — muxer handles it via format
                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
                    bufferInfo.size = 0;
                }

                if (bufferInfo.size > 0) {
                    synchronized (muxerLock) {
                        if (muxerStarted) {
                            int trackIndex = isVideo ? videoTrackIndex : audioTrackIndex;
                            if (trackIndex >= 0) {
                                outputBuffer.position(bufferInfo.offset);
                                outputBuffer.limit(bufferInfo.offset + bufferInfo.size);
                                try {
                                    muxer.writeSampleData(trackIndex, outputBuffer, bufferInfo);
                                } catch (IllegalStateException e) {
                                    Log.e(TAG, "drainEncoder: writeSampleData failed.", e);
                                } catch (IllegalArgumentException e) {
                                    Log.e(TAG, "drainEncoder: writeSampleData bad argument.", e);
                                }
                            }
                        }
                    }
                }

                codec.releaseOutputBuffer(outputIndex, false);

                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    Log.d(TAG, "drainEncoder: EOS received for " + (isVideo ? "video" : "audio"));
                    break;
                }
            }
        }
    }

    /**
     * Drain an encoder until EOS is received (blocking with timeout).
     * Used during stopRecording to flush all remaining data.
     */
    private void drainEncoderToEos(MediaCodec codec, boolean isVideo) {
        if (codec == null) return;

        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();
        long deadlineMs = System.currentTimeMillis() + 5000; // 5 second timeout

        while (System.currentTimeMillis() < deadlineMs) {
            int outputIndex;
            try {
                outputIndex = codec.dequeueOutputBuffer(bufferInfo, 10000); // 10ms timeout
            } catch (MediaCodec.CodecException e) {
                Log.e(TAG, "drainEncoderToEos: CodecException.", e);
                break;
            } catch (IllegalStateException e) {
                Log.e(TAG, "drainEncoderToEos: IllegalStateException.", e);
                break;
            }

            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                continue;

            } else if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                handleFormatChanged(codec, isVideo);

            } else if (outputIndex >= 0) {
                ByteBuffer outputBuffer = codec.getOutputBuffer(outputIndex);

                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
                    bufferInfo.size = 0;
                }

                if (bufferInfo.size > 0 && outputBuffer != null) {
                    synchronized (muxerLock) {
                        if (muxerStarted) {
                            int trackIndex = isVideo ? videoTrackIndex : audioTrackIndex;
                            if (trackIndex >= 0) {
                                outputBuffer.position(bufferInfo.offset);
                                outputBuffer.limit(bufferInfo.offset + bufferInfo.size);
                                try {
                                    muxer.writeSampleData(trackIndex, outputBuffer, bufferInfo);
                                } catch (Exception e) {
                                    Log.e(TAG, "drainEncoderToEos: writeSampleData failed.", e);
                                }
                            }
                        }
                    }
                }

                codec.releaseOutputBuffer(outputIndex, false);

                if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    Log.d(TAG, "drainEncoderToEos: EOS received for "
                            + (isVideo ? "video" : "audio"));
                    return;
                }
            }
        }

        Log.w(TAG, "drainEncoderToEos: Timed out waiting for EOS ("
                + (isVideo ? "video" : "audio") + ").");
    }

    /**
     * Handle INFO_OUTPUT_FORMAT_CHANGED: add the track to the muxer,
     * and start the muxer once all expected tracks are added.
     */
    private void handleFormatChanged(MediaCodec codec, boolean isVideo) {
        MediaFormat newFormat = codec.getOutputFormat();
        Log.d(TAG, "handleFormatChanged: " + (isVideo ? "video" : "audio")
                + " format: " + newFormat);

        synchronized (muxerLock) {
            if (muxer == null) {
                Log.e(TAG, "handleFormatChanged: Muxer is null.");
                return;
            }

            if (isVideo) {
                if (videoTrackIndex >= 0) {
                    Log.w(TAG, "handleFormatChanged: Video track already added.");
                    return;
                }
                videoTrackIndex = muxer.addTrack(newFormat);
                Log.d(TAG, "handleFormatChanged: Video track index = " + videoTrackIndex);
            } else {
                if (audioTrackIndex >= 0) {
                    Log.w(TAG, "handleFormatChanged: Audio track already added.");
                    return;
                }
                audioTrackIndex = muxer.addTrack(newFormat);
                Log.d(TAG, "handleFormatChanged: Audio track index = " + audioTrackIndex);
            }

            tracksAdded++;

            if (tracksAdded >= totalTracksExpected && !muxerStarted) {
                Log.i(TAG, "handleFormatChanged: All tracks added. Starting muxer.");
                try {
                    muxer.start();
                    muxerStarted = true;
                } catch (IllegalStateException e) {
                    Log.e(TAG, "handleFormatChanged: Failed to start muxer.", e);
                }
            }
        }
    }

    // =========================================================================
    // Private: EGL helpers
    // =========================================================================

    private void restoreUnityEglContext() {
        if (unitySavedContext != EGL14.EGL_NO_CONTEXT) {
            EGL14.eglMakeCurrent(eglDisplay, unitySavedDrawSurface, unitySavedReadSurface,
                    unitySavedContext);
        }
    }

    private void restoreEglContext(EGLContext context, EGLSurface drawSurface,
                                   EGLSurface readSurface) {
        if (context != EGL14.EGL_NO_CONTEXT) {
            EGL14.eglMakeCurrent(eglDisplay, drawSurface, readSurface, context);
        }
    }

    private void destroyEglContext() {
        if (eglDisplay != EGL14.EGL_NO_DISPLAY) {
            // Make sure we're not current on the shared context
            EGL14.eglMakeCurrent(eglDisplay, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE,
                    EGL14.EGL_NO_CONTEXT);

            if (eglSurface != EGL14.EGL_NO_SURFACE) {
                EGL14.eglDestroySurface(eglDisplay, eglSurface);
                eglSurface = EGL14.EGL_NO_SURFACE;
            }
            if (eglContext != EGL14.EGL_NO_CONTEXT) {
                EGL14.eglDestroyContext(eglDisplay, eglContext);
                eglContext = EGL14.EGL_NO_CONTEXT;
            }
        }
        // Do NOT destroy eglDisplay — it's Unity's display, not ours.
        eglDisplay = EGL14.EGL_NO_DISPLAY;
    }

    // =========================================================================
    // Private: Resource release
    // =========================================================================

    /**
     * Release all resources. Safe to call multiple times.
     */
    private void release() {
        Log.d(TAG, "release: Releasing all resources.");

        isRecording = false;

        // Stop video encoder
        if (videoEncoder != null) {
            try {
                videoEncoder.stop();
            } catch (Exception e) {
                Log.w(TAG, "release: videoEncoder.stop() failed.", e);
            }
            try {
                videoEncoder.release();
            } catch (Exception e) {
                Log.w(TAG, "release: videoEncoder.release() failed.", e);
            }
            videoEncoder = null;
        }

        // Stop audio encoder
        if (audioEncoder != null) {
            try {
                audioEncoder.stop();
            } catch (Exception e) {
                Log.w(TAG, "release: audioEncoder.stop() failed.", e);
            }
            try {
                audioEncoder.release();
            } catch (Exception e) {
                Log.w(TAG, "release: audioEncoder.release() failed.", e);
            }
            audioEncoder = null;
        }

        // Stop muxer
        synchronized (muxerLock) {
            if (muxer != null) {
                if (muxerStarted) {
                    try {
                        muxer.stop();
                    } catch (Exception e) {
                        Log.w(TAG, "release: muxer.stop() failed.", e);
                    }
                    muxerStarted = false;
                }
                try {
                    muxer.release();
                } catch (Exception e) {
                    Log.w(TAG, "release: muxer.release() failed.", e);
                }
                muxer = null;
            }
        }

        // Delete GL program
        if (glProgram != 0) {
            // We'd need the EGL context current to delete GL resources.
            // If EGL context is still valid, do so; otherwise, they're leaked
            // (GPU will reclaim them when the context is destroyed).
            if (eglContext != EGL14.EGL_NO_CONTEXT && eglDisplay != EGL14.EGL_NO_DISPLAY) {
                try {
                    EGL14.eglMakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext);
                    GLES20.glDeleteProgram(glProgram);
                } catch (Exception e) {
                    Log.w(TAG, "release: Failed to delete GL program.", e);
                }
            }
            glProgram = 0;
        }

        // Destroy EGL context and surface
        destroyEglContext();

        // Release input surface
        if (inputSurface != null) {
            try {
                inputSurface.release();
            } catch (Exception e) {
                Log.w(TAG, "release: inputSurface.release() failed.", e);
            }
            inputSurface = null;
        }

        // Stop encoder thread
        if (encoderThread != null) {
            try {
                encoderThread.quitSafely();
                encoderThread.join(2000);
            } catch (InterruptedException e) {
                Log.w(TAG, "release: Interrupted while waiting for encoder thread.", e);
            }
            encoderThread = null;
            encoderHandler = null;
        }

        // Reset state
        videoTrackIndex = -1;
        audioTrackIndex = -1;
        tracksAdded = 0;
        initialized = false;
        vertexBuffer = null;

        Log.d(TAG, "release: Done.");
    }
}
