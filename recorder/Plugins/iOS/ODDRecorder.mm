// ODDRecorder.mm
// Native recording implementation for ODDGames.Recorder
//
// iOS:   ReplayKit in-app capture (RPScreenRecorder.startCapture) + AVAssetWriter
// macOS: AVAssetWriter with manual frame/audio input from C#
//
// Copyright (c) ODDGames. All rights reserved.

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <TargetConditionals.h>

#if TARGET_OS_IOS
#import <ReplayKit/ReplayKit.h>
#import <UIKit/UIKit.h>
#endif

#include "ODDRecorder.h"

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------

#define ODDLog(fmt, ...) NSLog(@"[ODDRecorder] " fmt, ##__VA_ARGS__)
#define ODDLogError(fmt, ...) NSLog(@"[ODDRecorder][ERROR] " fmt, ##__VA_ARGS__)

// ---------------------------------------------------------------------------
// ODDRecorderImpl
// ---------------------------------------------------------------------------

@interface ODDRecorderImpl : NSObject
{
    AVAssetWriter *_writer;
    AVAssetWriterInput *_videoInput;
    AVAssetWriterInput *_audioInput;
    AVAssetWriterInputPixelBufferAdaptor *_adaptor;
    CVPixelBufferPoolRef _pixelBufferPool;

    NSString *_outputPath;
    int _width, _height, _fps, _bitrate;
    BOOL _includeAudio;
    BOOL _isRecording;
    BOOL _sessionStarted;
    dispatch_queue_t _encodeQueue;

    // ReplayKit mode (iOS only)
    BOOL _useReplayKit;
    BOOL _hasFirstTimestamp;
    BOOL _stopping;
}

+ (instancetype)shared;

- (void)startWithPath:(NSString *)path
                width:(int)width
               height:(int)height
                  fps:(int)fps
              bitrate:(int)bitrate
         includeAudio:(BOOL)includeAudio;

- (NSString *)stop;
- (BOOL)isRecording;

- (void)appendVideoFrameRGBA:(const uint8_t *)rgbaData
                       length:(int)dataLength
                        width:(int)width
                       height:(int)height
                  timestampNs:(int64_t)timestampNs;

- (void)appendAudioSamples:(const float *)pcmData
                sampleCount:(int)sampleCount
                   channels:(int)channels
                 sampleRate:(int)sampleRate
                timestampNs:(int64_t)timestampNs;

@end

// ---------------------------------------------------------------------------
#pragma mark - Implementation
// ---------------------------------------------------------------------------

@implementation ODDRecorderImpl

+ (instancetype)shared
{
    static ODDRecorderImpl *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[ODDRecorderImpl alloc] init];
    });
    return instance;
}

- (instancetype)init
{
    self = [super init];
    if (self) {
        _encodeQueue = dispatch_queue_create("com.oddgames.recorder.encode",
                                             DISPATCH_QUEUE_SERIAL);
        _isRecording = NO;
        _sessionStarted = NO;
        _hasFirstTimestamp = NO;
        _stopping = NO;
        _useReplayKit = NO;
        _pixelBufferPool = NULL;
    }
    return self;
}

// ---------------------------------------------------------------------------
#pragma mark - AVAssetWriter Setup
// ---------------------------------------------------------------------------

