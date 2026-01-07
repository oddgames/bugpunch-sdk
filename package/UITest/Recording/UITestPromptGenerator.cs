using System.Text;
using UnityEngine;

namespace ODDGames.UITest
{
    public static class UITestPromptGenerator
    {
        public static string GeneratePrompt(UITestRecordingData recording)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# UI Test Recording");
            sb.AppendLine();
            sb.AppendLine($"- **Recorded At:** {recording.recordedAt}");
            sb.AppendLine($"- **Duration:** {recording.totalDuration:F1} seconds");
            sb.AppendLine($"- **Steps:** {recording.steps.Count}");
            if (!string.IsNullOrEmpty(recording.testDataSourcePath))
            {
                sb.AppendLine($"- **Test Data:** {recording.testDataSourcePath}");
            }
            sb.AppendLine();
            sb.AppendLine("## Recorded Steps Summary");
            sb.AppendLine();

            for (int i = 0; i < recording.steps.Count; i++)
            {
                var step = recording.steps[i];
                sb.AppendLine($"{i + 1}. [{step.timestamp:F1}s] {FormatStep(step)}");
            }

            return sb.ToString();
        }

        public static string GetDocumentation()
        {
            return @"# UITestBehaviour Framework Documentation

## Overview

UITestBehaviour is a Unity UI testing framework that enables automated UI testing through code. Tests inherit from `UITestBehaviour` and implement the `Test()` method.

## Class Structure

### Required Attribute

Every test class MUST have the `[UITest]` attribute:

```csharp
[UITest(
    Scenario = 1,                           // REQUIRED: Unique test scenario number (must be > 0)
    Feature = ""Feature Name"",               // Category/feature being tested
    Story = ""Story description"",            // User story or test purpose
    Severity = TestSeverity.Normal,         // Blocker, Critical, Normal, Minor, Trivial
    Tags = new[] { ""Tag1"", ""Tag2"" },        // Test tags for filtering
    Description = ""Test description"",       // Detailed test description
    Owner = ""Team Name"",                    // Test owner/maintainer
    TimeoutSeconds = 180,                   // Test timeout (default: 180)
    DataMode = TestDataMode.UseDefined      // UseDefined (use bundled), UseCurrent (use existing), Ask (prompt user)
)]
public class MyTest : UITestBehaviour
{
    protected override async UniTask Test()
    {
        // Test implementation
    }
}
```

### Inheritance

```
UITestBehaviour (base class)
    └── YourTestClass
```

## Core Methods

### Click Methods

```csharp
// Basic click - searches by name, path, or text content
await Click(""ButtonName"");
await Click(""*partial*"");              // Wildcard: contains 'partial'
await Click(""prefix*"");                // Wildcard: starts with 'prefix'
await Click(""*suffix"");                // Wildcard: ends with 'suffix'

// Click with options
await Click(""search"", throwIfMissing: true, searchTime: 10);
await Click(""search"", repeat: 3, delay: 0.5f);  // Click 3 times with delay

// Click with multiple search patterns (finds first match)
await Click(new[] { ""Option1"", ""Option2"" });

// Click any matching element
await ClickAny(""search"", seconds: 10);

// Click center of screen (no target)
await Click();
```

### Wait Methods

```csharp
// Simple delay
await Wait(5);                           // Wait 5 seconds

// Wait for UI element to appear
await Wait(""ElementName"", seconds: 10);
await Wait(new[] { ""Element1"", ""Element2"" }, 10);

// Wait for condition (VERY IMPORTANT for state changes)
await WaitFor(() => someCondition, seconds: 60, description: ""describe what you wait for"");

// Example: Wait for game state
await WaitFor(() => GameManager.CurrentPhase == GamePhase.Playing, 30, ""game to enter Playing phase"");
```

### Hold/Long Press

```csharp
// Hold element for duration
await Hold(""ButtonName"", seconds: 2.0f);
await Hold(""search"", 3.0f, throwIfMissing: true, searchTime: 10);
```

### Drag/Swipe

```csharp
// Drag by delta (relative movement)
await Drag(direction: new Vector2(0, -500), duration: 0.5f);     // Swipe down
await Drag(direction: new Vector2(300, 0), duration: 0.3f);      // Swipe right

// Drag on specific element (scrolling a ScrollRect)
await Drag(""ScrollView"", new Vector2(0, -400), 0.5f);

// Drag from point to point
await DragFromTo(startPos: new Vector2(500, 800), endPos: new Vector2(500, 400), duration: 0.5f);
```

### Text Input

```csharp
// Type text into InputField
await TextInput(""InputFieldName"", ""text to type"", seconds: 10);
```

### Find Methods

```csharp
// Find component by type
var component = await Find<MyComponent>(throwIfMissing: true, seconds: 10);

// Find by search pattern
var button = await Find<Button>(""ButtonName"", throwIfMissing: true, seconds: 10);
var buttons = await FindAll<Button>(""*btn*"", seconds: 10);
```

### Step Grouping (for test reports)

```csharp
using (BeginStep(""Navigate to Settings""))
{
    await Click(""Settings"");
    await Wait(""SettingsPanel"");
}

using (BeginStep(""Configure options""))
{
    await Click(""Option1"");
    await Click(""Save"");
}
```

### Screenshots and Attachments

```csharp
CaptureScreenshot(""screenshot_name"");
AttachText(""log"", ""Some text content"");
AttachJson(""data"", myObject);
AttachFile(""path/to/file.png"");
```

### Performance Tracking

```csharp
using (TrackPerformance(""operation_name""))
{
    // Code to measure
    await DoSomethingExpensive();
}
```

## Search Pattern Matching

The framework searches for UI elements in this order:
1. Exact GameObject name match (case-insensitive)
2. Wildcard pattern on GameObject name
3. Exact hierarchy path match
4. Wildcard pattern on hierarchy path
5. Text content of child Text/TMP_Text components

### Wildcard Examples
- `""*Play*""` - matches ""PlayButton"", ""ReplayGame"", ""AutoPlay""
- `""Btn_*""` - matches ""Btn_Start"", ""Btn_Stop""
- `""*_Panel""` - matches ""Settings_Panel"", ""Main_Panel""

## Handling Multiple Elements with Same Name

When recording shows `[sibling X of Y]`, it means there are multiple elements with the same name. Use these techniques:

### Click by Sibling Index
```csharp
// Click the 3rd item named ""ItemButton"" (0-based index)
await Click(""ItemButton"", index: 2);

// Or use FindAll and click specific one
var items = await FindAll<Button>(""ItemButton"");
if (items.Count > 2)
    await Click(items[2]);
```

### Click by Parent/Container
```csharp
// Click element inside specific parent
await Click(""ParentContainer/ItemButton"");

// Or find parent first, then child
var container = await Find<Transform>(""Category_Trucks"");
var button = container.GetComponentInChildren<Button>();
await Click(button);
```

### Scroll Then Click
When items are in a scroll view:
```csharp
// Scroll to reveal item, then click
await Drag(""ScrollView"", new Vector2(0, -300), 0.3f);  // Scroll down
await Wait(0.2f);
await Click(""TargetItem"");
```

## Best Practices

1. **Use BeginStep for logical groupings** - Makes reports readable
2. **Prefer text content over names** - More stable: `await Click(""PLAY"")` vs `await Click(""PlayButton"")`
3. **Use WaitFor after scene changes** - Wait for game state, not just UI
4. **Add waits between rapid clicks** - UI animations need time
5. **Use descriptive step names** - ""Navigate to garage"" not ""Click buttons""
6. **Handle loading screens** - Wait for them to complete
7. **Check the log.txt** - Look for GamePhase/GameMode changes to understand state

## Common Patterns

### Navigate through menus
```csharp
using (BeginStep(""Navigate to Garage""))
{
    await Click(""Play"");
    await Wait(1);
    await Click(""Garage"");
    await WaitFor(() => GarageManager.IsReady, 30, ""garage to load"");
}
```

### Handle popups/dialogs
```csharp
// Try to dismiss optional popup
await Click(""Close"", throwIfMissing: false, searchTime: 2);
await Click(""Accept"", throwIfMissing: false, searchTime: 2);
```

### Wait for scene transitions
```csharp
await Click(""StartRace"");
await WaitFor(() => SceneManager.GetActiveScene().name == ""RaceScene"", 30, ""race scene to load"");
await Wait(2); // Let scene stabilize
```

### Verify element exists
```csharp
var element = await Find<Button>(""SuccessIndicator"", throwIfMissing: false, seconds: 5);
if (element == null)
    throw new TestException(""Expected success indicator not found"");
```

## Error Handling

The framework throws:
- `TimeoutException` - When element not found within timeout
- `TestException` - Custom test failures

Always provide reasonable timeouts - default is 10 seconds, but scene loads may need 30-60 seconds.
";
        }

