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

For a specific version:

```json
{
  "dependencies": {
    "com.oddgames.uitest": "https://github.com/oddgames/ui-automation.git?path=package#v1.0.30"
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
        await TextInput(Adjacent("Email:"), "test@example.com");
        await TextInput(Adjacent("Password:"), "password123");
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

## Search API

The Search class provides a fluent API for finding UI elements. All string patterns support wildcards (`*`) and OR patterns (`|`).

### Creating Searches

From within `UITestBehaviour` (using protected helpers):
```csharp
await Click(Text("Play"));                    // By visible text
await Click(Name("btn_*"));                   // By name with wildcard
await Click(Text("OK|Okay|Confirm"));         // OR pattern
await Click(Name("Button").Type<Button>());   // Chained conditions
```

From anywhere:
```csharp
await Click(new Search().Name("btn_*").Type<Button>());
await Click(new Search("Play"));  // Constructor shorthand for text search
```

### Basic Filters

| Method | Description | Example |
|--------|-------------|---------|
| `Name(pattern)` | Match by GameObject name | `Name("PlayButton")`, `Name("btn_*")` |
| `Text(pattern)` | Match by visible text (TMP_Text or Text) | `Text("Submit")`, `Text("Level *")` |
| `Type<T>()` | Match by component type | `Type<Button>()`, `Type<Slider>()` |
| `Type(name)` | Match by type name string | `Type("*Button*")` |
| `Sprite(pattern)` | Match by sprite name | `Sprite("icon_*")` |
| `Path(pattern)` | Match by hierarchy path | `Path("*/Panel/Button*")` |
| `Tag(tag)` | Match by Unity tag | `Tag("Player")` |
| `Any(pattern)` | Match name, text, sprite, or path | `Any("*Settings*")` |

### Hierarchy Filters

| Method | Description | Example |
|--------|-------------|---------|
| `HasParent(search)` | Immediate parent matches | `Type<Button>().HasParent("Toolbar")` |
| `HasAncestor(search)` | Any ancestor matches | `Type<Button>().HasAncestor("*Settings*")` |
| `HasChild(search)` | Has matching immediate child | `Name("*Panel*").HasChild("Icon")` |
| `HasDescendant(search)` | Has matching descendant | `Type<ScrollRect>().HasDescendant(Text("Load More"))` |
| `HasSibling(search)` | Has matching sibling | `Type<TMP_InputField>().HasSibling("Label")` |
| `GetParent<T>()` | Has component in parent chain | `Type<Button>().GetParent<ScrollRect>()` |
| `GetChild<T>()` | Has component in children | `Name("*Panel*").GetChild<Button>()` |

### Spatial Filters

| Method | Description | Example |
|--------|-------------|---------|
| `Adjacent(text, direction)` | Find interactable adjacent to label | `Adjacent("Username:", Direction.Right)` |
| `Near(text, direction?)` | Find nearest interactable to text | `Near("Settings")`, `Near("Options", Direction.Below)` |
| `InRegion(region)` | Filter by screen region (3x3 grid) | `InRegion(ScreenRegion.TopRight)` |
| `InRegion(xMin,yMin,xMax,yMax)` | Filter by custom bounds | `InRegion(0f, 0f, 0.5f, 1f)` |
| `Visible()` | Only visible in viewport | `Type<Button>().Visible()` |

### Ordering & Selection

| Method | Description | Example |
|--------|-------------|---------|
| `First()` | Take first by screen position | `Type<Button>().First()` |
| `Last()` | Take last by screen position | `Name("ListItem*").Last()` |
| `Skip(n)` | Skip first N elements | `Type<Button>().Skip(1).First()` |
| `Take(n)` | Take first N elements | `Type<Button>().Take(3)` |
| `OrderBy<T>(selector)` | Order by component value | `Type<Slider>().OrderBy<Slider>(s => s.value)` |
| `OrderByDescending<T>(selector)` | Order descending | `OrderByDescending<RectTransform>(r => r.rect.width)` |
| `OrderByPosition()` | Order by screen position | `Type<Button>().OrderByPosition()` |
| `Randomize()` | Randomize order | `Type<Button>().Randomize().First()` |

### Transform Results

| Method | Description | Example |
|--------|-------------|---------|
| `GetParent()` | Return parent instead of match | `Name("Icon").GetParent()` |
| `GetChild(index)` | Return child at index | `Name("Container").GetChild(0)` |
| `GetSibling(offset)` | Return sibling at offset | `Name("Label").GetSibling(1)` |

### Other Modifiers

| Method | Description | Example |
|--------|-------------|---------|
| `Not` | Negate next condition | `Type<Button>().Not.HasParent("DisabledPanel")` |
| `With<T>(predicate)` | Filter by component property | `Type<Slider>().With<Slider>(s => s.value > 0.5f)` |
| `Where(predicate)` | Filter by GameObject predicate | `Name("Item*").Where(go => go.activeInHierarchy)` |
| `Or(search)` | Combine with OR logic | `Name("OK").Or(Text("Confirm"))` |
| `IncludeInactive()` | Include inactive GameObjects | `Name("HiddenPanel").IncludeInactive()` |
| `IncludeDisabled()` | Include disabled components | `Type<Button>().IncludeDisabled()` |
| `Interactable()` | Only interactable Selectables | `Type<Button>().Interactable()` |

## Test Methods

### Navigation & Waiting

| Method | Description |
|--------|-------------|
| `Wait(name)` | Wait for element to appear |
| `WaitFor(condition)` | Wait for custom condition |
| `WaitFramerate(fps)` | Wait until framerate stabilizes |
| `SceneChange(sceneName)` | Wait for scene to load |

### Input Actions

| Method | Description |
|--------|-------------|
| `Click(search)` | Click a UI element |
| `ClickAny(search)` | Click any matching element |
| `Hold(search, duration)` | Hold/long press element |
| `DoubleClick(search)` | Double-click element |
| `Drag(search, direction)` | Drag element in a direction |
| `DragTo(source, target)` | Drag one element to another |
| `DragFromTo(from, to)` | Drag between screen positions |
| `TextInput(search, text)` | Enter text via Input System |
| `PressKey(key)` | Press a keyboard key |
| `PressKeys(text)` | Type a string of characters |

### Complex Controls

| Method | Description |
|--------|-------------|
| `ClickDropdown(search, index)` | Select dropdown option by index |
| `ClickDropdown(search, label)` | Select dropdown option by label |
| `ClickSlider(search, percent)` | Click slider at percentage (0-1) |
| `DragSlider(search, from, to)` | Drag slider between positions |
| `Scroll(search, delta)` | Scroll wheel input |
| `ScrollTo(scrollView, target)` | Auto-scroll until target visible |

### Gestures

All gesture distances use percentage of screen height for device independence.

| Method | Description |
|--------|-------------|
| `Swipe(direction, distance)` | Single-finger swipe |
| `SwipeAt(x%, y%, direction)` | Swipe at screen position |
| `Pinch(scale, fingerDistance)` | Two-finger pinch (scale < 1 = zoom out) |
| `PinchAt(x%, y%, scale)` | Pinch at screen position |
| `Rotate(degrees, fingerDistance)` | Two-finger rotation |
| `RotateAt(x%, y%, degrees)` | Rotation at screen position |
| `TwoFingerSwipe(direction)` | Two-finger swipe |
| `TwoFingerSwipeAt(x%, y%, direction)` | Two-finger swipe at position |

### Finding Elements

| Method | Description |
|--------|-------------|
| `Find<T>(search)` | Find component by search |
| `FindAll<T>(search)` | Find all matching components |
| `FindItems(container, item?)` | Find container items for iteration |

### Recording & Reporting

| Method | Description |
|--------|-------------|
| `CaptureScreenshot()` | Capture test screenshot |
| `AttachJson(name, data)` | Attach JSON data to report |
| `AttachText(name, text)` | Attach text to report |
| `AttachFile(path)` | Attach file to report |
| `BeginStep(name)` | Start named test step |
| `TrackPerformance(name)` | Track performance metrics |

### Random Click & Auto-Explore

| Method | Description |
|--------|-------------|
| `SetRandomSeed(seed)` | Set seed for deterministic clicks |
| `RandomClick(filter?)` | Click a random clickable element |
| `RandomClickExcept(exclude)` | Click random, excluding patterns |
| `AutoExploreForSeconds(seconds)` | Explore for specified duration |
| `AutoExploreForActions(count)` | Explore for N actions |
| `AutoExploreUntilDeadEnd()` | Explore until no new elements |
| `TryClickBackButton()` | Click back/exit/close buttons |

## Best Practices

### Finding Elements by Adjacent Labels

```csharp
// Login form - find inputs by their labels
await TextInput(Adjacent("Username:"), "testuser");
await TextInput(Adjacent("Password:"), "password123");
await Click("Login");
```

### Interacting with Complex Controls

```csharp
// Dropdowns - select by text or index
await ClickDropdown("CategoryDropdown", "Electronics");
await ClickDropdown("SizeDropdown", 2);

