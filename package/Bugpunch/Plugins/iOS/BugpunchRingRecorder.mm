// BugpunchRingRecorder.mm
// Always-on ring buffer video recorder for Bugpunch SDK (iOS)
//
// Architecture mirrors the Android BugpunchRecorder.java:
//   - ReplayKit RPScreenRecorder.startCapture provides raw CMSampleBuffers
//   - VTCompressionSession encodes to H.264 with 1s keyframe interval
//   - Encoded samples (NAL units) are stored in a circular deque
//   - The deque is trimmed to windowSeconds on every new sample
//   - dump() snapshots the buffer, finds the oldest keyframe, writes MP4
//     via AVAssetWriter
//
// Memory budget: ~2 Mbps * 30s = ~7.5 MB typical ring buffer size at 720p.
// CPU impact: VideoToolbox uses hardware encoder; near-zero CPU overhead.
//
// Copyright (c) ODDGames. All rights reserved.

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <VideoToolbox/VideoToolbox.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#include <unistd.h>

#if TARGET_OS_IOS
#import <ReplayKit/ReplayKit.h>
#import <UIKit/UIKit.h>
#endif

#include "BugpunchRingRecorder.h"

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------

#define BPLog(fmt, ...)      NSLog(@"[BugpunchRing] " fmt, ##__VA_ARGS__)
#define BPLogError(fmt, ...) NSLog(@"[BugpunchRing][ERROR] " fmt, ##__VA_ARGS__)

// ---------------------------------------------------------------------------
// EncodedSample — mirrors Android's BugpunchRecorder.Sample
// ---------------------------------------------------------------------------

struct EncodedSample {
    NSData *data;           // Raw H.264 NAL unit(s)
    CMTime pts;             // Presentation timestamp
    CMTime duration;        // Frame duration
    bool isKeyframe;        // IDR frame?

    EncodedSample(NSData *d, CMTime p, CMTime dur, bool kf)
        : data(d), pts(p), duration(dur), isKeyframe(kf) {}
};

// ---------------------------------------------------------------------------
// BugpunchRingRecorderImpl
// ---------------------------------------------------------------------------

@interface BugpunchRingRecorderImpl : NSObject
{
    // Config
    int _width, _height, _fps, _bitrate, _windowSeconds;

    // VideoToolbox encoder
    VTCompressionSessionRef _compressionSession;

    // Ring buffer
    NSMutableArray<NSValue *> *_ringBuffer;  // NSValue wrapping EncodedSample*
    NSLock *_bufferLock;
    int64_t _totalBufferBytes;

    // SPS/PPS for the current encoding session (needed by AVAssetWriter)
    NSData *_sps;
    NSData *_pps;

    // State
    BOOL _running;
    BOOL _configured;
    CMTime _sessionStartTime;
}

+ (instancetype)shared;

- (void)configureWidth:(int)width height:(int)height fps:(int)fps
               bitrate:(int)bitrate windowSeconds:(int)windowSeconds;
- (BOOL)start;
- (void)stop;
- (BOOL)isRunning;
- (BOOL)hasFootage;
- (BOOL)dumpToPath:(NSString *)outputPath;
- (int64_t)bufferSizeBytes;
- (void)handleEncodedSampleBuffer:(CMSampleBufferRef)sampleBuffer;

@end

// ---------------------------------------------------------------------------
#pragma mark - VideoToolbox Compression Callback (C function)
// ---------------------------------------------------------------------------

static void BugpunchVTCompressionOutputCallback(void *outputCallbackRefCon,
                                         void *sourceFrameRefCon,
                                         OSStatus status,
                                         VTEncodeInfoFlags infoFlags,
                                         CMSampleBufferRef sampleBuffer)
{
    if (status != noErr) {
        BPLogError(@"VTCompressionSession encode error: %d", (int)status);
        return;
    }
    if (!sampleBuffer) return;

    BugpunchRingRecorderImpl *impl = (__bridge BugpunchRingRecorderImpl *)outputCallbackRefCon;
    [impl handleEncodedSampleBuffer:sampleBuffer];
}

// ---------------------------------------------------------------------------
#pragma mark - Implementation
// ---------------------------------------------------------------------------

@implementation BugpunchRingRecorderImpl