- (BOOL)setupWriterAtPath:(NSString *)path
                    width:(int)width
                   height:(int)height
                      fps:(int)fps
                  bitrate:(int)bitrate
             includeAudio:(BOOL)includeAudio
{
    NSError *error = nil;
    NSURL *url = [NSURL fileURLWithPath:path];

    // Remove existing file if present
    NSFileManager *fm = [NSFileManager defaultManager];
    if ([fm fileExistsAtPath:path]) {
        [fm removeItemAtPath:path error:&error];
        if (error) {
            ODDLogError(@"Failed to remove existing file at %@: %@", path, error);
            // Non-fatal; AVAssetWriter may still succeed
        }
    }

    // Ensure parent directory exists
    NSString *parentDir = [path stringByDeletingLastPathComponent];
    if (![fm fileExistsAtPath:parentDir]) {
        [fm createDirectoryAtPath:parentDir
      withIntermediateDirectories:YES
                       attributes:nil
                            error:&error];
        if (error) {
            ODDLogError(@"Failed to create directory %@: %@", parentDir, error);
            return NO;
        }
    }

    _writer = [[AVAssetWriter alloc] initWithURL:url
                                        fileType:AVFileTypeMPEG4
                                           error:&error];
    if (!_writer || error) {
        ODDLogError(@"Failed to create AVAssetWriter: %@", error);
        return NO;
    }

    // ---- Video input ----
    NSDictionary *compressionProps = @{
        AVVideoAverageBitRateKey: @(bitrate),
        AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
        AVVideoExpectedSourceFrameRateKey: @(fps),
        AVVideoMaxKeyFrameIntervalKey: @(fps * 2)
    };

    NSDictionary *videoSettings = @{
        AVVideoCodecKey: AVVideoCodecTypeH264,
        AVVideoWidthKey: @(width),
        AVVideoHeightKey: @(height),
        AVVideoCompressionPropertiesKey: compressionProps
    };

    _videoInput = [[AVAssetWriterInput alloc] initWithMediaType:AVMediaTypeVideo
                                                 outputSettings:videoSettings];
    _videoInput.expectsMediaDataInRealTime = YES;

    if (![_writer canAddInput:_videoInput]) {
        ODDLogError(@"Cannot add video input to AVAssetWriter");
        return NO;
    }
    [_writer addInput:_videoInput];

    // ---- Pixel buffer adaptor (for manual frame path) ----
    NSDictionary *adaptorAttrs = @{
        (NSString *)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
        (NSString *)kCVPixelBufferWidthKey: @(width),
        (NSString *)kCVPixelBufferHeightKey: @(height),
        (NSString *)kCVPixelBufferIOSurfacePropertiesKey: @{}
    };

    _adaptor = [[AVAssetWriterInputPixelBufferAdaptor alloc]
                    initWithAssetWriterInput:_videoInput
                sourcePixelBufferAttributes:adaptorAttrs];

    // ---- Audio input (optional) ----
    if (includeAudio) {
        // AAC encoding settings
        AudioChannelLayout channelLayout;
        memset(&channelLayout, 0, sizeof(AudioChannelLayout));
        channelLayout.mChannelLayoutTag = kAudioChannelLayoutTag_Stereo;

        NSDictionary *audioSettings = @{
            AVFormatIDKey: @(kAudioFormatMPEG4AAC),
            AVSampleRateKey: @(44100),
            AVNumberOfChannelsKey: @(2),
            AVEncoderBitRateKey: @(128000),
            AVChannelLayoutKey: [NSData dataWithBytes:&channelLayout
                                               length:sizeof(AudioChannelLayout)]
        };

        _audioInput = [[AVAssetWriterInput alloc] initWithMediaType:AVMediaTypeAudio
                                                     outputSettings:audioSettings];
        _audioInput.expectsMediaDataInRealTime = YES;

        if ([_writer canAddInput:_audioInput]) {
            [_writer addInput:_audioInput];
        } else {
            ODDLogError(@"Cannot add audio input to AVAssetWriter; continuing without audio");
            _audioInput = nil;
        }
    } else {
        _audioInput = nil;
    }

    // ---- Start writing ----
    BOOL started = [_writer startWriting];
    if (!started) {
        ODDLogError(@"AVAssetWriter failed to start: %@", _writer.error);
        return NO;
    }

    _outputPath = [path copy];
    _width = width;
    _height = height;
    _fps = fps;
    _bitrate = bitrate;
    _includeAudio = includeAudio;
    _sessionStarted = NO;
    _hasFirstTimestamp = NO;
    _stopping = NO;

    return YES;
}

// ---------------------------------------------------------------------------
#pragma mark - Pixel Buffer Pool
// ---------------------------------------------------------------------------

- (BOOL)ensurePixelBufferPool
{
    if (_pixelBufferPool != NULL) {
        return YES;
    }

    // Create pool if the adaptor didn't provide one
    if (_adaptor.pixelBufferPool != NULL) {
        _pixelBufferPool = _adaptor.pixelBufferPool;
        // Do not release adaptor's pool; it manages lifetime
        return YES;
    }

    NSDictionary *attrs = @{
        (NSString *)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
        (NSString *)kCVPixelBufferWidthKey: @(_width),
        (NSString *)kCVPixelBufferHeightKey: @(_height),
        (NSString *)kCVPixelBufferIOSurfacePropertiesKey: @{}
    };

    CVReturn status = CVPixelBufferPoolCreate(kCFAllocatorDefault, NULL,
                                              (__bridge CFDictionaryRef)attrs,
                                              &_pixelBufferPool);
    if (status != kCVReturnSuccess) {
        ODDLogError(@"Failed to create CVPixelBufferPool: %d", (int)status);
        return NO;
    }

    return YES;
}

