# Building the Android Recorder Plugin

## Quick Start (Source File in Unity)

For initial testing and development, the `.java` source file can be placed directly in
`Plugins/Android/` inside your Unity project. Unity's build pipeline will compile it
automatically when building an APK/AAB. No separate build step is needed.

**Files to include in Unity:**
- `MediaCodecBridge.java` — Main encoder bridge class
- `AndroidManifest.xml` — Minimal manifest (no permissions)

Place these at:
```
YourUnityProject/
  Assets/
    Plugins/
      Android/
        MediaCodecBridge.java
        AndroidManifest.xml
```

> **Note**: Unity compiles `.java` files in `Plugins/Android/` using the Android SDK
> configured in Preferences > External Tools. Ensure the Android SDK and NDK are
> installed and configured.

## Building as AAR (Optional, for Release)

If you prefer to distribute a pre-compiled AAR instead of raw source:

### Prerequisites

- Android Studio (Arctic Fox or later)
- Android SDK with API level 34 (or latest)
- Minimum SDK: 21 (Android 5.0 Lollipop)

### Project Setup

1. Open Android Studio and create a new **Android Library** module.

2. Configure `build.gradle`:

```groovy
plugins {
    id 'com.android.library'
}

android {
    namespace 'au.com.oddgames.recorder'
    compileSdk 34

    defaultConfig {
        minSdk 21
        targetSdk 34
    }

    buildTypes {
        release {
            minifyEnabled false
            proguardFiles getDefaultProguardFile('proguard-android-optimize.txt')
        }
    }

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_1_8
        targetCompatibility JavaVersion.VERSION_1_8
    }
}

dependencies {
    // No external dependencies.
    // Only uses android.media.* and android.opengl.* from the Android SDK.
}
```

3. Place `MediaCodecBridge.java` in:
   ```
   src/main/java/au/com/oddgames/recorder/MediaCodecBridge.java
   ```

4. Place `AndroidManifest.xml` in:
   ```
   src/main/AndroidManifest.xml
   ```

### Build

```bash
./gradlew :recorder:assembleRelease
```

Output AAR will be at:
```
build/outputs/aar/recorder-release.aar
```

Rename to `recorder-plugin.aar` and place in Unity at:
```
Assets/Plugins/Android/recorder-plugin.aar
```

### AAR Contents

The AAR contains only:
- Compiled `.class` files (from `MediaCodecBridge.java`)
- `AndroidManifest.xml`
- No native `.so` libraries
- No third-party dependencies

## API Summary

The plugin is used from Unity C# via `AndroidJavaObject`:

```csharp
// Create the bridge
var bridge = new AndroidJavaObject("au.com.oddgames.recorder.MediaCodecBridge");

// Initialize encoder
bool success = bridge.Call<bool>("init", outputPath, width, height, fps, bitrate, includeAudio);

// Check if GPU-direct path is available
bool eglAvailable = bridge.Call<bool>("isEglContextAvailable");

// Start recording
bridge.Call("startRecording");

// Each frame (on render thread for captureFrame):
bridge.Call("captureFrame", textureId);
// OR fallback:
bridge.Call("captureFrameFromPixels", rgbaBytes, width, height, timestampNs);

// Audio frames (from OnAudioFilterRead):
bridge.Call("encodeAudioFrame", pcmBytes, timestampNs);

// Stop and finalize
string outputFile = bridge.Call<string>("stopRecording");
```

## Dependencies

**None.** The plugin uses only standard Android SDK APIs:

| Package | Usage |
|---------|-------|
| `android.media.MediaCodec` | Hardware H.264/AAC encoding |
| `android.media.MediaMuxer` | MP4 container muxing |
| `android.media.MediaFormat` | Codec configuration |
| `android.opengl.EGL14` | Shared EGL context creation |
| `android.opengl.EGLExt` | `eglPresentationTimeANDROID` for frame timing |
| `android.opengl.GLES20` | Texture blit to encoder surface |

## Minimum Android Version

- **Minimum SDK 21** (Android 5.0 Lollipop)
  - `MediaCodec.createInputSurface()` requires API 18
  - `EGL14` requires API 17
  - `EGLExt.eglPresentationTimeANDROID` requires API 18
  - API 21 chosen for broad compatibility while ensuring all required APIs exist

## Permissions

**No permissions required.** The plugin:
- Does NOT use `MediaProjection` (no screen capture permission)
- Does NOT access the microphone (no `RECORD_AUDIO` permission)
- Does NOT need `WRITE_EXTERNAL_STORAGE` (caller provides the full output path)
- Encodes frames provided directly by Unity via the shared GL context
- Receives audio PCM data from Unity's `OnAudioFilterRead` callback

## Architecture Notes

### GPU-Direct Path (OpenGL ES)
When Unity uses OpenGL ES, the plugin creates a shared EGL context that can read
Unity's textures directly. Frames are blitted to the encoder's input Surface via
a simple fullscreen quad shader. This is the zero-copy fast path.

### Pixel-Copy Fallback (Vulkan / any renderer)
When the shared EGL context cannot be created (e.g., Vulkan renderer), the C# side
must use `AsyncGPUReadback` to read pixels from the GPU, then pass the raw RGBA byte
array to `captureFrameFromPixels()`. This involves a GPU-to-CPU-to-GPU round-trip
and is slower, but works with any renderer.

### Thread Safety
- The `MediaMuxer` is protected by a lock (`muxerLock`) since video and audio
  encoders may write from different threads.
- The `isRecording` flag is `volatile` for safe cross-thread visibility.
- Encoder drain operations are performed inline (non-blocking) after each frame
  submission, so no separate drain thread is needed during recording. The drain
  thread (`HandlerThread`) is available for future async drain if needed.
