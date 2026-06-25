// BugpunchRecentReports.h — in-memory ring of the bug reports this INTERNAL
// tester just sent, kept for a short window. Backs BugpunchReportChooser: an
// internal tester who fires another bug report inside the window can comment on
// a recent one (carrying the fresh capture as a new occurrence) instead of
// filing a duplicate issue.
//
// Only populated for internal testers — the only role the chooser shows for.
// Each entry keeps a PRIVATE COPY of the report's screenshot + video dump so
// the preview survives BugpunchUploader's post-upload cleanup; the copies are
// deleted when the entry ages out of the window or is evicted. issueId is nil
// until the report's ingest upload acks (the uploader parses {issueId} and
// calls +onUploaded:issueId:).
//
// Mirror: Android BugpunchRecentReports.java, C# BugpunchRecentReports.cs.

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchRecentReportEntry : NSObject
@property (nonatomic, copy)   NSString* localId;
@property (nonatomic, copy)   NSString* desc;
@property (nonatomic, copy, nullable) NSString* previewVideoPath;
@property (nonatomic, copy, nullable) NSString* previewImagePath;
@property (nonatomic, assign) double createdAtMs;
@property (nonatomic, copy, nullable) NSString* issueId; // nil until ingest acks
@end

@interface BugpunchRecentReports : NSObject

/// Window (ms) after sending a report during which the comment shortcut is offered.
+ (double)windowMs;

/// Record a just-submitted bug report. Copies the screenshot + video dump into
/// a private dir that outlives the uploader cleanup. Returns the localId to
/// stamp into the upload manifest so +onUploaded:issueId: can fill in the
/// server issueId, or nil if nothing was kept.
+ (nullable NSString*)recordWithDescription:(nullable NSString*)description
                             screenshotPath:(nullable NSString*)screenshotPath
                                  videoPath:(nullable NSString*)videoPath;

/// Patch in the server issueId once the report's ingest upload succeeds.
+ (void)onUploaded:(NSString*)localId issueId:(NSString*)issueId;

/// Recent reports inside the window that have a known issueId (commentable),
/// newest first. Empty when nothing is in window.
+ (NSArray<BugpunchRecentReportEntry*>*)recentCommentable;

@end

NS_ASSUME_NONNULL_END