+ (instancetype)shared
{
    static BugpunchRingRecorderImpl *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[BugpunchRingRecorderImpl alloc] init];
    });
    return instance;
}

- (instancetype)init
{
    self = [super init];
    if (self) {
        _ringBuffer = [NSMutableArray new];
        _bufferLock = [NSLock new];
        _totalBufferBytes = 0;
        _running = NO;
        _configured = NO;
        _compressionSession = NULL;
        _sps = nil;
        _pps = nil;
        _sessionStartTime = kCMTimeInvalid;
    }
    return self;
}

// ---------------------------------------------------------------------------
#pragma mark - Configure
// ---------------------------------------------------------------------------

- (void)configureWidth:(int)width height:(int)height fps:(int)fps
               bitrate:(int)bitrate windowSeconds:(int)windowSeconds
{
    _width = width;
    _height = height;
    _fps = MAX(1, MIN(fps, 60));
    _bitrate = MAX(100000, bitrate);
    _windowSeconds = MAX(5, windowSeconds);
    _configured = YES;

    BPLog(@"Configured: %dx%d @ %dfps, %d bps, %ds window",
          _width, _height, _fps, _bitrate, _windowSeconds);
}

// ---------------------------------------------------------------------------
#pragma mark - VideoToolbox Encoder Setup
// ---------------------------------------------------------------------------

- (BOOL)createCompressionSession:(int)width height:(int)height
{
    if (_compressionSession) {
        VTCompressionSessionInvalidate(_compressionSession);
        CFRelease(_compressionSession);
        _compressionSession = NULL;
    }

    OSStatus status = VTCompressionSessionCreate(
        kCFAllocatorDefault,
        width,
        height,
        kCMVideoCodecType_H264,
        NULL,  // encoderSpecification — let the system pick hardware encoder
        NULL,  // sourceImageBufferAttributes
        NULL,  // compressedDataAllocator
        BugpunchVTCompressionOutputCallback,
        (__bridge void *)self,
        &_compressionSession
    );

    if (status != noErr) {
        BPLogError(@"VTCompressionSessionCreate failed: %d", (int)status);
        return NO;
    }

    // Configure for real-time encoding with low latency
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_RealTime, kCFBooleanTrue);
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_AllowFrameReordering, kCFBooleanFalse);

    // Bitrate
    CFNumberRef bitrateRef = CFNumberCreate(NULL, kCFNumberSInt32Type, &_bitrate);
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_AverageBitRate, bitrateRef);
    CFRelease(bitrateRef);

    // Keyframe interval: 1 second (critical for clean ring buffer trimming)
    int keyframeInterval = _fps;  // 1 keyframe per second
    CFNumberRef kfRef = CFNumberCreate(NULL, kCFNumberSInt32Type, &keyframeInterval);
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_MaxKeyFrameInterval, kfRef);
    CFRelease(kfRef);

    // Also set the duration-based interval to 1 second
    float kfDuration = 1.0f;
    CFNumberRef kfDurRef = CFNumberCreate(NULL, kCFNumberFloat32Type, &kfDuration);
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, kfDurRef);
    CFRelease(kfDurRef);

    // Frame rate
    CFNumberRef fpsRef = CFNumberCreate(NULL, kCFNumberSInt32Type, &_fps);
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_ExpectedFrameRate, fpsRef);
    CFRelease(fpsRef);

    // Profile: Baseline for maximum compatibility, or High for better compression
    VTSessionSetProperty(_compressionSession,
                         kVTCompressionPropertyKey_ProfileLevel,
                         kVTProfileLevel_H264_High_AutoLevel);

    // Prepare to encode
    status = VTCompressionSessionPrepareToEncodeFrames(_compressionSession);
    if (status != noErr) {
        BPLogError(@"VTCompressionSessionPrepareToEncodeFrames failed: %d", (int)status);
        VTCompressionSessionInvalidate(_compressionSession);
        CFRelease(_compressionSession);
        _compressionSession = NULL;
        return NO;
    }

    BPLog(@"VTCompressionSession created: %dx%d", width, height);
    return YES;
}

// ---------------------------------------------------------------------------
#pragma mark - Start / Stop
// ---------------------------------------------------------------------------

