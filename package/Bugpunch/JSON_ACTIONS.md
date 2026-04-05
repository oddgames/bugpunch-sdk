# JSON Action API Reference

> Complete reference for AI agents producing JSON action objects to drive Unity UI via `UIACTION` commands.

## Overview

Actions are JSON objects sent via the `UIACTION` bridge command. Every action has a required `"action"` field and optional parameters. A screenshot is automatically captured after every action.

```
UIACTION {"action":"click", "text":"Settings"}
```

Or programmatically:

```csharp
var result = await ActionExecutor.Execute("{\"action\":\"click\", \"text\":\"Settings\"}");
// result.Success, result.Error, result.ElapsedMs, result.ScreenshotPath
```

### Response Format

| Field | Type | Description |
|-------|------|-------------|
| `Success` | bool | Whether the action completed without error |
| `Error` | string | Error message if failed (element not found, timeout, invalid JSON) |
| `ElapsedMs` | float | Execution time in milliseconds |
| `ScreenshotPath` | string | Path to the post-action screenshot |

### Sessions

Wrap action sequences in a session for automatic logging and HTML report generation:

```
UISESSION START MyTest "Description of what we're testing"
UIACTION {"action":"click", "text":"Play"}
UIACTION {"action":"waitfor", "text":"Game Over", "seconds":60}
UISESSION STOP
```

---

## Element Targeting (Search Fields)

Most actions need to find a UI element. Specify targets using **search fields** -- multiple fields are AND-combined.

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | Match by visible text content (Text, TMP_Text) |
| `name` | string | Match by GameObject name in the hierarchy |
| `type` | string | Match by component type name (e.g. `"Button"`, `"Slider"`) |
| `adjacent` | string | Find the nearest interactable element next to this text label |
| `near` | string | Find the nearest element to this text label (pure distance) |
| `tag` | string | Match by Unity tag |
| `path` | string | Match by hierarchy path |
| `any` | string | Match by any of: text, name, or type |
| `at` | `[x, y]` | Normalized screen coordinates (0-1); bypasses element search |
| `direction` | string | Direction for `adjacent`/`near`: `"right"`, `"left"`, `"above"`/`"up"`, `"below"`/`"down"` |
| `index` | int | When multiple elements match, select by 0-based index (for click/doubleclick/tripleclick/hold) |

### Wildcard Patterns

All string search fields support `*` wildcards and `|` OR syntax:

```json
{"action":"click", "text":"Play*"}
{"action":"click", "name":"btn_*"}
{"action":"click", "text":"OK|Confirm|Accept"}
{"action":"click", "any":"*Settings*"}
```

### Coordinate System

`at` values are **normalized screen coordinates**:
- `[0, 0]` = bottom-left
- `[1, 1]` = top-right
- `[0.5, 0.5]` = screen center

### Combining Search Fields

Multiple fields create an AND query:

```json
{"action":"click", "text":"Submit", "type":"Button"}
```

This finds a Button component whose text is "Submit".

### Choosing the Right Search Field

| Scenario | Recommended | Example |
|----------|-------------|---------|
| Button/label with visible text | `text` | `{"text":"Play Game"}` |
| Element with a known hierarchy name | `name` | `{"name":"SettingsPanel"}` |
| Input field next to a label | `adjacent` | `{"adjacent":"Username:"}` |
| Closest element to a reference point | `near` | `{"near":"Settings"}` |
| No clear identifier | `at` | `{"at":[0.5, 0.3]}` |
| Broad match across name/text/path | `any` | `{"any":"*volume*"}` |

---

## Adjacent vs Near

Both find elements relative to a text label, but with different strategies.

### `adjacent`

Finds the **single best-scoring interactable** (Button, InputField, Dropdown, Slider, Toggle, or any Selectable) next to a text label. Uses **alignment-weighted scoring** -- strongly prefers elements in the same row/column.

Best for: form layouts, settings screens, any label + control pair.

```json
{"action":"click", "adjacent":"Remember me"}
{"action":"type", "adjacent":"Username:", "value":"admin"}
{"action":"slider", "adjacent":"Volume:", "value":0.8}
{"action":"dropdown", "adjacent":"Language:", "option":"English"}
{"action":"click", "adjacent":"Actions", "direction":"below"}
{"action":"type", "adjacent":"Description", "direction":"below", "value":"text"}
```

