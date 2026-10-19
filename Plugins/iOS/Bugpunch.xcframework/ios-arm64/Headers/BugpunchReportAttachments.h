// BugpunchReportAttachments.h — per-report attachments: the files a game hands
// to one Bugpunch.Report(..., attachments) call (a save file in a failure case,
// a folder of state, a wildcard of logs). Distinct from attachment RULES, which
// ride every report and are budget-gated on error types.
//
// C# hands the specs over in the report's extra under BPReportAttachmentsExtraKey;
// native owns the rest — matching, copying, zipping, caps and skip reasons.
// Files are copied at the Report() call (the upload is async, retries across
// launches, and deletes what it sent). Missing paths / over-cap files become
// `attachmentStatuses` rows the dashboard shows as skipped.
//
// Mirror: Android BugpunchReportAttachments.java, C# BugpunchReportAttachments.cs.

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

extern NSString* const BPReportAttachmentsExtraKey;

/// Staged result: @{ @"files": NSArray<attach dict>, @"statuses": NSArray<{name,reason}> }.
/// Attach dicts are { field, filename, contentType, path } — the shape the
/// uploader's extraAttachJson / preflight attachments take. Ungated.
typedef NSDictionary BPStagedReportAttachments;

/// Stage the spec array from `extra[BPReportAttachmentsExtraKey]`. Returns nil
/// when there is none. `outExtra` receives a copy of `extra` without the key.
BPStagedReportAttachments* _Nullable BPReportAttachmentsStageFromExtra(
    NSDictionary* _Nullable extra, NSDictionary* _Nullable * _Nullable outExtra);

/// Bug-report form hand-off: staged at trigger time, taken by Send, discarded
/// on cancel / comment-on-recent.
void BPReportAttachmentsSetPending(BPStagedReportAttachments* _Nullable staged);
BPStagedReportAttachments* _Nullable BPReportAttachmentsTakePending(void);
void BPReportAttachmentsDiscardPending(void);

/// Append the staged files to `attach`, renaming a field that collides with
/// one already present (an attachment rule of the same name).
void BPReportAttachmentsAppendTo(BPStagedReportAttachments* _Nullable staged,
                                 NSMutableArray* attach);

/// Fold the skip reasons into the metadata's top-level `attachmentStatuses`.
NSString* BPReportAttachmentsWithStatuses(NSString* metadataJson,
                                          BPStagedReportAttachments* _Nullable staged);

NS_ASSUME_NONNULL_END
