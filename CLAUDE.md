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

**CRITICAL**: Always use realistic input injection via the helpers. NEVER bypass the UI by setting values directly. This is the core philosophy of this testing framework - tests must exercise the actual UI interaction path exactly as a user would.

Forbidden patterns (do NOT use these):
- `dropdown.value = 1` - instead use `ClickDropdown()`
- `slider.value = 0.5f` - instead use `ClickSlider()` or `DragSlider()`
- `inputField.text = "..."` - instead use `Type()`
- `toggle.isOn = true` - instead use `Click()`
- `scrollRect.normalizedPosition = ...` - instead use drag gestures via `InjectMouseDrag()`
- Any direct component value manipulation

The framework exists specifically to simulate real user input through the Input System. Direct manipulation defeats the purpose of UI testing.

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
3. Create and push git tag (e.g., `v1.0.23`)

Projects can reference specific versions via git tag:
```json
"com.oddgames.uitest": "https://github.com/nickhudson4/tool_ui_automation.git?path=package#v1.0.23"
```

**IMPORTANT**: Do NOT commit or push changes unless explicitly asked via `/deploy`. All code changes should remain local until the user requests deployment.

## Samples and Tests

**Samples** (`package/UITest/Samples/`):
- Runtime UI tests that extend `UITestBehaviour`
- Run by attaching to a GameObject in a scene
- Used for demonstrating framework capabilities
- Examples: `ComprehensiveSampleTest.cs`, `SearchMethodTests.cs`

**PlayMode Tests** (`test/Assets/Tests/PlayMode/`):
- NUnit PlayMode tests that run in Unity Test Runner
- Use `[UnityTest]` with `IEnumerator` and `UniTask.ToCoroutine()`
- Located in the test Unity project, NOT in the package
- Test async UITestBehaviour methods, input injection, Search API

## Test Project

The `test/` folder contains a Unity project for development testing:
- Has Input System set to "New" mode only
- References the package via `file:../../package`

## GitHub Wiki

The wiki is a separate git repository. To update it:

```bash
# Clone wiki (already exists at wiki_temp/ if previously cloned)
git clone https://github.com/oddgames/ui-automation.wiki.git wiki_temp

# Edit files in wiki_temp/
# Commit and push changes
cd wiki_temp && git add -A && git commit -m "Update wiki" && git push
```

### Wiki Structure
- `Home.md` - Landing page with quick start
- `_Sidebar.md` - Navigation sidebar
- `Installation.md`, `Creating-Your-First-Test.md`, `Test-Attributes.md` - Getting started
- `Search-Queries.md`, `Search.ByName.md`, `Search.ByText.md`, etc. - Search API docs
- `Click-Actions.md`, `Text-Input.md`, `Drag-Actions.md`, `Gesture-Input.md` - Action docs
- `Availability-Filtering.md` - IncludeInactive/IncludeDisabled
- `Best-Practices.md`, `Samples.md`, `Test-Recording.md` - Advanced topics

### Wiki Update Checklist
When updating the wiki, check these pages for API changes:
- [ ] `Home.md` - Version number, quick start example
- [ ] `Search.ByAdjacent.md` - Now `Adjacent()` with `Direction` enum (not `ByAdjacent` with `Adjacent` enum)
- [ ] `Availability-Filtering.md` - Uses `IncludeInactive()`, `IncludeDisabled()`
- [ ] `Search-Chaining.md` - Check for new methods like `Near()`, `HasSibling()`, `GetParent()`, etc.

## Local Changelog (Uncommitted)

**IMPORTANT**: This section tracks all local changes since the last deploy. Update this section as you work:
- Add entries when making API changes, fixes, or new features
- Remove entries if you undo/revert a change
- This section is used to generate the CHANGELOG.md entry during `/deploy`
- After `/deploy`, clear this section and write "(No uncommitted changes)"

### Current Local Changes

(No uncommitted changes)

## Change History

Reference of recent API changes. See [CHANGELOG.md](package/CHANGELOG.md) for complete version history.

### v1.0.30 - 2026-01-13
- **Extracted Search class** to separate `Search.cs` file
- **Fixed two-finger gesture reliability** - added extra frame yields after ending touches

### v1.0.29 - 2026-01-12
- **Added `Search.Near()`** - distance-based proximity search
- **Added `Search.HasSibling()`** - filter by sibling matching criteria
- **Renamed `ByAdjacent` to `Adjacent`** - removed "By" prefix
- **Renamed `Adjacent` enum to `Direction`** - avoids method name conflict
- **Renamed transform methods** - `Parent()` → `GetParent()`, `Child()` → `GetChild()`, `Sibling()` → `GetSibling()`

### v1.0.28 - 2026-01-12
- **Visual Test Builder** - drag-and-drop block-based test creation
- **AI Test Generation** - Gemini-powered test generation
- **InputInjector public class** - extracted for reuse
- **Keys fluent builder** - `Keys.Hold(Key.W).For(2f)`

### v1.0.27 - 2026-01-10
- **Random Click methods** - `RandomClick()`, `RandomClickExcept()`, `SetRandomSeed()`
- **AutoExplorer** - static API, runtime component, CI batch mode
- **Smart exploration** - exclusion patterns, action variety, priority scoring

### v1.0.26 - 2026-01-09
- **Component overloads** - `Click(Component)`, `Hold(Component)`, etc.
- **FindItems** - iterate over scroll view/layout group items

### v1.0.25 - 2026-01-09
- **Search ordering** - `First()`, `Last()`, `Skip()`, `OrderBy()`, `OrderByPosition()`
- **Search hierarchy** - `GetParent<T>()`, `GetChild<T>()`, `InRegion()`
- **ScrollTo** - auto-scroll until target visible

### v1.0.23 - 2026-01-08
- **Removed `Availability` enum** - replaced with `IncludeInactive()`, `IncludeDisabled()`
- **Added `Search.Adjacent()`** - find interactables by adjacent text labels