- (BOOL)start
{
#if !TARGET_OS_IOS
    BPLogError(@"Ring buffer recorder requires iOS (ReplayKit)");
    return NO;
#else
    if (_running) {
        BPLog(@"Already running");
        return YES;
    }

    if (!_configured) {
        BPLogError(@"Not configured; call configure first");
        return NO;
    }

    RPScreenRecorder *recorder = [RPScreenRecorder sharedRecorder];
    if (!recorder.available) {
        BPLogError(@"ReplayKit is not available on this device");
        return NO;
    }

    // Resolve dimensions from screen if not specified
    int captureWidth = _width;
    int captureHeight = _height;
    if (captureWidth <= 0 || captureHeight <= 0) {
        CGSize screenSize = [UIScreen mainScreen].bounds.size;
        CGFloat scale = [UIScreen mainScreen].scale;
        captureWidth = (int)(screenSize.width * scale);
        captureHeight = (int)(screenSize.height * scale);
    }
    // Ensure even dimensions
    captureWidth = (captureWidth + 1) & ~1;
    captureHeight = (captureHeight + 1) & ~1;
    _width = captureWidth;
    _height = captureHeight;

    // Create the H.264 encoder
    if (![self createCompressionSession:captureWidth height:captureHeight]) {
        return NO;
    }

    // Clear any previous buffer
    [_bufferLock lock];
    [self clearBufferLocked];
    [_bufferLock unlock];

    _sps = nil;
    _pps = nil;
    _sessionStartTime = kCMTimeInvalid;

    // Start ReplayKit in-app capture — feeds CMSampleBuffers to our callback
    __weak typeof(self) weakSelf = self;

    [recorder startCaptureWithHandler:^(CMSampleBufferRef sampleBuffer,
                                        RPSampleBufferType bufferType,
                                        NSError *error) {
        __strong typeof(weakSelf) strongSelf = weakSelf;
        if (!strongSelf || !strongSelf->_running) return;

        if (error) {
            BPLogError(@"ReplayKit capture error: %@", error);
            return;
        }

        if (bufferType == RPSampleBufferTypeVideo) {
            @autoreleasepool {
                [strongSelf encodeVideoSampleBuffer:sampleBuffer];
            }
        }
        // We ignore audio — ring buffer is video-only (same as Android)

    } completionHandler:^(NSError *error) {
        if (error) {
            BPLogError(@"ReplayKit startCapture failed: %@", error);
            __strong typeof(weakSelf) strongSelf = weakSelf;
            if (strongSelf) strongSelf->_running = NO;
        } else {
            BPLog(@"ReplayKit capture started");
        }
    }];

    _running = YES;
    BPLog(@"Ring buffer recorder started: %dx%d @ %dfps, %ds window",
          captureWidth, captureHeight, _fps, _windowSeconds);
    return YES;
#endif
}

- (void)stop
{
    if (!_running) return;
    _running = NO;

#if TARGET_OS_IOS
    // Stop ReplayKit capture
    [[RPScreenRecorder sharedRecorder] stopCaptureWithHandler:^(NSError *error) {
        if (error) {
            BPLogError(@"ReplayKit stopCapture error: %@", error);
        } else {
            BPLog(@"ReplayKit capture stopped");
        }
    }];
#endif

    // Tear down encoder
    if (_compressionSession) {
        VTCompressionSessionCompleteFrames(_compressionSession, kCMTimeInvalid);
        VTCompressionSessionInvalidate(_compressionSession);
        CFRelease(_compressionSession);
        _compressionSession = NULL;
    }

    // Clear buffer
    [_bufferLock lock];
    [self clearBufferLocked];
    [_bufferLock unlock];

    _sps = nil;
    _pps = nil;

    BPLog(@"Ring buffer recorder stopped");
}

// ---------------------------------------------------------------------------
#pragma mark - Encode Video Frames via VideoToolbox
// ---------------------------------------------------------------------------

