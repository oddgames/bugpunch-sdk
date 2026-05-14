# xdelta3 binaries

Drop the platform-specific `xdelta3` executable into the matching folder:

- `windows/xdelta3.exe`
- `macos/arm64/xdelta3` (Apple Silicon)
- `macos/x86_64/xdelta3` (Intel)
- `linux/xdelta3`

These are invoked by the Bugpunch Editor's symbol-upload hook to produce a
binary patch of the new `libil2cpp.so` against a prior version already on
the symbol server. The patch is typically 1–10 MB instead of 100–500 MB,
so the upload finishes in seconds instead of minutes.

The folder name `Tools~` (with the trailing tilde) is a Unity convention
that hides the contents from the AssetDatabase, so the binaries aren't
imported as Unity assets and aren't bundled into shipped game builds.
They live only on the developer machine inside the imported package.

## Where to get xdelta3

- **Windows**: https://github.com/jmacd/xdelta-gpl/releases
  – grab `xdelta3-3.1.0-x86_64.exe`, rename to `xdelta3.exe`.
- **macOS**: `brew install xdelta` — produces `/opt/homebrew/bin/xdelta3`
  (Apple Silicon) or `/usr/local/bin/xdelta3` (Intel). Copy to `macos/`.
  Make sure it's executable (`chmod +x macos/xdelta3`).
- **Linux**: `apt install xdelta3`. Binary lives at `/usr/bin/xdelta3`.

xdelta3 is GPL-2.0 licensed. We invoke it as a spawned process (not via
linking), and we only ship it as a separate editor-side binary that
never gets bundled into a customer's game build — so GPL's viral
linking clause doesn't apply.

## Fallback behaviour

If the binary is missing for the current host OS, the symbol upload
flow falls back to the regular full-file upload path. Symbol uploads
still work, just without the delta speedup.
