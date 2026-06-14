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
/// Always includes X-Api-Key + X-Device-Id; conditionally adds X-Player-Email
/// when the native SSO flow has surfaced a player email.
+ (nullable NSDictionary<NSString*, NSString*>*)baseHeaders;

/// Add the X-Player-Email header to {@code dst} when the SDK has a player
/// email configured. Lets callers compose player identity onto an arbitrary
/// header map.
+ (void)addPlayerIdentityHeadersTo:(NSMutableDictionary<NSString*, NSString*>*)dst;

/// Open a server attachment/media URL in the OS browser or player. The
/// `/api/files` route that serves screenshots, videos and attachments is
/// auth-gated; handing a bare URL to Safari / the system player (which can't
/// carry our X-Api-Key header) 401s to a login page and the clip never plays.
/// So `/api/files` URLs are first re-minted as a short-lived signed URL via
/// `/api/files/sign` (the same `?t=` token the dashboard uses) and THAT is
/// opened; other URLs open directly. Async; the open runs on the main thread.
/// Mirrors C# BugpunchHttp.OpenServerFile / Android BugpunchHttp.openServerFile.
+ (void)openServerFileURL:(NSString*)url;

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
