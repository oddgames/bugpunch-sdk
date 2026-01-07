# Changelog

All notable changes to this project will be documented in this file.

## [1.0.20] - 2026-01-07

### Fixed
- Menu item "Create Test Recorder" moved to `Window/Analysis/UI Automation/` for consistency with other menu items

## [1.0.19] - 2026-01-07

### Fixed
- TouchPhase injection now uses enum values directly (fixed `ArgumentException: Expecting control of type 'UInt16'`)
- Gesture methods (`Pinch`, `Rotate`, `TwoFingerSwipe`) now use `Find<Transform>` to work with both UI and 3D objects
- `PanelSwitcher` now finds inactive 3D objects using `Resources.FindObjectsOfTypeAll<Transform>()`
- `DoubleClick` signature simplified with consistent parameter ordering

### Changed
- Standardized debug logging across all gesture methods (removed verbose internal logs)
- `RotateAt` refactored to remove redundant calculations
- Sample `GestureCube` added for 3D gesture demonstrations
- Sample `GestureTargetUI` added for UI gesture demonstrations

## [1.0.18] - 2026-01-07

### Fixed
- `Find<RectTransform>` and other non-Behaviour component types now work correctly
- Drag and drop operations can now find elements properly
- `ClickDropdown` now supports both legacy `Dropdown` and `TMP_Dropdown` components
- `ClickDropdown` uses more robust item finding (handles different Unity naming patterns)

### Added
- `DragTo(source, target)` - Drag one element to another for drag-and-drop testing
- `ClickDropdown(name, index)` - Select dropdown option by index using realistic clicks
- `ClickDropdown(name, label)` - Select dropdown option by label text using realistic clicks
- `ClickSlider(name, percent)` - Click slider at percentage position (0-1)
- `DragSlider(name, fromPercent, toPercent)` - Drag slider between positions
- `DoubleClick(name)` - Double-click an element
- `Scroll(name, delta)` - Scroll wheel input on an element
- `Swipe(name, direction)` - Swipe gesture helper (Left, Right, Up, Down)
- `Pinch(name, scale)` - Two-finger pinch gesture (scale < 1 = pinch in, scale > 1 = pinch out)
- `TwoFingerSwipe(name, direction)` - Two-finger swipe gesture
- `Rotate(name, degrees)` - Two-finger rotation gesture
- `DraggableUI` and `DropZoneUI` sample components for drag-and-drop demos
- `PanelSwitcher` component for sample scene navigation

### Changed
- Simplified `ComprehensiveSampleTest` for faster execution
- Removed EzGUI support (moved to separate package)
- Removed verbose iteration logging from Find method
- Gesture methods (`Swipe`, `Pinch`, `TwoFingerSwipe`, `Rotate`) now use screen-relative percentages instead of fixed pixels for device independence
- Added `fingerDistance` and `fingerSpacing` parameters to gesture methods for customization

## [1.0.17] - 2025-01-07

### Fixed
- `TextInput` now works with TMP_InputField by using direct text property injection (character-by-character)
- Sample scenes use TMP_InputField instead of legacy InputField for proper Input System compatibility

## [1.0.16] - 2025-01-07

### Breaking Changes
- **Removed EZ GUI support**: The `HAS_EZ_GUI` conditional compilation and all EZ GUI/AnB UI SDK integrations have been removed

### Changed
- `TextInput` now uses Input System injection (click to focus, type characters, optional Enter)
- Reduced debug logging verbosity (removed per-frame Find/injection logs)
- Simplified test runner context menus (removed popup dialog)

### Added
- `KeyPressTest` sample for keyboard input testing
- Context menu options: Run Test, Run Test (Clear Data), Run Test with Data Folder/Zip

## [1.0.15] - 2025-01-07

### Breaking Changes
- **Requires Unity Input System**: Projects must now use the Input System Package (New) - legacy Input Manager is no longer supported

### Changed
- Restructured repository: package content now in `package/` subfolder for proper UPM git URL references
- Click/Hold/Drag now use Unity Input System injection for reliable UI event handling
- Sample scenes now use `InputSystemUIInputModule` instead of `StandaloneInputModule`
- Replaced Odin Inspector dependency with `[ContextMenu]` attribute

### Added
- Test Unity project in `test/` folder for framework development
- `ClickFeedback` component for visual click confirmation in samples
- Input System as package dependency (required)
- PressKey/PressKeys methods for keyboard input simulation

### Fixed
- Input System clicks not reaching UI elements (was using wrong input module)

## [1.0.7] - 2025-01-05

### Added
- Sample tests demonstrating UITest framework capabilities
  - BasicNavigationTest - menu navigation patterns
  - ButtonInteractionTest - clicks, toggles, indexes, repeats, availability checks
  - FormInputTest - text input, dropdowns, sliders, form submission
  - DragAndDropTest - scrolling, swiping, drag-and-drop
  - PerformanceTest - framerate monitoring, scene load timing
- SampleSceneGenerator editor tool (UITest > Samples > Generate All Sample Scenes)
- Assembly definitions for Samples module

## [1.0.0] - 2024-12-16

### Added
- Initial package release
- Core UITestBehaviour base class with async test support
- UITestAttribute for test metadata (Scenario, Feature, Story, Severity, etc.)
- Recording system for capturing UI interactions
- Test generator for creating test code from recordings
- Editor toolbar integration for recording
- UITestRunner for batch test execution
- Assembly definitions for proper code organization
- README documentation

### Supported Projects
- MTD (Monster Truck Destruction)
- TOR (Trucks Off Road)
