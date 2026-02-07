# UIAutomation Package - Claude Context

## Project Structure

- `package/` - UPM package (core framework code, samples)
- `test/` - Unity test project (PlayMode tests, references package locally)

## Real-World Test Project

The primary project used to exercise/test the library in a real game:
- **Path**: `C:\Workspaces\game_monster_truck_destruction_steam\UnityProj_MTD`
- PlayMode tests live in `Assets/Tests/PlayMode/` with a `PlayModeTests.asmdef`
- References the UIAutomation package via Git URL in `Packages/manifest.json`

## Core Philosophy

**Async First** - Prefer `async Task` over synchronous patterns everywhere. The entire API is async-based.

**Real Touch/Input Simulation** - ALL input injection MUST use Unity's new Input System to simulate real touches, clicks, and gestures. NEVER use:
- Direct value manipulation (`dropdown.value`, `slider.value`, `toggle.isOn`)
- IMGUI events (`Event.current`, `ProcessEvent()`)

Use the appropriate helper instead: `ClickDropdown()`, `ClickSlider()`, `Click()`, etc.

**Exception**: `TypeIntoField()` uses direct text manipulation because TMP_InputField doesn't support Input System text events (known Unity limitation).

**Search is Central** - The `Search` class (fluent query builder) is the backbone of the framework. All element finding flows through it. Invest in keeping its API clean, expressive, and well-tested.

**No External Dependencies** - The package uses only standard .NET and Unity APIs:
- Use `System.Threading.Tasks` (async/await) - NOT UniTask
- Use `System.Text.Json` or manual JSON - NOT Newtonsoft.Json
- Use `IAsyncDisposable` pattern for action lifecycle management

## Key Commands

- `/deploy` - Version bump, changelog, commit, tag, and push. **Do NOT commit/push unless explicitly asked via `/deploy`**

## Testing

**PlayMode Tests** (`test/Assets/Tests/PlayMode/`) - Primary testing approach. NUnit tests with `[Test]` attribute and `async Task` methods.

**Manual Test Execution** - The user runs tests manually in Unity Editor. Do NOT attempt to run Unity tests from the command line.

**Samples** (`package/UITest/Samples/`) - Demonstrations only, not for framework testing.

## Conditional Compilation

Auto-detected via `versionDefines`:
- `UNITY_RECORDER` - when `com.unity.recorder` installed
- `HAS_TOOLBAR_EXTENDER` - when `com.marijnzwemmer.unity-toolbar-extender` installed

## GitHub Wiki

Separate repo at `https://github.com/oddgames/ui-automation.wiki.git`. Clone to `wiki_temp/` to edit.

### Wiki Update Process (Pre-Deploy Step)

The wiki sidebar and homepage don't auto-update when new pages are added. Before `/deploy`:

1. **Update existing wiki pages** - Document any new methods/features in the appropriate pages
2. **Update `_Sidebar.md`** - Add links to any new wiki pages
3. **Update `Home.md`** - Add new pages to the appropriate documentation section
4. **Push wiki changes** - `cd wiki_temp && git add -A && git commit -m "message" && git push`

Key wiki files to update:
- `wiki_temp/_Sidebar.md` - Navigation sidebar (appears on all pages)
- `wiki_temp/Home.md` - Main documentation landing page
- `wiki_temp/Reflection-Access.md` - Value properties, GetValue<T>, Invoke(), Property()
- `wiki_temp/Search.Spatial.md` - Spatial filters (InRegion, Visible) and positioning helpers
- `wiki_temp/Test-Actions.md` - Overview of all test action categories

## Local Changelog (Uncommitted)

Track changes methodically as you work. Used to generate CHANGELOG.md during `/deploy`.

### Rules for Local Changes

1. **Add entries as you complete work** - After implementing a feature/fix, add a bullet point immediately
2. **Be specific** - Include method names, class names, and what changed
3. **Categorize entries**:
   - `**BREAKING**:` - API changes requiring user code updates
   - `**Added**:` - New features/methods
   - `**Changed**:` - Modifications to existing behavior
   - `**Fixed**:` - Bug fixes
   - `**Removed**:` - Deleted features/methods

4. **Handle iterative development** - When going back and forth between implementations:
   - Remove entries for approaches that were abandoned
   - Keep only the final implemented approach
   - If reverting a change entirely, remove its entry
   - Don't track intermediate experiments that didn't ship

5. **Resolve conflicts** - If a new task contradicts a previous local change:
   - Remove the conflicting entry
   - Add the new approach instead
   - Only track what will actually be deployed

6. **Clear after deploy** - Replace all entries with `(None - cleared after vX.Y.Z deploy)` after successful `/deploy`

### Current Local Changes

(None - cleared after v1.1.40 deploy)