- (void)releasePixelBufferPool
{
    // Only release if we created the pool ourselves (not the adaptor's pool)
    if (_pixelBufferPool != NULL && _adaptor.pixelBufferPool != _pixelBufferPool) {
        CVPixelBufferPoolRelease(_pixelBufferPool);
    }
    _pixelBufferPool = NULL;
}

// ---------------------------------------------------------------------------
#pragma mark - Start Recording
// ---------------------------------------------------------------------------

- (void)startWithPath:(NSString *)path
                width:(int)width
               height:(int)height
                  fps:(int)fps
              bitrate:(int)bitrate
         includeAudio:(BOOL)includeAudio
{
    if (_isRecording) {
        ODDLogError(@"Already recording; call stop first");
        return;
    }

    if (!path || path.length == 0) {
        ODDLogError(@"Output path is nil or empty");
        return;
    }

    // Append .mp4 extension
    NSString *fullPath = [path stringByAppendingString:@".mp4"];

    // Clamp parameters
    fps = MAX(1, MIN(fps, 120));
    bitrate = MAX(100000, bitrate);

#if TARGET_OS_IOS
    [self startWithReplayKit:fullPath width:width height:height
                         fps:fps bitrate:bitrate includeAudio:includeAudio];
#else
    // macOS: manual frame input only
    if (width <= 0 || height <= 0) {
        ODDLogError(@"macOS requires explicit width and height (got %dx%d)", width, height);
        return;
    }

    _useReplayKit = NO;
    if (![self setupWriterAtPath:fullPath width:width height:height
                             fps:fps bitrate:bitrate includeAudio:includeAudio]) {
        ODDLogError(@"Failed to set up AVAssetWriter for macOS manual mode");
        return;
    }
    _isRecording = YES;
    ODDLog(@"Recording started (macOS manual mode) -> %@", fullPath);
#endif
}

// ---------------------------------------------------------------------------
#pragma mark - iOS ReplayKit Capture
// ---------------------------------------------------------------------------

#if TARGET_OS_IOS

- (void)startWithReplayKit:(NSString *)fullPath
                     width:(int)width
                    height:(int)height
                       fps:(int)fps
                   bitrate:(int)bitrate
              includeAudio:(BOOL)includeAudio
{
    RPScreenRecorder *recorder = [RPScreenRecorder sharedRecorder];
    if (!recorder.available) {
        ODDLogError(@"ReplayKit is not available on this device");
        return;
    }

    _useReplayKit = YES;

    // Resolve width/height from screen if zero
    if (width <= 0 || height <= 0) {
        CGSize screenSize = [UIScreen mainScreen].bounds.size;
        CGFloat scale = [UIScreen mainScreen].scale;
        width = (int)(screenSize.width * scale);
        height = (int)(screenSize.height * scale);
    }

    // Ensure even dimensions (H.264 requirement)
    width = (width + 1) & ~1;
    height = (height + 1) & ~1;

    if (![self setupWriterAtPath:fullPath width:width height:height
                             fps:fps bitrate:bitrate includeAudio:includeAudio]) {
        ODDLogError(@"Failed to set up AVAssetWriter for ReplayKit");
        return;
    }

    __weak typeof(self) weakSelf = self;

    [recorder startCaptureWithHandler:^(CMSampleBufferRef _Nonnull sampleBuffer,
                                        RPSampleBufferType bufferType,
                                        NSError * _Nullable error) {
        __strong typeof(weakSelf) strongSelf = weakSelf;
        if (!strongSelf || strongSelf->_stopping) return;

        if (error) {
            ODDLogError(@"ReplayKit capture error: %@", error);
            return;
        }

        @autoreleasepool {
            [strongSelf handleReplayKitBuffer:sampleBuffer type:bufferType];
        }

    } completionHandler:^(NSError * _Nullable error) {
        if (error) {
            ODDLogError(@"ReplayKit startCapture failed: %@", error);
            __strong typeof(weakSelf) strongSelf = weakSelf;
            if (strongSelf) {
                strongSelf->_isRecording = NO;
            }
        } else {
            ODDLog(@"ReplayKit capture started successfully");
        }
    }];

    _isRecording = YES;
    ODDLog(@"Recording started (ReplayKit) -> %@", fullPath);
}