Use the `"direction"` field to search in a specific direction: `"right"` (default), `"left"`, `"above"`/`"up"`, `"below"`/`"down"`.

### `near`

Finds the **nearest interactable** by pure Euclidean distance, with no alignment preference. Optionally filterable by direction in C#.

Best for: diagonal/irregular layouts, when the element isn't strictly beside the label.

```json
{"action":"click", "near":"Settings"}
{"action":"click", "near":"Audio", "direction":"below"}
```

Use the optional `"direction"` field to filter to a specific direction.

### Comparison

| | `adjacent` | `near` |
|---|---|---|
| Returns | Single best match | Nearest by distance |
| Scoring | Alignment-weighted (row/column preference) | Pure Euclidean distance |
| Default direction | Right | All directions |
| Best for | Form fields next to labels | Irregular layouts |

---

## Actions Reference

### click

Single click on an element.

```json
{"action":"click", "text":"Settings"}
{"action":"click", "name":"SettingsBtn"}
{"action":"click", "at":[0.5, 0.5]}
{"action":"click", "text":"Submit", "type":"Button"}
{"action":"click", "adjacent":"Enable Sounds"}
{"action":"click", "any":"*play*"}
{"action":"click", "text":"Item", "index":2}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `index` | int | 0 | When multiple elements match, click the Nth one (0-based) |

### doubleclick

```json
{"action":"doubleclick", "text":"Item"}
{"action":"doubleclick", "name":"FileEntry"}
{"action":"doubleclick", "at":[0.5, 0.5]}
{"action":"doubleclick", "text":"ListItem", "index":1}
```

Supports `index` param (same as click).

### tripleclick

```json
{"action":"tripleclick", "name":"TextArea"}
{"action":"tripleclick", "at":[0.3, 0.7]}
```

Supports `index` param (same as click).

### hold

Press and hold for a duration.

```json
{"action":"hold", "text":"Delete", "seconds":2}
{"action":"hold", "at":[0.5, 0.5], "seconds":1.5}
{"action":"hold", "name":"ChargeButton", "seconds":3}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 1 | Hold duration |

### type

Type text into an input field. Finds the field, clicks to focus, then types.

```json
{"action":"type", "name":"InputField", "value":"hello world"}
{"action":"type", "adjacent":"Username:", "value":"admin"}
{"action":"type", "adjacent":"Password:", "value":"secret123", "enter":true}
{"action":"type", "name":"SearchBox", "value":"query", "clear":true, "enter":true}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `value` | string | `""` | Text to type |
| `clear` | bool | `true` | Clear existing text before typing |
| `enter` | bool | `false` | Press Enter after typing |

### textinput

Lower-level text injection into a found input field.

```json
{"action":"textinput", "name":"Field", "value":"raw text"}
{"action":"textinput", "adjacent":"Comment:", "value":"some text"}
```

### typetext

Simulate keyboard character presses globally (no target element).

```json
{"action":"typetext", "text":"Hello World"}
{"action":"typetext", "value":"abc123"}
```

### keys

Type a string of characters via keyboard simulation (no target element).

```json
{"action":"keys", "text":"abc123"}
{"action":"keys", "value":"test input"}
```

### key

Press a single keyboard key.

```json
{"action":"key", "key":"enter"}
{"action":"key", "key":"escape"}
{"action":"key", "key":"space"}
{"action":"key", "key":"tab"}
{"action":"key", "key":"backspace"}
{"action":"key", "key":"a"}
{"action":"key", "key":"F1"}
```

**Key aliases**: `enter`, `esc`, `up`, `down`, `left`, `right`, `backspace`/`bs`, `del`, `space`, `tab`, `shift`, `ctrl`/`control`, `alt`. Single characters (`a`-`z`, `0`-`9`) and Unity `KeyCode` enum names also work.

### holdkey

Press and hold a key for a duration.

```json
{"action":"holdkey", "key":"shift", "duration":1.0}
{"action":"holdkey", "key":"space", "seconds":2.0}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `key` | string | **required** | Key name or alias |
| `duration` / `seconds` | float | 1 | Hold duration |

### holdkeys

Press and hold multiple keys simultaneously.

