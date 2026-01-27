# ODD Games UI Automation

A UI automation testing framework for Unity. Write tests using Unity's standard NUnit framework with async/await.

## Installation

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "au.com.oddgames.uiautomation": "https://github.com/oddgames/ui-automation.git?path=package#v1.1.1"
  }
}
```

## Quick Start

```csharp
using System.Threading.Tasks;
using NUnit.Framework;
using static ODDGames.UIAutomation.UIAutomation;

[TestFixture]
public class LoginTests
{
    [Test]
    public async Task Login_WithValidCredentials_Succeeds()
    {
        await EnsureSceneLoaded("LoginScene");

        // Fill form using adjacent labels
        await TextInput(Adjacent("Username:"), "testuser");
        await TextInput(Adjacent("Password:"), "password123");

        // Submit and verify
        await Click(Text("Login"));
        await WaitFor(Name("MainMenu"), seconds: 10);
    }
}
```

## Understanding `using static`

The `using static ODDGames.UIAutomation.UIAutomation;` directive imports all helpers directly:

```csharp
// With 'using static' - clean and readable
await Click(Name("Button"));
await TextInput(Adjacent("Username:"), "test");

// Without - verbose
await UIAutomation.Click(UIAutomation.Name("Button"));
```

This gives you direct access to:
- **Search helpers**: `Name()`, `Text()`, `Type<T>()`, `Adjacent()`, `Near()`, `Path()`, `Tag()`, `Texture()`, `Any()`, `Reflect()`
- **Actions**: `Click()`, `DoubleClick()`, `Hold()`, `TextInput()`, `Drag()`, `DragTo()`, `Swipe()`, `Scroll()`
- **Waits**: `Wait()`, `WaitFor()`, `WaitFramerate()`
- **Finders**: `Find<T>()`, `FindAll<T>()`, `ScrollTo()`
- **Scene**: `EnsureSceneLoaded()`

## Search API

Find UI elements with a fluent API. All patterns support wildcards (`*`) and OR (`|`).

### Basic Filters

```csharp
await Click(Text("Play"));                    // By visible text
await Click(Name("btn_*"));                   // By name with wildcard
await Click(Text("OK|Okay|Confirm"));         // OR pattern
await Click(Name("Button").Type<Button>());   // Chained conditions
```

| Method | Description | Example |
|--------|-------------|---------|
| `Name(pattern)` | Match by GameObject name | `Name("PlayButton")` |
| `Text(pattern)` | Match by visible text | `Text("Submit")` |
| `Type<T>()` | Match by component type | `Type<Button>()` |
| `Texture(pattern)` | Match by texture/sprite name | `Texture("icon_*")` |
| `Path(pattern)` | Match by hierarchy path | `Path("*/Panel/Button*")` |
| `Tag(tag)` | Match by Unity tag | `Tag("Player")` |
| `Any(pattern)` | Match name, text, sprite, or path | `Any("*Settings*")` |

### Spatial Filters

```csharp
// Find input field next to "Username:" label
await TextInput(Adjacent("Username:"), "test");

// Find nearest element to "Settings" text
await Click(Near("Settings"));

// Find in specific direction
await Click(Near("Options", Direction.Below));
```

| Method | Description |
|--------|-------------|
| `Adjacent(text, direction?)` | Find interactable adjacent to label |
| `Near(text, direction?)` | Find nearest interactable to text |
| `InRegion(region)` | Filter by screen region |
| `Visible()` | Only visible in viewport |

### Hierarchy Filters

| Method | Description |
|--------|-------------|
| `HasParent(search)` | Immediate parent matches |
| `HasAncestor(search)` | Any ancestor matches |
| `HasChild(search)` | Has matching child |
| `HasDescendant(search)` | Has matching descendant |
| `HasSibling(search)` | Has matching sibling |

### Ordering & Selection

```csharp
await Click(Type<Button>().First());           // First by screen position
await Click(Name("ListItem*").Last());         // Last by screen position
await Click(Type<Button>().Skip(1).First());   // Second button
```

### Modifiers

```csharp
// Negate condition
Type<Button>().Not.HasParent("DisabledPanel")

// Filter by component property
Type<Slider>().With<Slider>(s => s.value > 0.5f)

// Include inactive/disabled
Name("HiddenPanel").IncludeInactive()
Type<Button>().IncludeDisabled()
```

## Test Actions

### Click & Input

```csharp
await Click(Name("Button"));
await DoubleClick(Name("Item"));
await Hold(Name("Button"), seconds: 2);
await TextInput(Adjacent("Email:"), "test@example.com");
await PressKey(Key.Enter);
```

### Complex Controls

```csharp
// Dropdowns
await ClickDropdown(Name("Dropdown"), "Option 1");
await ClickDropdown(Name("Dropdown"), 2);  // By index

// Sliders
await ClickSlider(Name("Volume"), 0.75f);
await DragSlider(Name("Brightness"), 0.2f, 0.8f);

// Scroll views
await ScrollTo(Name("ScrollView"), Text("Target Item"));
```

### Gestures

```csharp
await Swipe(SwipeDirection.Left, distance: 0.3f);
await Pinch(scale: 2.0f);    // Zoom in
await Pinch(scale: 0.5f);    // Zoom out
await Rotate(degrees: 45f);
await TwoFingerSwipe(SwipeDirection.Up);
```

### Waiting

```csharp
await Wait(seconds: 1);
await WaitFor(Name("LoadingComplete"), seconds: 10);
await WaitFor(Text("Submit").With<Button>(b => b.interactable), seconds: 5);
```

## Reflection Access

Access game state for assertions:

```csharp
// Read values
var health = Reflect("Player.Instance.Health").FloatValue;
var score = Reflect("GameManager.Score").IntValue;

// Navigate nested properties
var damage = Reflect("Player.Instance").Property("Stats.Damage").FloatValue;

// With NUnit assertions
Assert.AreEqual(100f, Reflect("Player.Health").FloatValue);
Assert.Greater(Reflect("Player.Score").IntValue, 0);
```

## Auto-Explorer (Monkey Testing)

```csharp
await AutoExplorer.StartExploration(new ExploreSettings
{
    DurationSeconds = 60f,
    MaxActions = 100,
    Seed = 12345,
    ExcludePatterns = new[] { "*Logout*", "*Delete*" }
});
```

Or use **Window > Analysis > UI Automation > Test Explorer** with the Auto-Explore dropdown.

## Requirements

- Unity 2022.3+
- **Input System Package** (New Input System) - set in Player Settings
- TextMeshPro
- Newtonsoft JSON

## Documentation

Full documentation: https://github.com/oddgames/ui-automation/wiki

## Version History

See [CHANGELOG.md](CHANGELOG.md)
