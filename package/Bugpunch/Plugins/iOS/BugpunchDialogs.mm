#import <UIKit/UIKit.h>

typedef void (*PermissionCallback)(int result);
typedef void (*BugReportSubmitCallback)(const char* title, const char* description, const char* severity);
typedef void (*BugReportCancelCallback)(void);

extern "C" {

void Bugpunch_ShowPermissionDialog(const char* title, const char* message, PermissionCallback callback) {
    NSString* nsTitle = [NSString stringWithUTF8String:title];
    NSString* nsMessage = [NSString stringWithUTF8String:message];

    dispatch_async(dispatch_get_main_queue(), ^{
        UIAlertController* alert = [UIAlertController alertControllerWithTitle:nsTitle
                                                                      message:nsMessage
                                                               preferredStyle:UIAlertControllerStyleAlert];

        [alert addAction:[UIAlertAction actionWithTitle:@"Allow Once" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            callback(0); // AllowOnce
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Always Allow" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            callback(1); // AllowAlways
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Deny" style:UIAlertActionStyleCancel handler:^(UIAlertAction* action) {
            callback(2); // Deny
        }]];

        UIViewController* rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) rootVC = rootVC.presentedViewController;
        [rootVC presentViewController:alert animated:YES completion:nil];
    });
}

void Bugpunch_ShowBugReportDialog(BugReportSubmitCallback onSubmit, BugReportCancelCallback onCancel) {
    dispatch_async(dispatch_get_main_queue(), ^{
        UIAlertController* alert = [UIAlertController alertControllerWithTitle:@"Bug Report"
                                                                      message:@"Describe the issue:"
                                                               preferredStyle:UIAlertControllerStyleAlert];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Title";
        }];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Description (optional)";
        }];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Severity: low / medium / high / critical";
            field.text = @"medium";
        }];

        [alert addAction:[UIAlertAction actionWithTitle:@"Submit" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            NSString* title = alert.textFields[0].text;
            NSString* desc = alert.textFields[1].text;
            NSString* sev = alert.textFields[2].text;
            onSubmit([title UTF8String], [desc UTF8String], [sev UTF8String]);
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:^(UIAlertAction* action) {
            onCancel();
        }]];

        UIViewController* rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) rootVC = rootVC.presentedViewController;
        [rootVC presentViewController:alert animated:YES completion:nil];
    });
}

}
