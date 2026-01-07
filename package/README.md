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

## Supported Projects

| Project | Description | Special Setup |
|---------|-------------|---------------|
| **MTD** | Monster Truck Destruction | Input System required |
| **TOR** | Trucks Off Road | None required |

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
