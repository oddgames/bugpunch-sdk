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
│   │   └── Samples/
│   ├── README.md
│   ├── CHANGELOG.md
│   └── LICENSE
├── test/                     # Test Unity project
│   ├── Assets/
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
- Projects must have `activeInputHandler` set to `2` (New only)

**IMPORTANT**: Always use realistic input injection via the helpers. Never bypass the UI by setting values directly (e.g., `dropdown.value = 1` or `slider.value = 0.5f`). This ensures tests exercise the actual UI interaction path.

## Advanced Control Helpers

For complex UI controls that need precise positioning, use the specialized helpers instead of generic Click/Drag:

### Sliders
Sliders use percentage-based positioning (0-1) on the visible area:

```csharp
// Click at 75% position on a horizontal slider
await ClickSlider("VolumeSlider", 0.75f);

// Drag from 25% to 75% position
await DragSlider("VolumeSlider", 0.25f, 0.75f);
```

These helpers:
- Find the Slider component by name/pattern
- Calculate screen position based on the slider's RectTransform bounds
- Handle horizontal (LeftToRight, RightToLeft) and vertical (BottomToTop, TopToBottom) directions
- Use standard Click/Drag input injection under the hood

### Dropdowns
Use the ClickDropdown helper to select options via realistic clicks:

```csharp
// Select by index (0-based)
await ClickDropdown("CategoryDropdown", 1);

// Select by label text
await ClickDropdown("CategoryDropdown", "Option 2");
```

This clicks the dropdown to open it, then clicks the target option in the dynamically created list.

### Drag and Drop
Use the drag helpers for element-to-element or position-based dragging:

```csharp
// Drag one element to another by name
await DragTo("DraggableItem", "DropZone", duration: 0.5f);

// Drag from element in a direction
await Drag("ScrollView", new Vector2(0, -200), duration: 0.5f);

// Drag from specific screen positions
await DragFromTo(new Vector2(100, 200), new Vector2(300, 200), duration: 0.5f);
```

## Deploy Command

Run `/deploy` to:
1. Version bump and changelog update
2. Commit and push pending changes
3. Get latest commit hash (for updating project manifests manually)

## Test Project

The `test/` folder contains a Unity project for development testing:
- Has Input System set to "New" mode only
- References the package via `file:../../package`
