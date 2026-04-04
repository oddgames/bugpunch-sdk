# ODDRecorder Native Plugin Build Notes

## Platform Overview

| Platform | Capture Method | Build Approach |
|----------|---------------|----------------|
| iOS | ReplayKit in-app capture (no permission prompt) | Unity compiles `.mm` into Xcode project automatically |
| macOS | Manual frame input from C# via `AsyncGPUReadback` | Pre-built `.bundle` via Xcode |

## iOS

### Build Steps

No separate build step is required. Unity automatically includes `.mm` and `.h` files
from the plugin folder into the generated Xcode project.

1. Place `ODDRecorder.h` and `ODDRecorder.mm` in `Plugins/iOS/`.
2. In Unity, select both files and configure the Plugin Inspector:
   - Platform: **iOS** only
   - CPU: **Any**
3. Unity will compile the Objective-C++ source as part of the Xcode build.

### Required Frameworks

The following frameworks must be linked in the Xcode project. Use a
`PostProcessBuild_iOS.cs` editor script to add them automatically:

- `ReplayKit.framework`
- `AVFoundation.framework`
- `CoreMedia.framework`
- `CoreVideo.framework`
- `AudioToolbox.framework`

Example PostProcessBuild snippet:

```csharp
[PostProcessBuild]
public static void OnPostProcessBuild(BuildTarget target, string path)
{
    if (target != BuildTarget.iOS) return;

    string projPath = PBXProject.GetPBXProjectPath(path);
    var project = new PBXProject();
    project.ReadFromFile(projPath);

    string targetGuid = project.GetUnityMainTargetGuid();

    project.AddFrameworkToProject(targetGuid, "ReplayKit.framework", false);
    project.AddFrameworkToProject(targetGuid, "AVFoundation.framework", false);
    project.AddFrameworkToProject(targetGuid, "CoreMedia.framework", false);
    project.AddFrameworkToProject(targetGuid, "CoreVideo.framework", false);
    project.AddFrameworkToProject(targetGuid, "AudioToolbox.framework", false);

    project.WriteToFile(projPath);
}
```

### Minimum iOS Version

**iOS 11.0** is required for `RPScreenRecorder.startCapture(handler:completionHandler:)`,
which provides zero-permission in-app screen recording.

### ReplayKit Notes

- `startCapture(handler:completionHandler:)` does NOT show a permission dialog.
  It captures only the app's own screen content.
- The handler delivers three buffer types:
  - `.video` -- screen frames as `CMSampleBuffer`
  - `.audioApp` -- app audio output (no mic permission needed)
  - `.audioMic` -- microphone (ignored by this plugin)
- ReplayKit is unavailable in the iOS Simulator.

## macOS

### Build Steps

macOS requires a pre-built `.bundle` because Unity does not compile `.mm` files for
macOS standalone builds the same way it does for iOS.

#### Option A: Build with Xcode

1. Open Xcode and create a new **macOS Bundle** target.
2. Set the following build settings:
   - **Product Name**: `ODDRecorder`
   - **Bundle Identifier**: `com.oddgames.recorder.native`
   - **Deployment Target**: `10.13`
   - **Architectures**: `x86_64 arm64` (Universal)
   - **Build Active Architecture Only**: `No`
   - **Other Linker Flags**: `-framework AVFoundation -framework CoreMedia -framework CoreVideo -framework AudioToolbox`
3. Add `ODDRecorderMac.mm` as the source file (it `#include`s the shared `ODDRecorder.mm`).
4. Add `ODDRecorder.h` to the project (for reference; not strictly needed).
5. Build for **Release**.
6. Copy the resulting `ODDRecorder.bundle` to `Plugins/macOS/`.

#### Option B: Build from command line

```bash
# From the recorder/Plugins directory:
clang++ -shared -fPIC -fobjc-arc -std=c++17 \
    -framework AVFoundation \
    -framework CoreMedia \
    -framework CoreVideo \
    -framework AudioToolbox \
    -arch x86_64 -arch arm64 \
    -mmacosx-version-min=10.13 \
    -o macOS/ODDRecorder.bundle/Contents/MacOS/ODDRecorder \
    macOS/ODDRecorderMac.mm

# Create bundle structure first:
mkdir -p macOS/ODDRecorder.bundle/Contents/MacOS
cp macOS/Info.plist macOS/ODDRecorder.bundle/Contents/
```

### Unity Plugin Inspector (macOS)

Select `ODDRecorder.bundle` in Unity and configure:
- Platform: **macOS** (Standalone)
- CPU: **Any** (Universal)

### Minimum macOS Version

**macOS 10.13** (High Sierra) is required for:
- `AVAssetWriter` with H.264 encoding
- `AVVideoCodecTypeH264` constant

### macOS Capture Flow

Unlike iOS, macOS does not use ReplayKit for capture. The recording flow is:

1. C# calls `ODDRecorder_Start()` with explicit width/height.
2. Each frame, C# uses `AsyncGPUReadback` to read the screen texture.
3. C# passes raw RGBA pixel data to `ODDRecorder_AppendVideoFrame()`.
4. For audio, C# hooks `OnAudioFilterRead` and passes PCM data to
   `ODDRecorder_AppendAudioSamples()`.
5. C# calls `ODDRecorder_Stop()` to finalize the MP4 file.

## Shared Code Architecture

Both platforms share the same `ODDRecorder.mm` source file. Platform-specific
behavior is controlled by `#if TARGET_OS_IOS` / `#else` preprocessor guards:

- **iOS path**: ReplayKit capture delivers `CMSampleBuffer` directly to `AVAssetWriter`.
- **macOS path**: Raw RGBA frames are converted to BGRA `CVPixelBuffer` and appended
  via `AVAssetWriterInputPixelBufferAdaptor`.

The macOS bundle source (`ODDRecorderMac.mm`) simply `#include`s the shared source,
which compiles with `TARGET_OS_IOS = 0` on macOS, disabling ReplayKit code paths.

## Troubleshooting

### ReplayKit reports "not available"

- Check `RPScreenRecorder.shared.available` -- returns `false` in Simulator and
  on some older devices.
- Ensure no other recording (e.g., screen broadcast) is active.

### AVAssetWriter fails to start

- Verify the output directory exists (the plugin creates it automatically).
- Check that no file lock exists at the output path.
- Ensure width and height are even numbers (H.264 requirement; the plugin rounds up).

### No audio in recording

- Confirm `includeAudio` is `true` in the `Start` call.
- On macOS, ensure `OnAudioFilterRead` is active on a listener and calling
  `AppendAudioSamples`.
- On iOS, verify the app has an active audio session (`AVAudioSession`).

### Frame drops / encoding lag

- Reduce resolution or bitrate.
- Ensure `AsyncGPUReadback` (macOS) is not blocking the main thread.
- The plugin drops frames when `AVAssetWriterInput.readyForMoreMediaData` is `false`.
