// BugpunchUploadCompression.h — transport compression for upload file parts.
// A part that compresses well is gzipped on the wire as `<filename>.bpgz`; the
// server's multipartOrJson inflates it on arrival, so storage and dashboard
// downloads only ever see the original file.
//
// Deciding is cheap: known-compressed extensions are skipped outright, and
// anything else deflates a 64 KB sample at the fastest level — the whole file
// is compressed only when that sample shrinks enough.
//
// Mirror: Android BugpunchUploadCompression.java, C# BugpunchUploadCompression.cs.

#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

extern NSString* const BPUploadCompressedSuffix;

/// Only the issue ingest + enrich routes run multipartOrJson, which is where
/// `.bpgz` parts get inflated; other endpoints get files as-is.
BOOL BPUploadServerInflates(NSString* _Nullable url);

BOOL BPUploadWorthCompressing(NSString* path, NSString* _Nullable filename);

/// gzip the file at `path` (fastest level) into `fh`. NO on any read / zlib /
/// write failure; the multipart body is then unusable.
BOOL BPUploadWriteGzipped(NSString* path, NSFileHandle* fh);

NS_ASSUME_NONNULL_END
