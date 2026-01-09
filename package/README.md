# ODD Games UI Test Framework

A Unity Package Manager compatible UI automation testing framework for recording and replaying UI interactions.

## Repository Structure

```
ui-automation/
├── package/           # The UPM package (reference this in manifest.json)
│   ├── package.json
│   ├── UITest/        # Core framework code
│   └── ...
└── test/              # Test Unity project for development
```

## Installation

### Via Git URL (recommended)

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.oddgames.uitest": "https://github.com/oddgames/ui-automation.git?path=package"
  }
}
```

### Via Local Path (development)

```json
{
  "dependencies": {
    "com.oddgames.uitest": "file:../ui-automation/package"
  }
}
```

## Project Setup

**All projects must use the Input System Package (New) - see Requirements section.**

### For All Projects

1. Set **Active Input Handling** to **Input System Package (New)** in Player Settings
2. No additional setup required - works out of the box with Unity UI and TextMeshPro

## Writing Tests

### Basic Test Structure

```csharp
using ODDGames.UITest;
using Cysharp.Threading.Tasks;

[UITest(Scenario = 1, Feature = "Login", Story = "User can log in")]
public class LoginTest : UITestBehaviour
{
    protected override async UniTask Test()
    {
        await Click("LoginButton");
        await TextInput("EmailField", "test@example.com");
        await TextInput("PasswordField", "password123");
        await Click("SubmitButton");
        await Wait("WelcomeScreen");
    }
}
```

### UITest Attribute Properties

| Property | Type | Description |
|----------|------|-------------|
| `Scenario` | int | Unique test scenario ID (required) |
| `Feature` | string | Feature being tested |
| `Story` | string | User story description |
| `Severity` | TestSeverity | Blocker, Critical, Normal, Minor, Trivial |
| `Tags` | string[] | Tags for filtering tests |
| `Description` | string | Detailed test description |
| `Owner` | string | Test maintainer |
| `TimeoutSeconds` | int | Test timeout (default: 180) |
| `DataMode` | TestDataMode | UseDefined, UseCurrent, Ask |

### Available Test Methods

#### Navigation & Waiting
- `Wait(name)` - Wait for element to appear
- `WaitFor(condition)` - Wait for custom condition
- `WaitFramerate(fps)` - Wait until framerate stabilizes
- `SceneChange(sceneName)` - Wait for scene to load

#### Input Actions
- `Click(name)` - Click a UI element by name
- `ClickAny(name)` - Click any matching element
- `Hold(name, duration)` - Hold/long press element
- `Drag(name, direction)` - Drag element in a direction
- `DragTo(source, target)` - Drag one element to another (drag and drop)
- `DragFromTo(from, to)` - Drag between screen positions
- `TextInput(name, text, seconds, pressEnter)` - Enter text via Input System (click, type, optional Enter)
- `PressKey(key)` - Press a keyboard key
- `PressKeys(text)` - Type a string of characters

#### Complex Controls
- `ClickDropdown(name, index)` - Select dropdown option by index using realistic clicks
- `ClickDropdown(name, label)` - Select dropdown option by label text
- `ClickSlider(name, percent)` - Click slider at percentage position (0-1)
- `DragSlider(name, fromPercent, toPercent)` - Drag slider between positions
- `DoubleClick(name)` - Double-click a UI element
- `Scroll(name, delta)` - Scroll wheel input on an element

#### Gestures
All gesture distances use percentage of screen height (e.g., 0.2 = 20%) for device independence.

- `Swipe(name, direction, distance)` - Swipe gesture (Left, Right, Up, Down)
- `Pinch(name, scale, fingerDistance)` - Two-finger pinch (scale < 1 = pinch in, scale > 1 = pinch out)
- `TwoFingerSwipe(name, direction, distance, fingerSpacing)` - Two-finger swipe gesture
- `Rotate(name, degrees, fingerDistance)` - Two-finger rotation gesture (positive = clockwise)

#### Finding Elements
- `Find<T>(name)` - Find component by name (supports wildcards)
- `FindAll<T>(name)` - Find all matching components

#### Recording & Reporting
- `CaptureScreenshot()` - Capture test screenshot
- `AttachJson(name, data)` - Attach JSON data to report
- `AttachText(name, text)` - Attach text to report
- `AttachFile(path)` - Attach file to report
- `BeginStep(name)` - Start named test step
- `TrackPerformance(name)` - Track performance metrics
- `AddParameter(key, value)` - Add test parameter

#### Custom Clickables
- `RegisterClickable<T>()` - Register custom clickable type
- `RegisterRaycaster(raycaster)` - Register custom raycaster

## Best Practices

### Finding Elements

| Scenario | Method | Example |
|----------|--------|---------|
| Find by exact name | `Click("SubmitButton")` | Button named "SubmitButton" |
| Find by name pattern | `Click("Item*")` | Matches "Item1", "ItemGold", etc. |
| Find by visible text | `Click(Search.ByText("Continue"))` | Button showing "Continue" |
| Find input next to label | `TextInput(Search.ByAdjacent("Username:"), "test")` | Input field to the right of "Username:" label |
| Find in specific region | `Click(Search.ByName("Button").InRegion(ScreenRegion.TopRight))` | Button in top-right corner |
| Find first of many | `Click(Search.ByName("ListItem*").First())` | First item by screen position |
| Find with component | `Click(Search.ByType<Toggle>())` | Any Toggle component |
| Find with predicate | `Click(Search.ByType<Button>().With<Button>(b => b.interactable))` | Only interactable buttons |

### Interacting with Controls

| Control Type | Method | Notes |
|--------------|--------|-------|
| Buttons | `Click("ButtonName")` | Standard click |
| Toggles | `Click("ToggleName")` | Toggle on/off |
| Input Fields | `TextInput(Search.ByAdjacent("Label:"), "text")` | Find by adjacent label |
| Dropdowns | `ClickDropdown("Dropdown", "Option Text")` | Select by visible text |
| Dropdowns | `ClickDropdown("Dropdown", 2)` | Select by index (0-based) |
| Sliders | `ClickSlider("Volume", 0.75f)` | Click at 75% position |
| Sliders | `DragSlider("Volume", 0.2f, 0.8f)` | Drag from 20% to 80% |
| Scroll Views | `Drag("ScrollView", new Vector2(0, -200))` | Drag to scroll |
| Scroll Views | `ScrollTo("ListView", Search.ByText("Item 50"))` | Auto-scroll to hidden item |

### Iterating Over Container Items

```csharp
// Iterate over items in a scroll view, layout group, or dropdown
var container = await FindItems("InventoryList");
foreach (var (scrollRect, item) in container)
{
    // Use ScrollTo to bring item into view, then interact
    await ScrollTo(scrollRect, item);
    await Click(item);
}

