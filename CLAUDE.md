# UITest Package - Claude Context

## Repository Structure

```
ui-automation/
├── package/                  # The UPM package
│   ├── package.json          # UPM package manifest
│   ├── UITest/               # Core framework code
│   │   ├── ODDGames.UITest.asmdef
│   │   ├── UITestAttribute.cs
│   │   ├── UITestBehaviour.cs
│   │   ├── Editor/
│   │   ├── Recording/
│   │   └── EzGUI/            # HAS_EZ_GUI only
│   ├── README.md
│   ├── CHANGELOG.md
│   └── LICENSE
├── test/                     # Test Unity project
│   ├── Assets/
│   │   └── Plugins/EzGUI/    # EzGUI stubs for testing
│   ├── Packages/
│   └── ProjectSettings/
├── .gitignore
└── CLAUDE.md                 # This file
```

## Package Installation

Projects reference via git with path:
```json
"com.oddgames.uitest": "https://github.com/oddgames/ui-automation.git?path=package"
```

Test project references locally:
```json
"com.oddgames.uitest": "file:../../package"
```

## Conditional Compilation

### versionDefines (auto-detected from packages)
- `UNITY_RECORDER` - Defined when `com.unity.recorder` is installed
- `HAS_TOOLBAR_EXTENDER` - Defined when `com.marijnzwemmer.unity-toolbar-extender` is installed

### Project Defines (must be added manually)
- `HAS_EZ_GUI` - Add to MTD project for AnB UI SDK (EZ GUI) support

## Adding New Conditional Features

1. Create a new folder under `package/UITest/` (e.g., `package/UITest/NewFeature/`)
2. Create an asmdef with:
   - `defineConstraints` for manual defines (e.g., `["HAS_NEW_FEATURE"]`)
   - `versionDefines` for package-based defines
3. Reference `ODDGames.UITest` in the asmdef
4. Update this CLAUDE.md

## Event Hooking

Two approaches for recording UI events:

### 1. Automatic (UITestInputInterceptor)
- Uses new Input System (`Mouse.current`, `Keyboard.current`, `EnhancedTouchSupport`)
- Works with any input module
- Auto-spawned when recording starts
- May duplicate events if game also reports them

### 2. Manual (UITestInputEvents)
- Games call `UITestInputEvents.ReportClick(pointerEvent)` from their input module
- More precise, no duplicates
- Requires game code changes

**Current Setup**: Uses `UITestInputInterceptor` for automatic event capture with Input System.

## Input System

The package requires Unity's new Input System (`com.unity.inputsystem`):
- Test playback uses `InputSystem.QueueEvent()` for true input injection
- Recording uses `Mouse.current`, `Keyboard.current`, `EnhancedTouchSupport`
- Projects should have `activeInputHandler` set to `3` (Both) or `1` (New only)

## Deploy Command

Run `/deploy` to:
1. Version bump and changelog update
2. Commit and push pending changes
3. Get latest commit hash (for updating project manifests manually)

## Test Project

The `test/` folder contains a Unity project for development testing:
- Has Input System set to "Both" mode
- References the package via `file:../../package`
- Includes EzGUI stubs in `Assets/Plugins/EzGUI/` for testing EzGUI integration