- (void)handleReplayKitBuffer:(CMSampleBufferRef)sampleBuffer
                          type:(RPSampleBufferType)bufferType
{
    if (!_isRecording || _stopping) return;
    if (!CMSampleBufferIsValid(sampleBuffer)) return;

    switch (bufferType) {
        case RPSampleBufferTypeVideo: {
            dispatch_sync(_encodeQueue, ^{
                if (!self->_sessionStarted) {
                    CMTime pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer);
                    [self->_writer startSessionAtSourceTime:pts];
                    self->_sessionStarted = YES;
                }

                if (self->_videoInput.readyForMoreMediaData) {
                    if (![self->_videoInput appendSampleBuffer:sampleBuffer]) {
                        ODDLogError(@"Failed to append video sample buffer: %@",
                                    self->_writer.error);
                    }
                }
            });
            break;
        }

        case RPSampleBufferTypeAudioApp: {
            if (!_includeAudio || !_audioInput) break;

            dispatch_sync(_encodeQueue, ^{
                if (!self->_sessionStarted) {
                    // Wait for video to start the session
                    return;
                }

                if (self->_audioInput.readyForMoreMediaData) {
                    if (![self->_audioInput appendSampleBuffer:sampleBuffer]) {
                        ODDLogError(@"Failed to append audio sample buffer: %@",
                                    self->_writer.error);
                    }
                }
            });
            break;
        }

        case RPSampleBufferTypeAudioMic:
            // Ignore microphone audio; we only want app audio
            break;
    }
}

#endif // TARGET_OS_IOS

// ---------------------------------------------------------------------------
#pragma mark - Stop Recording
// ---------------------------------------------------------------------------

- (NSString *)stop
{
    if (!_isRecording) {
        ODDLogError(@"Not currently recording");
        return nil;
    }

    _stopping = YES;
    _isRecording = NO;

    __block NSString *result = nil;
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);

#if TARGET_OS_IOS
    if (_useReplayKit) {
        [[RPScreenRecorder sharedRecorder] stopCaptureWithHandler:^(NSError * _Nullable error) {
            if (error) {
                ODDLogError(@"ReplayKit stopCapture error: %@", error);
            }
            result = [self finalizeWriter];
            dispatch_semaphore_signal(sem);
        }];

        // Wait up to 10 seconds for ReplayKit to stop
        dispatch_time_t timeout = dispatch_time(DISPATCH_TIME_NOW,
                                                (int64_t)(10.0 * NSEC_PER_SEC));
        if (dispatch_semaphore_wait(sem, timeout) != 0) {
            ODDLogError(@"Timed out waiting for ReplayKit to stop; finalizing anyway");
            result = [self finalizeWriter];
        }
    } else {
        result = [self finalizeWriter];
    }
#else
    result = [self finalizeWriter];
#endif

    ODDLog(@"Recording stopped -> %@", result ?: @"(nil)");
    return result;
}

- (NSString *)finalizeWriter
{
    __block NSString *result = nil;

    if (!_writer) {
        ODDLogError(@"No AVAssetWriter to finalize");
        return nil;
    }

    if (_writer.status == AVAssetWriterStatusUnknown) {
        ODDLogError(@"AVAssetWriter never started a session; no frames written");
        [self cleanupWriter];
        return nil;
    }

    if (_writer.status == AVAssetWriterStatusFailed) {
        ODDLogError(@"AVAssetWriter is in failed state: %@", _writer.error);
        [self cleanupWriter];
        return nil;
    }

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);

    dispatch_sync(_encodeQueue, ^{
        if (self->_videoInput) {
            [self->_videoInput markAsFinished];
        }
        if (self->_audioInput) {
            [self->_audioInput markAsFinished];
        }

        [self->_writer finishWritingWithCompletionHandler:^{
            if (self->_writer.status == AVAssetWriterStatusCompleted) {
                result = [self->_outputPath copy];
                ODDLog(@"AVAssetWriter finished successfully");
            } else {
                ODDLogError(@"AVAssetWriter finished with status %ld: %@",
                            (long)self->_writer.status, self->_writer.error);
            }
            dispatch_semaphore_signal(sem);
        }];
    });

    // Wait up to 30 seconds for finalization
    dispatch_time_t timeout = dispatch_time(DISPATCH_TIME_NOW,
                                            (int64_t)(30.0 * NSEC_PER_SEC));
    if (dispatch_semaphore_wait(sem, timeout) != 0) {
        ODDLogError(@"Timed out waiting for AVAssetWriter to finish");
        result = [_outputPath copy];
    }

    [self cleanupWriter];
    return result;
}

