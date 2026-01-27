# UITest Framework API Reference

> AI-digestible reference for the UITest UI automation framework for Unity.

## Quick Start

```csharp
[UITest(Feature = "Login")]
public class LoginTest : UITestBehaviour
{
    protected override async UniTask Test()
    {
        await Click("Login");                              // Click button with "Login" text
        await TextInput(Adjacent("Username:"), "user");    // Type in field next to label
        await TextInput(Adjacent("Password:"), "pass");
        await Click("Submit");
        await Wait(Name("Dashboard"), seconds: 10);        // Wait for element
    }
}
```

---

## Search API

### Basic Filters

| Method | Description | Example |
|--------|-------------|---------|
| `Name(pattern)` | GameObject name | `Name("btn_*")`, `Name("Player")` |
| `Text(pattern)` | Text/TMP_Text content | `Text("Submit")`, `Text("*score*")` |
| `Type<T>()` | Component type | `Type<Button>()`, `Type<Slider>()` |
| `Texture(pattern)` | Sprite/texture name | `Texture("icon_*")` |
| `Path(path)` | Hierarchy path | `Path("Canvas/Panel/Button")` |
| `Tag(tag)` | Unity tag (exact) | `Tag("Player")` |
| `Any(pattern)` | Name, text, sprite, or path | `Any("Submit")` |

**Pattern Syntax:**
- `*` wildcard: `"btn_*"` (prefix), `"*_selected"` (suffix), `"*score*"` (contains)
- `|` OR: `"OK|Confirm|Cancel"` matches any

**Implicit Conversion:** `Click("Play")` equals `Click(Text("Play"))`

### Proximity Filters

| Method | Description | Example |
|--------|-------------|---------|
| `Adjacent(text, direction)` | Interactable next to label | `Adjacent("Username:", Direction.Right)` |
| `Near(text, direction?)` | Nearest by distance | `Near("Settings")`, `Near("Flag", Direction.Below)` |

Directions: `Direction.Right` (default), `Left`, `Above`, `Below`

### Hierarchy Filters

| Method | Description |
|--------|-------------|
| `HasParent(search)` | Immediate parent matches |
| `HasAncestor(search)` | Any ancestor matches |
| `HasChild(search)` | Has child matching |
| `HasDescendant(search)` | Has descendant matching |
| `HasSibling(search)` | Sibling matches |
| `GetParent()` | Return parent instead |
| `GetChild(index)` | Return child at index |
| `GetSibling(offset)` | Return sibling at offset |

**Important (v1.0.38+):** `Text()` matches only the element with text. Use `HasChild(Text("..."))` to find parent containers like buttons.

### Spatial Filters

| Method | Description |
|--------|-------------|
| `InRegion(ScreenRegion.TopLeft)` | 9-region grid filter |
| `InRegion(xMin, yMin, xMax, yMax)` | Custom bounds (0-1) |
| `Visible()` | On-screen elements only |

Regions: `TopLeft`, `TopCenter`, `TopRight`, `MiddleLeft`, `Center`, `MiddleRight`, `BottomLeft`, `BottomCenter`, `BottomRight`

### Ordering & Selection

| Method | Description |
|--------|-------------|
| `First()` | First by screen position |
| `Last()` | Last by screen position |
| `Skip(n)` | Skip first N matches |
| `Take(n)` | Take only N matches |
| `OrderBy<T>(selector)` | Sort by property |
| `OrderByDescending<T>(selector)` | Sort descending |
| `Randomize()` | Random order |

### Modifiers

| Method | Description |
|--------|-------------|
| `With<T>(predicate)` | Filter by component property |
| `Where(predicate)` | Filter by GameObject |
| `Interactable()` | Must be interactable |
| `Not.HasAncestor(search)` | Negate next condition |
| `Or(otherSearch)` | OR with another search |
| `IncludeInactive()` | Include SetActive(false) |
| `IncludeDisabled()` | Include non-interactable |

### Chaining Order

```csharp
new Search()
    .Name("Button")           // 1. Base filter
    .HasParent(Name("Panel")) // 2. Hierarchy
    .IncludeInactive()        // 3. Availability
    .With<Button>(b => b.interactable) // 4. Predicates
    .InRegion(ScreenRegion.Center)     // 5. Spatial
    .First();                 // 6. Selection
```

---

## Test Actions

### Click Actions

```csharp
await Click(search);                    // Click element
await Click("ButtonText");              // Implicit Text() search
await ClickAt(0.5f, 0.5f);             // Click at screen position (0-1)
await DoubleClick(search);
await Hold(search, duration: 2f);       // Long press
await ClickDropdown(search, 2);         // Select by index
await ClickDropdown(search, "Option");  // Select by text
await ClickSlider(search, 0.75f);       // Click at position (0-1)
await ClickAny("OK", "Cancel", "Close"); // First found
await Scroll(search, delta: -120);      // Scroll (negative = down)
```