```json
{"action":"holdkeys", "keys":["ctrl", "a"], "duration":0.5}
{"action":"holdkeys", "keys":["ctrl", "shift", "s"], "duration":0.3}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `keys` | string[] | **required** | Array of key names |
| `duration` / `seconds` | float | 1 | Hold duration |

### swipe

Single-finger swipe gesture.

```json
{"action":"swipe", "direction":"left"}
{"action":"swipe", "direction":"up", "name":"ScrollPanel", "distance":0.3}
{"action":"swipe", "direction":"down", "at":[0.5, 0.5], "duration":0.3}
{"action":"swipe", "direction":"right", "text":"Card"}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | **required** | `"left"`, `"right"`, `"up"`, `"down"` |
| `distance` | float | 0.2 | Normalized screen distance |
| `duration` | float | 0.15 | Swipe duration in seconds |

Without a target, swipes from screen center.

### twofingerswipe

Two-finger swipe gesture.

```json
{"action":"twofingerswipe", "direction":"up"}
{"action":"twofingerswipe", "direction":"left", "distance":0.3, "fingerSpacing":0.05}
{"action":"twofingerswipe", "direction":"down", "at":[0.5, 0.5]}
{"action":"twofingerswipe", "direction":"right", "name":"MapView"}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | **required** | `"left"`, `"right"`, `"up"`, `"down"` |
| `distance` | float | 0.2 | Normalized screen distance |
| `duration` | float | 0.15 | Duration in seconds |
| `fingerSpacing` | float | 0.03 | Spacing between fingers (normalized) |

### scroll

Mouse scroll wheel input.

```json
{"action":"scroll", "name":"ListView", "delta":-120}
{"action":"scroll", "at":[0.5, 0.5], "delta":120}
{"action":"scroll", "name":"Panel", "direction":"down", "amount":0.3}
```

Two modes:

**Delta mode** (mouse wheel units):

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `delta` | float | -120 | Scroll amount (negative = down, positive = up) |

**Direction mode** (on a target element):

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | - | `"up"`, `"down"`, `"left"`, `"right"` |
| `amount` | float | 0.3 | Scroll distance (normalized) |

Without a target, scrolls at screen center.

### scrollto

Scroll a container until a target element becomes visible. Uses drag input.

```json
{"action":"scrollto", "name":"ScrollView", "target":{"text":"TargetItem"}}
{"action":"scrollto", "name":"InventoryList", "target":{"name":"RareItem"}}
{"action":"scrollto", "path":"Canvas/Settings/ScrollView", "target":{"text":"Advanced*"}}
```

| Param | Type | Description |
|-------|------|-------------|
| `target` | object | **Required.** A nested search object (same fields: text, name, type, etc.) |

The container is identified by the outer search fields; the `target` is what to scroll into view.

### drag

Drag elements or between positions.

**Direction-based drag** (on element or from center):

```json
{"action":"drag", "name":"Handle", "direction":[0.3, 0.0]}
{"action":"drag", "direction":[0.0, -0.2], "duration":0.3}
{"action":"drag", "text":"Slider", "direction":[0.5, 0.0], "holdTime":0.1}
```

**From/to drag** (between two elements or positions):

```json
{"action":"drag", "from":{"name":"Source"}, "to":{"name":"Target"}}
{"action":"drag", "from":{"at":[0.2, 0.5]}, "to":{"at":[0.8, 0.5]}}
{"action":"drag", "from":{"text":"Item"}, "to":{"name":"DropZone"}}
{"action":"drag", "name":"Object", "direction":[0.5, 0], "button":"right"}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | `[x, y]` | - | Normalized drag vector |
| `from` / `to` | object | - | Search objects or `at` coordinates |
| `duration` | float | 0.15 | Drag duration in seconds |
| `holdTime` | float | 0.05 | Hold time at start before dragging |
| `button` | string | `"left"` | Mouse button: `"left"`, `"right"`, `"middle"` |

### dropdown

Open a dropdown and select an option.

```json
{"action":"dropdown", "name":"Dropdown", "option":2}
{"action":"dropdown", "name":"Dropdown", "option":"Option Label"}
{"action":"dropdown", "adjacent":"Language:", "option":"English"}
{"action":"dropdown", "text":"Select Country", "option":5}
```

| Param | Type | Description |
|-------|------|-------------|
| `option` | int or string | **Required.** Index (0-based) or label text |

### slider

Set a slider to a normalized value.

```json
{"action":"slider", "name":"VolumeSlider", "value":0.75}
{"action":"slider", "adjacent":"Brightness:", "value":0.5}
{"action":"slider", "name":"VolumeSlider", "value":0.75, "from":0.0}
```

