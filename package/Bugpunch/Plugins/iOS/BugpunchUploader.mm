// BugpunchUploader.mm — native iOS uploader.
//
// Owns the upload queue, does multipart POSTs via NSURLSession with retry,
// cleans up on success. Queue lives in {caches}/bugpunch_uploads/*.upload.json.
//
// Manifest schema — see BugpunchUploader.java for details. Same format on both
// platforms so server sees identical requests.

#import <Foundation/Foundation.h>

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

    NSString* boundary = [NSString stringWithFormat:@"----BugpunchBoundary%@",
        [[NSUUID UUID] UUIDString]];
    NSData* body = BPBuildMultipart(manifest, boundary);

    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:urlStr]];
    req.HTTPMethod = @"POST";
    [req setValue:[NSString stringWithFormat:@"multipart/form-data; boundary=%@", boundary]
        forHTTPHeaderField:@"Content-Type"];
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
                BPCleanup(manifest);
                [[NSFileManager defaultManager] removeItemAtPath:manifestPath error:nil];
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

/// Enqueue a report. All args passed as primitive C strings — no JSON glue
/// in C#. Empty-string paths are treated as absent.
void Bugpunch_EnqueueReport(const char* url, const char* apiKey,
                            const char* metadataJson,
                            const char* screenshotPath, const char* videoPath,
                            const char* annotationsPath) {
    Bugpunch_EnqueueReportWithTraces(url, apiKey, metadataJson,
        screenshotPath, videoPath, annotationsPath, NULL, NULL);
}

/// Extended variant — additionally accepts a traces JSON file path and a
/// CSV string of per-trace screenshot paths. Either may be NULL.
void Bugpunch_EnqueueReportWithTraces(const char* url, const char* apiKey,
                                      const char* metadataJson,
                                      const char* screenshotPath, const char* videoPath,
                                      const char* annotationsPath,
                                      const char* tracesJsonPath,
                                      const char* traceScreenshotPathsCsv) {
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
        m[@"files"] = files;
        m[@"cleanupPaths"] = cleanup;
        m[@"attempts"] = @0;

        NSError* err = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:m options:0 error:&err];
        if (!data) {
            NSLog(@"[BugpunchUploader] manifest serialize failed: %@", err);
            return;
        }
        NSString* dir = BPQueueDirPath();
        NSString* path = [dir stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%@.upload.json", [[NSUUID UUID] UUIDString]]];
        if (![data writeToFile:path atomically:YES]) {
            NSLog(@"[BugpunchUploader] manifest write failed: %@", path);
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

}