- (void)encodeVideoSampleBuffer:(CMSampleBufferRef)sampleBuffer
{
    if (!_compressionSession) return;
    if (!CMSampleBufferIsValid(sampleBuffer)) return;

    CVImageBufferRef imageBuffer = CMSampleBufferGetImageBuffer(sampleBuffer);
    if (!imageBuffer) return;

    CMTime pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer);
    CMTime duration = CMSampleBufferGetDuration(sampleBuffer);

    if (!CMTIME_IS_VALID(duration) || CMTimeGetSeconds(duration) <= 0) {
        duration = CMTimeMake(1, _fps);
    }

    // Record the session start time for relative timestamps
    if (!CMTIME_IS_VALID(_sessionStartTime)) {
        _sessionStartTime = pts;
    }

    // Feed the pixel buffer to VideoToolbox for H.264 encoding
    OSStatus status = VTCompressionSessionEncodeFrame(
        _compressionSession,
        imageBuffer,
        pts,
        duration,
        NULL,  // frameProperties
        NULL,  // sourceFrameRefCon
        NULL   // infoFlagsOut
    );

    if (status != noErr) {
        BPLogError(@"VTCompressionSessionEncodeFrame failed: %d", (int)status);
    }
}

// ---------------------------------------------------------------------------
#pragma mark - Handle Encoded Output (called from VT callback)
// ---------------------------------------------------------------------------

- (void)handleEncodedSampleBuffer:(CMSampleBufferRef)sampleBuffer
{
    if (!_running) return;

    // Check if this is a keyframe
    CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, false);
    bool isKeyframe = false;
    if (attachments && CFArrayGetCount(attachments) > 0) {
        CFDictionaryRef dict = (CFDictionaryRef)CFArrayGetValueAtIndex(attachments, 0);
        CFBooleanRef notSync = NULL;
        if (CFDictionaryGetValueIfPresent(dict, kCMSampleAttachmentKey_NotSync, (const void **)&notSync)) {
            isKeyframe = !CFBooleanGetValue(notSync);
        } else {
            // If kCMSampleAttachmentKey_NotSync is absent, it's a sync frame
            isKeyframe = true;
        }
    } else {
        isKeyframe = true;  // No attachments = assume keyframe
    }

    // Extract SPS/PPS from the format description on keyframes
    if (isKeyframe) {
        CMFormatDescriptionRef formatDesc = CMSampleBufferGetFormatDescription(sampleBuffer);
        if (formatDesc) {
            [self extractParameterSets:formatDesc];
        }
    }

    // Get the encoded data
    CMBlockBufferRef blockBuffer = CMSampleBufferGetDataBuffer(sampleBuffer);
    if (!blockBuffer) return;

    size_t totalLength = 0;
    char *dataPointer = NULL;
    OSStatus status = CMBlockBufferGetDataPointer(blockBuffer, 0, NULL, &totalLength, &dataPointer);
    if (status != noErr || !dataPointer || totalLength == 0) return;

    NSData *encodedData = [NSData dataWithBytes:dataPointer length:totalLength];

    CMTime pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer);
    CMTime duration = CMSampleBufferGetDuration(sampleBuffer);

    // Create sample and add to ring buffer
    EncodedSample *sample = new EncodedSample(encodedData, pts, duration, isKeyframe);

    [_bufferLock lock];

    NSValue *wrapped = [NSValue valueWithPointer:sample];
    [_ringBuffer addObject:wrapped];
    _totalBufferBytes += (int64_t)totalLength;

    // Trim samples older than the window
    [self trimBufferLocked:pts];

    [_bufferLock unlock];
}

- (void)extractParameterSets:(CMFormatDescriptionRef)formatDesc
{
    // Extract SPS
    size_t spsSize = 0;
    const uint8_t *spsData = NULL;
    OSStatus status = CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
        formatDesc, 0, &spsData, &spsSize, NULL, NULL);
    if (status == noErr && spsData && spsSize > 0) {
        _sps = [NSData dataWithBytes:spsData length:spsSize];
    }

    // Extract PPS
    size_t ppsSize = 0;
    const uint8_t *ppsData = NULL;
    status = CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
        formatDesc, 1, &ppsData, &ppsSize, NULL, NULL);
    if (status == noErr && ppsData && ppsSize > 0) {
        _pps = [NSData dataWithBytes:ppsData length:ppsSize];
    }
}

// ---------------------------------------------------------------------------
#pragma mark - Ring Buffer Trimming (must hold _bufferLock)
// ---------------------------------------------------------------------------