| Param | Type | Description |
|-------|------|-------------|
| `value` | float | **Required.** Target value (0-1 normalized) |
| `from` | float | Optional. If specified, drags from this position to `value` instead of clicking |

### scrollbar

Set a scrollbar to a normalized position.

```json
{"action":"scrollbar", "name":"Scrollbar", "value":0.5}
{"action":"scrollbar", "name":"ContentScroll", "value":1.0}
```

| Param | Type | Description |
|-------|------|-------------|
| `value` | float | **Required.** Position (0-1 normalized) |

### pinch

Two-finger pinch gesture for zoom.

```json
{"action":"pinch", "scale":2.0}
{"action":"pinch", "scale":0.5, "at":[0.5, 0.5], "duration":0.3}
{"action":"pinch", "name":"MapView", "scale":1.5}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `scale` | float | 2 | Scale factor (>1 = zoom in, <1 = zoom out) |
| `duration` | float | 0.15 | Gesture duration |

Without a target or `at`, pinches at screen center.

### rotate

Two-finger rotation gesture.

```json
{"action":"rotate", "degrees":45}
{"action":"rotate", "name":"Dial", "degrees":-90, "duration":0.3}
{"action":"rotate", "at":[0.5, 0.5], "degrees":180, "fingerDistance":0.08}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `degrees` | float | 90 | Rotation angle (positive = clockwise) |
| `duration` | float | 0.15 | Gesture duration |
| `fingerDistance` | float | 0.05 | Distance of fingers from center (normalized) |

Without a target or `at`, rotates at screen center.

---

## Wait / Synchronization Actions

### wait

Static delay.

```json
{"action":"wait", "seconds":1.5}
```

### waitfor

Wait for an element to appear (with optional text match).

```json
{"action":"waitfor", "text":"Loading Complete", "seconds":10}
{"action":"waitfor", "name":"Dialog", "seconds":5}
{"action":"waitfor", "name":"ScoreText", "expected":"100", "seconds":10}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 10 | Timeout |
| `expected` | string | null | If set, waits for the element's text to match this value |

### waitfornot

Wait for an element to disappear.

```json
{"action":"waitfornot", "text":"Loading...", "seconds":10}
{"action":"waitfornot", "name":"Spinner", "seconds":15}
```

### waitfps

Wait for a stable framerate.

```json
{"action":"waitfps", "minFps":20, "stableFrames":5, "timeout":10}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `minFps` | float | 20 | Minimum acceptable FPS |
| `stableFrames` | int | 5 | Consecutive frames above minFps |
| `timeout` | float | 10 | Timeout in seconds |

### waitframerate

Wait until average FPS reaches a threshold.

```json
{"action":"waitframerate", "fps":30, "sampleDuration":2, "timeout":60}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `fps` | int | 30 | Target average FPS |
| `sampleDuration` | float | 2 | Seconds to sample |
| `timeout` | float | 60 | Timeout in seconds |

### scenechange

Wait for a Unity scene transition.

```json
{"action":"scenechange", "seconds":30}
```

---

## Utility Actions

### screenshot

No-op -- a screenshot is captured automatically after every action. Use this explicitly if you want a screenshot without any other action.

```json
{"action":"screenshot"}
```

### snapshot

Captures a full hierarchy snapshot for debugging.

```json
{"action":"snapshot"}
```

---

## Exploration Actions

### randomclick

Click a random clickable element on screen. Optionally filter by search fields.

```json
{"action":"randomclick"}
{"action":"randomclick", "type":"Button"}
{"action":"randomclick", "name":"MenuItem_*"}
```

### autoexplore

Automatically explore the UI by clicking random elements. Three modes available.

**Time-based** (default): explore for a duration.

```json
{"action":"autoexplore", "mode":"time", "seconds":30}
{"action":"autoexplore", "seconds":10, "seed":42, "delay":0.3}
```

**Action-count**: explore for N clicks.

```json
{"action":"autoexplore", "mode":"actions", "count":20}
{"action":"autoexplore", "mode":"actions", "count":50, "seed":42}
```

**Dead-end**: explore until no more clickable elements.

```json
{"action":"autoexplore", "mode":"deadend"}
{"action":"autoexplore", "mode":"deadend", "tryBack":true}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `mode` | string | `"time"` | `"time"`, `"actions"`, or `"deadend"` |
| `seconds` | float | 10 | Duration for time mode |
| `count` | int | 10 | Number of actions for actions mode |
| `seed` | int | null | Random seed for reproducibility |
| `delay` | float | 0.5 | Delay between actions in seconds |
| `tryBack` | bool | false | In deadend mode, try navigating back when stuck |