        static string FormatStep(UITestRecordingStep step)
        {
            string siblingInfo = step.siblingCount > 1
                ? $" [sibling {step.siblingIndex + 1} of {step.siblingCount}, use index: {step.siblingIndex}]"
                : "";
            string grandparentInfo = !string.IsNullOrEmpty(step.grandparentName) && step.siblingCount > 1
                ? $" grandparent=`{step.grandparentName}`"
                : "";

            return step.type switch
            {
                UITestRecordingStep.StepType.Click => $"Click `{step.targetName}` ({step.targetType})" +
                    (string.IsNullOrEmpty(step.textContent) ? "" : $" text=\"{step.textContent}\"") +
                    (string.IsNullOrEmpty(step.parentName) ? "" : $" parent=`{step.parentName}`") +
                    grandparentInfo +
                    siblingInfo,
                UITestRecordingStep.StepType.Hold => $"Hold `{step.targetName}` for {step.duration:F1}s" + siblingInfo,
                UITestRecordingStep.StepType.TextInput => $"Input `{step.targetName}` = \"{step.inputText}\"",
                UITestRecordingStep.StepType.Note => $"Note: {step.additionalContext}",
                UITestRecordingStep.StepType.Wait => $"Wait {step.duration:F1}s",
                UITestRecordingStep.StepType.SceneChange => $"Scene changed: `{step.previousScene}` → `{step.newScene}`",
                UITestRecordingStep.StepType.Drag => FormatDrag(step),
                UITestRecordingStep.StepType.Scroll => FormatScroll(step),
                _ => step.type.ToString()
            };
        }

        static string FormatDrag(UITestRecordingStep step)
        {
            string direction = GetDragDirectionName(step.dragDelta);
            string scrollRect = !string.IsNullOrEmpty(step.additionalContext) ? $" in ScrollRect=`{step.additionalContext}`" : "";
            return $"Drag {direction} from ({step.dragStartPosition.x:F0},{step.dragStartPosition.y:F0}) to ({step.dragEndPosition.x:F0},{step.dragEndPosition.y:F0}) delta=({step.dragDelta.x:F0},{step.dragDelta.y:F0}) for {step.duration:F1}s{scrollRect} target=`{step.targetName}`";
        }

        static string FormatScroll(UITestRecordingStep step)
        {
            string direction = step.dragDelta.y > 0 ? "UP" : "DOWN";
            return $"Scroll {direction} at ({step.screenPosition.x:F0},{step.screenPosition.y:F0}) target=`{step.targetName}`";
        }

        static string GetDragDirectionName(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                return delta.x > 0 ? "RIGHT" : "LEFT";
            }
            return delta.y > 0 ? "UP" : "DOWN";
        }
    }
}
