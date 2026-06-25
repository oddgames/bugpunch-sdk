// BugpunchReportChooser.h — "You just reported something — comment instead?"
// chooser, shown to an INTERNAL tester who fires another bug report within
// BugpunchRecentReports.windowMs of sending one. Lists the recent reports, each
// with a looping preview of the LAST 10 SECONDS of its captured video (or its
// screenshot when there was no recording) plus the description, so the tester
// can recognise it and add a comment (carrying THIS fresh capture as a new
// occurrence) instead of filing a duplicate issue.
//
// Mirror: Android BugpunchReportChooser.java, C# BugpunchReportChooser.cs.

#import <UIKit/UIKit.h>

@class BugpunchRecentReportEntry;

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchReportChooser : UIViewController

/// Present the chooser for `recent` (newest first) on the Bugpunch nav window.
/// `screenshotPath` / `title` / `description` are the fresh report the tester
/// just triggered — staged as new evidence on a comment, or forwarded to the
/// normal form on "Report a new bug instead".
+ (void)showWithScreenshot:(nullable NSString*)screenshotPath
                     title:(nullable NSString*)title
               description:(nullable NSString*)description
                    recent:(NSArray<BugpunchRecentReportEntry*>*)recent;

@end

NS_ASSUME_NONNULL_END