- (void)cleanupWriter
{
    [self releasePixelBufferPool];
    _writer = nil;
    _videoInput = nil;
    _audioInput = nil;
    _adaptor = nil;
    _outputPath = nil;
    _sessionStarted = NO;
    _hasFirstTimestamp = NO;
    _stopping = NO;
}

// ---------------------------------------------------------------------------
#pragma mark - Manual Video Frame Append (macOS / fallback)
// ---------------------------------------------------------------------------

- (void)appendVideoFrameRGBA:(const uint8_t *)rgbaData
                       length:(int)dataLength
                        width:(int)width
                       height:(int)height
                  timestampNs:(int64_t)timestampNs
{
    if (!_isRecording || _stopping) return;

#if TARGET_OS_IOS
    if (_useReplayKit) {
        // ReplayKit handles capture; ignore manual frames
        return;
    }
#endif

    if (!rgbaData) {
        ODDLogError(@"appendVideoFrame: rgbaData is NULL");
        return;
    }

    int expectedLen = width * height * 4;
    if (dataLength < expectedLen) {
        ODDLogError(@"appendVideoFrame: data too short (%d < %d expected)", dataLength, expectedLen);
        return;
    }

    dispatch_sync(_encodeQueue, ^{
        @autoreleasepool {
            [self _appendVideoFrameOnQueue:rgbaData width:width height:height timestampNs:timestampNs];
        }
    });
}

- (void)_appendVideoFrameOnQueue:(const uint8_t *)rgbaData
                           width:(int)width
                          height:(int)height
                     timestampNs:(int64_t)timestampNs
{
    if (_writer.status == AVAssetWriterStatusFailed) {
        ODDLogError(@"AVAssetWriter failed: %@", _writer.error);
        return;
    }

    // Ensure pixel buffer pool is ready
    if (![self ensurePixelBufferPool]) {
        ODDLogError(@"Pixel buffer pool unavailable; dropping frame");
        return;
    }

    // Get pixel buffer from pool
    CVPixelBufferRef pixelBuffer = NULL;
    CVReturn status = CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault,
                                                         _pixelBufferPool,
                                                         &pixelBuffer);
    if (status != kCVReturnSuccess || !pixelBuffer) {
        ODDLogError(@"Failed to get pixel buffer from pool: %d", (int)status);
        return;
    }

    CVPixelBufferLockBaseAddress(pixelBuffer, 0);

    uint8_t *dst = (uint8_t *)CVPixelBufferGetBaseAddress(pixelBuffer);
    size_t dstBytesPerRow = CVPixelBufferGetBytesPerRow(pixelBuffer);
    size_t srcBytesPerRow = (size_t)width * 4;

    // Convert RGBA -> BGRA and copy row by row (handles stride differences)
    for (int y = 0; y < height; y++) {
        const uint8_t *srcRow = rgbaData + y * srcBytesPerRow;
        uint8_t *dstRow = dst + y * dstBytesPerRow;

        for (int x = 0; x < width; x++) {
            int si = x * 4;
            int di = x * 4;
            dstRow[di + 0] = srcRow[si + 2]; // B <- R
            dstRow[di + 1] = srcRow[si + 1]; // G <- G
            dstRow[di + 2] = srcRow[si + 0]; // R <- B
            dstRow[di + 3] = srcRow[si + 3]; // A <- A
        }
    }

    CVPixelBufferUnlockBaseAddress(pixelBuffer, 0);

    // Create presentation time from nanoseconds
    CMTime pts = CMTimeMake(timestampNs, 1000000000);

    // Start session on first frame
    if (!_sessionStarted) {
        [_writer startSessionAtSourceTime:pts];
        _sessionStarted = YES;
    }

    // Wait for input to be ready (spin briefly)
    int waitCount = 0;
    while (!_videoInput.readyForMoreMediaData && waitCount < 100) {
        usleep(1000); // 1ms
        waitCount++;
    }

    if (!_videoInput.readyForMoreMediaData) {
        ODDLogError(@"Video input not ready; dropping frame at %lld ns", timestampNs);
        CVPixelBufferRelease(pixelBuffer);
        return;
    }

    if (![_adaptor appendPixelBuffer:pixelBuffer withPresentationTime:pts]) {
        ODDLogError(@"Failed to append pixel buffer: %@", _writer.error);
    }

    CVPixelBufferRelease(pixelBuffer);
}

