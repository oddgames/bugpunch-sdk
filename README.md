# Bugpunch SDK for Unity

Debugging, remote code execution, and automated UI testing for Unity games. Record user interactions and replay them as tests. Click buttons, fill forms, navigate menus, scroll lists, and perform touch gestures - all through real Input System events.

## Installation

### Option 1: Package Manager UI

1. Open **Window > Package Manager**
2. Click **+** dropdown → **Add package from git URL...**
3. Enter: `https://github.com/oddgames/bugpunch-sdk-unity.git?path=package`
4. Click **Add**

### Option 2: Edit manifest.json

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "au.com.oddgames.bugpunch": "https://github.com/oddgames/bugpunch-sdk-unity.git?path=package"
  }
}
```

### Pinning a Version

To lock to a specific version, append `#v1.1.6` (or any tag):

```
https://github.com/oddgames/bugpunch-sdk-unity.git?path=package#v1.1.6
```

### Private Repository Access

This is a private repository. To install, use a GitHub Personal Access Token:

1. Create a token at **GitHub > Settings > Developer settings > Personal access tokens**
2. Grant `repo` scope (full control of private repositories)
3. Use this URL format:

```
https://<YOUR_TOKEN>@github.com/oddgames/bugpunch-sdk-unity.git?path=package
```

Or in manifest.json:
```json
{
  "dependencies": {
    "au.com.oddgames.bugpunch": "https://<YOUR_TOKEN>@github.com/oddgames/bugpunch-sdk-unity.git?path=package"
  }
}
```

**Note:** Keep your token secure. Don't commit manifest.json with tokens to public repositories.

## Documentation

**[Full Documentation (Wiki)](https://github.com/oddgames/bugpunch-sdk-unity/wiki)** - Complete API reference and guides

**[Package README](package/README.md)** - Quick start and API overview

**[Changelog](package/CHANGELOG.md)** - Version history

## Repository Structure

```
├── package/          # UPM package - import this into your Unity project
│   ├── Bugpunch/     # Framework source code
│   ├── Samples/      # Example tests and demos
│   ├── README.md     # Package documentation
│   └── CHANGELOG.md  # Version history
│
└── test/             # Internal test project (not for distribution)
    └── Assets/
        └── Tests/
            └── PlayMode/  # Framework validation tests
```

- **`package/`** - The Unity Package Manager package. This is what you import.
- **`test/`** - Internal Unity project used to test the framework itself. Contains PlayMode tests that validate the framework works correctly. Not needed for using the package.

## Requirements

- Unity 2022.3+
- Input System Package (New Input System)
- TextMeshPro
- Newtonsoft JSON