// Sliders - click or drag to position
await ClickSlider("Volume", 0.75f);
await DragSlider("Brightness", 0.2f, 0.8f);

// Scroll views - auto-scroll to items
await ScrollTo("ProductList", Text("Rare Item"));
await Click(Text("Rare Item"));
```

### Iterating Over Container Items

```csharp
// Iterate over items in a scroll view or layout group
var container = await FindItems("InventoryList");
foreach (var (scrollRect, item) in container)
{
    await ScrollTo(scrollRect, item);
    await Click(item);
}

// With filtering
var rareItems = await FindItems("InventoryList", Name("Rare*"));
```

### Gestures

```csharp
// Swipe navigation
await Swipe(SwipeDirection.Left, distance: 0.3f);

// Pinch zoom
await Pinch(scale: 2.0f);  // Zoom in
await Pinch(scale: 0.5f);  // Zoom out

// Rotation
await Rotate(degrees: 45f);

// Two-finger pan
await TwoFingerSwipe(SwipeDirection.Up, distance: 0.2f);
```

### Search Method Selection Guide

```
Need to find element by...
├── Name → Name("ButtonName") or just "ButtonName"
├── Visible text → Text("Click Me")
├── Adjacent label → Adjacent("Email:", Direction.Right)
├── Nearest to text → Near("Settings")
├── Component type → Type<Slider>()
├── Screen position → Name("*").InRegion(ScreenRegion.Center)
├── Hierarchy path → Path("Canvas/Panel/Button")
└── Multiple conditions → Chain: Type<Button>().Name("Submit*").First()
```

## InputInjector Utility

The `InputInjector` static class provides low-level input injection for advanced use cases:

```csharp
// Position utilities
Vector2 screenPos = InputInjector.GetScreenPosition(gameObject);
Rect bounds = InputInjector.GetScreenBounds(gameObject);

