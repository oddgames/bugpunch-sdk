#!/usr/bin/env bash
# Build sdk/package/Plugins/iOS/Bugpunch.xcframework from sources in
# sdk/ios-src/Bugpunch/Sources/. Mirrors the role android-src/'s gradle
# build plays for BugpunchPlugin.aar — Unity consumers never need Xcode
# locally; CI rebuilds the binary and commits it back to the repo.
#
# Output: a static-library .xcframework with two slices —
#   - ios-arm64           (real device)
#   - ios-arm64_x86_64-simulator (Apple Silicon + Intel Mac sim)
#
# Unity's iOS player resolves UnitySendMessage / UnityFramework symbols
# at consumer link time, so we leave them as undefined externs here.
#
# Usage:
#   ./build-xcframework.sh
# (no args; macOS + Xcode required)
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
SRC="$DIR/Sources"
OUT="$DIR/../../package/Plugins/iOS/Bugpunch.xcframework"
BUILD="$DIR/build"
DEPLOY_TARGET="13.0"

# Common compile flags. Mirrors what Unity sets for its iOS player target —
# ARC on, Obj-C++ mode (.mm files force this anyway), modules enabled so
# `@import Foo` works. -fembed-bitcode is dropped since Apple deprecated
# bitcode in Xcode 14.
CFLAGS_COMMON=(
  -mios-version-min="$DEPLOY_TARGET"
  -fobjc-arc
  -fmodules
  -std=gnu++17
  -x objective-c++
  -O2
  -fvisibility=hidden
  -I "$SRC"
)

# Compile a single architecture into a static archive.
#   $1 — slice label (e.g. "ios-arm64")
#   $2 — clang sysroot SDK name (e.g. "iphoneos")
#   $3 — clang -arch value (e.g. "arm64")
#   $4 — extra flags for simulator (e.g. "-mios-simulator-version-min=13.0")
build_slice() {
  local slice="$1" sdk="$2" arch="$3"
  shift 3
  local extra=("$@")

  local out_dir="$BUILD/$slice"
  rm -rf "$out_dir"
  mkdir -p "$out_dir/obj"

  local sysroot
  sysroot="$(xcrun --sdk "$sdk" --show-sdk-path)"

  echo "── Compiling $slice ($arch / $sdk) ──"
  for src in "$SRC"/*.mm; do
    local base
    base="$(basename "$src" .mm)"
    xcrun --sdk "$sdk" clang \
      -c "${CFLAGS_COMMON[@]}" \
      -arch "$arch" \
      -isysroot "$sysroot" \
      "${extra[@]}" \
      "$src" \
      -o "$out_dir/obj/$base.o"
  done

  echo "── Archiving libBugpunch.a ($slice) ──"
  xcrun --sdk "$sdk" libtool -static -o "$out_dir/libBugpunch.a" "$out_dir/obj/"*.o
}

# ── Slices ───────────────────────────────────────────────────────────
# ios-arm64                      → real device (iPhone / iPad)
# ios-arm64_x86_64-simulator     → arm64 Mac sim + Intel Mac sim, fat archive
build_slice "ios-arm64"           "iphoneos"        "arm64"
build_slice "ios-arm64-simulator" "iphonesimulator" "arm64"  -mios-simulator-version-min="$DEPLOY_TARGET"
build_slice "ios-x86_64-simulator" "iphonesimulator" "x86_64" -mios-simulator-version-min="$DEPLOY_TARGET"

# Fat the two simulator archs into a single archive — xcframework requires
# one binary per slice, and Apple Silicon + Intel Mac sims share a slice.
echo "── Lipo-ing simulator archs ──"
mkdir -p "$BUILD/ios-arm64_x86_64-simulator"
xcrun lipo -create \
  "$BUILD/ios-arm64-simulator/libBugpunch.a" \
  "$BUILD/ios-x86_64-simulator/libBugpunch.a" \
  -output "$BUILD/ios-arm64_x86_64-simulator/libBugpunch.a"

# Public headers — anything declared in a .h. xcframework copies them
# alongside each slice. Unity doesn't import them itself but the
# xcframework spec requires headers when the binary references them.
HDRS_DIR="$BUILD/headers"
rm -rf "$HDRS_DIR"
mkdir -p "$HDRS_DIR"
cp "$SRC"/*.h "$HDRS_DIR/" 2>/dev/null || true

echo "── Creating xcframework ──"
rm -rf "$OUT"
xcrun xcodebuild -create-xcframework \
  -library "$BUILD/ios-arm64/libBugpunch.a"                   -headers "$HDRS_DIR" \
  -library "$BUILD/ios-arm64_x86_64-simulator/libBugpunch.a"  -headers "$HDRS_DIR" \
  -output "$OUT"

echo ""
echo "✓ Built $OUT"
ls -la "$OUT"