---

## Advanced Click Actions

### clickslider

Click a slider at a specific normalized position (alternative to `slider` which uses direct value setting).

```json
{"action":"clickslider", "name":"VolumeSlider", "value":0.75}
{"action":"clickslider", "adjacent":"Brightness:", "value":0.5}
```

| Param | Type | Description |
|-------|------|-------------|
| `value` | float | **Required.** Target position (0-1 normalized) |

### scrolltoandclick

Scroll a container until a target is visible, then click it. Combines `scrollto` + `click` in one action.

```json
{"action":"scrolltoandclick", "name":"ItemList", "target":{"text":"Rare Sword"}}
{"action":"scrolltoandclick", "name":"SettingsList", "target":{"text":"Advanced"}}
```

| Param | Type | Description |
|-------|------|-------------|
| `target` | object | **Required.** Nested search object for the element to scroll to and click |

---

## GameObject Manipulation Actions

These actions modify GameObjects directly -- useful for test setup (disabling tutorials, freezing AI, repositioning objects).

### enable

Activate a GameObject (sets active to true, can find inactive objects).

```json
{"action":"enable", "name":"TutorialPanel"}
```

### disable

Deactivate a GameObject (sets active to false).

```json
{"action":"disable", "name":"TutorialPanel"}
{"action":"disable", "name":"AdBanner"}
```

### freeze

Zero velocity and set kinematic on rigidbodies.

```json
{"action":"freeze", "name":"EnemyAI"}
{"action":"freeze", "name":"Player", "includeChildren":false}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `includeChildren` | bool | true | Also freeze child rigidbodies |

### teleport

Move a transform to a world position.

```json
{"action":"teleport", "name":"Player", "position":[100, 0, 50]}
{"action":"teleport", "name":"Camera", "position":[0, 10, -5]}
```

| Param | Type | Description |
|-------|------|-------------|
| `position` | `[x, y, z]` | **Required.** World-space coordinates |

### noclip

Disable all colliders on a GameObject (pass through objects).

```json
{"action":"noclip", "name":"Player"}
{"action":"noclip", "name":"Vehicle", "includeChildren":true}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `includeChildren` | bool | true | Also disable child colliders |

### clip

Enable all colliders on a GameObject (restore collision).

```json
{"action":"clip", "name":"Player"}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `includeChildren` | bool | true | Also enable child colliders |

---

## Inspection Actions

### getvalue

Read a value via static reflection path. The result is logged to the Unity console.

```json
{"action":"getvalue", "path":"GameManager.Instance.Score"}
{"action":"getvalue", "path":"Player.Instance.Health"}
{"action":"getvalue", "path":"GameManager.Instance.IsReady"}
```

| Param | Type | Description |
|-------|------|-------------|
| `path` | string | **Required.** Static reflection path (e.g. `"ClassName.StaticField.Property"`) |

### exists

Check whether an element exists. Succeeds silently if found; optionally throws if not found.

```json
{"action":"exists", "text":"TutorialPopup", "seconds":1}
{"action":"exists", "name":"ErrorDialog", "required":true, "seconds":3}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 1 | Timeout for search |
| `required` | bool | false | If true, throws error when element not found |

---

## Complete Action List