// Pointer input (cross-platform mouse/touch)
await InputInjector.InjectPointerTap(screenPos);
await InputInjector.InjectPointerHold(screenPos, duration: 2f);
await InputInjector.InjectPointerDrag(startPos, endPos, duration: 0.5f);

// Touch-specific input
await InputInjector.InjectTouchTap(screenPos);
await InputInjector.InjectTouchHold(screenPos, duration: 2f);
await InputInjector.InjectTouchDrag(startPos, endPos, duration: 0.5f);

// Keyboard input
await InputInjector.TypeText("Hello World");
await InputInjector.PressKey(Key.Enter);
await InputInjector.HoldKey(Key.W, duration: 2f);
await InputInjector.HoldKeys(new[] { Key.W, Key.A }, duration: 2f);
```

### Keys Fluent Builder

```csharp
// Hold a single key
await Keys.Hold(Key.W).For(2f);

// Hold multiple keys simultaneously
await Keys.Hold(Key.W, Key.A).For(2f);

// Sequential key holds
await Keys.Hold(Key.W).For(1f).Then(Key.A).For(0.5f);

// Press then hold
await Keys.Press(Key.Space).Then(Key.W).For(2f);
```

## Auto-Explorer (Monkey Testing)

### Test Explorer Window

Open **Window > Analysis > UI Automation > Test Explorer** and use the **Auto-Explore** dropdown.

### Runtime Component

Add the `AutoExplorer` component to any GameObject for runtime exploration.

### Static API

```csharp
var result = await AutoExplorer.StartExploration(new ExploreSettings
{
    DurationSeconds = 60f,
    MaxActions = 100,
    Seed = 12345,

    // Smart exploration (all enabled by default)
    EnableActionVariety = true,
    UsePriorityScoring = true,

    // Exclusion patterns
    ExcludePatterns = new[] { "*Logout*", "*Delete*" },
    ExcludeTexts = new[] { "Buy Now", "Confirm Delete" }
});
```

### Jenkins/CI Batch Mode

```bash
Unity.exe -batchmode -executeMethod ODDGames.UITest.AutoExplorer.RunBatch \
  -exploreSeconds 300 \
  -exploreSeed 12345
```

## Visual Test Builder

Create tests visually with drag-and-drop blocks:
- Block types: Click, Type, Wait, Scroll, KeyHold, Assert, Log, Screenshot
- Assert conditions: ElementExists, TextEquals, ToggleIsOn, SliderValue, etc.
- RunCode block for custom C# expressions
- Tests stored as ScriptableObjects

## AI Test Generation

Gemini-powered test generation from natural language descriptions.

Configure in **Edit > Project Settings > UITest**:
- Set Gemini API key
- Select model (gemini-2.0-flash, etc.)

## Requirements

**Important:** This package requires the **Unity Input System** package.

### Input System Setup

1. Install `com.unity.inputsystem` (automatically installed as dependency)
2. Go to **Edit > Project Settings > Player > Other Settings > Active Input Handling**
3. Set to **Input System Package (New)**
4. Unity will restart to apply the change

## Dependencies

- **Unity Input System** - Required for input injection
- **UniTask** - Async/await support
- **TextMeshPro** - UI text handling
- **Unity UI** - Core UI system
- **Unity Recorder** (Editor only, optional) - Video recording

## Assembly Structure

| Assembly | Platform | Description |
|----------|----------|-------------|
| `ODDGames.UITest` | Runtime | Core test framework |
| `ODDGames.UITest.Editor` | Editor | Test runner and editor tools |
| `ODDGames.UITest.Recording` | Runtime | Recording/playback system |
| `ODDGames.UITest.Recording.Editor` | Editor | Recording toolbar and generator |

## Version History

See [CHANGELOG.md](CHANGELOG.md) for version history.