- (void)trimBufferLocked:(CMTime)latestPts
{
    if (_ringBuffer.count < 2) return;

    double windowUs = (double)_windowSeconds * 1000000.0;
    double latestUs = CMTimeGetSeconds(latestPts) * 1000000.0;
    double cutoffUs = latestUs - windowUs;

    while (_ringBuffer.count > 2) {
        EncodedSample *oldest = (EncodedSample *)[_ringBuffer[0] pointerValue];
        double oldestUs = CMTimeGetSeconds(oldest->pts) * 1000000.0;

        if (oldestUs >= cutoffUs) break;

        // Only remove if there's a keyframe after this point we can start from
        if (![self hasKeyframeAfterCutoffLocked:cutoffUs]) break;

        _totalBufferBytes -= (int64_t)oldest->data.length;
        delete oldest;
        [_ringBuffer removeObjectAtIndex:0];
    }
}

- (BOOL)hasKeyframeAfterCutoffLocked:(double)cutoffUs
{
    for (NSValue *v in _ringBuffer) {
        EncodedSample *s = (EncodedSample *)[v pointerValue];
        double sUs = CMTimeGetSeconds(s->pts) * 1000000.0;
        if (sUs > cutoffUs && s->isKeyframe) return YES;
    }
    return NO;
}

- (void)clearBufferLocked
{
    for (NSValue *v in _ringBuffer) {
        EncodedSample *s = (EncodedSample *)[v pointerValue];
        delete s;
    }
    [_ringBuffer removeAllObjects];
    _totalBufferBytes = 0;
}

// ---------------------------------------------------------------------------
#pragma mark - Query
// ---------------------------------------------------------------------------

- (BOOL)isRunning { return _running; }

- (BOOL)hasFootage
{
    [_bufferLock lock];
    BOOL found = NO;
    for (NSValue *v in _ringBuffer) {
        EncodedSample *s = (EncodedSample *)[v pointerValue];
        if (s->isKeyframe) { found = YES; break; }
    }
    [_bufferLock unlock];
    return found;
}

- (int64_t)bufferSizeBytes
{
    [_bufferLock lock];
    int64_t size = _totalBufferBytes;
    [_bufferLock unlock];
    return size;
}

// ---------------------------------------------------------------------------
#pragma mark - Dump to MP4
// ---------------------------------------------------------------------------

