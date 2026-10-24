# UI Automation API Reference

> Compact reference for writing Unity UI tests with `ODDGames.BugpunchSdk.ActionExecutor` and `Search`.

Every helper on this page is a static member of `ActionExecutor` (namespace `ODDGames.BugpunchSdk`). Import it statically and write tests as ordinary async NUnit tests in a PlayMode test assembly:

```csharp
using System.Threading.Tasks;
using NUnit.Framework;
using ODDGames.BugpunchSdk;
using static ODDGames.BugpunchSdk.ActionExecutor;

public class LoginTests
{
    [Test]
    public async Task UserCanLogIn()
    {
        await Click(Text("Login"));
        await TextInput(Adjacent("Username:"), "user");
        await TextInput(Adjacent("Password:"), "pass");
        await Click(Text("Submit"));
        await Wait(Name("Dashboard"), seconds: 10);
    }
}
```

Input is injected through virtual Input System devices (`UIAutomation_Mouse`, `UIAutomation_Keyboard`, `UIAutomation_Touchscreen`) that are added on first use, so it reaches `InputSystemUIInputModule` and any `InputAction` bound to those device types.

Actions throw `ActionExecutor.BugpunchTestFailureException` when their target isn't found in time, so a failing step fails the test. There is no implicit string-to-`Search` conversion: pass `Text("Play")`, `Name("Play")`, or `new Search("Play")` (a text search).

---

## Search API

Build a `Search` with a shortcut (`Name(...)`) or `new Search()` and chain filters. Every filter narrows the match (AND).

### Basic Filters

| Method | Matches | Example |
|--------|---------|---------|
| `Name(pattern)` | GameObject name | `Name("btn_*")` |
| `Text(pattern)` | `Text` / `TMP_Text` on the element itself | `Text("Submit")`, `Text("*score*")` |
| `Type<T>()` / `Type(name)` | Component type | `Type<Button>()`, `Type("TMP_*")` |
| `Texture(pattern)` | Sprite / texture / renderer material texture name | `Texture("icon_*")` |
| `Path(pattern)` | Hierarchy path | `Path("*/Panel/Button*")` |
| `Tag(tag)` | Unity tag (exact) | `Tag("Player")` |
| `Any(pattern)` | Name, text, sprite, or path | `Any("*Settings*")` |

**Patterns:** `*` wildcard (`"btn_*"`, `"*_selected"`, `"*score*"`), `|` alternatives (`"OK|Confirm"`). Case-insensitive by default; `Name` and `Text` take `ignoreCase: false`.

`Text()` matches only the element that holds the text. To get the button around a label use `Type<Button>().HasChild(Text("Submit"))` — or just click the label, which taps the button beneath it.

### Proximity Filters

| Method | Description | Example |
|--------|-------------|---------|
| `Adjacent(text, direction = Right)` | Best-aligned interactable beside a label | `Adjacent("Username:", Direction.Right)` |
| `Near(text, direction = null)` | Nearest interactable by distance | `Near("Settings")`, `Near("Flag", Direction.Below)` |

Directions: `Direction.Right`, `Left`, `Above`, `Below`.

### Hierarchy Filters

| Method | Description |
|--------|-------------|
| `HasParent(search \| name)` | Immediate parent matches |
| `HasAncestor(search \| name)` | Any ancestor matches |
| `HasChild(search \| name)` | Has a matching child |
| `HasDescendant(search \| name)` | Has a matching descendant |
| `HasSibling(search \| name)` | Has a matching sibling |
| `GetParent<T>(predicate?)` | Has an ancestor with component `T` (filter) |
| `GetChild<T>(predicate?)` | Has a descendant with component `T` (filter) |
| `GetParent()` | Return the matched element's parent instead |
| `GetChild(index)` | Return the matched element's child at `index` instead |
| `GetSibling(offset)` | Return the sibling at `offset` instead |

### Spatial & State Filters

| Method | Description |
|--------|-------------|
| `InRegion(ScreenRegion.TopLeft)` | 3×3 screen grid region |
| `InRegion(xMin, yMin, xMax, yMax)` | Custom normalized bounds (0-1) |
| `Visible()` | Inside the viewport (not scrolled/culled away) |
| `Interactable()` | `Selectable.interactable` is true |
| `RequiresReceiver(params Type[])` | An EventSystem raycast at the element must hit a handler on it |

Regions: `TopLeft`, `TopCenter`, `TopRight`, `MiddleLeft`, `Center`, `MiddleRight`, `BottomLeft`, `BottomCenter`, `BottomRight`.

### Ordering & Selection