| Action | Description |
|--------|-------------|
| **Click & Tap** | |
| `click` | Single click (supports `index`) |
| `doubleclick` | Double click (supports `index`) |
| `tripleclick` | Triple click (supports `index`) |
| `hold` | Press and hold (supports `index`) |
| `clickslider` | Click slider at normalized position |
| `scrolltoandclick` | Scroll to element and click it |
| `randomclick` | Click random clickable element |
| **Text & Keyboard** | |
| `type` | Type into input field (click to focus, then type) |
| `textinput` | Inject text directly into input field |
| `typetext` | Simulate keyboard chars (no target) |
| `keys` | Type string of chars via keyboard (no target) |
| `key` | Press single key |
| `holdkey` | Hold single key |
| `holdkeys` | Hold multiple keys simultaneously |
| **Drag & Scroll** | |
| `swipe` | Single-finger swipe |
| `twofingerswipe` | Two-finger swipe |
| `scroll` | Mouse scroll wheel |
| `scrollto` | Scroll container until target is visible |
| `drag` | Drag by direction or between from/to (supports `button` for right-click) |
| `dropdown` | Select dropdown option |
| `slider` | Set slider value (direct or drag) |
| `scrollbar` | Set scrollbar value |
| **Gestures** | |
| `pinch` | Two-finger pinch (zoom) |
| `rotate` | Two-finger rotation |
| **Wait & Sync** | |
| `wait` | Static delay |
| `waitfor` | Wait for element to appear |
| `waitfornot` | Wait for element to disappear |
| `waitfps` | Wait for stable framerate |
| `waitframerate` | Wait for target FPS |
| `scenechange` | Wait for scene transition |
| **Exploration** | |
| `autoexplore` | Auto-click random elements (time/count/deadend modes) |
| **GameObject Manipulation** | |
| `enable` | Activate a GameObject |
| `disable` | Deactivate a GameObject |
| `freeze` | Zero velocity + kinematic on rigidbodies |
| `teleport` | Move transform to world position |
| `noclip` | Disable colliders |
| `clip` | Enable colliders |
| **Inspection** | |
| `getvalue` | Read value via static reflection path |
| `exists` | Check if element exists |
| **Utility** | |
| `screenshot` | Capture screenshot (auto after every action) |
| `snapshot` | Capture hierarchy snapshot |

---

## AI Agent Workflow

### Recommended Loop

1. **Observe** -- take a screenshot or snapshot to understand current UI state
2. **Plan** -- decide which element to interact with based on the screenshot
3. **Act** -- send a single JSON action
4. **Verify** -- check `Success` and the post-action screenshot
5. **Repeat** -- continue until the goal is achieved

### Error Handling

If an action fails, `Success` is `false` and `Error` describes the problem:
- `"Could not find ..."` -- element not found within the search timeout
- `"Invalid JSON: ..."` -- malformed JSON
- `"Missing 'action' field"` -- no action specified
- `"Unknown action: '...'"` -- unrecognized action name

**Strategy**: Wait briefly, retry with adjusted search parameters, or try a different targeting approach.

### Search Timeout

When invoked via the bridge (`UIACTION`), the search timeout defaults to **1 second** for fast-fail retry loops. In C# test code, the default is 10 seconds.

### Tips for Reliable Actions

1. **Prefer `text` for buttons** -- most stable and human-readable
2. **Use `adjacent` for form fields** -- finds inputs by their labels
3. **Include punctuation in labels** -- `"Username:"` is more precise than `"Username"`
4. **Wait before acting on dynamic content** -- use `waitfor` before clicking elements that load asynchronously
5. **Use `waitfornot` for loading screens** -- wait for spinners/loading text to disappear
6. **Combine search fields for precision** -- `{"text":"Submit", "type":"Button"}` avoids matching non-button text
7. **Use `scrollto` before clicking hidden items** -- elements in scroll views may be off-screen
8. **Use `at` as a fallback** -- when no other targeting works, use screen coordinates

### Example: Login Flow

```json
{"action":"click", "text":"Login"}
{"action":"waitfor", "name":"LoginPanel", "seconds":5}
{"action":"type", "adjacent":"Username:", "value":"testuser"}
{"action":"type", "adjacent":"Password:", "value":"secret123", "enter":true}
{"action":"waitfor", "text":"Welcome", "seconds":10}
```

### Example: Settings Navigation

```json
{"action":"click", "text":"Settings"}
{"action":"waitfor", "name":"SettingsPanel", "seconds":5}
{"action":"slider", "adjacent":"Volume:", "value":0.5}
{"action":"click", "adjacent":"Notifications"}
{"action":"dropdown", "adjacent":"Language:", "option":"English"}
{"action":"click", "text":"Save"}
```

### Example: Scroll and Select

```json
{"action":"scrollto", "name":"ItemList", "target":{"text":"Rare Sword"}}
{"action":"click", "text":"Rare Sword"}
{"action":"waitfor", "name":"ItemDetails", "seconds":5}
{"action":"click", "text":"Equip"}
```

### Example: Drag and Drop

```json
{"action":"drag", "from":{"name":"InventorySlot_3"}, "to":{"name":"EquipSlot_Weapon"}}
{"action":"waitfor", "name":"EquippedIcon", "seconds":3}
```

### Example: Gesture Interaction

