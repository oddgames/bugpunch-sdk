// BugpunchUploader.mm — native iOS uploader.
//
// Owns the upload queue, does multipart POSTs via NSURLSession with retry,
// cleans up on success. Queue lives in {caches}/bugpunch_uploads/*.upload.json.
//
// Manifest schema — see BugpunchUploader.java for details. Same format on both
// platforms so server sees identical requests.

#import <Foundation/Foundation.h>

// Defined in BugpunchDirectives.mm. Forward-declared at file scope because
// `extern "C"` is only valid at namespace/file scope in Obj-C++ — putting
// it inside a method body fails to compile (clang: "expected unqualified-id").
#ifdef __cplusplus
extern "C" {
#endif
void BPDirectives_OnUploadResponse(const char* url, const char* responseBody);
// Defined in BugpunchSdkErrorOverlay.mm — surfaces SDK self-errors on the
// on-screen diagnostic banner so swallowed failures don't leave the dev/QA in
// the dark.
void Bugpunch_ReportSdkError(const char* source, const char* message, const char* stack);
#ifdef __cplusplus
}
#endif

static inline void BPReportUploaderError(NSString* op, NSError* err) {
    NSString* msg = [NSString stringWithFormat:@"%@: %@", op, err ? err.localizedDescription : @"(no error info)"];
    Bugpunch_ReportSdkError("BugpunchUploader", msg.UTF8String, "");
}

static NSString* const kQueueDir = @"bugpunch_uploads";
static const NSInteger kMaxAttempts = 10;
static const NSTimeInterval kConnectTimeout = 15.0;
static const NSTimeInterval kResourceTimeout = 90.0;

static dispatch_queue_t BPUploaderQueue(void) {
    static dispatch_queue_t q;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        q = dispatch_queue_create("com.oddgames.bugpunch.uploader", DISPATCH_QUEUE_SERIAL);
    });
    return q;
}

static NSString* BPQueueDirPath(void) {
    static NSString* path;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSString* caches = NSSearchPathForDirectoriesInDomains(
            NSCachesDirectory, NSUserDomainMask, YES).firstObject;
        path = [caches stringByAppendingPathComponent:kQueueDir];
        [[NSFileManager defaultManager] createDirectoryAtPath:path
                                  withIntermediateDirectories:YES
                                                   attributes:nil
                                                        error:nil];
    });
    return path;
}

static NSURLSession* BPSession(void) {
    static NSURLSession* s;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
        cfg.timeoutIntervalForRequest = kConnectTimeout;
        cfg.timeoutIntervalForResource = kResourceTimeout;
        s = [NSURLSession sessionWithConfiguration:cfg];
    });
    return s;
}

static void BPCleanup(NSDictionary* manifest) {
    NSArray* paths = manifest[@"cleanupPaths"];
    if (![paths isKindOfClass:[NSArray class]]) return;
    NSFileManager* fm = [NSFileManager defaultManager];
    for (NSString* p in paths) {
        if (![p isKindOfClass:[NSString class]] || p.length == 0) continue;
        [fm removeItemAtPath:p error:nil];
    }
}