| Method | Description |
|--------|-------------|
| `First()` / `Last()` | First / last by screen position (top-left → bottom-right) |
| `Skip(n)` / `Take(n)` | Skip / keep N matches |
| `OrderBy<T>(selector)` / `OrderByDescending<T>(selector)` | Sort by a component value |
| `OrderByPosition()`, `OrderByName()`, `OrderByInstanceId()` (+ `Descending`) | Built-in sorts |
| `Randomize()` | Random order |

Without an explicit order, matches are sorted closest-to-camera first, UI before world objects.

### Modifiers

| Method | Description |
|--------|-------------|
| `With<T>(predicate)` | Filter by a component property |
| `Where(predicate)` | Filter by GameObject |
| `Not` | Negate the next filter: `new Search().Type<Button>().Not.HasAncestor(Name("Debug*"))` |
| `Or(otherSearch)` | Match either search |
| `IncludeInactive()` | Include `SetActive(false)` objects |
| `IncludeOffScreen()` | Include elements outside the screen |

### Using a Search directly

```csharp
var search = new Search().Name("Button").HasParent(Name("Panel")).With<Button>(b => b.interactable).First();

GameObject go = await search.Find(timeout: 5f);          // null when not found
List<GameObject> all = await search.FindAll(timeout: 5f);
bool present = await search.Exists(timeout: 1f);
Vector2 screenPos = await search.GetScreenPosition();
```

`Find()` waits until the match set has been identical for 3 consecutive frames, so animations and layout settle before you act.

---

## Test Actions

Parameters shown with their defaults. `searchTime = -1` means `ActionExecutor.DefaultSearchTime` (10 s). Normalized coordinates are 0-1 with Unity's bottom-left origin.

### Click Actions

```csharp
await Click(Name("PlayButton"));                    // Click(search, searchTime = -1, index = 0)
await Click(Text("Item"), index: 2);                // Nth match
await ClickAt(0.5f, 0.5f);                          // normalized screen position
await DoubleClick(Name("FileEntry"));
await TripleClick(Name("TextArea"));
await Hold(Name("ChargeButton"), 2f);               // Hold(search, seconds)
await ClickDropdown(Name("Country"), 2);            // by index
await ClickDropdown(Name("Country"), "Option");     // by label
await RandomClick(Type<Button>());
```

`DoubleClickAt`, `TripleClickAt` and `HoldAt` take a **pixel** `Vector2`.

### Text Input

```csharp
await TextInput(Name("Username"), "text");                    // TextInput(search, input, seconds = 10, pressEnter = false)
await TextInput(Name("Search"), "query", pressEnter: true);
await Type(Name("Notes"), " more", clearFirst: false);        // Type(search, text, clearFirst = true, pressEnter = false, ...)
await TypeText("Hello");                                      // Input System text events, no target
await PressKey(KeyCode.Escape);
await PressKey('a');
await PressKey("enter");                                      // KeyCode name or alias
await PressKeys("Hello World");                               // one key press per character
await HoldKey(KeyCode.LeftShift, 1f);
await HoldKeys(0.5f, KeyCode.LeftControl, KeyCode.A);
```

