# JSON Action API Reference

> Reference for tools and AI agents that drive a running Unity game with JSON action objects.

## Overview

An action is a single JSON object with a required `"action"` verb plus target fields and parameters. One object = one action.

```json
{"action":"click", "text":"Settings"}
```

The same dispatcher sits behind every entry point:

| Entry point | Where it runs | Result |
|---|---|---|
| `ActionExecutor.Execute(json)` (C#) | Any build with the SDK | `ActionResult` (below) |
| `clibridge4unity UIACTION <json>` | Unity Editor, Play Mode only | `OK (123ms)` or the error text |
| Remote IDE device route `POST /action` | A device connected to the dashboard (tunnel or local IDE server) | `{"ok":true,"elapsedMs":123}` / `{"ok":false,"error":"…","elapsedMs":123}` |

```csharp
var result = await ActionExecutor.Execute("{\"action\":\"click\", \"text\":\"Settings\"}");
// result.Success, result.Action, result.Error, result.ElapsedMs, result.ScreenshotPath
```

### `ActionResult`

| Field | Type | Description |
|-------|------|-------------|
| `Success` | bool | The action completed without throwing |
| `Action` | string | The JSON that was executed (trimmed) |
| `Error` | string | Failure reason — invalid JSON, missing/unknown action, element not found, timeout |
| `ElapsedMs` | float | Wall-clock time including the post-action screenshot |
| `ScreenshotPath` | string | PNG saved to `Application.persistentDataPath` after every action (also after failures) |

Actions run one at a time. `UIACTION` serialises concurrent calls through a queue.

### Sessions (clibridge4unity)

Wrap a sequence in a session to get a per-action screenshot log and an HTML report (`%TEMP%/clibridge4unity/sessions/<id>/index.html`):

```text
UISESSION start --name MyTest --desc "Navigate to settings and verify"
UIACTION {"action":"click", "text":"Play"}
UIACTION {"action":"waitfor", "text":"Game Over", "seconds":60}
UISESSION stop
```

---

## Element Targeting (Search Fields)

Most actions need to find an element. Targets are given with these fields:

| Field | Type | Matches |
|-------|------|---------|
| `text` | string | Text content of a `Text` / `TMP_Text` **on the element itself** (not its children) |
| `name` | string | GameObject name |
| `type` | string | Component type name, e.g. `"Button"`, `"Slider"`, `"TMP_*"` |
| `tag` | string | Unity tag (exact, no wildcards); on a UI Toolkit element, a USS class |
| `class` | string | UI Toolkit USS class, e.g. `"bbtn"`, `"promotile*"` |
| `path` | string | Hierarchy path, e.g. `"/Canvas/MainMenu/PlayButton"`, `"*/Panel/Button*"` |
| `near` | string | Nearest interactable to a text label (pure distance) |
| `adjacent` | string | Best-aligned interactable next to a text label |
| `any` | string | Name, text, sprite/texture name, or hierarchy path |
| `direction` | string | Direction for `near` / `adjacent`: `"right"`, `"left"`, `"above"`/`"up"`, `"below"`/`"down"` |
| `index` | int | 0-based pick when several elements match (`click`, `doubleclick`, `tripleclick`, `hold`) |

Pattern matching is case-insensitive. An element must be on screen to match.

**Combining fields.** Every search field in the object is AND-combined into one search — `near`, `adjacent` and `any` included. For example `{"type":"Toggle", "adjacent":"Music"}` matches only when the element adjacent to the "Music" label is a Toggle, and `{"any":"*Play*", "tag":"MainMenu"}` needs both. `near` also orders the matches by distance to its label.

```json
{"action":"click", "name":"btn_*", "type":"Button"}
```

Because `text` only matches the element carrying the text, `{"text":"Submit", "type":"Button"}` finds nothing on a standard UGUI button (the label is a child). Use `text` alone — the tap lands on the button under the label — or target the button by `name`.

### UI Toolkit

Elements of every live `UIDocument` panel are searched alongside uGUI with the same fields: `name` is the `VisualElement` name (`{"name":"back-button"}`), `type` is the element type or any base type (`"Button"`, `"Label"`, `"TextElement"`), `text` is a `Label` / `Button` / `TextElement`'s text, and `class` / `tag` match USS classes. A `Button` with no text of its own takes the text of the labels inside it, and matches instead of them, so `{"text":"REPAIR"}` finds the button once. Only elements a player could tap count: displayed, visible, enabled, not fully transparent and on screen, and picking-enabled (or a picking-ignore container holding such an element). The tap is a normal injected pointer at the element's on-screen centre, clipped to any `ScrollView` it is scrolled inside. In the hierarchy dump the elements sit under a `UI Toolkit` root, one child per panel.

### Screen coordinates (`at`)

`at` is not a search field: it replaces the target with a screen position. It is accepted by `click`, `doubleclick`, `tripleclick`, `hold`, `swipe`, `twofingerswipe`, `scroll`, `pinch`, `rotate`, and inside `drag`'s `from` / `to`.

Values are **normalized**, with Unity's bottom-left origin:
- `[0, 0]` = bottom-left
- `[1, 1]` = top-right
- `[0.5, 0.5]` = screen center

### Wildcard Patterns

String fields other than `tag` support `*` wildcards and `|` alternatives:

```json
[
  {"action":"click", "text":"Play*"},
  {"action":"click", "name":"btn_*"},
  {"action":"click", "text":"OK|Confirm|Accept"},
  {"action":"click", "any":"*Settings*"}
]
```

### Choosing the Right Search Field

| Scenario | Recommended | Example |
|----------|-------------|---------|
| Button/label with visible text | `text` | `{"text":"Play Game"}` |
| Element with a known hierarchy name | `name` | `{"name":"SettingsPanel"}` |
| Input field next to a label | `adjacent` | `{"adjacent":"Username:"}` |
| Closest element to a reference label | `near` | `{"near":"Settings"}` |
| No clear identifier | `at` | `{"at":[0.5, 0.3]}` |
| Broad match across name/text/sprite/path | `any` | `{"any":"*volume*"}` |

---

## Adjacent vs Near

Both find an element relative to a text label.

### `adjacent`

Finds the **single best-scoring interactable** (Button, InputField, Dropdown, Slider, Toggle, or any Selectable) beside a label, using **alignment-weighted scoring** that strongly prefers the same row/column. Default direction: right.

Best for: form layouts, settings screens, any label + control pair.

```json
[
  {"action":"click", "adjacent":"Remember me"},
  {"action":"type", "adjacent":"Username:", "value":"admin"},
  {"action":"slider", "adjacent":"Volume:", "value":0.8},
  {"action":"dropdown", "adjacent":"Language:", "option":"English"},
  {"action":"click", "adjacent":"Actions", "direction":"below"},
  {"action":"type", "adjacent":"Description", "direction":"below", "value":"text"}
]
```

### `near`

Finds the **nearest interactable** by Euclidean distance, with no alignment preference. Without `direction` it searches all directions.

Best for: diagonal/irregular layouts, when the element isn't strictly beside the label.

```json
[
  {"action":"click", "near":"Settings"},
  {"action":"click", "near":"Audio", "direction":"below"}
]
```

| | `adjacent` | `near` |
|---|---|---|
| Scoring | Alignment-weighted (row/column preference) | Pure Euclidean distance |
| Default direction | Right | All directions |
| Best for | Form fields next to labels | Irregular layouts |

---

## Actions Reference

### click

Single click / tap.

```json
[
  {"action":"click", "text":"Settings"},
  {"action":"click", "name":"SettingsBtn"},
  {"action":"click", "at":[0.5, 0.5]},
  {"action":"click", "adjacent":"Enable Sounds"},
  {"action":"click", "any":"*play*"},
  {"action":"click", "text":"Item", "index":2}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `index` | int | 0 | When several elements match, click the Nth one (0-based) |

### doubleclick

```json
[
  {"action":"doubleclick", "text":"Item"},
  {"action":"doubleclick", "at":[0.5, 0.5]},
  {"action":"doubleclick", "text":"ListItem", "index":1}
]
```

Supports `index`.

### tripleclick

```json
[
  {"action":"tripleclick", "name":"TextArea"},
  {"action":"tripleclick", "at":[0.3, 0.7]}
]
```

Supports `index`.

### hold

Press and hold.

```json
[
  {"action":"hold", "text":"Delete", "seconds":2},
  {"action":"hold", "at":[0.5, 0.5], "seconds":1.5}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 1 | Hold duration |
| `index` | int | 0 | Pick among several matches |

### type

Taps the field to focus it, then sets its text. The matched element must carry a `TMP_InputField` or legacy `InputField` (text is written to the field directly — `TMP_InputField` does not consume Input System text events).

```json
[
  {"action":"type", "name":"InputField", "value":"hello world"},
  {"action":"type", "adjacent":"Password:", "value":"secret123", "enter":true},
  {"action":"type", "name":"SearchBox", "value":" more", "clear":false}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `value` | string | `""` | Text to enter |
| `clear` | bool | `true` | Replace existing text; `false` appends |
| `enter` | bool | `false` | Press Enter afterwards |

### textinput

Like `type` with `clear: true` and no Enter; looks for a `TMP_InputField` first, then a legacy `InputField`.

```json
{"action":"textinput", "adjacent":"Comment:", "value":"some text"}
```

### typetext

Queues Input System text events for each character, with no target. Reaches `Keyboard.onTextInput` listeners — not `TMP_InputField` (use `type` for that).

```json
{"action":"typetext", "text":"Hello World"}
```

`value` is accepted as an alias for `text`.

### keys

Presses the key for each character in turn (letters, digits, space and common punctuation; other characters are skipped).

```json
{"action":"keys", "text":"abc123"}
```

`value` is accepted as an alias for `text`.

### key

Press and release one key.

```json
[
  {"action":"key", "key":"enter"},
  {"action":"key", "key":"escape"},
  {"action":"key", "key":"space"},
  {"action":"key", "key":"a"},
  {"action":"key", "key":"F1"}
]
```

Accepted names: a single character, any Unity `KeyCode` name (case-insensitive: `space`, `tab`, `escape`, `return`, `f1`, `leftshift`, …) and the aliases `enter`, `esc`, `up`, `down`, `left`, `right`, `backspace`/`bs`, `del`, `space`, `tab`, `shift`, `ctrl`/`control`, `alt`. An unrecognised name — or a `KeyCode` with no Input System equivalent — fails the action with an error naming the key.

### holdkey

Hold one key for a duration.

```json
[
  {"action":"holdkey", "key":"shift", "duration":1.0},
  {"action":"holdkey", "key":"space", "seconds":2.0}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `key` | string | **required** | Key name |
| `duration` / `seconds` | float | 1 | Hold duration |

Key names are the same as for `key`. An unknown name fails with `Unknown key: '…'`.

### holdkeys

Hold several keys together (same key names as `holdkey`).

```json
[
  {"action":"holdkeys", "keys":["ctrl", "a"], "duration":0.5},
  {"action":"holdkeys", "keys":["ctrl", "shift", "s"], "duration":0.3}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `keys` | string[] | **required** | Key names |
| `duration` / `seconds` | float | 1 | Hold duration |

### swipe

Single-finger swipe. Without a target or `at`, swipes from screen center.

```json
[
  {"action":"swipe", "direction":"left"},
  {"action":"swipe", "direction":"up", "name":"ScrollPanel", "distance":0.3},
  {"action":"swipe", "direction":"down", "at":[0.5, 0.5], "duration":0.3}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | `"left"` | `"left"`, `"right"`, `"up"`, `"down"` |
| `distance` | float | 0.2 | Normalized screen distance (0-1) |
| `duration` | float | 0.15 | Seconds |

### twofingerswipe

```json
[
  {"action":"twofingerswipe", "direction":"up"},
  {"action":"twofingerswipe", "direction":"left", "distance":0.3, "fingerSpacing":0.05},
  {"action":"twofingerswipe", "direction":"right", "name":"MapView"}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | `"up"` | `"left"`, `"right"`, `"up"`, `"down"` |
| `distance` | float | 0.2 | Normalized screen distance |
| `duration` | float | 0.15 | Seconds |
| `fingerSpacing` | float | 0.03 | Normalized spacing between the fingers |

### scroll

```json
[
  {"action":"scroll", "name":"ListView", "delta":-120},
  {"action":"scroll", "at":[0.5, 0.5], "delta":120},
  {"action":"scroll", "name":"Panel", "direction":"down", "amount":0.3}
]
```

**Delta mode** (mouse wheel) — used unless both `direction` and a target are given. Without a target or `at`, scrolls at screen center.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `delta` | float | -120 | Wheel amount (negative = down) |

**Direction mode** (requires a target):

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | string | — | `"up"`, `"down"`, `"left"`, `"right"` |
| `amount` | float | 0.3 | Normalized scroll distance |

### scrollto

Drag-scroll a container until a target is visible (up to 20 attempts). The outer fields identify the container; `target` is a nested search object.

```json
[
  {"action":"scrollto", "name":"ScrollView", "target":{"text":"TargetItem"}},
  {"action":"scrollto", "path":"*/Settings/ScrollView", "target":{"text":"Advanced*"}}
]
```

### scrolltoandclick

`scrollto`, then click the target.

```json
{"action":"scrolltoandclick", "name":"ItemList", "target":{"text":"Rare Sword"}}
```

### drag

**Direction drag** — from the target (or screen center when there is no target) by a normalized offset (`[0.3, 0]` = 30% of the screen width to the right):

```json
[
  {"action":"drag", "name":"Handle", "direction":[0.3, 0.0]},
  {"action":"drag", "direction":[0.0, -0.2], "duration":0.3},
  {"action":"drag", "name":"Object", "direction":[0.5, 0], "button":"right"}
]
```

**From/to drag** — both `from` and `to` are required. Give both as search objects, or both as `at` positions:

```json
[
  {"action":"drag", "from":{"name":"Source"}, "to":{"name":"Target"}},
  {"action":"drag", "from":{"at":[0.2, 0.5]}, "to":{"at":[0.8, 0.5]}},
  {"action":"drag", "from":{"text":"Item"}, "to":{"name":"DropZone"}}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `direction` | `[x, y]` | — | Normalized drag offset |
| `from` / `to` | object | — | Search objects, or `{"at":[x, y]}` |
| `duration` | float | 0.15 | Seconds |
| `holdTime` | float | 0.05 | Hold at the start before moving |
| `button` | string | `"left"` | `"left"`, `"right"`, `"middle"` (ignored for a center drag with no target) |

### dropdown

Open a dropdown and pick an option.

```json
[
  {"action":"dropdown", "name":"Dropdown", "option":2},
  {"action":"dropdown", "adjacent":"Language:", "option":"English"}
]
```

| Param | Type | Description |
|-------|------|-------------|
| `option` | int or string | **Required.** 0-based index or option label |

### slider

Tap a slider at a normalized position, or drag from one position to another.

```json
[
  {"action":"slider", "name":"VolumeSlider", "value":0.75},
  {"action":"slider", "name":"VolumeSlider", "value":0.75, "from":0.0}
]
```

| Param | Type | Description |
|-------|------|-------------|
| `value` | float | **Required.** Target position (0-1) |
| `from` | float | Optional. Drag from this position to `value` instead of tapping |

### clickslider

Tap a slider at a normalized position (same as `slider` without `from`).

```json
{"action":"clickslider", "adjacent":"Brightness:", "value":0.5}
```

### scrollbar

Tap a scrollbar at a normalized position.

```json
{"action":"scrollbar", "name":"Scrollbar", "value":0.5}
```

### pinch

Two-finger pinch. Without a target or `at`, pinches at screen center.

```json
[
  {"action":"pinch", "scale":2.0},
  {"action":"pinch", "scale":0.5, "at":[0.5, 0.5], "duration":0.3},
  {"action":"pinch", "name":"MapView", "scale":1.5}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `scale` | float | 2 | >1 zooms in, <1 zooms out |
| `duration` | float | 0.15 | Seconds |

### rotate

Two-finger rotation. Without a target or `at`, rotates at screen center.

```json
[
  {"action":"rotate", "degrees":45},
  {"action":"rotate", "name":"Dial", "degrees":-90, "duration":0.3},
  {"action":"rotate", "at":[0.5, 0.5], "degrees":180, "fingerDistance":0.08}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `degrees` | float | 90 | Positive = clockwise |
| `duration` | float | 0.15 | Seconds |
| `fingerDistance` | float | 0.05 | Normalized finger distance from center (used with `at`) |

---

## Wait / Synchronization Actions

### wait

Fixed delay (default 1 second).

```json
{"action":"wait", "seconds":1.5}
```

### waitfor

Wait for an element to appear, optionally until its text equals `expected`.

```json
[
  {"action":"waitfor", "text":"Loading Complete", "seconds":10},
  {"action":"waitfor", "name":"ScoreText", "expected":"100", "seconds":10}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 10 | Timeout |
| `expected` | string | — | Wait for the element's text to match |

### waitfornot

Wait for an element to disappear.

```json
{"action":"waitfornot", "name":"Spinner", "seconds":15}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 10 | Timeout |

### waitfps

Wait for a stable frame rate.

```json
{"action":"waitfps", "minFps":20, "stableFrames":5, "timeout":10}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `minFps` | float | 20 | Minimum acceptable FPS |
| `stableFrames` | int | 5 | Consecutive frames at or above `minFps` |
| `timeout` | float | 10 | Seconds |

### waitframerate

Wait until the average FPS over a sample window reaches a target.

```json
{"action":"waitframerate", "fps":30, "sampleDuration":2, "timeout":60}
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `fps` | int | 30 | Target average FPS |
| `sampleDuration` | float | 2 | Seconds per sample |
| `timeout` | float | 60 | Seconds |

### scenechange

Wait for the active scene to change.

```json
{"action":"scenechange", "seconds":30}
```

---

## Utility Actions

### screenshot

No-op — a screenshot is captured after every action anyway. Use it to grab one without doing anything else.

```json
{"action":"screenshot"}
```

### snapshot

Records a detailed UI hierarchy snapshot (component properties, text, bounds) into the active diagnostics session (`TestReport`), viewable in the Diagnostics Viewer. It is not returned in the action result, and logs a warning when no session is active.

```json
{"action":"snapshot"}
```

---

## Exploration Actions

### randomclick

Click a random clickable element, optionally limited by search fields.

```json
[
  {"action":"randomclick"},
  {"action":"randomclick", "type":"Button"},
  {"action":"randomclick", "name":"MenuItem_*"}
]
```

### autoexplore

Click random elements.

```json
[
  {"action":"autoexplore", "mode":"time", "seconds":30},
  {"action":"autoexplore", "seconds":10, "seed":42, "delay":0.3},
  {"action":"autoexplore", "mode":"actions", "count":50, "seed":42},
  {"action":"autoexplore", "mode":"deadend", "tryBack":true}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `mode` | string | `"time"` | `"time"`, `"actions"`, or `"deadend"` (until nothing is clickable) |
| `seconds` | float | 10 | Duration for `time` mode (`value` is an alias) |
| `count` | int | 10 | Clicks for `actions` mode (`value` is an alias) |
| `seed` | int | — | Random seed for reproducibility |
| `delay` | float | 0.5 | Seconds between clicks |
| `tryBack` | bool | false | `deadend` mode: try navigating back when stuck |

---

## GameObject Manipulation Actions

Modify GameObjects directly — useful for test setup (hiding tutorials, freezing AI, repositioning). The C# API returns restore tokens; JSON actions do not restore.

```json
[
  {"action":"enable", "name":"TutorialPanel"},
  {"action":"disable", "name":"AdBanner"},
  {"action":"freeze", "name":"EnemyAI"},
  {"action":"freeze", "name":"Player", "includeChildren":false},
  {"action":"teleport", "name":"Player", "position":[100, 0, 50]},
  {"action":"noclip", "name":"Player"},
  {"action":"clip", "name":"Player"}
]
```

| Action | Effect | Params |
|--------|--------|--------|
| `enable` | `SetActive(true)` (finds inactive objects) | — |
| `disable` | `SetActive(false)` | — |
| `freeze` | Zero velocity + kinematic on rigidbodies (3D and 2D) | `includeChildren` (default true) |
| `teleport` | Move the transform to a world position | `position: [x, y, z]` (required) |
| `noclip` | Disable colliders (3D and 2D) | `includeChildren` (default true) |
| `clip` | Enable colliders | `includeChildren` (default true) |

---

## Inspection Actions

### getvalue

Read a value through a static reflection path. The value is written to the Unity console (`[GetValue] path = value`); it is not returned in the action result.

```json
[
  {"action":"getvalue", "path":"GameManager.Instance.Score"},
  {"action":"getvalue", "path":"GameManager.Instance.IsReady"}
]
```

| Param | Type | Description |
|-------|------|-------------|
| `path` | string | **Required.** `TypeName.StaticMember.Member…` |

### exists

Check whether an element exists. Succeeds either way unless `required` is true.

```json
[
  {"action":"exists", "text":"TutorialPopup", "seconds":1},
  {"action":"exists", "name":"ErrorDialog", "required":true, "seconds":3}
]
```

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `seconds` | float | 1 | Search timeout |
| `required` | bool | false | Fail when the element is not found |

---

## Complete Action List

| Action | Description |
|--------|-------------|
| **Click & Tap** | |
| `click` | Single click (supports `index`, `at`) |
| `doubleclick` | Double click (supports `index`, `at`) |
| `tripleclick` | Triple click (supports `index`, `at`) |
| `hold` | Press and hold (supports `index`, `at`) |
| `clickslider` | Tap a slider at a normalized position |
| `scrolltoandclick` | Scroll to an element and click it |
| `randomclick` | Click a random clickable element |
| **Text & Keyboard** | |
| `type` | Focus an input field and set its text |
| `textinput` | Set an input field's text (clears first) |
| `typetext` | Input System text events, no target |
| `keys` | Press a key per character, no target |
| `key` | Press one key |
| `holdkey` | Hold one key |
| `holdkeys` | Hold several keys together |
| **Drag & Scroll** | |
| `swipe` | Single-finger swipe |
| `twofingerswipe` | Two-finger swipe |
| `scroll` | Mouse wheel, or directional scroll on a target |
| `scrollto` | Scroll a container until the target is visible |
| `drag` | Drag by offset or from/to (supports `button`) |
| `dropdown` | Select a dropdown option |
| `slider` | Tap or drag a slider |
| `scrollbar` | Tap a scrollbar |
| **Gestures** | |
| `pinch` | Two-finger pinch |
| `rotate` | Two-finger rotation |
| **Wait & Sync** | |
| `wait` | Fixed delay |
| `waitfor` | Wait for an element (optionally its text) |
| `waitfornot` | Wait for an element to disappear |
| `waitfps` | Wait for a stable frame rate |
| `waitframerate` | Wait for a target average FPS |
| `scenechange` | Wait for a scene change |
| **Exploration** | |
| `autoexplore` | Random clicking (time / count / dead-end modes) |
| **GameObject Manipulation** | |
| `enable` / `disable` | Activate / deactivate |
| `freeze` | Zero velocity + kinematic |
| `teleport` | Move to a world position |
| `noclip` / `clip` | Disable / enable colliders |
| **Inspection** | |
| `getvalue` | Log a value read via a static path |
| `exists` | Check whether an element exists |
| **Utility** | |
| `screenshot` | Screenshot only (one is taken after every action) |
| `snapshot` | Hierarchy snapshot into the diagnostics session |

---

## AI Agent Workflow

### Recommended Loop

1. **Observe** — look at the latest screenshot (and the UI hierarchy if you have it)
2. **Plan** — choose the element to interact with
3. **Act** — send one JSON action
4. **Verify** — check `Success` / `Error` and the post-action screenshot
5. **Repeat** until the goal is reached

### Error Handling

When an action fails, `Success` is `false` and `Error` explains why:
- `"<Action>(…) failed: Element not found within Ns"` — no match within the search timeout (with a diagnostics session active the message is a fuller diagnostic report)
- `"Invalid JSON: …"` — malformed JSON
- `"Missing 'action' field"` — no verb
- `"Unknown action: '…'. Valid: …"` — unrecognised verb
- `"No target specified. Use one or more of: …"` — the action needs a target and none was given

**Strategy:** wait briefly, retry with different search fields, or target differently.

### Search Timeout

`ActionExecutor.DefaultSearchTime` controls how long a search waits for a match. It defaults to **10 seconds**; the `UIACTION` bridge command sets it to **1 second** (for fast-fail retry loops), and the setting is global — it stays at 1 second for that Play session.

### Tips for Reliable Actions

1. **Prefer `text` for buttons** — stable and human-readable
2. **Use `adjacent` for form fields** — finds inputs by their labels
3. **Include punctuation in labels** — `"Username:"` is more precise than `"Username"`
4. **Wait before acting on dynamic content** — `waitfor` before clicking things that load asynchronously
5. **Use `waitfornot` for loading screens** — wait for spinners to disappear
6. **Combine `name` / `type` / `tag` / `path` for precision** — remember `text` only matches the element that holds the text
7. **Use `scrollto` for items in scroll views** — off-screen elements don't match
8. **Use `at` as a fallback** — when nothing else identifies the element

### Example: Login Flow

```json
[
  {"action":"click", "text":"Login"},
  {"action":"waitfor", "name":"LoginPanel", "seconds":5},
  {"action":"type", "adjacent":"Username:", "value":"testuser"},
  {"action":"type", "adjacent":"Password:", "value":"secret123", "enter":true},
  {"action":"waitfor", "text":"Welcome", "seconds":10}
]
```

### Example: Settings Navigation

```json
[
  {"action":"click", "text":"Settings"},
  {"action":"waitfor", "name":"SettingsPanel", "seconds":5},
  {"action":"slider", "adjacent":"Volume:", "value":0.5},
  {"action":"click", "adjacent":"Notifications"},
  {"action":"dropdown", "adjacent":"Language:", "option":"English"},
  {"action":"click", "text":"Save"}
]
```

### Example: Scroll and Select

```json
[
  {"action":"scrollto", "name":"ItemList", "target":{"text":"Rare Sword"}},
  {"action":"click", "text":"Rare Sword"},
  {"action":"waitfor", "name":"ItemDetails", "seconds":5},
  {"action":"click", "text":"Equip"}
]
```

### Example: Drag and Drop

```json
[
  {"action":"drag", "from":{"name":"InventorySlot_3"}, "to":{"name":"EquipSlot_Weapon"}},
  {"action":"waitfor", "name":"EquippedIcon", "seconds":3}
]
```

### Example: Gesture Interaction

```json
[
  {"action":"pinch", "at":[0.5, 0.5], "scale":2.0},
  {"action":"wait", "seconds":0.5},
  {"action":"swipe", "direction":"left", "distance":0.3},
  {"action":"rotate", "at":[0.5, 0.5], "degrees":45}
]
```

### Example: Form with Validation

```json
[
  {"action":"type", "adjacent":"Email:", "value":"invalid-email"},
  {"action":"click", "text":"Submit"},
  {"action":"waitfor", "text":"*invalid*", "seconds":3},
  {"action":"type", "adjacent":"Email:", "value":"user@example.com", "clear":true},
  {"action":"click", "text":"Submit"},
  {"action":"waitfornot", "text":"*invalid*", "seconds":3},
  {"action":"waitfor", "text":"Success", "seconds":10}
]
```

The examples are arrays for readability — send each object as its own action.

---

## Prompting Guide for AI Systems

For developers building agents or prompt pipelines that produce action JSON.

### System Prompt Design

Include:

1. **The action schema** — the Complete Action List and the search fields table from this doc
2. **The output format** — exactly one JSON object per step
3. **The observation loop** — each action returns a result and a screenshot; reason about them before the next action

**Minimal system prompt template:**

```text
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
- "text": text on the element itself (a button's label, not the button)
- "name": GameObject name in the hierarchy
- "adjacent": finds the input/toggle/slider next to a text label
- "at": [x, y] normalized screen coordinates (0-1), bottom-left origin

## Rules
- Always wait for transitions: use {"action":"waitfor", ...} after navigation
- If an action fails, try a different search field or wait and retry
- Prefer "text" for buttons, "adjacent" for form inputs
- Use "at" only when text/name targeting fails
```

### Providing Context

- **Send the screenshot after every action** — `ScreenshotPath` points at it
- **Include the action result** — success or the error message
- **Resize screenshots** — 512–768 px is usually enough for UI recognition
- **Give the model the hierarchy when you can** — exact GameObject names and texts (for example from the Remote IDE hierarchy panel) are far more reliable than reading pixels

**Hierarchy-first strategy** (recommended for structured UIs):

```text
Turn 1 (system): Current UI hierarchy:
  Canvas/MainMenu/PlayButton [Button] text="Play"
  Canvas/MainMenu/SettingsButton [Button] text="Settings"
  Canvas/MainMenu/QuitButton [Button] text="Quit"

Turn 1 (assistant): {"action":"click", "text":"Settings"}
```

### Structured Output vs Free-form

**Structured output** (recommended): constrain the model to valid JSON with JSON mode, function calling or tool use — no markdown wrapping, commentary or multi-action replies to parse around.

**Free-form with extraction**: if you can't constrain output, ask for a predictable delimiter and parse the first JSON object.

### One Action Per Turn vs Batching

**One action per turn** (recommended for reactive agents): the agent reacts to popups, loading screens and errors, at the cost of one LLM call per action.

**Batched plans** (predictable flows): produce a sequence up front and stop on the first failure. Lower latency, but any unexpected UI state breaks the rest of the plan — best for stable flows like a fixed login screen.

### Handling Failures

```text
## When an action fails
1. Read the error message
2. If "Element not found": the element may not be visible yet. Try:
   - {"action":"waitfor", ...} to wait for it
   - {"action":"screenshot"} to see the current state
   - A different search field (text vs name vs adjacent)
   - {"action":"scrollto", ...} if it may be off-screen
3. If the same action fails twice, try a completely different approach
4. Never repeat the exact same failed action more than once
```

### Context Window Management

- **Summarize history** every N actions and drop old screenshots
- **Keep only the last 2–3 screenshots**
- **Keep a compact action log** (a `UISESSION` report provides one)

### Common Prompting Mistakes

| Mistake | Problem | Fix |
|---------|---------|-----|
| No screenshot after each action | Model guesses blindly | Always send the post-action screenshot |
| Asking for several actions at once | Model can't react to failures | One action per turn |
| Not explaining `adjacent` | Model sends `{"action":"type", "text":"Username:"}` (targets the label) | Explain that `adjacent` finds the input next to a label |
| Not explaining coordinates | Model sends pixels or assumes a top-left origin | `at` is 0-1 normalized, bottom-left origin |
| No failure-recovery rules | Model retries the same failure forever | Include failure-handling rules |
| No `waitfor` after navigation | Model clicks elements that haven't loaded | Require `waitfor` after scene/panel changes |
| Full-resolution screenshots | Wastes tokens | Resize to 512–768 px |

### Example: Complete Agent Prompt

```text
You are controlling a Unity mobile game to test the settings menu.

## Your Goal
Navigate to Settings, set Volume to 50%, enable Notifications, and save.

## How You Interact
Each turn, you send exactly one JSON action. You then receive:
- Whether it succeeded or failed (with error message)
- A screenshot of the current screen

## Action Format
{"action":"<verb>", ...search fields..., ...params...}

Search fields:
- "text": match text on the element itself
- "name": match GameObject name
- "adjacent": find the control next to a label (best for settings/forms)
- "at": [x, y] normalized coords (0,0 = bottom-left, 1,1 = top-right)

Key actions:
- click, type, slider, dropdown, swipe, scroll, scrollto, drag
- waitfor (wait for element), waitfornot (wait for element to disappear)
- wait (fixed delay), screenshot

## Rules
1. Send ONE action per turn, wait for the result
2. After clicking a navigation button, use waitfor to confirm the new screen loaded
3. For sliders/toggles in settings, use "adjacent" to target by label
4. If an action fails, try waitfor first, then a different search approach
5. Respond with only the JSON object, no other text
```

### Token-Efficient Reference

```text
Action JSON: {"action":"<verb>", ...target fields...}

Target fields:
  text, name, type, tag, path  (AND-combined; text = text on the element itself)
  near, adjacent  (relative to a text label; ignore text/name/type)
  any  (name/text/sprite/path; replaces other fields)
  direction: for adjacent/near ("right","left","above","below")
  index: 0-based match index for click/doubleclick/tripleclick/hold
  at:[x,y] normalized, bottom-left origin (click, doubleclick, tripleclick, hold,
         swipe, twofingerswipe, scroll, pinch, rotate, drag from/to)

Actions:
  click, doubleclick, tripleclick, hold(seconds),
  type(value,clear,enter), textinput(value), typetext(text), keys(text),
  key(key), holdkey(key,duration), holdkeys(keys:[...],duration),
  swipe(direction,distance,duration), twofingerswipe(direction,distance,fingerSpacing),
  scroll(delta | direction+amount), scrollto(target:{...}), scrolltoandclick(target:{...}),
  drag(direction:[dx,dy] | from:{...},to:{...}, button), dropdown(option),
  slider(value,from), clickslider(value), scrollbar(value),
  pinch(scale), rotate(degrees),
  wait(seconds), waitfor(seconds,expected), waitfornot(seconds),
  waitfps(minFps,stableFrames,timeout), waitframerate(fps), scenechange(seconds),
  randomclick, autoexplore(mode,seconds|count,seed,delay,tryBack),
  enable, disable, freeze(includeChildren), teleport(position:[x,y,z]),
  noclip(includeChildren), clip(includeChildren),
  getvalue(path), exists(seconds,required),
  screenshot, snapshot

Notes:
  - drag direction is a normalized offset ([0.3,0] = 30% of screen width)
  - "button" on drag: "left" (default), "right", "middle"
  - waitfor after navigation; waitfornot for loading screens
  - type clear defaults true; enter defaults false
```
