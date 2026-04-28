// BugpunchBootstrap.mm — iOS early-boot hook for the Bugpunch SDK.
//
// The +load method below runs at image-load time, before main() fires. We
// use it to install crash handlers / log capture / upload-queue drain before
// Unity's mono runtime has booted, so crashes during app startup or first-
// scene load get captured (today they don't — native waits for C# to hand
// it the config after Unity boots).
//
// Reads bugpunch_config.json from the main bundle (placed there at Unity
// build time by BugpunchConfigBundle). If the file isn't present the host
// app wasn't built with the bundle post-processor and we quietly do
// nothing — the legacy C# path still drives init via Bugpunch_StartDebugMode
// once Unity is ready.
//
// When C# later calls Bugpunch_StartDebugMode with the richer Unity-runtime
// config (Application.version, deviceId, resolved attachment paths), the
// function detects an already-started state, skips one-time init, and
// refreshes metadata + crash handler values.

#import <Foundation/Foundation.h>

extern "C" bool Bugpunch_StartDebugMode(const char* configJson);
extern "C" void Bugpunch_StartTunnel(const char* configJson);

@interface BugpunchBootstrap : NSObject @end

@implementation BugpunchBootstrap

+ (void)load {
    @autoreleasepool {
        NSString* path = [[NSBundle mainBundle] pathForResource:@"bugpunch_config"
                                                         ofType:@"json"];
        if (!path) {
            // No bundled config — quietly fall back to the legacy C# path.
            return;
        }

        NSError* err = nil;
        NSString* json = [NSString stringWithContentsOfFile:path
                                                   encoding:NSUTF8StringEncoding
                                                      error:&err];
        if (!json || json.length == 0 || err) {
            NSLog(@"[Bugpunch] bootstrap: failed to read bugpunch_config.json: %@", err);
            return;
        }

        @try {
            const char* cJson = [json UTF8String];
            Bugpunch_StartDebugMode(cJson);
            // Native WebSocket tunnel (N2) — up before Unity boots. Captures
            // the signed pin config from the registered ack; survives a
            // managed crash because nothing on this path touches Mono.
            Bugpunch_StartTunnel(cJson);
            NSLog(@"[Bugpunch] early bootstrap complete");
        } @catch (NSException* e) {
            NSLog(@"[Bugpunch] bootstrap exception: %@", e);
        }
    }
}

@end