// ---------------------------------------------------------------------------
#pragma mark - Manual Audio Append (macOS / fallback)
// ---------------------------------------------------------------------------

- (void)appendAudioSamples:(const float *)pcmData
                sampleCount:(int)sampleCount
                   channels:(int)channels
                 sampleRate:(int)sampleRate
                timestampNs:(int64_t)timestampNs
{
    if (!_isRecording || _stopping) return;
    if (!_includeAudio || !_audioInput) return;

#if TARGET_OS_IOS
    if (_useReplayKit) {
        // ReplayKit handles audio capture; ignore manual samples
        return;
    }
#endif

    if (!pcmData) {
        ODDLogError(@"appendAudioSamples: pcmData is NULL");
        return;
    }

    if (sampleCount <= 0 || channels <= 0 || sampleRate <= 0) {
        ODDLogError(@"appendAudioSamples: invalid parameters (samples=%d, ch=%d, rate=%d)",
                    sampleCount, channels, sampleRate);
        return;
    }

    dispatch_sync(_encodeQueue, ^{
        @autoreleasepool {
            [self _appendAudioOnQueue:pcmData sampleCount:sampleCount
                             channels:channels sampleRate:sampleRate
                          timestampNs:timestampNs];
        }
    });
}

- (void)_appendAudioOnQueue:(const float *)pcmData
                 sampleCount:(int)sampleCount
                    channels:(int)channels
                  sampleRate:(int)sampleRate
                 timestampNs:(int64_t)timestampNs
{
    if (_writer.status == AVAssetWriterStatusFailed) {
        ODDLogError(@"AVAssetWriter failed: %@", _writer.error);
        return;
    }

    if (!_sessionStarted) {
        // Audio must not start a session; wait for video
        return;
    }

    if (!_audioInput.readyForMoreMediaData) {
        // Drop audio if encoder is backed up
        return;
    }

    // Convert float [-1.0, 1.0] PCM to signed 16-bit PCM
    int totalSamples = sampleCount * channels;
    size_t pcm16Size = (size_t)totalSamples * sizeof(int16_t);
    int16_t *pcm16 = (int16_t *)malloc(pcm16Size);
    if (!pcm16) {
        ODDLogError(@"Failed to allocate PCM16 buffer (%zu bytes)", pcm16Size);
        return;
    }

    for (int i = 0; i < totalSamples; i++) {
        float sample = pcmData[i];
        // Clamp to [-1.0, 1.0]
        if (sample > 1.0f) sample = 1.0f;
        if (sample < -1.0f) sample = -1.0f;
        pcm16[i] = (int16_t)(sample * 32767.0f);
    }

    // Build AudioStreamBasicDescription for 16-bit PCM
    AudioStreamBasicDescription asbd;
    memset(&asbd, 0, sizeof(asbd));
    asbd.mSampleRate = (Float64)sampleRate;
    asbd.mFormatID = kAudioFormatLinearPCM;
    asbd.mFormatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked;
    asbd.mBytesPerPacket = (UInt32)(channels * sizeof(int16_t));
    asbd.mFramesPerPacket = 1;
    asbd.mBytesPerFrame = (UInt32)(channels * sizeof(int16_t));
    asbd.mChannelsPerFrame = (UInt32)channels;
    asbd.mBitsPerChannel = 16;

    // Create format description
    CMAudioFormatDescriptionRef formatDesc = NULL;
    OSStatus err = CMAudioFormatDescriptionCreate(kCFAllocatorDefault,
                                                   &asbd,
                                                   0, NULL,   // channel layout
                                                   0, NULL,   // magic cookie
                                                   NULL,
                                                   &formatDesc);
    if (err != noErr) {
        ODDLogError(@"CMAudioFormatDescriptionCreate failed: %d", (int)err);
        free(pcm16);
        return;
    }

    // Create CMBlockBuffer wrapping our PCM data
    CMBlockBufferRef blockBuffer = NULL;
    err = CMBlockBufferCreateWithMemoryBlock(kCFAllocatorDefault,
                                             pcm16,
                                             pcm16Size,
                                             kCFAllocatorMalloc, // will free pcm16
                                             NULL,
                                             0,
                                             pcm16Size,
                                             0,
                                             &blockBuffer);
    if (err != noErr) {
        ODDLogError(@"CMBlockBufferCreateWithMemoryBlock failed: %d", (int)err);
        CFRelease(formatDesc);
        free(pcm16);
        return;
    }
    // pcm16 is now owned by blockBuffer (kCFAllocatorMalloc will free it)

    // Create CMSampleBuffer
    CMSampleBufferRef sampleBuffer = NULL;
    CMTime pts = CMTimeMake(timestampNs, 1000000000);

    err = CMAudioSampleBufferCreateReadyWithPacketDescriptions(
        kCFAllocatorDefault,
        blockBuffer,
        formatDesc,
        (CMItemCount)sampleCount,
        pts,
        NULL, // packet descriptions (not needed for PCM)
        &sampleBuffer);

    if (err != noErr) {
        ODDLogError(@"CMAudioSampleBufferCreateReady failed: %d", (int)err);
        CFRelease(blockBuffer);
        CFRelease(formatDesc);
        return;
    }

    // Append to audio input
    if (![_audioInput appendSampleBuffer:sampleBuffer]) {
        ODDLogError(@"Failed to append audio sample buffer: %@", _writer.error);
    }

    CFRelease(sampleBuffer);
    CFRelease(blockBuffer);
    CFRelease(formatDesc);
}

