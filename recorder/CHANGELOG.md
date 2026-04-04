# Changelog

## [0.1.0] - 2026-02-11

### Added
- Initial release of ODDGames Recorder package
- `MediaRecorder.StartAsync()` public API for cross-platform recording
- `RecordingSession` handle with `StopAsync()` and `IDisposable` support
- `RecorderSettings` configuration DTO (resolution, frame rate, bitrate, audio)
- Editor backend using Unity Recorder (`com.unity.recorder`) with automatic factory registration
- Android backend via `MediaCodecBridge` JNI bridge
- iOS backend via AVFoundation P/Invoke
- Windows backend via Media Foundation native plugin
- macOS backend via AVFoundation native plugin
- `NullBackend` fallback for unsupported platforms
- iOS post-process build step to add required frameworks (AVFoundation, VideoToolbox, CoreMedia, ReplayKit)
- Basic recording sample (start/stop with R key)
