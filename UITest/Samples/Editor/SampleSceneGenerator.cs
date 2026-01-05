using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples.Editor
{
    /// <summary>
    /// Editor tool to generate sample test scenes for UITest demonstration.
    /// Creates fully functional UI scenes that work with the sample tests.
    /// </summary>
    public static class SampleSceneGenerator
    {
        private const string SamplesPath = "Assets/UITest/Samples/Scenes";

        [MenuItem("UITest/Samples/Generate All Sample Scenes")]
        public static void GenerateAllScenes()
        {
            EnsureDirectoryExists();
            GenerateButtonSampleScene();
            GenerateFormSampleScene();
            GenerateDragSampleScene();
            GenerateNavigationSampleScene();
            AssetDatabase.Refresh();
            Debug.Log("All sample scenes generated successfully!");
        }

        [MenuItem("UITest/Samples/Generate Button Sample Scene")]
        public static void GenerateButtonSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateEventSystem();
            var canvas = CreateCanvas();

            // Create main panel
            var panel = CreatePanel(canvas.transform, "SampleButtonPanel");

            // Title
            CreateText(panel.transform, "Title", "Button Interaction Sample", 24, new Vector2(0, 180));

            // Result label
            var resultLabel = CreateText(panel.transform, "ResultLabel", "Click a button...", 16, new Vector2(0, 140));

            // Simple button
            var simpleBtn = CreateButton(panel.transform, "SimpleButton", "Simple Button", new Vector2(0, 80));

            // Toggle
            CreateToggle(panel.transform, "SampleToggle", "Sample Toggle", new Vector2(0, 30));

            // Item buttons (for index testing)
            for (int i = 0; i < 3; i++)
            {
                CreateButton(panel.transform, "ItemButton", $"Item {i + 1}", new Vector2(0, -20 - (i * 40)));
            }

            // Counter section
            var counterLabel = CreateText(panel.transform, "CounterLabel", "Counter: 0", 16, new Vector2(0, -150));
            CreateButton(panel.transform, "IncrementButton", "Increment", new Vector2(0, -180));

            // Disabled button
            var disabledBtn = CreateButton(panel.transform, "DisabledButton", "Disabled", new Vector2(0, -230));
            disabledBtn.interactable = false;

            // Hold button
            CreateButton(panel.transform, "HoldButton", "Hold Me", new Vector2(0, -280));

            SaveScene(scene, "ButtonSampleScene");
        }

        [MenuItem("UITest/Samples/Generate Form Sample Scene")]
        public static void GenerateFormSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateEventSystem();
            var canvas = CreateCanvas();

            var panel = CreatePanel(canvas.transform, "SampleFormPanel");

            // Title
            CreateText(panel.transform, "Title", "Form Input Sample", 24, new Vector2(0, 220));

            // Username input
            CreateText(panel.transform, "UsernameLabel", "Username:", 14, new Vector2(-100, 170));
            CreateInputField(panel.transform, "UsernameInput", "Enter username...", new Vector2(50, 170));

            // Email input
            CreateText(panel.transform, "EmailLabel", "Email:", 14, new Vector2(-100, 120));
            CreateInputField(panel.transform, "EmailInput", "Enter email...", new Vector2(50, 120));

            // Password input
            CreateText(panel.transform, "PasswordLabel", "Password:", 14, new Vector2(-100, 70));
            var passwordInput = CreateInputField(panel.transform, "PasswordInput", "Enter password...", new Vector2(50, 70));
            passwordInput.contentType = InputField.ContentType.Password;

            // Dropdown
            CreateText(panel.transform, "CategoryLabel", "Category:", 14, new Vector2(-100, 20));
            CreateDropdown(panel.transform, "CategoryDropdown", new Vector2(50, 20));

            // Slider
            CreateText(panel.transform, "VolumeLabel", "Volume:", 14, new Vector2(-100, -30));
            CreateSlider(panel.transform, "VolumeSlider", new Vector2(50, -30));

            // Agree toggle
            CreateToggle(panel.transform, "AgreeToggle", "I agree to terms", new Vector2(0, -80));

            // Submit button
            CreateButton(panel.transform, "SubmitButton", "Submit", new Vector2(0, -130));

            // Success message (hidden initially)
            var successMsg = CreateText(panel.transform, "SuccessMessage", "Form submitted successfully!", 18, new Vector2(0, -180));
            successMsg.gameObject.SetActive(false);

            SaveScene(scene, "FormSampleScene");
        }

        [MenuItem("UITest/Samples/Generate Drag Sample Scene")]
        public static void GenerateDragSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateEventSystem();
            var canvas = CreateCanvas();

            var panel = CreatePanel(canvas.transform, "SampleDragPanel");

            // Title
            CreateText(panel.transform, "Title", "Drag & Drop Sample", 24, new Vector2(0, 220));

            // Scroll view
            CreateScrollView(panel.transform, "ScrollView", new Vector2(0, 50), new Vector2(300, 200));

            // Horizontal carousel
            CreateScrollView(panel.transform, "Carousel", new Vector2(0, -120), new Vector2(350, 80), horizontal: true);

            // Draggable item
            var draggable = CreatePanel(panel.transform, "DraggableItem", new Vector2(-100, -220), new Vector2(80, 80));
            draggable.GetComponent<Image>().color = new Color(0.3f, 0.6f, 1f);
            CreateText(draggable.transform, "Label", "Drag", 12, Vector2.zero);

            // Drop zone
            var dropZone = CreatePanel(panel.transform, "DropZone", new Vector2(100, -220), new Vector2(100, 100));
            dropZone.GetComponent<Image>().color = new Color(0.3f, 0.8f, 0.3f, 0.5f);
            CreateText(dropZone.transform, "Label", "Drop Here", 12, Vector2.zero);

            SaveScene(scene, "DragSampleScene");
        }

        [MenuItem("UITest/Samples/Generate Navigation Sample Scene")]
        public static void GenerateNavigationSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateEventSystem();
            var canvas = CreateCanvas();

            // Main Menu
            var mainMenu = CreatePanel(canvas.transform, "MainMenu");

            CreateText(mainMenu.transform, "Title", "Main Menu", 32, new Vector2(0, 150));
            CreateButton(mainMenu.transform, "PlayButton", "Play", new Vector2(0, 60));
            CreateButton(mainMenu.transform, "SettingsButton", "Settings", new Vector2(0, 0));
            CreateButton(mainMenu.transform, "CreditsButton", "Credits", new Vector2(0, -60));
            CreateButton(mainMenu.transform, "QuitButton", "Quit", new Vector2(0, -120));

            // Settings Panel (hidden)
            var settingsPanel = CreatePanel(canvas.transform, "SettingsPanel");
            settingsPanel.SetActive(false);

            CreateText(settingsPanel.transform, "Title", "Settings", 28, new Vector2(0, 150));
            CreateToggle(settingsPanel.transform, "SoundToggle", "Sound Effects", new Vector2(0, 80));
            CreateToggle(settingsPanel.transform, "MusicToggle", "Music", new Vector2(0, 40));
            CreateSlider(settingsPanel.transform, "VolumeSlider", new Vector2(0, 0));
            CreateButton(settingsPanel.transform, "GraphicsOption", "Graphics: High", new Vector2(0, -50));
            CreateButton(settingsPanel.transform, "BackButton", "Back", new Vector2(0, -120));

            // Credits Panel (hidden)
            var creditsPanel = CreatePanel(canvas.transform, "CreditsPanel");
            creditsPanel.SetActive(false);

            CreateText(creditsPanel.transform, "Title", "Credits", 28, new Vector2(0, 150));
            CreateText(creditsPanel.transform, "Content", "Made with UITest\nSample Project", 16, new Vector2(0, 50));
            CreateButton(creditsPanel.transform, "BackButton", "Back", new Vector2(0, -100));

            SaveScene(scene, "NavigationSampleScene");
        }

        private static void EnsureDirectoryExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UITest"))
                AssetDatabase.CreateFolder("Assets", "UITest");
            if (!AssetDatabase.IsValidFolder("Assets/UITest/Samples"))
                AssetDatabase.CreateFolder("Assets/UITest", "Samples");
            if (!AssetDatabase.IsValidFolder(SamplesPath))
                AssetDatabase.CreateFolder("Assets/UITest/Samples", "Scenes");
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2? position = null, Vector2? size = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size ?? new Vector2(400, 500);
            rect.anchoredPosition = position ?? Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            return go;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300, 30);
            rect.anchoredPosition = position;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160, 35);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            CreateText(go.transform, "Text", label, 14, Vector2.zero);

            return button;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 30);
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

            CreateText(go.transform, "Label", label, 14, new Vector2(30, 0)).alignment = TextAnchor.MiddleLeft;

            return toggle;
        }

        private static InputField CreateInputField(Transform parent, string name, string placeholder, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);

            var inputField = go.AddComponent<InputField>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);
            textRect.offsetMax = new Vector2(-10, -5);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(go.transform, false);
            var phRect = placeholderGo.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 5);
            phRect.offsetMax = new Vector2(-10, -5);
            var phText = placeholderGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyle.Italic;
            phText.alignment = TextAnchor.MiddleLeft;

            inputField.textComponent = text;
            inputField.placeholder = phText;

            return inputField;
        }

        private static Dropdown CreateDropdown(Transform parent, string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.3f);

            var dropdown = go.AddComponent<Dropdown>();

            var label = CreateText(go.transform, "Label", "Option 1", 14, Vector2.zero);
            label.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            label.GetComponent<RectTransform>().anchorMax = Vector2.one;
            label.GetComponent<RectTransform>().offsetMin = new Vector2(10, 0);
            label.GetComponent<RectTransform>().offsetMax = new Vector2(-30, 0);
            label.alignment = TextAnchor.MiddleLeft;

            dropdown.captionText = label;
            dropdown.options.Add(new Dropdown.OptionData("Option 1"));
            dropdown.options.Add(new Dropdown.OptionData("Option 2"));
            dropdown.options.Add(new Dropdown.OptionData("Option 3"));

            return dropdown;
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
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f);

            // Fill area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f);

            // Handle
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.value = 0.5f;

            return slider;
        }

        private static ScrollRect CreateScrollView(Transform parent, string name, Vector2 position, Vector2 size, bool horizontal = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);
            go.AddComponent<Mask>().showMaskGraphic = true;

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.horizontal = horizontal;
            scrollRect.vertical = !horizontal;

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();

            if (horizontal)
            {
                contentRect.anchorMin = new Vector2(0, 0);
                contentRect.anchorMax = new Vector2(0, 1);
                contentRect.pivot = new Vector2(0, 0.5f);
                contentRect.sizeDelta = new Vector2(size.x * 3, 0);

                var layout = content.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 10;
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;

                // Add items
                for (int i = 0; i < 8; i++)
                {
                    var item = new GameObject($"CarouselItem{i}");
                    item.transform.SetParent(content.transform, false);
                    var itemRect = item.AddComponent<RectTransform>();
                    itemRect.sizeDelta = new Vector2(100, 60);
                    var itemImage = item.AddComponent<Image>();
                    itemImage.color = new Color(Random.Range(0.3f, 0.8f), Random.Range(0.3f, 0.8f), Random.Range(0.3f, 0.8f));
                    var le = item.AddComponent<LayoutElement>();
                    le.minWidth = 100;
                }
            }
            else
            {
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0.5f, 1);
                contentRect.sizeDelta = new Vector2(0, size.y * 2);

                var layout = content.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 5;
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                // Add items
                for (int i = 0; i < 15; i++)
                {
                    var item = new GameObject($"ListItem{i}");
                    item.transform.SetParent(content.transform, false);
                    var itemImage = item.AddComponent<Image>();
                    itemImage.color = new Color(0.25f, 0.25f, 0.25f);
                    var le = item.AddComponent<LayoutElement>();
                    le.minHeight = 40;
                    le.preferredHeight = 40;

                    CreateText(item.transform, "Label", $"List Item {i + 1}", 14, Vector2.zero);
                }
            }

            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;

            return scrollRect;
        }

        private static void SaveScene(UnityEngine.SceneManagement.Scene scene, string name)
        {
            EnsureDirectoryExists();
            string path = $"{SamplesPath}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"Saved scene: {path}");
        }
    }
}