static NSData* BPBuildMultipart(NSDictionary* manifest, NSString* boundary) {
    NSMutableData* body = [NSMutableData data];
    NSDictionary* fields = manifest[@"fields"];
    if ([fields isKindOfClass:[NSDictionary class]]) {
        for (NSString* k in fields) {
            id v = fields[k];
            NSString* sval = [v isKindOfClass:[NSString class]] ? (NSString*)v
                : [NSString stringWithFormat:@"%@", v];
            [body appendData:[[NSString stringWithFormat:@"--%@\r\n", boundary]
                dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[[NSString stringWithFormat:
                @"Content-Disposition: form-data; name=\"%@\"\r\n", k]
                dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[@"Content-Type: text/plain; charset=utf-8\r\n\r\n"
                dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[sval dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[@"\r\n" dataUsingEncoding:NSUTF8StringEncoding]];
        }
    }
    NSArray* files = manifest[@"files"];
    if ([files isKindOfClass:[NSArray class]]) {
        NSFileManager* fm = [NSFileManager defaultManager];
        for (NSDictionary* f in files) {
            if (![f isKindOfClass:[NSDictionary class]]) continue;
            NSString* path = f[@"path"];
            if (![path isKindOfClass:[NSString class]] || ![fm fileExistsAtPath:path]) continue;
            NSData* data = [NSData dataWithContentsOfFile:path];
            if (!data) continue;
            NSString* field = f[@"field"] ?: @"file";
            NSString* filename = f[@"filename"] ?: path.lastPathComponent;
            NSString* contentType = f[@"contentType"] ?: @"application/octet-stream";
            [body appendData:[[NSString stringWithFormat:@"--%@\r\n", boundary]
                dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[[NSString stringWithFormat:
                @"Content-Disposition: form-data; name=\"%@\"; filename=\"%@\"\r\n",
                field, filename] dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:[[NSString stringWithFormat:@"Content-Type: %@\r\n\r\n", contentType]
                dataUsingEncoding:NSUTF8StringEncoding]];
            [body appendData:data];
            [body appendData:[@"\r\n" dataUsingEncoding:NSUTF8StringEncoding]];
        }
    }
    [body appendData:[[NSString stringWithFormat:@"--%@--\r\n", boundary]
        dataUsingEncoding:NSUTF8StringEncoding]];
    return body;
}

/**
 * After a successful phase-1 preflight, parse {@code responseBody} for
 * {@code eventId} + {@code collect[]} and rewrite {@code manifest} in place
 * to the phase-2 enrich shape (url with {id} substituted, rawJsonBody
 * removed, files filtered by {@code requires} ∈ collect, attempts reset).
 * Returns YES if the manifest was written back; NO when nothing needs to be
 * sent (empty collect, no matching files, or invalid response).
 */
static BOOL BPTransitionToEnrich(NSMutableDictionary* manifest, NSString* manifestPath,
                                 NSData* responseData) {
    if (!responseData || responseData.length == 0) return NO;
    NSError* err = nil;
    id parsed = [NSJSONSerialization JSONObjectWithData:responseData options:0 error:&err];
    if (err || ![parsed isKindOfClass:[NSDictionary class]]) return NO;
    NSDictionary* res = (NSDictionary*)parsed;
    NSString* eventId = res[@"eventId"];
    NSArray* collectArr = res[@"collect"];
    if (![eventId isKindOfClass:[NSString class]] || eventId.length == 0) return NO;
    if (![collectArr isKindOfClass:[NSArray class]] || collectArr.count == 0) return NO;

    NSString* enrichTemplate = manifest[@"enrichUrlTemplate"];
    if (![enrichTemplate isKindOfClass:[NSString class]] || enrichTemplate.length == 0) return NO;

    NSMutableSet* collect = [NSMutableSet set];
    for (id v in collectArr) if ([v isKindOfClass:[NSString class]]) [collect addObject:v];

    NSArray* inFiles = manifest[@"files"];
    NSMutableArray* outFiles = [NSMutableArray array];
    if ([inFiles isKindOfClass:[NSArray class]]) {
        for (NSDictionary* f in inFiles) {
            if (![f isKindOfClass:[NSDictionary class]]) continue;
            NSString* req = f[@"requires"];
            if (![req isKindOfClass:[NSString class]] || req.length == 0
                || [collect containsObject:req]) {
                [outFiles addObject:f];
            }
        }
    }
    if (outFiles.count == 0) return NO;

    manifest[@"stage"] = @"enrich";
    manifest[@"url"] = [enrichTemplate stringByReplacingOccurrencesOfString:@"{id}" withString:eventId];
    [manifest removeObjectForKey:@"enrichUrlTemplate"];
    [manifest removeObjectForKey:@"rawJsonBody"];
    NSMutableDictionary* headers = [manifest[@"headers"] mutableCopy];
    [headers removeObjectForKey:@"Content-Type"];
    manifest[@"headers"] = headers ?: @{};
    manifest[@"files"] = outFiles;
    manifest[@"attempts"] = @0;

    NSData* out = [NSJSONSerialization dataWithJSONObject:manifest options:0 error:nil];
    if (!out) return NO;
    [out writeToFile:manifestPath atomically:YES];
    return YES;
}

static void BPProcessOne(NSString* manifestPath, dispatch_group_t group) {
    NSError* err = nil;
    NSData* raw = [NSData dataWithContentsOfFile:manifestPath];
    if (!raw) return;
    NSMutableDictionary* manifest =
        [[NSJSONSerialization JSONObjectWithData:raw options:0 error:&err] mutableCopy];
    if (err || !manifest) {
        NSLog(@"[BugpunchUploader] bad manifest %@: %@", manifestPath, err);
        [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
        return;
    }

    NSString* urlStr = manifest[@"url"];
    if (![urlStr isKindOfClass:[NSString class]] || urlStr.length == 0) {
        [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
        return;
    }

    NSString* stage = manifest[@"stage"];
    BOOL isPreflight = [stage isKindOfClass:[NSString class]] && [stage isEqualToString:@"preflight"];
    NSString* rawJsonBody = manifest[@"rawJsonBody"];
    BOOL isJson = [rawJsonBody isKindOfClass:[NSString class]];

    NSString* boundary = nil;
    NSData* body;
    if (isJson) {
        body = [rawJsonBody dataUsingEncoding:NSUTF8StringEncoding];
    } else {
        boundary = [NSString stringWithFormat:@"----BugpunchBoundary%@",
            [[NSUUID UUID] UUIDString]];
        body = BPBuildMultipart(manifest, boundary);
    }

    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:urlStr]];
    req.HTTPMethod = @"POST";
    if (isJson) {
        [req setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    } else {
        [req setValue:[NSString stringWithFormat:@"multipart/form-data; boundary=%@", boundary]
            forHTTPHeaderField:@"Content-Type"];
    }
    NSDictionary* headers = manifest[@"headers"];
    if ([headers isKindOfClass:[NSDictionary class]]) {
        for (NSString* k in headers) {
            id v = headers[k];
            if ([v isKindOfClass:[NSString class]]) [req setValue:v forHTTPHeaderField:k];
        }
    }
    req.HTTPBody = body;

    dispatch_group_enter(group);
    NSURLSessionDataTask* task = [BPSession() dataTaskWithRequest:req
        completionHandler:^(NSData * _Nullable d, NSURLResponse * _Nullable resp, NSError * _Nullable e) {
            NSInteger code = 0;
            if ([resp isKindOfClass:[NSHTTPURLResponse class]]) {
                code = ((NSHTTPURLResponse*)resp).statusCode;
            }
            BOOL ok = (code >= 200 && code < 300) && !e;

            if (ok) {
                NSLog(@"[BugpunchUploader] uploaded %@", urlStr);
                if (isPreflight) {
                    // Phase-1 /api/crashes response carries matchedDirectives[]
                    // + eventId + collect[]. Dispatch directives now, then
                    // transition the manifest to phase 2.
                    if (d.length > 0) {
                        NSString* respBody = [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding];
                        if (respBody) BPDirectives_OnUploadResponse([urlStr UTF8String], [respBody UTF8String]);
                    }
                    BOOL advanced = BPTransitionToEnrich(manifest, manifestPath, d);
                    if (!advanced) {
                        // collect=[] (budget spent) or nothing to send —
                        // clean up all attachments and drop the manifest.
                        BPCleanup(manifest);
                        [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
                    } else {
                        // Kick another drain so phase 2 runs immediately.
                        dispatch_async(BPUploaderQueue(), ^{ BPDrainQueueSync(); });
                    }
                } else {
                    BPCleanup(manifest);
                    [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
                }
            } else {
                NSInteger attempts = [manifest[@"attempts"] integerValue] + 1;
                BOOL terminal = (code == 400 || code == 401 || code == 403);
                if (attempts >= kMaxAttempts || terminal) {
                    NSLog(@"[BugpunchUploader] dropping after %ld attempts (HTTP %ld, err=%@): %@",
                        (long)attempts, (long)code, e.localizedDescription, urlStr);
                    BPCleanup(manifest);
                    [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
                } else {
                    manifest[@"attempts"] = @(attempts);
                    NSData* out = [NSJSONSerialization dataWithJSONObject:manifest options:0 error:nil];
                    if (out) [out writeToFile:manifestPath atomically:YES];
                    NSLog(@"[BugpunchUploader] retry %ld/%ld (HTTP %ld, err=%@): %@",
                        (long)attempts, (long)kMaxAttempts, (long)code, e.localizedDescription, urlStr);
                }
            }
            dispatch_group_leave(group);
        }];
    [task resume];
}

static void BPDrainQueueSync(void) {
    NSString* dir = BPQueueDirPath();
    NSArray<NSString*>* files = [[NSFileManager defaultManager]
        contentsOfDirectoryAtPath:dir error:nil];
    if (files.count == 0) return;
    dispatch_group_t group = dispatch_group_create();
    for (NSString* name in files) {
        if (![name hasSuffix:@".upload.json"]) continue;
        NSString* path = [dir stringByAppendingPathComponent:name];
        BPProcessOne(path, group);
    }
    dispatch_group_wait(group, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(kResourceTimeout * NSEC_PER_SEC)));
}

extern "C" {

// Forward declaration so the thin `Bugpunch_EnqueueReport` trampoline can
// call the extended variant defined below.
void Bugpunch_EnqueueReportFull(const char* url, const char* apiKey,
                                const char* metadataJson,
                                const char* screenshotPath, const char* videoPath,
                                const char* annotationsPath,
                                const char* tracesJsonPath,
                                const char* traceScreenshotPathsCsv,
                                const char* logsGzPath);

/// Enqueue a report. All args passed as primitive C strings — no JSON glue
/// in C#. Empty-string paths are treated as absent.
void Bugpunch_EnqueueReport(const char* url, const char* apiKey,
                            const char* metadataJson,
                            const char* screenshotPath, const char* videoPath,
                            const char* annotationsPath) {
    Bugpunch_EnqueueReportFull(url, apiKey, metadataJson,
        screenshotPath, videoPath, annotationsPath, NULL, NULL, NULL);
}

/// Legacy variant without logsGzPath — forwards to full version.
void Bugpunch_EnqueueReportWithTraces(const char* url, const char* apiKey,
                                      const char* metadataJson,
                                      const char* screenshotPath, const char* videoPath,
                                      const char* annotationsPath,
                                      const char* tracesJsonPath,
                                      const char* traceScreenshotPathsCsv) {
    Bugpunch_EnqueueReportFull(url, apiKey, metadataJson,
        screenshotPath, videoPath, annotationsPath,
        tracesJsonPath, traceScreenshotPathsCsv, NULL);
}

/// Full variant with context screenshot — includes the "before" frame from
/// the rolling buffer alongside the event screenshot.
void Bugpunch_EnqueueReportWithContext(const char* url, const char* apiKey,
                                       const char* metadataJson,
                                       const char* screenshotPath,
                                       const char* contextScreenshotPath,
                                       const char* videoPath,
                                       const char* annotationsPath,
                                       const char* tracesJsonPath,
                                       const char* traceScreenshotPathsCsv,
                                       const char* logsGzPath) {
    if (!url || !*url) return;
    NSString* nsUrl = [NSString stringWithUTF8String:url];
    NSString* nsKey = apiKey ? [NSString stringWithUTF8String:apiKey] : @"";
    NSString* nsMeta = metadataJson ? [NSString stringWithUTF8String:metadataJson] : @"";
    NSString* nsShot = (screenshotPath && *screenshotPath)
        ? [NSString stringWithUTF8String:screenshotPath] : nil;
    NSString* nsCtxShot = (contextScreenshotPath && *contextScreenshotPath)
        ? [NSString stringWithUTF8String:contextScreenshotPath] : nil;
    NSString* nsVideo = (videoPath && *videoPath)
        ? [NSString stringWithUTF8String:videoPath] : nil;
    NSString* nsAnno = (annotationsPath && *annotationsPath)
        ? [NSString stringWithUTF8String:annotationsPath] : nil;
    NSString* nsTraces = (tracesJsonPath && *tracesJsonPath)
        ? [NSString stringWithUTF8String:tracesJsonPath] : nil;
    NSString* nsLogsGz = (logsGzPath && *logsGzPath)
        ? [NSString stringWithUTF8String:logsGzPath] : nil;
    NSArray<NSString*>* nsTraceShots = nil;
    if (traceScreenshotPathsCsv && *traceScreenshotPathsCsv) {
        NSString* csv = [NSString stringWithUTF8String:traceScreenshotPathsCsv];
        nsTraceShots = [csv componentsSeparatedByString:@","];
    }

    dispatch_async(BPUploaderQueue(), ^{
        NSMutableDictionary* m = [NSMutableDictionary dictionary];
        m[@"url"] = nsUrl;
        m[@"headers"] = @{ @"X-Api-Key": nsKey };
        m[@"fields"] = @{ @"metadata": nsMeta };
        NSMutableArray* files = [NSMutableArray array];
        NSMutableArray* cleanup = [NSMutableArray array];
        if (nsCtxShot) {
            [files addObject:@{ @"field": @"context_screenshot", @"filename": @"context_screenshot.jpg",
                                @"contentType": @"image/jpeg", @"path": nsCtxShot }];
            [cleanup addObject:nsCtxShot];
        }
        if (nsShot) {
            [files addObject:@{ @"field": @"screenshot", @"filename": @"screenshot.jpg",
                                @"contentType": @"image/jpeg", @"path": nsShot }];
            [cleanup addObject:nsShot];
        }
        if (nsVideo) {
            [files addObject:@{ @"field": @"video", @"filename": @"video.mp4",
                                @"contentType": @"video/mp4", @"path": nsVideo }];
            [cleanup addObject:nsVideo];
        }
        if (nsAnno) {
            [files addObject:@{ @"field": @"annotations", @"filename": @"annotations.png",
                                @"contentType": @"image/png", @"path": nsAnno }];
            [cleanup addObject:nsAnno];
        }
        if (nsTraces) {
            [files addObject:@{ @"field": @"traces", @"filename": @"traces.json",
                                @"contentType": @"application/json", @"path": nsTraces }];
            [cleanup addObject:nsTraces];
        }
        if (nsTraceShots) {
            for (NSUInteger i = 0; i < nsTraceShots.count; i++) {
                NSString* p = nsTraceShots[i];
                if (p.length == 0) continue;
                [files addObject:@{
                    @"field": [NSString stringWithFormat:@"trace_%lu", (unsigned long)i],
                    @"filename": [NSString stringWithFormat:@"trace_%lu.jpg", (unsigned long)i],
                    @"contentType": @"image/jpeg",
                    @"path": p }];
                [cleanup addObject:p];
            }
        }
        if (nsLogsGz) {
            [files addObject:@{ @"field": @"logs", @"filename": @"logs.log.gz",
                                @"contentType": @"application/gzip", @"path": nsLogsGz }];
            [cleanup addObject:nsLogsGz];
        }
        m[@"files"] = files;
        m[@"cleanupPaths"] = cleanup;
        m[@"attempts"] = @0;

        NSError* err = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:m options:0 error:&err];
        if (!data) {
            NSLog(@"[BugpunchUploader] manifest serialize failed: %@", err);
            BPReportUploaderError(@"manifest serialize", err);
            return;
        }
        NSString* dir = BPQueueDirPath();
        NSString* path = [dir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%@.upload.json", [[NSUUID UUID] UUIDString]]];
        if (![data writeToFile:path atomically:YES]) {
            NSLog(@"[BugpunchUploader] manifest write failed: %@", path);
            BPReportUploaderError([NSString stringWithFormat:@"manifest write failed: %@", path], nil);
            return;
        }
        BPDrainQueueSync();
    });
}

/// Full variant — traces + gzip'd logs file.
void Bugpunch_EnqueueReportFull(const char* url, const char* apiKey,
                                const char* metadataJson,
                                const char* screenshotPath, const char* videoPath,
                                const char* annotationsPath,
                                const char* tracesJsonPath,
                                const char* traceScreenshotPathsCsv,
                                const char* logsGzPath) {
    if (!url || !*url) return;
    NSString* nsUrl = [NSString stringWithUTF8String:url];
    NSString* nsKey = apiKey ? [NSString stringWithUTF8String:apiKey] : @"";
    NSString* nsMeta = metadataJson ? [NSString stringWithUTF8String:metadataJson] : @"";
    NSString* nsShot = (screenshotPath && *screenshotPath)
        ? [NSString stringWithUTF8String:screenshotPath] : nil;
    NSString* nsVideo = (videoPath && *videoPath)
        ? [NSString stringWithUTF8String:videoPath] : nil;
    NSString* nsAnno = (annotationsPath && *annotationsPath)
        ? [NSString stringWithUTF8String:annotationsPath] : nil;
    NSString* nsTraces = (tracesJsonPath && *tracesJsonPath)
        ? [NSString stringWithUTF8String:tracesJsonPath] : nil;
    NSString* nsLogsGz = (logsGzPath && *logsGzPath)
        ? [NSString stringWithUTF8String:logsGzPath] : nil;
    NSArray<NSString*>* nsTraceShots = nil;
    if (traceScreenshotPathsCsv && *traceScreenshotPathsCsv) {
        NSString* csv = [NSString stringWithUTF8String:traceScreenshotPathsCsv];
        nsTraceShots = [csv componentsSeparatedByString:@","];
    }

    dispatch_async(BPUploaderQueue(), ^{
        NSMutableDictionary* m = [NSMutableDictionary dictionary];
        m[@"url"] = nsUrl;
        m[@"headers"] = @{ @"X-Api-Key": nsKey };
        m[@"fields"] = @{ @"metadata": nsMeta };
        NSMutableArray* files = [NSMutableArray array];
        NSMutableArray* cleanup = [NSMutableArray array];
        if (nsShot) {
            [files addObject:@{ @"field": @"screenshot", @"filename": @"screenshot.jpg",
                                @"contentType": @"image/jpeg", @"path": nsShot }];
            [cleanup addObject:nsShot];
        }
        if (nsVideo) {
            [files addObject:@{ @"field": @"video", @"filename": @"video.mp4",
                                @"contentType": @"video/mp4", @"path": nsVideo }];
            [cleanup addObject:nsVideo];
        }
        if (nsAnno) {
            [files addObject:@{ @"field": @"annotations", @"filename": @"annotations.png",
                                @"contentType": @"image/png", @"path": nsAnno }];
            [cleanup addObject:nsAnno];
        }
        if (nsTraces) {
            [files addObject:@{ @"field": @"traces", @"filename": @"traces.json",
                                @"contentType": @"application/json", @"path": nsTraces }];
            [cleanup addObject:nsTraces];
        }
        if (nsTraceShots) {
            for (NSUInteger i = 0; i < nsTraceShots.count; i++) {
                NSString* p = nsTraceShots[i];
                if (p.length == 0) continue;
                [files addObject:@{
                    @"field": [NSString stringWithFormat:@"trace_%lu", (unsigned long)i],
                    @"filename": [NSString stringWithFormat:@"trace_%lu.jpg", (unsigned long)i],
                    @"contentType": @"image/jpeg",
                    @"path": p }];
                [cleanup addObject:p];
            }
        }
        if (nsLogsGz) {
            [files addObject:@{ @"field": @"logs", @"filename": @"logs.log.gz",
                                @"contentType": @"application/gzip", @"path": nsLogsGz }];
            [cleanup addObject:nsLogsGz];
        }
        m[@"files"] = files;
        m[@"cleanupPaths"] = cleanup;
        m[@"attempts"] = @0;

        NSError* err = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:m options:0 error:&err];
        if (!data) {
            NSLog(@"[BugpunchUploader] manifest serialize failed: %@", err);
            BPReportUploaderError(@"manifest serialize", err);
            return;
        }
        NSString* dir = BPQueueDirPath();
        NSString* path = [dir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%@.upload.json", [[NSUUID UUID] UUIDString]]];
        if (![data writeToFile:path atomically:YES]) {
            NSLog(@"[BugpunchUploader] manifest write failed: %@", path);
            BPReportUploaderError([NSString stringWithFormat:@"manifest write failed: %@", path], nil);
            return;
        }
        BPDrainQueueSync();
    });
}

/// Kick the worker to scan and drain the queue.
void Bugpunch_DrainUploadQueue(void) {
    dispatch_async(BPUploaderQueue(), ^{
        BPDrainQueueSync();
    });
}

/**
 * Two-phase preflight + enrich. Phase 1 posts `jsonBody` to `preflightUrl`
 * (/api/crashes); server response returns `eventId` + `collect[]`. On
 * success the uploader rewrites the manifest to phase 2: multipart POST to
 * `enrichUrlTemplate` (with {id} substituted) carrying only the attachments
 * whose `requires` key appears in `collect`. Empty collect = skip phase 2
 * and clean up. Both phases persist across app kill via the disk queue.
 *
 * `attachmentsJson` is a JSON array: [{field,filename,contentType,path,requires}, ...]
 * (same shape as the on-disk manifest files[]). Passed as a string so the
 * C# / callsite doesn't need to know iOS-specific collection types.
 */
void Bugpunch_EnqueuePreflight(const char* preflightUrlC, const char* enrichUrlTemplateC,
                               const char* apiKeyC, const char* jsonBodyC,
                               const char* attachmentsJsonC) {
    if (!preflightUrlC || !*preflightUrlC) return;
    NSString* preflightUrl = [NSString stringWithUTF8String:preflightUrlC];
    NSString* enrichTemplate = (enrichUrlTemplateC && *enrichUrlTemplateC)
        ? [NSString stringWithUTF8String:enrichUrlTemplateC] : @"";
    NSString* apiKey = apiKeyC ? [NSString stringWithUTF8String:apiKeyC] : @"";
    NSString* jsonBody = (jsonBodyC && *jsonBodyC)
        ? [NSString stringWithUTF8String:jsonBodyC] : @"{}";
    NSString* attachmentsJson = (attachmentsJsonC && *attachmentsJsonC)
        ? [NSString stringWithUTF8String:attachmentsJsonC] : @"[]";

    dispatch_async(BPUploaderQueue(), ^{
        NSArray* parsedAttach = nil;
        NSError* parseErr = nil;
        id parsed = [NSJSONSerialization JSONObjectWithData:
            [attachmentsJson dataUsingEncoding:NSUTF8StringEncoding]
            options:0 error:&parseErr];
        if ([parsed isKindOfClass:[NSArray class]]) parsedAttach = (NSArray*)parsed;

        NSMutableArray* files = [NSMutableArray array];
        NSMutableArray* cleanup = [NSMutableArray array];
        if (parsedAttach) {
            for (NSDictionary* a in parsedAttach) {
                if (![a isKindOfClass:[NSDictionary class]]) continue;
                NSString* path = a[@"path"];
                if (![path isKindOfClass:[NSString class]] || path.length == 0) continue;
                NSMutableDictionary* f = [NSMutableDictionary dictionary];
                f[@"field"] = a[@"field"] ?: @"file";
                f[@"filename"] = a[@"filename"] ?: path.lastPathComponent;
                f[@"contentType"] = a[@"contentType"] ?: @"application/octet-stream";
                f[@"path"] = path;
                NSString* req = a[@"requires"];
                if ([req isKindOfClass:[NSString class]] && req.length > 0) f[@"requires"] = req;
                [files addObject:f];
                [cleanup addObject:path];
            }
        }

        NSMutableDictionary* m = [NSMutableDictionary dictionary];
        m[@"stage"] = @"preflight";
        m[@"url"] = preflightUrl;
        m[@"enrichUrlTemplate"] = enrichTemplate;
        m[@"headers"] = @{ @"X-Api-Key": apiKey, @"Content-Type": @"application/json" };
        m[@"rawJsonBody"] = jsonBody;
        m[@"files"] = files;
        m[@"cleanupPaths"] = cleanup;
        m[@"attempts"] = @0;

        NSError* err = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:m options:0 error:&err];
        if (!data) { NSLog(@"[BugpunchUploader] preflight serialize failed: %@", err); BPReportUploaderError(@"preflight serialize", err); return; }
        NSString* dir = BPQueueDirPath();
        NSString* path = [dir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%@.upload.json", [[NSUUID UUID] UUIDString]]];
        if (![data writeToFile:path atomically:YES]) {
            NSLog(@"[BugpunchUploader] preflight manifest write failed: %@", path);
            BPReportUploaderError([NSString stringWithFormat:@"preflight manifest write: %@", path], nil);
            return;
        }
        BPDrainQueueSync();
    });
}

/// Enqueue a JSON POST. Used by directive enrichment \u2014 same retry +
/// app-kill survival as multipart uploads.
void BugpunchUploader_EnqueueJson(const char* urlC, const char* apiKeyC,
                                  const char* jsonBodyC) {
    if (!urlC || !apiKeyC) return;
    NSString* url = [NSString stringWithUTF8String:urlC];
    NSString* apiKey = [NSString stringWithUTF8String:apiKeyC];
    NSString* body = jsonBodyC ? [NSString stringWithUTF8String:jsonBodyC] : @"{}";

    dispatch_async(BPUploaderQueue(), ^{
        NSMutableDictionary* m = [NSMutableDictionary dictionary];
        m[@"url"] = url;
        m[@"headers"] = @{ @"X-Api-Key": apiKey, @"Content-Type": @"application/json" };
        m[@"rawJsonBody"] = body;
        m[@"attempts"] = @0;

        NSError* err = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:m options:0 error:&err];
        if (!data) { NSLog(@"[BugpunchUploader] json manifest serialize failed: %@", err); BPReportUploaderError(@"json manifest serialize", err); return; }
        NSString* dir = BPQueueDirPath();
        NSString* path = [dir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%@.upload.json", [[NSUUID UUID] UUIDString]]];
        [data writeToFile:path atomically:YES];
        BPDrainQueueSync();
    });
}

}
