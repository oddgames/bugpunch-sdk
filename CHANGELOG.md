# Changelog

All notable changes to this project will be documented in this file.

## [1.0.7] - 2025-01-05

### Added
- Sample tests demonstrating UITest framework capabilities
  - BasicNavigationTest - menu navigation patterns
  - ButtonInteractionTest - clicks, toggles, indexes, repeats, availability checks
  - FormInputTest - text input, dropdowns, sliders, form submission
  - DragAndDropTest - scrolling, swiping, drag-and-drop
  - PerformanceTest - framerate monitoring, scene load timing
- EZ GUI sample tests (HAS_EZ_GUI)
  - EzGUIButtonTest - UIButton3D and AutoSpriteControlBase interactions
  - EzGUINavigationTest - MTD-style menu flows
  - EzGUIPurchaseFlowTest - shop and purchase flow testing
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
- EZ GUI support (HAS_EZ_GUI define) for MTD project
- Assembly definitions for proper code organization
- README documentation

### Supported Projects
- MTD (Monster Truck Destruction) - with EZ GUI support
- TOR (Trucks Off Road)