```json
{"action":"pinch", "at":[0.5, 0.5], "scale":2.0}
{"action":"wait", "seconds":0.5}
{"action":"swipe", "direction":"left", "distance":0.3}
{"action":"rotate", "at":[0.5, 0.5], "degrees":45}
```

### Example: Form with Validation

```json
{"action":"type", "adjacent":"Email:", "value":"invalid-email"}
{"action":"click", "text":"Submit"}
{"action":"waitfor", "text":"*invalid*", "seconds":3}
{"action":"type", "adjacent":"Email:", "value":"user@example.com", "clear":true}
{"action":"click", "text":"Submit"}
{"action":"waitfornot", "text":"*invalid*", "seconds":3}
{"action":"waitfor", "text":"Success", "seconds":10}
```

---

## Prompting Guide for AI Systems

This section is for developers building AI agents or prompt pipelines that produce UIACTION JSON. It covers how to structure system prompts, what context to provide, and common pitfalls.

### System Prompt Design

When instructing an LLM to generate actions, your system prompt should include:

1. **The action schema** -- include the Complete Action List table and the search fields table from this doc
2. **The output format constraint** -- tell the model to produce exactly one JSON object per step
3. **The observation loop** -- explain that each action returns a screenshot, and the model should reason about it before choosing the next action

**Minimal system prompt template:**

```
You are a UI automation agent controlling a Unity game. You interact by producing
one JSON action object at a time. After each action, you receive a screenshot of
the result.

## Output Format
Respond with exactly one JSON object per turn. Do not wrap in markdown code fences.
Do not include commentary outside the JSON.

Example: {"action":"click", "text":"Play"}

## Available Actions
[paste the Complete Action List table here]

## Search Fields
To target UI elements, use these fields in your JSON:
- "text": visible text on the element
- "name": GameObject name in the hierarchy  
- "adjacent": finds the input/toggle/slider next to a text label
- "at": [x, y] normalized screen coordinates (0-1), bottom-left origin

## Rules
- Always wait for transitions: use {"action":"waitfor", ...} after navigation
- If an action fails, try a different search field or wait and retry
- Prefer "text" for buttons, "adjacent" for form inputs
- Use "at" only when text/name targeting fails
```

### Providing Screenshots

The model needs visual context to decide what to click. Best practices:

- **Send the screenshot after every action** -- the bridge captures one automatically
- **Include the action result** -- tell the model whether the previous action succeeded or failed and why
- **Resize screenshots for token efficiency** -- 512x512 or 768x768 is usually sufficient for UI recognition
- **Include a `snapshot` early** -- a hierarchy snapshot gives the model exact GameObject names and text, which is far more reliable than reading screenshots visually

**Snapshot-first strategy** (recommended for structured UIs):

```
Turn 1 (system): Here is the current UI hierarchy snapshot:
  Canvas/MainMenu/PlayButton [Button] text="Play"
  Canvas/MainMenu/SettingsButton [Button] text="Settings"
  Canvas/MainMenu/QuitButton [Button] text="Quit"

Turn 1 (assistant): {"action":"click", "text":"Settings"}
```

This avoids OCR errors entirely. The model can reference exact text and names from the snapshot.

### Structured Output vs Free-form

**Structured output** (recommended): Force the model to return only valid JSON. Use JSON mode, function calling, or tool_use to constrain output. This eliminates parsing failures from markdown wrapping, commentary, or multi-action responses.

**Free-form with extraction**: If you can't use structured output, instruct the model to wrap actions in a predictable delimiter and parse the first JSON object from the response.

### One Action Per Turn vs Batching

**One action per turn** (recommended for reactive agents):
- Send one action, get result + screenshot, reason about next step
- More reliable -- the agent can react to unexpected states (popups, loading screens, errors)
- Higher latency (one LLM call per action)

**Batched action plans** (for predictable flows):
- Ask the model to produce a sequence of actions upfront
- Execute them in order, stopping on first failure
- Lower latency but brittle -- any unexpected UI state breaks the remaining plan
- Best for well-known, stable flows (e.g. always-the-same login screen)

### Handling Failures

Teach the model to handle failures explicitly in your system prompt:

```
## When an action fails
If the previous action failed:
1. Read the error message carefully
2. If "Could not find": the element may not be visible yet. Try:
   - {"action":"waitfor", ...} to wait for it to appear
   - {"action":"screenshot"} to see current state
   - A different search field (text vs name vs adjacent)
   - {"action":"scrollto", ...} if the element might be off-screen
3. If the same action fails twice, try a completely different approach
4. Never repeat the exact same failed action more than once
```

