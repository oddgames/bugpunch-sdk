using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Test specifically for Search class features.
    /// Generates its own test UI at runtime - no pre-saved scene needed.
    /// Tests all Search methods: Name, Text, Sprite, Type, Path, Tag, With, Not, Any.
    /// </summary>
    [UITest(
        Scenario = 9001,
        Feature = "Search",
        Story = "User can find UI elements using Search queries",
        Severity = TestSeverity.Critical,
        Description = "Tests all Search API features",
        Tags = new[] { "sample", "search", "api" }
    )]
    public class SearchFeatureTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Generate the test UI at runtime
            GenerateTestUI();

            await UniTask.Yield();

            // ==========================================
            // Search.ByName - Find by GameObject name
            // ==========================================
            // Exact name
            await Click(Search.ByName("NameButton"));

            // Wildcard prefix
            await Click(Search.ByName("Name*"));

            // Wildcard suffix
            await Click(Search.ByName("*Button"), index: 0);

            // Wildcard contains
            await Click(Search.ByName("*ame*"));

            // Complex glob pattern
            await Click(Search.ByName("btn_*_icon"));

            // ==========================================
            // Search.ByText - Find by text content (Text/TMP_Text)
            // ==========================================
            await Click(Search.ByText("Click Me"));
            await Click(Search.ByText("*Me"), index: 0);
            await Click(Search.ByText("Click*"));

            // ==========================================
            // Search.ByType - Find by component type
            // ==========================================
            // Generic type
            await Click(Search.ByType<Toggle>(), index: 0);

            // ==========================================
            // Search.ByPath - Find by hierarchy path
            // ==========================================
            await Click(Search.ByPath("*ChildPanel*/*ChildButton*"));

            // ==========================================
            // Search.ByAny - Match name OR text OR sprite
            // ==========================================
            await Click(Search.ByAny("AnyMatch"));

            // ==========================================
            // Chaining - Multiple conditions (AND logic)
            // ==========================================
            // Type AND Name
            await Click(Search.ByType<Button>().Name("ChainButton"));

            // Name AND Text
            await Click(Search.ByName("*Button").Text("Submit"));

            // Type AND Text AND Name
            await Click(Search.ByType<Button>().Text("OK").Name("*Confirm*"));

            // ==========================================
            // Search.With - Component property checks
            // ==========================================
            // Button that is interactable
            await Click(Search.ByType<Button>().Name("InteractableBtn").With<Button>(b => b.interactable));

            // Toggle that is off
            await Click(Search.ByType<Toggle>().With<Toggle>(t => !t.isOn));

            // Find slider with value > 0.5
            var slider = await Find<Slider>(Search.ByType<Slider>().With<Slider>(s => s.value > 0.5f));
            Assert(slider != null, "Should find slider with value > 0.5");

            // ==========================================
            // Search.Not - Negation
            // ==========================================
            // Button that is NOT named "DisabledButton"
            await Click(Search.ByType<Button>().Name("Not*").Not.Name("NotThisOne"));

            // ==========================================
            // Search.Where - Custom predicate
            // ==========================================
            await Click(Search.Where(go => go.name == "CustomPredicateBtn"));

            // ==========================================
            // Implicit string conversion
            // ==========================================
            await Click("SimpleButton");
            var found = await Find<Button>("SimpleButton");
            Assert(found != null, "Should find SimpleButton via implicit conversion");

            // ==========================================
            // Index parameter
            // ==========================================
            // Click second item with matching name
            await Click(Search.ByName("ListItem"), index: 1);

            CaptureScreenshot("search_test_complete");
        }

        private void GenerateTestUI()
        {
            // Create EventSystem if needed
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // Create Canvas
            var canvasGO = new GameObject("TestCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            var panel = CreatePanel(canvasGO.transform, "SearchTestPanel", Vector2.zero, new Vector2(600, 700));

            // Name-based targets
            CreateButton(panel.transform, "NameButton", "Name Button", new Vector2(-200, 280));
            CreateButton(panel.transform, "btn_play_icon", "Glob Pattern", new Vector2(0, 280));
            CreateButton(panel.transform, "AnotherButton", "Another", new Vector2(200, 280));

            // Text-based targets
            CreateButton(panel.transform, "TextButton1", "Click Me", new Vector2(-200, 220));
            CreateButton(panel.transform, "TextButton2", "Press Me", new Vector2(0, 220));
            CreateButton(panel.transform, "TextButton3", "Tap Me", new Vector2(200, 220));

            // Type-based targets
            CreateToggle(panel.transform, "SearchToggle1", "Toggle A", new Vector2(-100, 160));
            CreateToggle(panel.transform, "SearchToggle2", "Toggle B", new Vector2(100, 160));

            // Slider with value > 0.5
            var slider = CreateSlider(panel.transform, "SearchSlider", new Vector2(0, 100));
            slider.value = 0.75f;

            // Hierarchy path targets
            var childPanel = CreatePanel(panel.transform, "ChildPanel", new Vector2(0, 30), new Vector2(400, 60));
            childPanel.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);
            CreateButton(childPanel.transform, "ChildButton", "Child", new Vector2(-100, 0));
            CreateButton(childPanel.transform, "DeepButton", "Deep", new Vector2(100, 0));

            // Chaining targets
            CreateButton(panel.transform, "ChainButton", "Chain Test", new Vector2(-200, -40));
            CreateButton(panel.transform, "SubmitButton", "Submit", new Vector2(0, -40));
            CreateButton(panel.transform, "ConfirmButton", "OK", new Vector2(200, -40));

            // Property check targets
            var interactableBtn = CreateButton(panel.transform, "InteractableBtn", "Interactable", new Vector2(-200, -100));
            var disabledBtn = CreateButton(panel.transform, "DisabledButton", "Disabled", new Vector2(0, -100));
            disabledBtn.interactable = false;

            var offToggle = CreateToggle(panel.transform, "OffToggle", "Off Toggle", new Vector2(150, -100));
            offToggle.isOn = false;

            // Not targets
            CreateButton(panel.transform, "NotThisBtn", "Not This", new Vector2(-200, -160));
            CreateButton(panel.transform, "NotThisOne", "Wrong One", new Vector2(0, -160));

            // Custom predicate target
            CreateButton(panel.transform, "CustomPredicateBtn", "Custom", new Vector2(200, -160));

            // List items for index testing
            for (int i = 0; i < 3; i++)
            {
                CreateButton(panel.transform, "ListItem", $"List {i + 1}", new Vector2(-200 + (i * 200), -220));
            }

            // Simple button and Any match
            CreateButton(panel.transform, "SimpleButton", "Simple", new Vector2(-100, -280));
            CreateButton(panel.transform, "AnyMatch", "Any Match", new Vector2(100, -280));
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

            return go;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(140, 35);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // Create TMP text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 14;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;

            return button;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 30);
            rect.anchoredPosition = position;

            var toggle = go.AddComponent<Toggle>();

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(0, 0.5f);
            bgRect.sizeDelta = new Vector2(20, 20);
            bgRect.anchoredPosition = new Vector2(10, 0);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.4f, 0.4f, 0.4f);

            // Checkmark
            var check = new GameObject("Checkmark");
            check.transform.SetParent(bg.transform, false);
            var checkRect = check.AddComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-4, -4);
            var checkImage = check.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.8f, 0.3f);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(35, 0);
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 14;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = Color.white;

            return toggle;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 20);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.4f, 0.4f, 0.4f);

            // Fill area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            // Fill
            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f);

            // Handle area
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            // Handle
            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(20, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            return slider;
        }
    }
}
