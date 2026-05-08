// BugpunchHttpClient.h — iOS lane mirror of Android's BugpunchHttp.java.
//
// First slice of issue #44 — consolidates the multipart-upload pattern that
// was hand-rolled in -[BugpunchChatViewController uploadAttachmentSync:] and
// -[BugpunchFeedbackViewController uploadAttachmentSync:].
//
// Scope (matches the Android slice):
//   - +baseHeaders                  — composes X-Api-Key + X-Device-Id +
//                                     X-Player-* from BPRuntime.
//   - +multipartUploadURL:...       — boundary + headers + body assembly,
//                                     synchronous via NSURLSession.
//
// Out of scope for this slice:
//   - JSON requests (each VC keeps its own -http: helper for now; next slice).
//   - Retry / queueing — chat + feedback uploads fail-fast on purpose.
//   - Response parsing — caller owns the response shape (chat → {ref};
//     feedback → {url, mime, width, height}).

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BPHttpResult : NSObject
@property (nonatomic, assign, readonly) NSInteger code;
@property (nonatomic, copy,   readonly) NSString*  body;
@property (nonatomic, assign, readonly) BOOL       ok;
- (instancetype)initWithCode:(NSInteger)code body:(nullable NSString*)body;
@end

@interface BPHttpClient : NSObject

/// Compose the SDK's standard request headers. Returns nil when the SDK
/// isn't configured (no API key) so callers can fail-fast without a try/catch.
/// Always includes X-Api-Key + X-Device-Id; conditionally adds X-Player-Auth-*
/// and X-Player-Email when player identity is configured.
+ (nullable NSDictionary<NSString*, NSString*>*)baseHeaders;

/// Add the X-Player-* headers to {@code dst} when player identity is set.
/// Lets callers compose player auth onto an arbitrary header map.
+ (void)addPlayerAuthHeadersTo:(NSMutableDictionary<NSString*, NSString*>*)dst;

/// Synchronous multipart/form-data POST of a single file part. Reuses the
/// caller's NSURLSession so connection-keepalive, timeout config, and
/// session delegate stay shared with the VC's other HTTP. Body is read
/// even on non-2xx so error envelopes surface in the warn log.
///
/// Returns a BPHttpResult with code=0 + the error message in body on IO
/// failure; callers branch on .ok.
+ (BPHttpResult*)multipartUploadURL:(NSURL*)url
                            headers:(NSDictionary<NSString*, NSString*>*)headers
                           filePath:(NSString*)filePath
                          fieldName:(NSString*)fieldName
                           filename:(NSString*)filename
                               mime:(NSString*)mime
                            timeout:(NSTimeInterval)timeout
                            session:(NSURLSession*)session;

@end

NS_ASSUME_NONNULL_END