- (BOOL)dumpToPath:(NSString *)outputPath
{
    if (!_sps || !_pps) {
        BPLogError(@"dump: no SPS/PPS available (no keyframes encoded yet)");
        return NO;
    }

    // Snapshot the ring buffer so the encoder can keep writing
    NSArray<NSValue *> *snapshot;
    NSData *snapshotSPS, *snapshotPPS;

    [_bufferLock lock];
    snapshot = [_ringBuffer copy];
    snapshotSPS = [_sps copy];
    snapshotPPS = [_pps copy];
    [_bufferLock unlock];

    if (snapshot.count == 0) {
        BPLogError(@"dump: buffer empty");
        return NO;
    }

    // Find the oldest keyframe within the window
    EncodedSample *lastSample = (EncodedSample *)[snapshot.lastObject pointerValue];
    double latestSec = CMTimeGetSeconds(lastSample->pts);
    double cutoffSec = latestSec - (double)_windowSeconds;

    NSInteger firstKeyframeIdx = -1;
    for (NSInteger i = 0; i < (NSInteger)snapshot.count; i++) {
        EncodedSample *s = (EncodedSample *)[snapshot[i] pointerValue];
        if (!s->isKeyframe) continue;
        double sSec = CMTimeGetSeconds(s->pts);
        if (sSec >= cutoffSec) {
            firstKeyframeIdx = i;
            break;
        }
        // Remember this as a candidate (most recent keyframe before cutoff)
        firstKeyframeIdx = i;
    }

    if (firstKeyframeIdx < 0) {
        BPLogError(@"dump: no keyframe found in buffer");
        return NO;
    }

    // Remove existing output file
    NSFileManager *fm = [NSFileManager defaultManager];
    NSError *error = nil;
    if ([fm fileExistsAtPath:outputPath]) {
        [fm removeItemAtPath:outputPath error:&error];
    }

    // Ensure parent directory exists
    NSString *parentDir = [outputPath stringByDeletingLastPathComponent];
    if (![fm fileExistsAtPath:parentDir]) {
        [fm createDirectoryAtPath:parentDir withIntermediateDirectories:YES attributes:nil error:&error];
    }

    // Create AVAssetWriter for MP4 output
    NSURL *outputURL = [NSURL fileURLWithPath:outputPath];
    AVAssetWriter *writer = [[AVAssetWriter alloc] initWithURL:outputURL
                                                      fileType:AVFileTypeMPEG4
                                                         error:&error];
    if (!writer || error) {
        BPLogError(@"dump: AVAssetWriter creation failed: %@", error);
        return NO;
    }

    // Create a CMFormatDescription from the SPS/PPS
    CMFormatDescriptionRef formatDesc = NULL;
    const uint8_t *parameterSetPointers[2] = {
        (const uint8_t *)snapshotSPS.bytes,
        (const uint8_t *)snapshotPPS.bytes
    };
    size_t parameterSetSizes[2] = {
        snapshotSPS.length,
        snapshotPPS.length
    };

    OSStatus status = CMVideoFormatDescriptionCreateFromH264ParameterSets(
        kCFAllocatorDefault,
        2,
        parameterSetPointers,
        parameterSetSizes,
        4,  // NAL unit header length (AVCC format uses 4-byte length prefix)
        &formatDesc
    );

    if (status != noErr) {
        BPLogError(@"dump: CMVideoFormatDescriptionCreateFromH264ParameterSets failed: %d", (int)status);
        return NO;
    }

    // Configure video input with passthrough (no re-encoding)
    AVAssetWriterInput *videoInput = [[AVAssetWriterInput alloc]
        initWithMediaType:AVMediaTypeVideo
           outputSettings:nil  // nil = passthrough, no re-encoding
         sourceFormatHint:formatDesc];
    videoInput.expectsMediaDataInRealTime = NO;

    if (![writer canAddInput:videoInput]) {
        BPLogError(@"dump: cannot add video input to AVAssetWriter");
        CFRelease(formatDesc);
        return NO;
    }
    [writer addInput:videoInput];

    // Start writing
    if (![writer startWriting]) {
        BPLogError(@"dump: AVAssetWriter startWriting failed: %@", writer.error);
        CFRelease(formatDesc);
        return NO;
    }

    // Rebase timestamps: first keyframe becomes time 0
    EncodedSample *firstSample = (EncodedSample *)[snapshot[firstKeyframeIdx] pointerValue];
    CMTime basePts = firstSample->pts;
    [writer startSessionAtSourceTime:kCMTimeZero];

    int samplesWritten = 0;

    for (NSInteger i = firstKeyframeIdx; i < (NSInteger)snapshot.count; i++) {
        EncodedSample *s = (EncodedSample *)[snapshot[i] pointerValue];

        // Rebase PTS to start at zero
        CMTime rebasedPts = CMTimeSubtract(s->pts, basePts);

        // Create CMBlockBuffer from the encoded data
        CMBlockBufferRef blockBuffer = NULL;
        status = CMBlockBufferCreateWithMemoryBlock(
            kCFAllocatorDefault,
            NULL,  // will allocate
            s->data.length,
            kCFAllocatorDefault,
            NULL,
            0,
            s->data.length,
            0,
            &blockBuffer
        );

        if (status != noErr) {
            BPLogError(@"dump: CMBlockBufferCreate failed at sample %d", (int)i);
            continue;
        }

        status = CMBlockBufferReplaceDataBytes(s->data.bytes, blockBuffer, 0, s->data.length);
        if (status != noErr) {
            BPLogError(@"dump: CMBlockBufferReplaceDataBytes failed at sample %d", (int)i);
            CFRelease(blockBuffer);
            continue;
        }

        // Create CMSampleBuffer
        CMSampleBufferRef sampleBuffer = NULL;
        size_t sampleSize = s->data.length;
        CMSampleTimingInfo timing;
        timing.presentationTimeStamp = rebasedPts;
        timing.duration = s->duration;
        timing.decodeTimeStamp = kCMTimeInvalid;

        status = CMSampleBufferCreateReady(
            kCFAllocatorDefault,
            blockBuffer,
            formatDesc,
            1,          // numSamples
            1,          // numSampleTimingEntries
            &timing,
            1,          // numSampleSizeEntries
            &sampleSize,
            &sampleBuffer
        );

        if (status != noErr) {
            BPLogError(@"dump: CMSampleBufferCreateReady failed at sample %d: %d",
                       (int)i, (int)status);
            CFRelease(blockBuffer);
            continue;
        }

        // Set sync sample attachment for keyframes
        CFArrayRef attachArr = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, true);
        if (attachArr && CFArrayGetCount(attachArr) > 0) {
            CFMutableDictionaryRef dict = (CFMutableDictionaryRef)CFArrayGetValueAtIndex(attachArr, 0);
            CFDictionarySetValue(dict, kCMSampleAttachmentKey_NotSync,
                                s->isKeyframe ? kCFBooleanFalse : kCFBooleanTrue);
        }

        // Wait for the input to be ready (brief spin)
        int waitCount = 0;
        while (!videoInput.readyForMoreMediaData && waitCount < 200) {
            usleep(500);
            waitCount++;
        }

        if (videoInput.readyForMoreMediaData) {
            if ([videoInput appendSampleBuffer:sampleBuffer]) {
                samplesWritten++;
            } else {
                BPLogError(@"dump: appendSampleBuffer failed at sample %d: %@",
                           (int)i, writer.error);
            }
        } else {
            BPLogError(@"dump: videoInput not ready at sample %d", (int)i);
        }

        CFRelease(sampleBuffer);
        CFRelease(blockBuffer);
    }

    // Finalize
    [videoInput markAsFinished];

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    __block BOOL writeSuccess = NO;

    [writer finishWritingWithCompletionHandler:^{
        writeSuccess = (writer.status == AVAssetWriterStatusCompleted);
        if (!writeSuccess) {
            BPLogError(@"dump: AVAssetWriter finishWriting failed: %@", writer.error);
        }
        dispatch_semaphore_signal(sem);
    }];

    dispatch_time_t timeout = dispatch_time(DISPATCH_TIME_NOW, (int64_t)(30.0 * NSEC_PER_SEC));
    if (dispatch_semaphore_wait(sem, timeout) != 0) {
        BPLogError(@"dump: timed out waiting for AVAssetWriter to finish");
        writeSuccess = NO;
    }

    CFRelease(formatDesc);

    if (writeSuccess) {
        BPLog(@"dump: wrote %d samples to %@", samplesWritten, outputPath);
    }

    return writeSuccess;
}

