# C# SDK source

Source for the Bugpunch Unity SDK's runtime DLL. Mirrors the role
`android-src/` plays for the Android AAR — the SDK ships a pre-built
`ODDGames.Bugpunch.dll` at `package/Plugins/`; consumers never see this
source tree.

## Layout

- `Bugpunch/Bugpunch.csproj` — netstandard2.1 project, target Unity 6.0
- `Bugpunch/Sources/` — every runtime `.cs` file (≈108 files)
- `Bugpunch/refs/` — vendored Unity + package DLLs the project references via `<HintPath>`. Committed because the repo is private and the DLLs are tiny (~13MB total)
- `Bugpunch/build/` — local build output (gitignored)
- `Bugpunch/obj/` — MSBuild intermediates (gitignored)

Editor extensions, samples, tests, and the Purchasing shim still ship as
source under `package/` — only the runtime is binary.

## Build

```bash
cd sdk/csharp-src/Bugpunch
dotnet build -c Release
cp build/ODDGames.Bugpunch.dll ../../package/Plugins/ODDGames.Bugpunch.dll
```

`/deploy-sdk` does this automatically when `csharp-src/` has changes.

## Refreshing reference DLLs

When Unity bumps versions, or a Unity package (Input System / TextMeshPro
/ WebRTC / Newtonsoft) updates, the references in `refs/` may need to
match. Pull from a fresh Unity install + project compile:

```bash
UNITY=/c/Program\ Files/Unity/Hub/Editor/<version>/Editor/Data/Managed
ARTIFACTS=/c/Workspaces/odddev/sdk/test/Library/Bee/artifacts/<id>.dag
cp "$UNITY/UnityEngine/"UnityEngine*.dll refs/
cp "$ARTIFACTS"/Unity.{InputSystem,TextMeshPro,WebRTC}.dll refs/
cp "$ARTIFACTS"/UnityEngine.UI.dll refs/
cp /c/Workspaces/odddev/sdk/test/Library/PackageCache/com.unity.nuget.newtonsoft-json@*/Runtime/AOT/Newtonsoft.Json.dll refs/
cp /c/Workspaces/odddev/sdk/test/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll refs/
cp /c/Workspaces/odddev/scripting/Runtime/bin/Release/netstandard2.1/ODDGames.Scripting.dll refs/
```

Then rebuild and verify in Unity (clibridge → COMPILE).

## Why no CI workflow?

Building the DLL requires the vendored Unity reference assemblies. Doing
this on GitHub Actions would either need:
- Unity install + license activation on the runner (expensive macOS-style
  minutes for what's effectively a 30-second `dotnet build`)
- Or a snapshot of the refs/ tree shipped to the runner (this is what's
  in the repo already, but committing the build output back from CI
  duplicates `/deploy-sdk`'s local build)

`/deploy-sdk` builds the DLL locally and commits it alongside the version
bump — same model as the Android AAR. Faster failure surface (you find
out within 2 seconds, not after a 5-minute CI round-trip), no minutes
burnt, no license dance.

The iOS xcframework is the exception: Windows can't build it, so that
one stays on a macOS GitHub Actions runner.
