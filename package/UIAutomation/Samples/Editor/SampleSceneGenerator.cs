using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ODDGames.UIAutomation.Samples.Editor
{
    /// <summary>
    /// Editor tool to generate the comprehensive sample test scene for UITest demonstration.
    /// Creates a fully functional UI scene that works with ComprehensiveSampleTest.
    /// </summary>
    public static class SampleSceneGenerator
    {
        [MenuItem("Window/UI Automation/Generate Sample Scene")]
        public static void GenerateComprehensiveSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateEventSystem();
            var canvas = CreateCanvas();

            // Create all panels for comprehensive test
            CreateMainMenuPanel(canvas.transform);
            CreateSettingsPanel(canvas.transform);
            CreateButtonPanel(canvas.transform);
            CreateFormPanel(canvas.transform);
            CreateDragPanel(canvas.transform);
            CreateKeyboardPanel(canvas.transform);
            CreateAdvancedPanel(canvas.transform);

            MarkSceneDirty(scene, "ComprehensiveSampleScene");
        }

        private static void CreateMainMenuPanel(Transform canvas)
        {
            var mainMenu = CreatePanel(canvas, "MainMenu");

            CreateText(mainMenu.transform, "Title", "UITest Sample", 32, new Vector2(0, 180));
            CreateText(mainMenu.transform, "Subtitle", "Comprehensive Test Scene", 16, new Vector2(0, 140));

            CreateButton(mainMenu.transform, "SettingsButton", "Settings", new Vector2(0, 80));
            CreateButton(mainMenu.transform, "ButtonsButton", "Buttons", new Vector2(0, 35));
            CreateButton(mainMenu.transform, "FormsButton", "Forms", new Vector2(0, -10));
            CreateButton(mainMenu.transform, "DragButton", "Drag & Drop", new Vector2(0, -55));
            CreateButton(mainMenu.transform, "KeyboardButton", "Keyboard", new Vector2(0, -100));
            CreateButton(mainMenu.transform, "AdvancedButton", "Advanced Input", new Vector2(0, -145));
        }

        private static void CreateSettingsPanel(Transform canvas)
        {
            var settingsPanel = CreatePanel(canvas, "SettingsPanel");
            settingsPanel.SetActive(false);

            CreateText(settingsPanel.transform, "Title", "Settings", 28, new Vector2(0, 180));
            CreateToggle(settingsPanel.transform, "SoundToggle", "Sound Effects", new Vector2(0, 100));
            CreateToggle(settingsPanel.transform, "MusicToggle", "Music", new Vector2(0, 50));
            CreateSlider(settingsPanel.transform, "SettingsVolumeSlider", new Vector2(0, 0));
            CreateButton(settingsPanel.transform, "GraphicsOption", "Graphics: High", new Vector2(0, -60));
            CreateButton(settingsPanel.transform, "BackButton", "Back", new Vector2(0, -140));
        }

        private static void CreateButtonPanel(Transform canvas)
        {
            var panel = CreatePanel(canvas, "SampleButtonPanel", size: new Vector2(400, 580));
            panel.SetActive(false);

            CreateText(panel.transform, "Title", "Button Tests", 24, new Vector2(0, 250));

            // Result label
            CreateText(panel.transform, "ResultLabel", "Click a button...", 16, new Vector2(0, 210));

            // Simple button
            CreateButton(panel.transform, "SimpleButton", "Simple Button", new Vector2(0, 160));

            // Toggle
            CreateToggle(panel.transform, "SampleToggle", "Sample Toggle", new Vector2(0, 110));

            // Item buttons (for index testing)
            for (int i = 0; i < 3; i++)
            {
                CreateButton(panel.transform, "ItemButton", $"Item {i + 1}", new Vector2(0, 60 - (i * 40)));
            }

            // Counter section
            CreateText(panel.transform, "CounterLabel", "Counter: 0", 16, new Vector2(0, -70));
            CreateButton(panel.transform, "IncrementButton", "Increment", new Vector2(0, -100));

            // Disabled button
            var disabledBtn = CreateButton(panel.transform, "DisabledButton", "Disabled", new Vector2(-80, -150));
            disabledBtn.interactable = false;

            // Hold button
            CreateButton(panel.transform, "HoldButton", "Hold Me", new Vector2(80, -150));

            // Back button
            CreateButton(panel.transform, "BackButton", "Back", new Vector2(0, -210));
        }

        private static void CreateFormPanel(Transform canvas)
        {
            var panel = CreatePanel(canvas, "SampleFormPanel");
            panel.SetActive(false);

            CreateText(panel.transform, "Title", "Form Input", 24, new Vector2(0, 220));

            // Username input (TMP_InputField)
            CreateText(panel.transform, "UsernameLabel", "Username:", 14, new Vector2(-100, 170));
            CreateInputField(panel.transform, "UsernameInput", "Enter username...", new Vector2(50, 170));

            // Email input (legacy InputField to test both code paths)
            CreateText(panel.transform, "EmailLabel", "Email:", 14, new Vector2(-100, 120));
            CreateLegacyInputField(panel.transform, "EmailInput", "Enter email...", new Vector2(50, 120));

            // Password input
            CreateText(panel.transform, "PasswordLabel", "Password:", 14, new Vector2(-100, 70));
            var passwordInput = CreateInputField(panel.transform, "PasswordInput", "Enter password...", new Vector2(50, 70));
            passwordInput.contentType = TMP_InputField.ContentType.Password;

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

            // Back button
            CreateButton(panel.transform, "BackButton", "Back", new Vector2(0, -220));
        }

        private static void CreateDragPanel(Transform canvas)
        {
            var panel = CreatePanel(canvas, "SampleDragPanel", size: new Vector2(400, 560));
            panel.SetActive(false);

            CreateText(panel.transform, "Title", "Drag & Drop", 24, new Vector2(0, 240));

            // Scroll view
            CreateScrollView(panel.transform, "ScrollView", new Vector2(0, 90), new Vector2(300, 150));

            // Horizontal carousel
            CreateScrollView(panel.transform, "Carousel", new Vector2(0, -40), new Vector2(350, 70), horizontal: true);

            // Draggable item
            var draggable = CreatePanel(panel.transform, "DraggableItem", new Vector2(-100, -140), new Vector2(70, 70));
            draggable.GetComponent<Image>().color = new Color(0.3f, 0.6f, 1f);
            draggable.AddComponent<DraggableUI>();
            CreateText(draggable.transform, "Label", "Drag", 12, Vector2.zero);

            // Drop zone
            var dropZone = CreatePanel(panel.transform, "DropZone", new Vector2(100, -140), new Vector2(90, 90));
            dropZone.GetComponent<Image>().color = new Color(0.3f, 0.8f, 0.3f, 0.5f);
            dropZone.AddComponent<DropZoneUI>();
            CreateText(dropZone.transform, "Label", "Drop Here", 12, Vector2.zero);

            // Back button
            CreateButton(panel.transform, "BackButton", "Back", new Vector2(0, -220));
        }

        private static void CreateKeyboardPanel(Transform canvas)
        {
            var panel = CreatePanel(canvas, "SampleKeyboardPanel", size: new Vector2(400, 580));
            panel.SetActive(false);

            CreateText(panel.transform, "Title", "Keyboard Input", 24, new Vector2(0, 250));

            // Key press target
            CreateButton(panel.transform, "KeyPressTarget", "Press Space", new Vector2(0, 200));

            // Input field for typing
            CreateText(panel.transform, "TypeLabel", "Type here:", 14, new Vector2(-80, 150));
            CreateLegacyInputField(panel.transform, "KeyboardInput", "Type something...", new Vector2(50, 150));

            // Second input for tab navigation
            CreateText(panel.transform, "Tab2Label", "Tab to:", 14, new Vector2(-80, 100));
            CreateLegacyInputField(panel.transform, "SecondInput", "Second field...", new Vector2(50, 100));

            // === Key Hold Demo Section ===
            CreateText(panel.transform, "KeyHoldTitle", "Key Hold Demo (WASD)", 16, new Vector2(0, 50));

            // Create key hold indicator container
            var keyHoldContainer = CreatePanel(panel.transform, "KeyHoldContainer", new Vector2(0, -40), new Vector2(200, 130));
            keyHoldContainer.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);
            var indicator = keyHoldContainer.AddComponent<KeyHoldIndicator>();

            // WASD keys visual
            var wKey = CreateKeyVisual(keyHoldContainer.transform, "WKey", "W", new Vector2(0, 35));
            var aKey = CreateKeyVisual(keyHoldContainer.transform, "AKey", "A", new Vector2(-35, 0));
            var sKey = CreateKeyVisual(keyHoldContainer.transform, "SKey", "S", new Vector2(0, 0));
            var dKey = CreateKeyVisual(keyHoldContainer.transform, "DKey", "D", new Vector2(35, 0));
            var shiftKey = CreateKeyVisual(keyHoldContainer.transform, "ShiftKey", "⇧", new Vector2(-70, 0), new Vector2(25, 25));

            // Position indicator (moves when WASD held)
            var posArea = CreatePanel(keyHoldContainer.transform, "PositionArea", new Vector2(0, -45), new Vector2(180, 40));
            posArea.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f);
            var posIndicator = CreatePanel(posArea.transform, "PositionIndicator", Vector2.zero, new Vector2(15, 15));
            posIndicator.GetComponent<Image>().color = new Color(0.3f, 0.8f, 1f);

            // Status text
            var statusText = CreateText(keyHoldContainer.transform, "KeyHoldStatus", "Press WASD to move", 10, new Vector2(0, -55));

            // Wire up the indicator component using serialized fields reflection
            var indicatorType = indicator.GetType();
            SetPrivateField(indicator, "wKey", wKey.GetComponent<Image>());
            SetPrivateField(indicator, "aKey", aKey.GetComponent<Image>());
            SetPrivateField(indicator, "sKey", sKey.GetComponent<Image>());
            SetPrivateField(indicator, "dKey", dKey.GetComponent<Image>());
            SetPrivateField(indicator, "shiftKey", shiftKey.GetComponent<Image>());
            SetPrivateField(indicator, "positionIndicator", posIndicator.GetComponent<RectTransform>());
            SetPrivateField(indicator, "statusText", statusText);

            // Back button
            CreateButton(panel.transform, "BackButton", "Back", new Vector2(0, -140));
        }

        private static GameObject CreateKeyVisual(Transform parent, string name, string label, Vector2 position, Vector2? size = null)
        {
            var keySize = size ?? new Vector2(30, 30);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = keySize;
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.3f);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = GetDefaultFont();
            text.material = GetDefaultFontMaterial();

            return go;
        }

        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(obj, value);
        }

        private static void CreateAdvancedPanel(Transform canvas)
        {
            // Position panel on the right side so the 3D gesture cube is visible on the left
            var panel = CreatePanel(canvas, "SampleAdvancedPanel", position: new Vector2(150, 0), size: new Vector2(350, 580));
            panel.SetActive(false);

            CreateText(panel.transform, "Title", "Advanced Input", 24, new Vector2(0, 250));
            CreateText(panel.transform, "Subtitle", "Touch Gestures & Mouse", 14, new Vector2(0, 220));

            // Double-click button
            var doubleClickBtn = CreateButton(panel.transform, "DoubleClickButton", "Double-Click Me", new Vector2(0, 170));
            var dcFeedback = CreateText(panel.transform, "DoubleClickFeedback", "Double-click count: 0", 12, new Vector2(0, 140));

            // Scroll area
            CreateText(panel.transform, "ScrollLabel", "Scroll Area:", 12, new Vector2(-120, 100));
            CreateScrollView(panel.transform, "ScrollArea", new Vector2(50, 70), new Vector2(180, 100));

            // Swipe area
            CreateText(panel.transform, "SwipeLabel", "Swipe Area:", 12, new Vector2(-120, -10));
            var swipeArea = CreatePanel(panel.transform, "SwipeArea", new Vector2(50, -20), new Vector2(180, 60));
            swipeArea.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.45f);
            var swipeIndicator = CreateText(swipeArea.transform, "SwipeIndicator", "← Swipe →", 14, Vector2.zero);

            // 3D Gesture target - create a cube in front of the camera
            CreateText(panel.transform, "GestureLabel", "3D Gesture Target:", 12, new Vector2(0, -80));
            CreateText(panel.transform, "GestureHint", "Pinch to scale, Rotate, Two-finger pan", 10, new Vector2(0, -95));
            Create3DGestureTarget();

            // Status display
            CreateText(panel.transform, "GestureStatus", "Gesture: None", 12, new Vector2(0, -150));

            // Back button - moved up to ensure visibility
            CreateButton(panel.transform, "BackButton", "Back", new Vector2(0, -200));
        }

        private static void Create3DGestureTarget()
        {
            // Create a 3D cube that responds to gestures
            // Position it to the left of center so it's visible alongside the offset panel
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "GestureTarget";
            cube.transform.position = new Vector3(-2, 0, 3); // Left side, closer to camera
            cube.transform.localScale = Vector3.one * 2f;

            // Add colorful material
            var renderer = cube.GetComponent<Renderer>();
            var material = new Material(Shader.Find("Standard"));
            material.color = new Color(0.4f, 0.3f, 0.8f);
            renderer.material = material;

            // Add gesture component
            cube.AddComponent<GestureCube>();

            // Start inactive - will be shown when Advanced panel opens
            cube.SetActive(false);
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();

            // Add panel switcher to handle navigation
            go.AddComponent<PanelSwitcher>();

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
            text.font = GetDefaultFont();
            text.material = GetDefaultFontMaterial();

            return text;
        }

        private static Material GetDefaultFontMaterial()
        {
            // Use the default UI font material
            var font = GetDefaultFont();
            return font != null ? font.material : null;
        }

        private static Font _cachedFont;
        private static Font GetDefaultFont()
        {
            if (_cachedFont != null)
                return _cachedFont;

            // Try multiple font paths for different Unity versions
            _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_cachedFont == null)
                _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_cachedFont == null)
            {
                // Get system fonts and use Arial or first available
                var fonts = Font.GetOSInstalledFontNames();
                var fontName = System.Array.Find(fonts, f => f.Contains("Arial")) ?? fonts[0];
                _cachedFont = Font.CreateDynamicFontFromOSFont(fontName, 14);
            }
            return _cachedFont;
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

            go.AddComponent<ClickFeedback>();

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

        private static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, Vector2 position)
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

            var inputField = go.AddComponent<TMP_InputField>();

            // Text area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 5);
            textAreaRect.offsetMax = new Vector2(-10, -5);

            // Main text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 14;

            // Placeholder
            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(textArea.transform, false);
            var phRect = placeholderGo.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.sizeDelta = Vector2.zero;
            var phText = placeholderGo.AddComponent<TextMeshProUGUI>();
            phText.text = placeholder;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyles.Italic;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.fontSize = 14;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = phText;

            return inputField;
        }

        private static InputField CreateLegacyInputField(Transform parent, string name, string placeholder, Vector2 position)
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

            // Main text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);
            textRect.offsetMax = new Vector2(-10, -5);
            var text = textGo.AddComponent<Text>();
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = 14;
            text.font = GetDefaultFont();
            text.material = GetDefaultFontMaterial();

            // Placeholder
            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(go.transform, false);
            var phRect = placeholderGo.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 5);
            phRect.offsetMax = new Vector2(-10, -5);
            var phText = placeholderGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyle.Italic;
            phText.alignment = TextAnchor.MiddleLeft;
            phText.fontSize = 14;
            phText.font = GetDefaultFont();
            phText.material = GetDefaultFontMaterial();

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

            // Caption label - create directly for proper stretching
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-30, 0);
            var label = labelGo.AddComponent<Text>();
            label.text = "Option 1";
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.font = GetDefaultFont();
            label.material = GetDefaultFontMaterial();

            // Arrow
            var arrow = new GameObject("Arrow");
            arrow.transform.SetParent(go.transform, false);
            var arrowRect = arrow.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0.5f);
            arrowRect.anchorMax = new Vector2(1, 0.5f);
            arrowRect.sizeDelta = new Vector2(20, 20);
            arrowRect.anchoredPosition = new Vector2(-15, 0);
            var arrowText = arrow.AddComponent<Text>();
            arrowText.text = "▼";
            arrowText.font = GetDefaultFont();
            arrowText.material = GetDefaultFontMaterial();
            arrowText.fontSize = 12;
            arrowText.alignment = TextAnchor.MiddleCenter;
            arrowText.color = Color.white;

            // Template
            var template = new GameObject("Template");
            template.transform.SetParent(go.transform, false);
            var templateRect = template.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.anchoredPosition = Vector2.zero;
            templateRect.sizeDelta = new Vector2(0, 150);
            var templateImage = template.AddComponent<Image>();
            templateImage.color = new Color(0.25f, 0.25f, 0.25f);
            var scrollRect = template.AddComponent<ScrollRect>();

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.pivot = new Vector2(0, 1);
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 28);
            contentRect.anchoredPosition = Vector2.zero;

            // Item
            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var itemRect = item.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 28);
            itemRect.anchoredPosition = Vector2.zero;
            var itemToggle = item.AddComponent<Toggle>();

            // Item background
            var itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, false);
            var itemBgRect = itemBg.AddComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.sizeDelta = Vector2.zero;
            var itemBgImage = itemBg.AddComponent<Image>();
            itemBgImage.color = new Color(0.3f, 0.3f, 0.3f);

            // Item checkmark
            var checkmark = new GameObject("Item Checkmark");
            checkmark.transform.SetParent(item.transform, false);
            var checkRect = checkmark.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0, 0.5f);
            checkRect.anchorMax = new Vector2(0, 0.5f);
            checkRect.sizeDelta = new Vector2(20, 20);
            checkRect.anchoredPosition = new Vector2(10, 0);
            var checkText = checkmark.AddComponent<Text>();
            checkText.text = "✓";
            checkText.font = GetDefaultFont();
            checkText.material = GetDefaultFontMaterial();
            checkText.fontSize = 14;
            checkText.alignment = TextAnchor.MiddleCenter;
            checkText.color = Color.white;

            // Item label - create directly instead of using CreateText to get proper stretching
            var itemLabelGo = new GameObject("Item Label");
            itemLabelGo.transform.SetParent(item.transform, false);
            var itemLabelRect = itemLabelGo.AddComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.sizeDelta = Vector2.zero;
            itemLabelRect.offsetMin = new Vector2(25, 0);
            itemLabelRect.offsetMax = new Vector2(-10, 0);
            var itemLabel = itemLabelGo.AddComponent<Text>();
            itemLabel.text = "Option";
            itemLabel.fontSize = 14;
            itemLabel.alignment = TextAnchor.MiddleLeft;
            itemLabel.color = Color.white;
            itemLabel.font = GetDefaultFont();
            itemLabel.material = GetDefaultFontMaterial();

            // Configure toggle
            itemToggle.targetGraphic = itemBgImage;
            itemToggle.graphic = checkText;
            itemToggle.isOn = true;

            // Configure scroll rect
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Configure dropdown
            dropdown.template = templateRect;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.options.Add(new Dropdown.OptionData("Option 1"));
            dropdown.options.Add(new Dropdown.OptionData("Option 2"));
            dropdown.options.Add(new Dropdown.OptionData("Option 3"));

            template.SetActive(false);

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

            // Background (track)
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            bgRect.anchoredPosition = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.4f, 0.4f, 0.4f);

            // Fill area - spans the full slider width
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            // Fill - stretches from left to handle position
            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = Vector2.zero;
            fillRect.anchoredPosition = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f);

            // Handle slide area
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
            handleRect.anchoredPosition = Vector2.zero;
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

                for (int i = 0; i < 8; i++)
                {
                    var item = new GameObject($"CarouselItem{i}");
                    item.transform.SetParent(content.transform, false);
                    var itemRect = item.AddComponent<RectTransform>();
                    itemRect.sizeDelta = new Vector2(80, 50);
                    var itemImage = item.AddComponent<Image>();
                    itemImage.color = new Color(Random.Range(0.3f, 0.8f), Random.Range(0.3f, 0.8f), Random.Range(0.3f, 0.8f));
                    var le = item.AddComponent<LayoutElement>();
                    le.minWidth = 80;
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

                for (int i = 0; i < 12; i++)
                {
                    var item = new GameObject($"ListItem{i}");
                    item.transform.SetParent(content.transform, false);
                    var itemImage = item.AddComponent<Image>();
                    itemImage.color = new Color(0.25f, 0.25f, 0.25f);
                    var le = item.AddComponent<LayoutElement>();
                    le.minHeight = 30;
                    le.preferredHeight = 30;

                    CreateText(item.transform, "Label", $"Item {i + 1}", 12, Vector2.zero);
                }
            }

            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;

            return scrollRect;
        }

        private static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene, string name)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[UITest] Generated {name}. Save it with Ctrl+S or File > Save Scene.");
        }
    }
}