// With filtering
var rareItems = await FindItems("InventoryList", Search.ByName("Rare*"));
foreach (var (_, item) in rareItems)
{
    Debug.Log($"Found rare item: {item.name}");
}

// Click all buttons found by search
var buttons = await FindAll<Button>(Search.ByName("ActionBtn*"));
foreach (var button in buttons)
{
    await Click(button);  // Component overload
}
```

### Gestures

| Gesture | Use Case | Example |
|---------|----------|---------|
| `Swipe` | Scroll/page navigation | `await Swipe(SwipeDirection.Left, distance: 0.3f)` |
| `Pinch` | Zoom in/out | `await Pinch(scale: 2.0f)` (zoom in) or `await Pinch(scale: 0.5f)` (zoom out) |
| `Rotate` | Rotation gestures | `await Rotate(degrees: 45f)` |
| `TwoFingerSwipe` | Map panning | `await TwoFingerSwipe(SwipeDirection.Up, distance: 0.2f)` |

### Waiting

| Scenario | Method | Example |
|----------|--------|---------|
| Wait for element | `Wait("LoadingComplete")` | Wait for named element to appear |
| Wait for condition | `WaitFor(() => score > 100)` | Wait for custom condition |
| Wait for framerate | `WaitFramerate(30)` | Wait until FPS stabilizes |
| Wait for scene | `SceneChange("GameScene")` | Wait for scene load |
| Fixed delay | `await UniTask.Delay(1000)` | Wait 1 second (avoid when possible) |

### Search Method Selection Guide

```
Need to find element by...
├── Name → Search.ByName("ButtonName") or just "ButtonName"
├── Visible text → Search.ByText("Click Me")
├── Adjacent label → Search.ByAdjacent("Email:", Adjacent.Right)
├── Component type → Search.ByType<Slider>()
├── Screen position → Search.ByName("*").InRegion(ScreenRegion.Center)
├── Hierarchy path → Search.ByPath("Canvas/Panel/Button")
└── Multiple conditions → Chain them: Search.ByType<Button>().ByName("Submit*").First()
```

### Common Patterns

```csharp
// Login form using adjacent labels
await TextInput(Search.ByAdjacent("Username:"), "testuser");
await TextInput(Search.ByAdjacent("Password:"), "password123");
await Click("Login");

// Navigate tabs by position
await Click(Search.ByName("Tab*").Skip(2).First());  // Click 3rd tab

// Select from dropdown
await ClickDropdown("CategoryDropdown", "Electronics");

// Scroll to and click hidden item
await ScrollTo("ProductList", Search.ByText("Rare Item"));
await Click(Search.ByText("Rare Item"));

// Pinch to zoom on a map
await Pinch("MapView", scale: 1.5f, duration: 0.5f);
```

## Recording Tests

Use the toolbar **"Record Test"** button to record user interactions:

1. Click the Record button in the Unity toolbar
2. Enter a name for your recording
3. Interact with the UI as you would in your test
4. Stop recording
5. Use the Generator window to create test code from the recording

## Requirements

**Important:** This package requires the **Unity Input System** package. Projects must be configured to use the new Input System (not the legacy Input Manager).

### Input System Setup

1. Install `com.unity.inputsystem` package (automatically installed as a dependency)
2. Go to **Edit > Project Settings > Player > Other Settings > Active Input Handling**
3. Set to **Input System Package (New)**
4. Unity will restart to apply the change

## Dependencies

- **Unity Input System** - Required for input injection (automatically installed)
- **UniTask** - Async/await support
- **TextMeshPro** - UI text handling
- **Unity UI** - Core UI system
- **Unity Recorder** (Editor only) - Video recording for test runs

## Assembly Structure

| Assembly | Platform | Description |
|----------|----------|-------------|
| `ODDGames.UITest` | Runtime | Core test framework |
| `ODDGames.UITest.Editor` | Editor | Test runner and editor tools |
| `ODDGames.UITest.Recording` | Runtime | Recording/playback system |
| `ODDGames.UITest.Recording.Editor` | Editor | Recording toolbar and generator |

## Version History

See [CHANGELOG.md](CHANGELOG.md) for version history.