### Text Input

```csharp
await TextInput(search, "text");              // Type into field
await TextInput(search, "text", submit: true); // Type and press Enter
await PressKey(KeyCode.Escape);
await PressKey('a');
await PressKeys("Hello World!");
```

### Drag Actions

```csharp
await Drag(search, new Vector2(200, 0));              // Drag by offset
await Drag(search, offset, duration: 1f, holdTime: 0.5f);
await DragTo(source, target);                         // Drag to element
await DragFromTo(startPos, endPos);                   // Between positions
await DragSlider(search, fromPercent: 0.25f, toPercent: 0.75f);
await Swipe(search, SwipeDirection.Left);
```

### Gestures (Touch)

```csharp
await Pinch(search, scale: 2.0f);       // >1 zoom in, <1 zoom out
await Rotate(search, degrees: 45f);     // Positive = clockwise
await TwoFingerSwipe(search, SwipeDirection.Up);
```

### Wait & Find

```csharp
var element = await Find<Button>(search);              // Required (throws)
var element = await Find<Button>(search, required: false); // Optional
var elements = await FindAll<Button>(search);

await Wait(search, seconds: 10);                       // Wait for element
await Wait(seconds: 2);                                // Fixed delay
await WaitFor(() => condition, seconds: 30);           // Wait for condition
await WaitForNot(search);                              // Wait to disappear
await SceneChange(seconds: 30);                        // Wait for scene load
```

---

## Reflection API

### Static Path Access

```csharp
// Read values
var score = Search.Reflect("GameManager.Instance.Score").IntValue;
var health = Search.Reflect("Player.Instance.Health").FloatValue;
var ready = Search.Reflect("GameManager.Instance.IsReady").BoolValue;
var name = Search.Reflect("Player.Instance.Name").StringValue;

// Value properties: .Value, .StringValue, .BoolValue, .IntValue, .FloatValue
//                   .Vector3Value, .Vector2Value, .ColorValue, .QuaternionValue, .ArrayValue
```

### Property Navigation

```csharp
Search.Reflect("Player.Instance")
    .Property("Inventory")
    .Property("EquippedWeapon")
    .Property("Damage")
    .FloatValue;
```

### Indexers

```csharp
// Method syntax
Search.Reflect("Game.Players").Index(0).Property("Name").StringValue;
Search.Reflect("Config.Settings").Index("volume").FloatValue;

// C# indexer syntax
Search.Reflect("Game.Players")[0].Property("Name").StringValue;
Search.Reflect("Config")["settings"]["audio"].FloatValue;

// Inline in path
Search.Reflect("Game.Players[0].Name").StringValue;
Search.Reflect("Config.Settings[\"volume\"]").FloatValue;
```

### Array Iteration

```csharp
foreach (var item in Search.Reflect("Inventory.Items"))
{
    var name = item.Property("Name").StringValue;
    var count = item.Property("Count").IntValue;
}
```

### Component Access

```csharp
Search.Reflect("Player.Instance")
    .Component<Rigidbody>()
    .Property("isKinematic")
    .SetValue(true);

new Search().Name("Player")
    .Component("PlayerStats")
    .Property("health")
    .SetValue(100f);
```

### Method Invocation

```csharp
Search.Reflect("GameManager.Instance").Invoke("StartGame");
Search.Reflect("Player.Instance").Invoke("TakeDamage", 10f);
var result = Search.Reflect("Validator").Invoke<bool>("Validate", data);
```

### SetValue

```csharp
Search.Reflect("GameManager.Instance")
    .Property("Score")
    .SetValue(100);
```

### WaitFor with Static Paths

```csharp
await WaitFor(Search.Reflect("GameManager.Instance.IsReady"));        // Truthy
await WaitFor(Search.Reflect("Player.Score"), 100);                   // Specific value
await WaitFor(Search.Reflect("Game.State"), "Playing", timeout: 10f);
```

---

## GameObject Manipulation

All methods return restoration tokens for cleanup.

| Method | Returns | Description |
|--------|---------|-------------|
| `.Disable()` | `ActiveState` | Deactivate GameObject |
| `.Enable()` | `ActiveState` | Activate (finds inactive) |
| `.Freeze(includeChildren)` | `FreezeState` | Zero velocity + kinematic |
| `.Teleport(Vector3)` | `PositionState` | Move transform |
| `.NoClip(includeChildren)` | `ColliderState` | Disable colliders |
| `.Clip(includeChildren)` | `ColliderState` | Enable colliders |