// ---------------------------------------------------------------------------
#pragma mark - Query
// ---------------------------------------------------------------------------

- (BOOL)isRecording
{
    return _isRecording;
}

@end

// ===========================================================================
#pragma mark - C Bridge Functions
// ===========================================================================

extern "C" {

void ODDRecorder_Start(const char* outputPath, int width, int height,
                       int fps, int bitrate, bool includeAudio)
{
    @autoreleasepool {
        if (!outputPath) {
            ODDLogError(@"ODDRecorder_Start: outputPath is NULL");
            return;
        }

        NSString *path = [NSString stringWithUTF8String:outputPath];
        [[ODDRecorderImpl shared] startWithPath:path
                                          width:width
                                         height:height
                                            fps:fps
                                        bitrate:bitrate
                                   includeAudio:(BOOL)includeAudio];
    }
}

void ODDRecorder_Stop(char* outPath, int outPathLen)
{
    @autoreleasepool {
        NSString *result = [[ODDRecorderImpl shared] stop];

        if (outPath && outPathLen > 0) {
            if (result) {
                const char *utf8 = [result UTF8String];
                size_t len = strlen(utf8);
                if (len >= (size_t)outPathLen) {
                    len = (size_t)(outPathLen - 1);
                }
                memcpy(outPath, utf8, len);
                outPath[len] = '\0';
            } else {
                outPath[0] = '\0';
            }
        }
    }
}

bool ODDRecorder_IsRecording(void)
{
    @autoreleasepool {
        return [[ODDRecorderImpl shared] isRecording];
    }
}

void ODDRecorder_AppendVideoFrame(const uint8_t* rgbaData, int dataLength,
                                  int width, int height, int64_t timestampNs)
{
    @autoreleasepool {
        [[ODDRecorderImpl shared] appendVideoFrameRGBA:rgbaData
                                                length:dataLength
                                                 width:width
                                                height:height
                                           timestampNs:timestampNs];
    }
}

void ODDRecorder_AppendAudioSamples(const float* pcmData, int sampleCount,
                                    int channels, int sampleRate, int64_t timestampNs)
{
    @autoreleasepool {
        [[ODDRecorderImpl shared] appendAudioSamples:pcmData
                                         sampleCount:sampleCount
                                            channels:channels
                                          sampleRate:sampleRate
                                         timestampNs:timestampNs];
    }
}

} // extern "C"
