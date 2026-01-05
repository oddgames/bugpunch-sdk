# UITest Package - Claude Context

## Package Structure

```
tool_ui_automation/
├── package.json              # UPM package manifest
├── UITest/
│   ├── ODDGames.UITest.asmdef     # Core runtime (always compiled)
│   ├── UITestAttribute.cs          # [UITest] attribute
│   ├── UITestBehaviour.cs          # Base test class
│   │
│   ├── Editor/
│   │   ├── ODDGames.UITest.Editor.asmdef  # Editor tools
│   │   └── UITestRunner.cs                 # Batch test runner
│   │
│   ├── Recording/
│   │   ├── ODDGames.UITest.Recording.asmdef      # Recording runtime
│   │   ├── UITestRecorder.cs                      # Main recorder
│   │   ├── UITestRecordingData.cs                 # Data structures
│   │   ├── UITestInputInterceptor.cs              # Auto-hooks into Input
│   │   ├── UITestInputEvents.cs                   # Static helpers for manual reporting
│   │   ├── UITestPromptGenerator.cs               # AI prompt generation
│   │   └── Editor/
│   │       ├── ODDGames.UITest.Recording.Editor.asmdef
│   │       ├── UITestGeneratorWindow.cs
│   │       ├── UITestRecorderToolbar.cs           # HAS_TOOLBAR_EXTENDER only
│   │       └── UITestRecordingSetupWindow.cs
│   │
│   └── EzGUI/                                     # HAS_EZ_GUI only
│       ├── ODDGames.UITest.EzGUI.asmdef
│       └── EzGUIClickableRegistration.cs          # Auto-registers AnB UI SDK types
```

## Conditional Compilation

### versionDefines (auto-detected from packages)
- `UNITY_RECORDER` - Defined when `com.unity.recorder` is installed
- `HAS_TOOLBAR_EXTENDER` - Defined when `com.marijnzwemmer.unity-toolbar-extender` is installed

### Project Defines (must be added manually)
- `HAS_EZ_GUI` - Add to MTD project for AnB UI SDK (EZ GUI) support

## Adding New Conditional Features

1. Create a new folder under `UITest/` (e.g., `UITest/NewFeature/`)
2. Create an asmdef with:
   - `defineConstraints` for manual defines (e.g., `["HAS_NEW_FEATURE"]`)
   - `versionDefines` for package-based defines
3. Reference `ODDGames.UITest` in the asmdef
4. Update this CLAUDE.md

## Event Hooking

Two approaches for recording UI events:

### 1. Automatic (UITestInputInterceptor)
- Hooks into `Input.GetMouseButton/Touch` directly
- Works with any input module
- Auto-spawned when recording starts
- May duplicate events if game also reports them

### 2. Manual (UITestInputEvents)
- Games call `UITestInputEvents.ReportClick(pointerEvent)` from their input module
- More precise, no duplicates
- Requires game code changes

**Current Setup**: Uses `UITestInputInterceptor` for automatic event capture.

## Deploy Command

Run `/deploy` to:
1. Version bump and changelog update
2. Commit and push pending changes
3. Get latest commit hash (for updating project manifests manually)