### Usage

```csharp
// Direct call
new Search().Name("Player").Freeze();
Search.Reflect("GameManager.player").NoClip();

// Manual restore
var state = new Search().Name("Player").NoClip();
// ... test code ...
state.Restore();

// Auto-restore with using
using (new Search().Name("Player").NoClip())
{
    await Click("Button");  // Colliders disabled
}
// Colliders restored

// Multiple states
using (new Search().Name("AI").Freeze())
using (new Search().Name("Player").NoClip())
{
    await Teleport(Name("Player"), targetPosition);
}
// Both restored
```

### UITestBehaviour Helpers

```csharp
Disable(Name("TutorialPanel"));
Enable(Name("TutorialPanel"));
Freeze(Name("AITruck"));
Teleport(Name("Player"), new Vector3(100, 0, 50));
NoClip(Name("Player"));
Clip(Name("Player"));
```

---

## Assertions

### With Find

```csharp
Assert.IsNotNull(await Find<Button>(search, required: false));
Assert.IsTrue((await Find<Toggle>(search)).isOn);
Assert.AreEqual("Expected", (await Find<TMP_Text>(search)).text);
```

### With GetValue

```csharp
Assert.AreEqual("Label", await GetValue<string>(Name("Text")));
Assert.IsTrue(await GetValue<bool>(Name("Toggle")));
Assert.AreEqual(0.5f, await GetValue<float>(Name("Slider")), 0.01f);
```

### With Static Paths

```csharp
Assert.IsTrue(Search.Reflect("GameManager.Instance.IsReady").BoolValue);
Assert.Greater(Search.Reflect("Player.Score").IntValue, 0);
Assert.AreEqual("MainLevel", Search.Reflect("LevelManager.CurrentLevel.Name").StringValue);
```

---

## Test Structure

```csharp
using Cysharp.Threading.Tasks;
using ODDGames.UITest;
using NUnit.Framework;

[UITest(Feature = "Gameplay", Story = "Player can collect coins", Severity = Severity.Critical)]
public class CoinCollectionTest : UITestBehaviour
{
    protected override async UniTask Test()
    {
        // Arrange
        await Wait(Name("GameScene"), seconds: 10);

        // Act
        await Click("StartGame");
        await WaitFor(Search.Reflect("Player.Instance"));

        // Assert
        Assert.Greater(Search.Reflect("Player.Score").IntValue, 0);
    }
}
```

### Attributes

- `Feature` - High-level area (Login, Gameplay, Settings)
- `Story` - User goal
- `Scenario` - Test variation
- `Severity` - Blocker, Critical, Major, Minor, Trivial
- `Tags` - Custom string tags

---

## Common Patterns

### Form Input

```csharp
await TextInput(Adjacent("Username:"), "testuser");
await TextInput(Adjacent("Password:"), "password123");
await Click("Submit");
```

### Dropdown Selection

```csharp
await ClickDropdown(Name("CountryDropdown"), "United States");
// or by index
await ClickDropdown(Name("CountryDropdown"), 5);
```

### Slider Adjustment

```csharp
await DragSlider(Name("VolumeSlider"), 0f, 0.8f);
// or click directly
await ClickSlider(Name("VolumeSlider"), 0.8f);
```

### Scroll to Element

```csharp
await ScrollTo(Name("ScrollView"), Name("TargetItem"));
await Click(Name("TargetItem"));
```

### Wait for Loading

```csharp
await WaitForNot(Name("LoadingSpinner"));
await Wait(Name("MainContent"), seconds: 30);
```

### Skip Tutorial

```csharp
Disable(Name("TutorialPanel"));
await Click("StartGame");
```

### Freeze AI During Setup

```csharp
using (Freeze(Name("EnemySpawner")))
{
    Teleport(Name("Player"), spawnPoint);
    await WaitForSeconds(1);
}
```

### Button with Text (v1.0.38+)

```csharp
// Find button containing "Submit" text
await Click(new Search().HasChild(Text("Submit")).Type<Button>());

// Or use Adjacent if there's a nearby label
await Click(Adjacent("Form").Type<Button>());
```

---

## Version Notes

**v1.0.50:** Added GameObject manipulation (Disable, Enable, Freeze, Teleport, NoClip, Clip) with restoration tokens.

**v1.0.49:** Added SetValue() for setting properties via reflection.

**v1.0.47:** Added indexer support, nested type dot syntax, right-click drag.

**v1.0.38 (BREAKING):** `Text()` now matches only elements with text directly attached. Use `HasChild(Text(...))` for parent containers.

**v1.0.29:** Removed `By` prefix (`ByName` → `Name`). Renamed `Adjacent` enum to `Direction`.
