# UITest Package - Claude Context

## Project Structure

- `package/` - UPM package (core framework code, samples)
- `test/` - Unity test project (PlayMode tests, references package locally)

## Core Philosophy

**Input System Only** - ALL input injection MUST use Unity's new Input System. NEVER use:
- Direct value manipulation (`dropdown.value`, `slider.value`, `toggle.isOn`)
- IMGUI events (`Event.current`, `ProcessEvent()`)

Use the appropriate helper instead: `ClickDropdown()`, `ClickSlider()`, `Click()`, etc.

**Exception**: `TypeIntoField()` uses direct text manipulation because TMP_InputField doesn't support Input System text events (known Unity limitation).

## Key Commands

- `/deploy` - Version bump, changelog, commit, tag, and push. **Do NOT commit/push unless explicitly asked via `/deploy`**

## Testing

**PlayMode Tests** (`test/Assets/Tests/PlayMode/`) - Primary testing approach. NUnit tests with `[UnityTest]`, `IEnumerator`, and `UniTask.ToCoroutine()`.

**Samples** (`package/UITest/Samples/`) - Demonstrations only, not for framework testing.

## Conditional Compilation

Auto-detected via `versionDefines`:
- `UNITY_RECORDER` - when `com.unity.recorder` installed
- `HAS_TOOLBAR_EXTENDER` - when `com.marijnzwemmer.unity-toolbar-extender` installed

## GitHub Wiki

Separate repo at `https://github.com/oddgames/ui-automation.wiki.git`. Clone to `wiki_temp/` to edit.

## Local Changelog (Uncommitted)

Update this section as you work. Used to generate CHANGELOG.md during `/deploy`. Clear after deploy.

### Current Local Changes

(none)