### Context Window Management

For long test sessions, the conversation grows quickly with screenshots. Strategies:

- **Summarize history** -- after every N actions, summarize what was accomplished and drop old screenshots
- **Keep only the last 2-3 screenshots** -- older ones are rarely needed
- **Include the session action log** -- a compact text summary of all actions taken so far (the session report provides this)

### Common Prompting Mistakes

| Mistake | Problem | Fix |
|---------|---------|-----|
| No screenshot after each action | Model guesses blindly | Always send the post-action screenshot |
| Asking for multiple actions at once | Model can't react to failures or unexpected UI | One action per turn |
| Not explaining `adjacent` | Model tries `{"action":"type", "text":"Username:"}` (clicks the label) | Explain that `adjacent` finds the input *next to* a label |
| Not explaining coordinate system | Model uses pixel coordinates | Clarify `at` uses 0-1 normalized, bottom-left origin |
| No failure recovery instructions | Model retries the same failed action forever | Include failure handling rules |
| Omitting `waitfor` after navigation | Model clicks elements that haven't loaded yet | Require `waitfor` after scene changes and panel transitions |
| Sending full-resolution screenshots | Wastes tokens, hits context limits | Resize to 512-768px |
| No hierarchy snapshot | Model struggles with elements that have no visible text | Use `{"action":"snapshot"}` and include the result |

### Example: Complete Agent Prompt

```
You are controlling a Unity mobile game to test the settings menu.

## Your Goal
Navigate to Settings, set Volume to 50%, enable Notifications, and save.

## How You Interact
Each turn, you send exactly one JSON action. You then receive:
- Whether it succeeded or failed (with error message)
- A screenshot of the current screen

## Action Format
{"action":"<verb>", ...search fields..., ...params...}

Search fields (combine for precision):
- "text": match visible text
- "name": match GameObject name
- "adjacent": find control next to a label (best for settings/forms)
- "at": [x, y] normalized coords (0=bottom-left, 1=top-right)

Key actions:
- click, type, slider, dropdown, swipe, scroll, scrollto, drag
- waitfor (wait for element), waitfornot (wait for element to disappear)
- wait (static delay), screenshot, snapshot

## Rules
1. Send ONE action per turn, wait for the result
2. After clicking a navigation button, use waitfor to confirm the new screen loaded
3. For sliders/toggles in settings, use "adjacent" to target by label
4. If an action fails, try waitfor first, then a different search approach
5. Respond with only the JSON object, no other text
```

### Token-Efficient Reference

If your system prompt has tight token limits, here is a minimal reference you can include:

```
UIACTION JSON format: {"action":"<verb>", ...target fields...}

Target fields (AND-combined):
  text, name, adjacent, near, type, tag, path, any, at:[x,y]
  direction: for adjacent/near ("right","left","above","below")
  index: 0-based element index for click/doubleclick/tripleclick/hold

Actions:
  click, doubleclick, tripleclick, hold(seconds),
  type(value,clear,enter), textinput(value), typetext(text), keys(text),
  key(key), holdkey(key,duration), holdkeys(keys:[...],duration),
  swipe(direction,distance), twofingerswipe(direction,distance,fingerSpacing),
  scroll(delta | direction+amount), scrollto(target:{...}), scrolltoandclick(target:{...}),
  drag(direction:[x,y] | from/to:{...}, button), dropdown(option),
  slider(value,from), clickslider(value), scrollbar(value),
  pinch(scale), rotate(degrees),
  wait(seconds), waitfor(seconds,expected), waitfornot(seconds),
  waitfps(minFps), waitframerate(fps), scenechange(seconds),
  randomclick, autoexplore(mode,seconds|count,seed,delay),
  enable, disable, freeze(includeChildren), teleport(position:[x,y,z]),
  noclip(includeChildren), clip(includeChildren),
  getvalue(path), exists(seconds,required),
  screenshot, snapshot

Notes:
  - "adjacent" finds the nearest control next to a text label (for forms)
  - "direction" controls search direction for adjacent/near
  - "at" is normalized [0,0]=bottom-left [1,1]=top-right
  - "index" selects Nth match when multiple elements found
  - "button" on drag: "left" (default), "right", "middle"
  - waitfor after navigation; waitfornot for loading screens
  - type clear defaults true; enter defaults false
```