@end

// ===========================================================================
#pragma mark - C Bridge Functions
// ===========================================================================

extern "C" {

void BugpunchRing_Configure(int width, int height, int fps, int bitrate, int windowSeconds)
{
    @autoreleasepool {
        [[BugpunchRingRecorderImpl shared] configureWidth:width height:height
                                                      fps:fps bitrate:bitrate
                                            windowSeconds:windowSeconds];
    }
}

bool BugpunchRing_Start(void)
{
    @autoreleasepool {
        return [[BugpunchRingRecorderImpl shared] start];
    }
}

void BugpunchRing_Stop(void)
{
    @autoreleasepool {
        [[BugpunchRingRecorderImpl shared] stop];
    }
}

bool BugpunchRing_IsRunning(void)
{
    @autoreleasepool {
        return [[BugpunchRingRecorderImpl shared] isRunning];
    }
}

bool BugpunchRing_HasFootage(void)
{
    @autoreleasepool {
        return [[BugpunchRingRecorderImpl shared] hasFootage];
    }
}

bool BugpunchRing_Dump(const char* outputPath)
{
    @autoreleasepool {
        if (!outputPath) {
            BPLogError(@"BugpunchRing_Dump: outputPath is NULL");
            return false;
        }
        NSString *path = [NSString stringWithUTF8String:outputPath];
        return [[BugpunchRingRecorderImpl shared] dumpToPath:path];
    }
}

int64_t BugpunchRing_GetBufferSizeBytes(void)
{
    @autoreleasepool {
        return [[BugpunchRingRecorderImpl shared] bufferSizeBytes];
    }
}

} // extern "C"
