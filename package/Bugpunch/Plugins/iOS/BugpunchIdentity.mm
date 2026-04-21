// BugpunchIdentity.mm — stable, reinstall-surviving device UUID for QA pins.
//
// Stores a UUID in the Keychain so it persists across app uninstall/reinstall
// (Keychain entries survive by default on iOS unless the profile is fully
// reset). Keyed by service + account so the value is app-scoped — no cross-
// app linkage.
//
// Accessed natively before Unity boots (by the +load bootstrap and later by
// the tunnel handshake via C#), so the identity is established early enough
// for the pin config to ride on the first register packet.

#import <Foundation/Foundation.h>
#import <Security/Security.h>

static NSString* const kBugpunchKeychainService = @"au.com.oddgames.bugpunch";
static NSString* const kBugpunchKeychainAccount = @"stable_device_id";

static NSString* BPReadKeychainUUID(void) {
    NSDictionary* query = @{
        (__bridge id)kSecClass:            (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:      kBugpunchKeychainService,
        (__bridge id)kSecAttrAccount:      kBugpunchKeychainAccount,
        (__bridge id)kSecReturnData:       @YES,
        (__bridge id)kSecMatchLimit:       (__bridge id)kSecMatchLimitOne,
    };
    CFTypeRef result = NULL;
    OSStatus s = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (s != errSecSuccess || !result) return nil;
    NSData* data = (__bridge_transfer NSData*)result;
    return [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
}

static void BPWriteKeychainUUID(NSString* uuid) {
    NSData* data = [uuid dataUsingEncoding:NSUTF8StringEncoding];
    NSDictionary* attrs = @{
        (__bridge id)kSecClass:            (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:      kBugpunchKeychainService,
        (__bridge id)kSecAttrAccount:      kBugpunchKeychainAccount,
        (__bridge id)kSecAttrAccessible:   (__bridge id)kSecAttrAccessibleAfterFirstUnlock,
        (__bridge id)kSecValueData:        data,
    };
    // Try add first; if an entry already exists, update instead.
    OSStatus s = SecItemAdd((__bridge CFDictionaryRef)attrs, NULL);
    if (s == errSecDuplicateItem) {
        NSDictionary* query = @{
            (__bridge id)kSecClass:       (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: kBugpunchKeychainService,
            (__bridge id)kSecAttrAccount: kBugpunchKeychainAccount,
        };
        SecItemUpdate((__bridge CFDictionaryRef)query,
            (__bridge CFDictionaryRef)@{ (__bridge id)kSecValueData: data });
    }
}

extern "C" const char* Bugpunch_GetStableDeviceId(void) {
    static char* cached = NULL;
    if (cached) return cached;

    @autoreleasepool {
        NSString* uuid = BPReadKeychainUUID();
        if (!uuid || uuid.length == 0) {
            uuid = [[NSUUID UUID] UUIDString];
            BPWriteKeychainUUID(uuid);
        }
        const char* c = [uuid UTF8String];
        if (!c) return "";
        cached = strdup(c);
    }
    return cached ? cached : "";
}