`TextInput` / `Type` focus the field with a tap, then set `TMP_InputField.text` / `InputField.text` directly (TMP doesn't read Input System text events).

### Drag & Scroll

```csharp
await Drag(Name("Handle"), new Vector2(200, 0));              // pixel offset
await Drag(Name("Handle"), new Vector2(200, 0), duration: 1f, holdTime: 0.5f);
await DragTo(Name("Item"), Name("DropZone"));
await DragFromTo(new Vector2(100, 300), new Vector2(600, 300)); // pixel positions
await DragSlider(Name("Volume"), 0.25f, 0.75f);               // DragSlider(search, fromValue, toValue)
await ClickSlider(Name("Volume"), 0.8f);
await SetSlider(Name("Volume"), 0.8f);
await SetScrollbar(Name("Scrollbar"), 1f);
await Scroll(Name("List"), -120f);                            // wheel delta (negative = down)
await Scroll(Name("List"), "down", amount: 0.3f);
await ScrollTo(Name("ScrollView"), Name("TargetItem"));       // returns the target GameObject
await ScrollToAndClick(Name("ScrollView"), Name("TargetItem"));
```

### Gestures (Touch)

```csharp
await Swipe(Name("Card"), SwipeDirection.Left);               // Swipe(search, direction, distance = 0.2, duration = 0.15)
await Swipe(SwipeDirection.Up);                               // from screen center
await SwipeAt(0.5f, 0.3f, SwipeDirection.Right);
await Pinch(Name("Map"), 2.0f);                               // >1 zoom in, <1 zoom out
await PinchAt(0.5f, 0.5f, 0.5f);
await Rotate(Name("Dial"), 45f);                              // positive = clockwise
await RotateAt(0.5f, 0.5f, -90f);
await TwoFingerSwipe(Name("Map"), "up");                      // direction is a string with a target
await TwoFingerSwipe(SwipeDirection.Up);                      // enum without one
```

Clicks, drags, `SwipeAt` and center swipes use touch on iOS/Android builds and the mouse elsewhere (touch when no mouse exists); `Swipe(search, …)` always injects a mouse drag; pinch, rotate and two-finger swipes always inject touch. `Pinch(search, …)` needs a `RectTransform` target — use `PinchAt` for world objects.

### Find & Wait

```csharp
var button = await Find<Button>(Name("OK"));                          // throws when missing
var maybe = await Find<Button>(Name("OK"), throwIfMissing: false);   // null when missing
var all = await FindAll<Toggle>(Name("Option*"));

await Wait(Name("MainContent"), seconds: 10);         // wait for an element
await Wait(2f);                                       // fixed delay
await WaitFor(Name("Dialog"));                        // WaitFor(search, timeout = 10)
await WaitFor(Name("Score"), "100", timeout: 10f);    // element text equals
await WaitForNot(Name("LoadingSpinner"));
await WaitFor(() => Time.timeScale > 0, seconds: 30); // custom condition
await SceneChange(seconds: 30);
await FastForward(scale: 8f, untilGoal: "match.finished"); // run 8× until that goal (or a scene change)
await FastForward(scale: 4f, untilGoal: null, seconds: 60);  // 4× until the scene changes
await WaitForStableFrameRate(minFps: 20);
await WaitFramerate(30);
```

### Diagnostics & Misc

```csharp
string png = await Screenshot();                      // saved under persistentDataPath
await Snapshot();                                     // hierarchy snapshot into the diagnostics session
var result = await Execute("{\"action\":\"click\", \"text\":\"Play\"}");  // JSON action (see JSON_ACTIONS.md)
await LoadTestData("TestData/SaveGame");              // Resources zip: files/ + playerprefs.json
await NavigateToMainMenu(Name("MainMenu"));           // click Back/Close/Escape until found
var explore = await AutoExploreForSeconds(30f, seed: 42);
var suggestions = LocatorsFor(button.gameObject);     // searches that resolve back to an object
```

Mark a static method `[BugpunchTestReset]` to reset game state before each automated test run.

---

## Reflection API

`Search.Reflect(path)` (or the shortcut `Reflect(path)`) resolves a static path — `TypeName.StaticMember.Member…` — against every loaded assembly. The result is a `Search` you can read, navigate, invoke and enumerate.

### Reading Values

```csharp
var score = Search.Reflect("GameManager.Instance.Score").GetValue<int>();
var ready = Search.Reflect("GameManager.Instance.IsReady").GetValue<bool>();
var position = Search.Reflect("Player.Instance.Position").GetValue<Vector3>();
var health = Search.Reflect("Player.Instance").GetValue<float>("Stats.Health");  // sub-path

var direct = GetValue<int>("GameManager.Instance.Score");  // ActionExecutor shortcut
```

`ActionExecutor.Static(path)` returns a lightweight `StaticPath` with `Value`, `StringValue`, `IntValue`, `FloatValue`, `BoolValue` and `GameObject` accessors.

### Property Navigation & Indexers

```csharp
Search.Reflect("Player.Instance").Property("Inventory").Property("EquippedWeapon").GetValue<float>("Damage");

Search.Reflect("Game.Players").Index(0).Property("Name").GetValue<string>();
Search.Reflect("Config.Settings").Index("volume").GetValue<float>();
Search.Reflect("Game.Players")[0].Property("Name").GetValue<string>();
Search.Reflect("Config")["settings"]["audio"].GetValue<float>();
Search.Reflect("Game.Players[0].Name").GetValue<string>();
```

### Iteration

```csharp
foreach (var item in Search.Reflect("Inventory.Items"))
{
    var itemName = item.Property("Name").GetValue<string>();
    var count = item.Property("Count").GetValue<int>();
}
```

### Components, Methods, Setting Values

```csharp
Search.Reflect("Player.Instance").Component<Rigidbody>().Property("isKinematic").SetValue(true);
Search.Reflect("GameManager.Instance").Property("Score").SetValue(100);

Search.Reflect("GameManager.Instance").Invoke("StartGame");
Search.Reflect("Player.Instance").Invoke("TakeDamage", 10f);
bool valid = Search.Reflect("Validator").Invoke<bool>("Validate", "input");
await Search.Reflect("SaveSystem.Instance").InvokeAsync("SaveAsync");
```

### Waiting on Static Paths

```csharp
await WaitFor("GameManager.Instance.IsReady");                   // truthy
await WaitFor<int>("Player.Score", 100);                         // equals
await WaitFor<string>("Game.State", "Playing", timeout: 10f);
```

Use the string overloads here — `WaitFor(Search, …)` waits for an on-screen element, not a value.

### UI Element Values

```csharp
string label = await GetValue<string>(Name("ScoreLabel"));   // text
bool isOn = await GetValue<bool>(Name("SoundToggle"));       // toggle
float volume = await GetValue<float>(Name("VolumeSlider"));  // slider / scrollbar
string[] options = await GetValue<string[]>(Name("Dropdown"));
```

---

## GameObject Manipulation

Each call finds its target (a search or a static path), changes it, and returns a restore token (`IDisposable`).

| Method | Returns | Description |
|--------|---------|-------------|
| `Disable(search)` | `Search.ActiveState` | `SetActive(false)` |
| `Enable(search)` | `Search.ActiveState` | `SetActive(true)` (finds inactive objects) |
| `Freeze(search, includeChildren = true)` | `Search.FreezeState` | Zero velocity + kinematic (3D and 2D) |
| `Teleport(search, worldPosition)` | `Search.PositionState` | Move the transform |
| `NoClip(search, includeChildren = true)` | `Search.ColliderState` | Disable colliders |
| `Clip(search, includeChildren = true)` | `Search.ColliderState` | Enable colliders |

The same methods exist on `Search` itself (`await Name("Player").NoClip()`).

```csharp
await Disable(Name("TutorialPanel"));                         // no restore needed

var state = await NoClip(Name("Player"));
await Click(Text("Button"));
state.Restore();

using (await Freeze(Name("AI")))
using (await Teleport(Name("Player"), new Vector3(100, 0, 50)))
{
    await Click(Text("Start"));
}                                                             // both restored

await Search.Reflect("GameManager.Instance.player").NoClip(); // static path target
```

---

## Assertions

```csharp
Assert.IsNotNull(await Find<Button>(Name("OK"), throwIfMissing: false));
Assert.IsTrue((await Find<Toggle>(Name("Sound"))).isOn);
Assert.AreEqual("Expected", (await Find<TMP_Text>(Name("Title"))).text);

Assert.AreEqual("Label", await GetValue<string>(Name("Text")));
Assert.AreEqual(0.5f, await GetValue<float>(Name("Slider")), 0.01f);

Assert.IsTrue(Search.Reflect("GameManager.Instance.IsReady").GetValue<bool>());
Assert.Greater(Search.Reflect("Player.Score").GetValue<int>(), 0);
```

---

## Configuration

| Member | Default | Description |
|--------|---------|-------------|
| `DefaultSearchTime` | 10 | Seconds a search waits when `searchTime` is -1 |
| `Interval` | 50 | Milliseconds paused after each action |
| `PollInterval` | 100 | Milliseconds between condition polls |
| `DebugMode` / `DebugIntervalMultiplier` | false / 3 | Slow everything down and log verbosely |
| `ShowInputOverlay` | — | Draw injected clicks, drags and keys on screen |
| `CaptureUnobservedExceptions` / `CapturedExceptions` | false | Collect unobserved `Task` exceptions |
| `SetRandomSeed(seed)` | — | Deterministic `Randomize()` / random clicks |
| `EnableButtonClickTracking()` / `LastClickedButton` | — | Log which `Button.onClick` actually fired |

---

## Common Patterns

### Form Input

```csharp
await TextInput(Adjacent("Username:"), "testuser");
await TextInput(Adjacent("Password:"), "password123");
await Click(Text("Submit"));
```

### Wait for Loading

```csharp
await WaitForNot(Name("LoadingSpinner"));
await Wait(Name("MainContent"), seconds: 30);
```

### Skip the Tutorial

```csharp
await Disable(Name("TutorialPanel"));
await Click(Text("StartGame"));
```

### Freeze AI During Setup

```csharp
using (await Freeze(Name("EnemySpawner")))
{
    await Teleport(Name("Player"), new Vector3(0, 0, 10));
    await Wait(1f);
}
```

### Button by Label

```csharp
await Click(new Search().Type<Button>().HasChild(Text("Submit")));
await Click(Adjacent("Accept terms"));
```
