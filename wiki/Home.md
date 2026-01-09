# UITest Framework

A UI automation testing framework for Unity projects.

## Quick Start

1. **Install** via Unity Package Manager:
   ```json
   "com.oddgames.uitest": "https://github.com/oddgames/ui-automation.git?path=package#v1.0.23"
   ```

2. **Create a test** by extending `UITestBehaviour`:
   ```csharp
   [UITest(Feature = "Login", Story = "User can log in")]
   public class LoginTest : UITestBehaviour
   {
       protected override async UniTask Test()
       {
           await Click("Login");
           await TextInput(Search.ByAdjacent("Username:"), "testuser");
           await TextInput(Search.ByAdjacent("Password:"), "password");
           await Click("Submit");
           await Wait(Search.ByName("MainMenu"), seconds: 10);
       }
   }
   ```

3. **Run** by attaching the test script to a GameObject in your scene.

## Wiki Pages

### Core Concepts
- [[Search Queries]] - How to find UI elements
- [[Test Actions]] - Click, TextInput, Drag, and more
- [[Test Attributes]] - Metadata for test organization

### Search Methods
- [[Search.ByName]] - Find by GameObject name
- [[Search.ByText]] - Find by text content
- [[Search.ByType]] - Find by component type
- [[Search.ByAdjacent]] - Find by adjacent label text
- [[Search.ByPath]] - Find by hierarchy path
- [[Search Chaining]] - Combine multiple conditions

### Advanced Topics
- [[Availability Filtering]] - Active/Inactive, Enabled/Disabled
- [[Gesture Input]] - Swipe, Pinch, Rotate
- [[Test Recording]] - Record interactions to generate tests
- [[Best Practices]] - Writing maintainable tests

## Requirements

- Unity 2022.3+
- Input System Package (New Input System only)
- TextMeshPro

## Links

- [GitHub Repository](https://github.com/oddgames/ui-automation)
- [Changelog](https://github.com/oddgames/ui-automation/blob/main/package/CHANGELOG.md)
