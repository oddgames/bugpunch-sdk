// BugpunchIssueDetailViewController.h — player-facing "issue affecting you"
// detail screen. Opened by tapping an issue card in the chat window. Shows a
// basic description + the team's PUBLIC comments and lets the player post a
// public reply. All data comes from the player-safe SDK routes
// (/api/v1/issues/:type/:id [+ /comment]); internal comments never reach here.
//
// Mirror of the Android BugpunchIssueDetailDialog and the C#
// BugpunchIssueDetailBoard.

#import <UIKit/UIKit.h>

@interface BugpunchIssueDetailViewController : UIViewController

- (instancetype)initWithType:(NSString*)issueType
                     issueId:(NSString*)issueId
                       title:(NSString*)title;

/** Invoked after the screen is dismissed so the chat can refresh its unread
 *  cards (opening the detail marks the issue read server-side). */
@property (nonatomic, copy) void (^onClosed)(void);

/** Local files to auto-stage into the reply composer on open (staged + uploaded
 *  exactly as a 📎-picked file). Set before presenting. Used by
 *  BugpunchReportChooser to carry the fresh capture as a new occurrence when a
 *  tester comments on a recent report instead of filing a duplicate. */
@property (nonatomic, copy) NSArray<NSString*>* preStagedPaths;

@end
