using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using ODDGames.UITest.AI;
using UIImage = UnityEngine.UI.Image;

namespace ODDGames.UITest.VisualBuilder.Editor
{
    /// <summary>
    /// Visual Test Builder with Scratch-style blocks.
    /// Flow: Select scene → Enter play mode → Add/edit blocks → Save each block → Run
    /// </summary>
    public class TestBuilder : EditorWindow
    {
        // Session state keys for domain reload persistence
        private const string kSessionTestPath = "TestBuilder.SessionTestPath";
        private const string kSessionSelectedBlock = "TestBuilder.SessionSelectedBlock";
        private const string kSessionEditingBlock = "TestBuilder.SessionEditingBlock";

        private VisualTest currentTest;
        private int selectedBlockIndex = -1;

        // UI Elements
        private VisualElement root;
        private VisualElement blockPanel;
        private VisualElement blockListContainer;
        private TextField testNameField;
        private DropdownField sceneDropdown;
        private Label statusLabel;
        private Button startEditBtn;
        private Button stopBtn;
        private Button playTestBtn;
        private VisualElement blockControls;
        private VisualElement blockControlsRow2;

        // Block editing state
        private int editingBlockIndex = -1;  // Which block is currently being edited (-1 = none)
        private bool blockHasUnsavedChanges;

        // Visual picking state
        private bool isPickingTarget;
        private bool isPickingDragTarget;
        private int pickingBlockIndex = -1;
        private ElementInfo hoveredElement;

        // State
        private AIAssistant aiAssistant;
        private List<ElementInfo> currentElements = new();

        // AI status panel
        private VisualElement aiStatusPanel;
        private Label aiStateLabel;
        private Label aiReasoningLabel;
        private ProgressBar aiProgressBar;
        private Button aiStopButton;
        private int aiActionCount;


        [MenuItem("Window/Analysis/UI Automation/Test Builder")]
        public static TestBuilder ShowWindow()
        {
            var window = GetWindow<TestBuilder>();
            window.titleContent = new GUIContent("Test Builder", EditorGUIUtility.IconContent("TestPassed").image);
            window.minSize = new Vector2(350, 400);
            return window;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;

            // Restore state from session if we're recovering from domain reload
            RestoreSessionState();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            aiAssistant?.Dispose();
            CancelPicking();
            TargetOverlay.Hide();

            // Save state to session for domain reload recovery
            SaveSessionState();
        }

        private void SaveSessionState()
        {
            // Save current test path
            var testPath = currentTest != null ? AssetDatabase.GetAssetPath(currentTest) : "";
            SessionState.SetString(kSessionTestPath, testPath);
            SessionState.SetInt(kSessionSelectedBlock, selectedBlockIndex);
            SessionState.SetInt(kSessionEditingBlock, editingBlockIndex);
        }

        private void RestoreSessionState()
        {
            // Only restore if we don't already have a test loaded
            if (currentTest != null) return;

            var testPath = SessionState.GetString(kSessionTestPath, "");
            if (!string.IsNullOrEmpty(testPath))
            {
                var test = AssetDatabase.LoadAssetAtPath<VisualTest>(testPath);
                if (test != null)
                {
                    currentTest = test;
                    selectedBlockIndex = SessionState.GetInt(kSessionSelectedBlock, -1);
                    editingBlockIndex = SessionState.GetInt(kSessionEditingBlock, -1);

                    // Clear editing state after domain reload - unsaved changes are lost
                    blockHasUnsavedChanges = false;
                }
            }
        }

        private void CancelPicking()
        {
            isPickingTarget = false;
            isPickingDragTarget = false;
            pickingBlockIndex = -1;
            hoveredElement = null;
        }

        private void OnUndoRedo()
        {
            if (currentTest != null)
            {
                if (selectedBlockIndex >= currentTest.blocks.Count)
                    selectedBlockIndex = currentTest.blocks.Count - 1;
                RefreshBlockList();
            }
        }

        private void OnEditorUpdate()
        {
            // Update target overlay when editing a block in play mode
            if (EditorApplication.isPlaying && editingBlockIndex >= 0 && currentTest != null)
            {
                UpdateTargetOverlay();
            }
            else
            {
                TargetOverlay.Hide();
            }
        }

        private void UpdateTargetOverlay()
        {
            if (currentTest == null || editingBlockIndex < 0 || editingBlockIndex >= currentTest.blocks.Count)
            {
                TargetOverlay.Hide();
                return;
            }

            var block = currentTest.blocks[editingBlockIndex];
            if (block.target?.query == null)
            {
                TargetOverlay.Hide();
                return;
            }

            // Evaluate the Search to find the target element
            try
            {
                var search = block.target.ToSearch();
                if (search == null)
                {
                    TargetOverlay.Hide();
                    return;
                }

                var target = search.FindFirst();
                if (target == null)
                {
                    TargetOverlay.Hide();
                    return;
                }

                // Get screen bounds of the target
                var bounds = InputInjector.GetScreenBounds(target);
                if (bounds.width <= 0 || bounds.height <= 0)
                {
                    TargetOverlay.Hide();
                    return;
                }

                // Calculate center position
                var centerX = bounds.x + bounds.width / 2f;
                var centerY = bounds.y + bounds.height / 2f;

                var label = block.target.GetDisplayText() ?? target.name;
                TargetOverlay.Show(new Vector2(centerX, centerY), label, bounds);
            }
            catch
            {
                TargetOverlay.Hide();
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Restore session state after domain reload
                RestoreSessionState();

                // Update UI fields to reflect restored state
                if (currentTest != null)
                {
                    testNameField?.SetValueWithoutNotify(currentTest.testName ?? "");
                    var sceneName = string.IsNullOrEmpty(currentTest.startScene) ? "-- Select Scene --" : currentTest.startScene;
                    sceneDropdown?.SetValueWithoutNotify(sceneName);
                }

                RefreshUI();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Save state before domain reload
                SaveSessionState();

                aiAssistant?.Stop();
                editingBlockIndex = -1;
                blockHasUnsavedChanges = false;
                RefreshUI();
            }
        }

        private void CreateGUI()
        {
            root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.flexDirection = FlexDirection.Column;
            root.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

            BuildToolbar();
            BuildMainContent();
            BuildStatusBar();

            // If test was already restored in OnEnable, sync UI fields
            // Otherwise load from EditorPrefs
            if (currentTest != null)
            {
                testNameField?.SetValueWithoutNotify(currentTest.testName ?? "");
                var sceneName = string.IsNullOrEmpty(currentTest.startScene) ? "-- Select Scene --" : currentTest.startScene;
                sceneDropdown?.SetValueWithoutNotify(sceneName);
            }
            else
            {
                LoadLastTest();
            }
            RefreshUI();
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();

            // File menu
            var fileMenu = new ToolbarMenu { text = "File" };
            fileMenu.menu.AppendAction("New Test", _ => OnNewTest());
            fileMenu.menu.AppendAction("Save Test", _ => OnSaveTest());
            fileMenu.menu.AppendAction("Load...", _ => OnLoadTest());
            toolbar.Add(fileMenu);

            // Test name
            testNameField = new TextField { style = { width = 120, marginLeft = 4 } };
            testNameField.RegisterValueChangedCallback(evt =>
            {
                if (currentTest != null)
                {
                    Undo.RecordObject(currentTest, "Rename test");
                    currentTest.testName = evt.newValue;
                    EditorUtility.SetDirty(currentTest);
                }
            });
            toolbar.Add(testNameField);

            // Scene selector
            var sceneNames = GetAllSceneNames();
            sceneDropdown = new DropdownField(sceneNames, 0) { style = { width = 120, marginLeft = 4 } };
            sceneDropdown.RegisterValueChangedCallback(OnSceneSelected);
            toolbar.Add(sceneDropdown);

            toolbar.Add(new ToolbarSpacer { flex = true });

            // Play test button (runs entire test from start)
            playTestBtn = new Button(OnPlayTest) { text = "▶ Play" };
            playTestBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f);
            playTestBtn.tooltip = "Run entire test from start";
            toolbar.Add(playTestBtn);

            // Start/Stop buttons
            startEditBtn = new Button(OnStartEdit) { text = "Edit" };
            startEditBtn.style.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
            startEditBtn.tooltip = "Enter play mode to edit blocks";
            toolbar.Add(startEditBtn);

            stopBtn = new Button(OnStopEdit) { text = "■" };
            stopBtn.style.backgroundColor = new Color(0.5f, 0.3f, 0.3f);
            stopBtn.tooltip = "Stop and exit play mode";
            toolbar.Add(stopBtn);

            root.Add(toolbar);
        }

        private void OnSceneSelected(ChangeEvent<string> evt)
        {
            if (currentTest == null) return;

            var sceneName = evt.newValue == "-- Select Scene --" ? "" : evt.newValue;
            Undo.RecordObject(currentTest, "Change scene");
            currentTest.startScene = sceneName;
            EditorUtility.SetDirty(currentTest);
            RefreshUI();
        }

        private void OnStartEdit()
        {
            if (currentTest == null || string.IsNullOrEmpty(currentTest.startScene))
            {
                statusLabel.text = "Select a scene first";
                return;
            }

            // Load the scene
            var scenePath = GetScenePath(currentTest.startScene);
            if (string.IsNullOrEmpty(scenePath))
            {
                statusLabel.text = $"Scene not found: {currentTest.startScene}";
                return;
            }

            // Save test first
            OnSaveTest();

            // Open scene and enter play mode
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;
            statusLabel.text = "Entering play mode...";
        }

        private void OnStopEdit()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
            editingBlockIndex = -1;
            blockHasUnsavedChanges = false;
            RefreshUI();
        }

        private async void OnPlayTest()
        {
            if (currentTest == null || currentTest.blocks.Count == 0)
            {
                statusLabel.text = "No blocks to run";
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                // Need to enter play mode first
                if (string.IsNullOrEmpty(currentTest.startScene))
                {
                    statusLabel.text = "Select a scene first";
                    return;
                }

                var scenePath = GetScenePath(currentTest.startScene);
                if (string.IsNullOrEmpty(scenePath))
                {
                    statusLabel.text = $"Scene not found: {currentTest.startScene}";
                    return;
                }

                OnSaveTest();
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
                EditorApplication.isPlaying = true;

                // Wait for play mode
                while (!EditorApplication.isPlaying)
                    await UniTask.Yield();
                await UniTask.DelayFrame(5);
            }
            else
            {
                // Already in play mode - restart scene
                await RestartScene();
            }

            // Run all blocks
            statusLabel.text = $"Running test ({currentTest.blocks.Count} blocks)...";

            for (int i = 0; i < currentTest.blocks.Count; i++)
            {
                if (!EditorApplication.isPlaying) break;

                selectedBlockIndex = i;
                RefreshBlockList();
                statusLabel.text = $"Running block {i + 1}/{currentTest.blocks.Count}...";

                await ExecuteBlock(currentTest.blocks[i]);
                await UniTask.Delay(200);
            }

            if (EditorApplication.isPlaying)
                statusLabel.text = $"Test completed ({currentTest.blocks.Count} blocks)";
        }

        private void BuildMainContent()
        {
            blockPanel = new VisualElement();
            blockPanel.style.flexGrow = 1;
            blockPanel.style.flexDirection = FlexDirection.Column;

            // Row 1: Mouse/Touch Actions (Blue/Orange theme)
            blockControls = new VisualElement();
            blockControls.style.flexDirection = FlexDirection.Row;
            blockControls.style.flexWrap = Wrap.Wrap;
            blockControls.style.paddingLeft = 4;
            blockControls.style.paddingTop = 2;
            blockControls.style.paddingBottom = 1;
            blockControls.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            AddBlockTypeButton(blockControls, "Click", BlockType.Click, new Color(0.3f, 0.6f, 1f), "Click on element");
            AddBlockTypeButton(blockControls, "2xClick", BlockType.DoubleClick, new Color(0.24f, 0.53f, 0.94f), "Double-click");
            AddBlockTypeButton(blockControls, "Hold", BlockType.Hold, new Color(0.42f, 0.65f, 1f), "Long press");
            AddBlockTypeButton(blockControls, "Drag", BlockType.Drag, new Color(1f, 0.55f, 0.1f), "Drag element");
            AddBlockTypeButton(blockControls, "Swipe", BlockType.Swipe, new Color(1f, 0.44f, 0.25f), "Swipe gesture");
            AddBlockTypeButton(blockControls, "2-Swipe", BlockType.TwoFingerSwipe, new Color(1f, 0.38f, 0.19f), "Two-finger swipe");
            AddBlockTypeButton(blockControls, "Pinch", BlockType.Pinch, new Color(1f, 0.31f, 0.5f), "Pinch zoom gesture");
            AddBlockTypeButton(blockControls, "Rotate", BlockType.Rotate, new Color(1f, 0.25f, 0.56f), "Two-finger rotate gesture");
            AddBlockTypeButton(blockControls, "Scroll", BlockType.Scroll, new Color(0.35f, 0.75f, 0.35f), "Scroll in direction");
            AddBlockTypeButton(blockControls, "Slider", BlockType.SetSlider, new Color(1f, 0.6f, 0.2f), "Set slider value");
            AddBlockTypeButton(blockControls, "Scrollbar", BlockType.SetScrollbar, new Color(0.2f, 0.8f, 0.6f), "Set scrollbar position");
            blockPanel.Add(blockControls);

            // Row 2: Keyboard Actions (Purple theme)
            blockControlsRow2 = new VisualElement();
            blockControlsRow2.style.flexDirection = FlexDirection.Row;
            blockControlsRow2.style.flexWrap = Wrap.Wrap;
            blockControlsRow2.style.paddingLeft = 4;
            blockControlsRow2.style.paddingBottom = 1;
            blockControlsRow2.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            AddBlockTypeButton(blockControlsRow2, "Type", BlockType.Type, new Color(0.6f, 0.4f, 1f), "Type text into field");
            AddBlockTypeButton(blockControlsRow2, "Key", BlockType.KeyPress, new Color(0.63f, 0.5f, 1f), "Press keyboard key");
            AddBlockTypeButton(blockControlsRow2, "Key Hold", BlockType.KeyHold, new Color(0.56f, 0.44f, 0.94f), "Hold keys for duration");
            AddSeparator(blockControlsRow2);
            // Wait Actions (Yellow/Orange theme)
            AddBlockTypeButton(blockControlsRow2, "Wait", BlockType.Wait, new Color(1f, 0.75f, 0f), "Wait seconds");
            AddBlockTypeButton(blockControlsRow2, "Wait For", BlockType.WaitForElement, new Color(0.88f, 0.63f, 0f), "Wait for element to appear");
            AddBlockTypeButton(blockControlsRow2, "Scroll To", BlockType.ScrollUntil, new Color(0.31f, 0.69f, 0.31f), "Scroll until element found");
            blockPanel.Add(blockControlsRow2);

            // Row 3: Assertions, Scene, Data
            var blockControlsRow3 = new VisualElement();
            blockControlsRow3.style.flexDirection = FlexDirection.Row;
            blockControlsRow3.style.flexWrap = Wrap.Wrap;
            blockControlsRow3.style.paddingLeft = 4;
            blockControlsRow3.style.paddingBottom = 1;
            blockControlsRow3.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            AddBlockTypeButton(blockControlsRow3, "Assert", BlockType.Assert, new Color(1f, 0.4f, 0.5f), "Assert condition");
            AddBlockTypeButton(blockControlsRow3, "Log", BlockType.Log, new Color(0.5f, 0.5f, 0.5f), "Log message");
            AddBlockTypeButton(blockControlsRow3, "Screenshot", BlockType.Screenshot, new Color(0.25f, 0.75f, 0.75f), "Capture screenshot");
            AddSeparator(blockControlsRow3);
            AddBlockTypeButton(blockControlsRow3, "Scene", BlockType.LoadScene, new Color(0.7f, 0.5f, 0.3f), "Load scene");
            AddBlockTypeButton(blockControlsRow3, "Clear Data", BlockType.ClearPersistentData, new Color(0.6f, 0.3f, 0.3f), "Clear persistent data");
            AddBlockTypeButton(blockControlsRow3, "Load Data", BlockType.LoadPersistentData, new Color(0.4f, 0.6f, 0.4f), "Load data to persistent path");
            blockPanel.Add(blockControlsRow3);

            // Row 4: Recording, Custom, AI, Clear
            var blockControlsRow4 = new VisualElement();
            blockControlsRow4.style.flexDirection = FlexDirection.Row;
            blockControlsRow4.style.flexWrap = Wrap.Wrap;
            blockControlsRow4.style.paddingLeft = 4;
            blockControlsRow4.style.paddingBottom = 2;
            blockControlsRow4.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            AddBlockTypeButton(blockControlsRow4, "Record", BlockType.RecordAction, new Color(0.8f, 0.2f, 0.2f), "Record user action");
            AddBlockTypeButton(blockControlsRow4, "Custom", BlockType.CustomAction, new Color(0.5f, 0.5f, 0.6f), "Call custom action");
            AddBlockTypeButton(blockControlsRow4, "Code", BlockType.RunCode, new Color(0.4f, 0.5f, 0.6f), "Run C# code");
            AddSeparator(blockControlsRow4);
            AddBlockTypeButton(blockControlsRow4, "ForEach", BlockType.ForEach, new Color(0.88f, 0.63f, 0f), "Loop over matching elements");
            AddBlockTypeButton(blockControlsRow4, "End", BlockType.EndForEach, new Color(0.75f, 0.5f, 0f), "End ForEach loop");

            AddSeparator(blockControlsRow4);

            // AI button
            var aiBtn = new Button(ShowAIPromptPopup) { text = "AI" };
            aiBtn.style.backgroundColor = new Color(0.4f, 0.3f, 0.7f);
            aiBtn.style.color = Color.white;
            aiBtn.style.height = 18;
            aiBtn.style.fontSize = 10;
            aiBtn.style.paddingLeft = 6;
            aiBtn.style.paddingRight = 6;
            aiBtn.style.borderTopLeftRadius = 3;
            aiBtn.style.borderTopRightRadius = 3;
            aiBtn.style.borderBottomLeftRadius = 3;
            aiBtn.style.borderBottomRightRadius = 3;
            aiBtn.tooltip = "AI-assisted test creation";
            blockControlsRow4.Add(aiBtn);

            // Settings button (gear icon)
            var settingsBtn = new Button(OpenAISettings) { text = "⚙" };
            settingsBtn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f);
            settingsBtn.style.color = Color.white;
            settingsBtn.style.height = 18;
            settingsBtn.style.fontSize = 12;
            settingsBtn.style.marginLeft = 2;
            settingsBtn.style.paddingLeft = 4;
            settingsBtn.style.paddingRight = 4;
            settingsBtn.style.borderTopLeftRadius = 3;
            settingsBtn.style.borderTopRightRadius = 3;
            settingsBtn.style.borderBottomLeftRadius = 3;
            settingsBtn.style.borderBottomRightRadius = 3;
            settingsBtn.tooltip = "AI Settings";
            blockControlsRow4.Add(settingsBtn);

            // Clear All button
            var clearAllBtn = new Button(OnClearAllBlocks) { text = "Clear All" };
            clearAllBtn.style.backgroundColor = new Color(0.5f, 0.25f, 0.25f);
            clearAllBtn.style.color = Color.white;
            clearAllBtn.style.height = 18;
            clearAllBtn.style.fontSize = 10;
            clearAllBtn.style.marginLeft = 8;
            clearAllBtn.style.paddingLeft = 6;
            clearAllBtn.style.paddingRight = 6;
            clearAllBtn.style.borderTopLeftRadius = 3;
            clearAllBtn.style.borderTopRightRadius = 3;
            clearAllBtn.style.borderBottomLeftRadius = 3;
            clearAllBtn.style.borderBottomRightRadius = 3;
            clearAllBtn.tooltip = "Delete all blocks";
            blockControlsRow4.Add(clearAllBtn);

            blockPanel.Add(blockControlsRow4);

            // Block list
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            blockListContainer = new VisualElement();
            scroll.Add(blockListContainer);
            blockPanel.Add(scroll);

            root.Add(blockPanel);
        }

        private void OnClearAllBlocks()
        {
            if (currentTest == null || currentTest.blocks == null || currentTest.blocks.Count == 0)
            {
                statusLabel.text = "No blocks to clear";
                return;
            }

            if (!EditorUtility.DisplayDialog("Clear All Blocks",
                $"Delete all {currentTest.blocks.Count} blocks?\n\nThis cannot be undone.",
                "Delete All", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(currentTest, "Clear all blocks");
            currentTest.blocks.Clear();
            EditorUtility.SetDirty(currentTest);

            editingBlockIndex = -1;
            blockHasUnsavedChanges = false;
            selectedBlockIndex = -1;

            RefreshBlockList();
            statusLabel.text = "All blocks cleared";
        }

        private void AddSeparator(VisualElement parent)
        {
            var sep = new VisualElement();
            sep.style.width = 1;
            sep.style.height = 14;
            sep.style.marginLeft = 4;
            sep.style.marginRight = 4;
            sep.style.marginTop = 2;
            sep.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
            parent.Add(sep);
        }

        private void AddBlockTypeButton(VisualElement parent, string label, BlockType type, Color color, string tooltip = null)
        {
            var btn = new Button(() => AddBlock(type)) { text = label };
            btn.style.backgroundColor = color;
            btn.style.color = Color.white;
            btn.style.height = 18;
            btn.style.fontSize = 10;
            btn.style.marginRight = 2;
            btn.style.paddingLeft = 4;
            btn.style.paddingRight = 4;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            if (!string.IsNullOrEmpty(tooltip))
                btn.tooltip = tooltip;
            parent.Add(btn);
        }

        private void BuildStatusBar()
        {
            // AI Status Panel (hidden by default)
            aiStatusPanel = new VisualElement();
            aiStatusPanel.style.flexDirection = FlexDirection.Column;
            aiStatusPanel.style.backgroundColor = new Color(0.2f, 0.15f, 0.3f);
            aiStatusPanel.style.paddingLeft = 6;
            aiStatusPanel.style.paddingRight = 6;
            aiStatusPanel.style.paddingTop = 4;
            aiStatusPanel.style.paddingBottom = 4;
            aiStatusPanel.style.borderTopWidth = 1;
            aiStatusPanel.style.borderTopColor = new Color(0.5f, 0.3f, 0.7f);
            aiStatusPanel.style.display = DisplayStyle.None;

            // Header row with state and stop button
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            var aiIcon = new Label("🤖");
            aiIcon.style.fontSize = 12;
            aiIcon.style.marginRight = 4;
            headerRow.Add(aiIcon);

            aiStateLabel = new Label("AI: Idle");
            aiStateLabel.style.color = new Color(0.8f, 0.7f, 1f);
            aiStateLabel.style.fontSize = 11;
            aiStateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            aiStateLabel.style.flexGrow = 1;
            headerRow.Add(aiStateLabel);

            aiStopButton = new Button(StopAI) { text = "■ Stop" };
            aiStopButton.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            aiStopButton.style.color = Color.white;
            aiStopButton.style.height = 18;
            aiStopButton.style.fontSize = 10;
            aiStopButton.style.paddingLeft = 6;
            aiStopButton.style.paddingRight = 6;
            aiStopButton.style.borderTopLeftRadius = 3;
            aiStopButton.style.borderTopRightRadius = 3;
            aiStopButton.style.borderBottomLeftRadius = 3;
            aiStopButton.style.borderBottomRightRadius = 3;
            headerRow.Add(aiStopButton);

            aiStatusPanel.Add(headerRow);

            // Progress bar
            aiProgressBar = new ProgressBar();
            aiProgressBar.style.height = 4;
            aiProgressBar.style.marginTop = 4;
            aiProgressBar.style.marginBottom = 2;
            aiStatusPanel.Add(aiProgressBar);

            // Reasoning label
            aiReasoningLabel = new Label("");
            aiReasoningLabel.style.color = new Color(0.7f, 0.7f, 0.8f);
            aiReasoningLabel.style.fontSize = 9;
            aiReasoningLabel.style.whiteSpace = WhiteSpace.Normal;
            aiReasoningLabel.style.maxHeight = 32;
            aiReasoningLabel.style.overflow = Overflow.Hidden;
            aiStatusPanel.Add(aiReasoningLabel);

            root.Add(aiStatusPanel);

            // Regular status bar
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.paddingLeft = 4;
            bar.style.paddingTop = 2;
            bar.style.paddingBottom = 2;
            bar.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);

            statusLabel = new Label("Select a scene");
            statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            statusLabel.style.fontSize = 10;
            bar.Add(statusLabel);

            root.Add(bar);
        }

        private void UpdateAIStatusPanel(bool show, string state = null, string reasoning = null, float progress = -1)
        {
            if (aiStatusPanel == null) return;

            aiStatusPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            if (show)
            {
                if (state != null)
                {
                    aiStateLabel.text = $"AI: {state}";

                    // Color based on state
                    aiStateLabel.style.color = state switch
                    {
                        "Running" or "Analyzing..." => new Color(0.5f, 1f, 0.5f),
                        "Executing action..." => new Color(1f, 0.9f, 0.4f),
                        "Completed" => new Color(0.4f, 1f, 0.4f),
                        "Failed" or "Error" => new Color(1f, 0.4f, 0.4f),
                        "Stopping..." => new Color(1f, 0.6f, 0.3f),
                        _ => new Color(0.8f, 0.7f, 1f)
                    };
                }

                if (reasoning != null)
                {
                    // Truncate long reasoning
                    var truncated = reasoning.Length > 120 ? reasoning.Substring(0, 117) + "..." : reasoning;
                    aiReasoningLabel.text = truncated;
                }

                if (progress >= 0)
                {
                    aiProgressBar.value = progress;
                    aiProgressBar.title = $"{(int)(progress * 100)}%";
                }
            }
        }

        // === UI State Management ===

        private void RefreshUI()
        {
            // Guard against being called before CreateGUI
            if (root == null || statusLabel == null) return;

            var hasScene = currentTest != null && !string.IsNullOrEmpty(currentTest.startScene);
            var hasBlocks = currentTest != null && currentTest.blocks != null && currentTest.blocks.Count > 0;
            var inPlayMode = EditorApplication.isPlaying;

            // Play button: enabled when we have a scene and blocks
            playTestBtn?.SetEnabled(hasScene && hasBlocks);

            // Start button: enabled when we have a scene and NOT in play mode
            startEditBtn?.SetEnabled(hasScene && !inPlayMode);

            // Stop button: enabled when in play mode
            stopBtn?.SetEnabled(inPlayMode);

            // Block controls: enabled when in play mode
            if (blockControls != null)
            {
                blockControls.SetEnabled(inPlayMode);
                blockControls.style.opacity = inPlayMode ? 1f : 0.5f;
            }
            if (blockControlsRow2 != null)
            {
                blockControlsRow2.SetEnabled(inPlayMode);
                blockControlsRow2.style.opacity = inPlayMode ? 1f : 0.5f;
            }

            // Update status
            if (!hasScene)
                statusLabel.text = "Select a scene to begin";
            else if (!inPlayMode)
                statusLabel.text = "Click 'Edit' to enter play mode";
            else
                statusLabel.text = "Add blocks and configure them";

            RefreshBlockList();
        }

        // === Block List ===

        private void RefreshBlockList()
        {
            if (blockListContainer == null) return;
            blockListContainer.Clear();

            if (currentTest == null || currentTest.blocks == null || currentTest.blocks.Count == 0)
            {
                var empty = new Label("No blocks - add using buttons above");
                empty.style.color = new Color(0.4f, 0.4f, 0.4f);
                empty.style.paddingTop = 8;
                empty.style.paddingLeft = 4;
                blockListContainer.Add(empty);
                return;
            }

            // Calculate indent levels for ForEach/EndForEach nesting
            int indentLevel = 0;
            for (int i = 0; i < currentTest.blocks.Count; i++)
            {
                var block = currentTest.blocks[i];
                // EndForEach decreases indent before rendering
                if (block.type == BlockType.EndForEach && indentLevel > 0)
                    indentLevel--;

                blockListContainer.Add(CreateBlockRow(block, i, indentLevel));

                // ForEach increases indent after rendering
                if (block.type == BlockType.ForEach)
                    indentLevel++;
            }
        }

        private VisualElement CreateBlockRow(VisualBlock block, int index, int indentLevel = 0)
        {
            var isEditing = (index == editingBlockIndex);
            var c = block.GetBlockColor();

            // Container for the entire row including indent
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            // Indentation visual (loop bracket)
            if (indentLevel > 0)
            {
                var indent = new VisualElement();
                indent.style.width = 12 * indentLevel;
                indent.style.borderLeftWidth = 2;
                indent.style.borderLeftColor = new Color(0.88f, 0.63f, 0f, 0.6f); // Gold color for loop
                indent.style.marginLeft = 4;
                container.Add(indent);
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 20;
            row.style.marginBottom = 1;
            row.style.paddingLeft = 2;
            row.style.paddingRight = 2;
            row.style.flexGrow = 1;
            row.style.backgroundColor = isEditing ? new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f, 1f) : new Color(0.22f, 0.22f, 0.22f);
            row.style.borderLeftWidth = 3;
            row.style.borderLeftColor = c;

            // Index
            row.Add(new Label($"{index + 1}") { style = { width = 14, fontSize = 9, color = new Color(0.5f, 0.5f, 0.5f) } });

            // Type label (short)
            var typeNames = new Dictionary<BlockType, string> {
                { BlockType.Click, "CLK" }, { BlockType.DoubleClick, "DBL" }, { BlockType.Hold, "HLD" },
                { BlockType.Type, "TYP" }, { BlockType.KeyPress, "KEY" }, { BlockType.KeyHold, "KyH" },
                { BlockType.SetSlider, "SLD" }, { BlockType.SetScrollbar, "SBR" },
                { BlockType.Drag, "DRG" }, { BlockType.Scroll, "SCR" }, { BlockType.ScrollUntil, "S4" },
                { BlockType.Wait, "W" }, { BlockType.WaitForElement, "W4" },
                { BlockType.Assert, "AST" }, { BlockType.Log, "LOG" },
                { BlockType.Screenshot, "SS" }, { BlockType.LoadScene, "SCN" },
                { BlockType.Swipe, "SWP" }, { BlockType.Pinch, "PCH" },
                { BlockType.TwoFingerSwipe, "2SW" }, { BlockType.Rotate, "ROT" },
                { BlockType.ForEach, "FOR" }, { BlockType.EndForEach, "END" }
            };
            var typeLbl = new Label(typeNames.GetValueOrDefault(block.type, "?"));
            typeLbl.style.width = 26;
            typeLbl.style.fontSize = 9;
            typeLbl.style.color = c;
            typeLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(typeLbl);

            // Content based on type - all inline, all editable when in edit mode
            BuildRowContent(row, block, index, isEditing);

            // Spacer
            row.Add(new VisualElement { style = { flexGrow = 1 } });

            // Unsaved marker
            if (isEditing && blockHasUnsavedChanges)
                row.Add(new Label("*") { style = { color = Color.yellow, marginRight = 2 } });

            // Action buttons
            if (isEditing)
            {
                var saveBtn = new Button(() => SaveBlock(index)) { text = "✓" };
                saveBtn.style.width = 20; saveBtn.style.height = 16; saveBtn.style.fontSize = 10;
                saveBtn.style.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
                saveBtn.SetEnabled(blockHasUnsavedChanges);
                row.Add(saveBtn);
            }
            else
            {
                var editBtn = new Button(() => StartEditingBlock(index)) { text = "✎" };
                editBtn.style.width = 20; editBtn.style.height = 16; editBtn.style.fontSize = 10;
                editBtn.SetEnabled(EditorApplication.isPlaying);
                row.Add(editBtn);
            }

            var runBtn = new Button(() => RunToBlock(index)) { text = "▶" };
            runBtn.style.width = 18; runBtn.style.height = 16; runBtn.style.fontSize = 9;
            runBtn.SetEnabled(EditorApplication.isPlaying && (!isEditing || !blockHasUnsavedChanges));
            row.Add(runBtn);

            var delBtn = new Button(() => DeleteBlock(index)) { text = "×" };
            delBtn.style.width = 18; delBtn.style.height = 16; delBtn.style.fontSize = 11;
            delBtn.style.color = new Color(1f, 0.4f, 0.4f);
            row.Add(delBtn);

            container.Add(row);
            return container;
        }

        private void BuildRowContent(VisualElement row, VisualBlock block, int index, bool isEditing)
        {
            // Allow target button editing when isEditing (even outside play mode for AI-created blocks)
            // But visual picking requires play mode
            var canEditTarget = isEditing;
            var canVisualPick = EditorApplication.isPlaying && isEditing;

            switch (block.type)
            {
                case BlockType.Click:
                    AddTargetButton(row, block, index, canEditTarget, 200, isEditing);
                    break;

                case BlockType.Type:
                    AddTargetButton(row, block, index, canEditTarget, 120, isEditing);
                    var txt = new TextField { value = block.text ?? "" };
                    txt.style.width = 80; txt.style.height = 16; txt.style.fontSize = 10;
                    txt.SetEnabled(isEditing);
                    txt.RegisterValueChangedCallback(e => { block.text = e.newValue; OnBlockChanged(index); });
                    row.Add(txt);
                    if (isEditing)
                    {
                        var clr = new Toggle("C") { value = block.clearFirst, tooltip = "Clear first" };
                        clr.style.marginLeft = 4;
                        clr.RegisterValueChangedCallback(e => { block.clearFirst = e.newValue; OnBlockChanged(index); });
                        row.Add(clr);
                        var ent = new Toggle("↵") { value = block.pressEnter, tooltip = "Press Enter" };
                        ent.RegisterValueChangedCallback(e => { block.pressEnter = e.newValue; OnBlockChanged(index); });
                        row.Add(ent);
                    }
                    break;

                case BlockType.Wait:
                    var wf = new FloatField { value = block.waitSeconds };
                    wf.style.width = 40; wf.style.height = 16;
                    wf.SetEnabled(isEditing);
                    wf.RegisterValueChangedCallback(e => { block.waitSeconds = e.newValue; OnBlockChanged(index); });
                    row.Add(wf);
                    row.Add(new Label("s") { style = { fontSize = 10 } });
                    break;

                case BlockType.Assert:
                    AddTargetButton(row, block, index, canEditTarget, 120, isEditing);
                    if (isEditing)
                    {
                        var conds = new List<string> { "exists", "!exists", "=text", "~text" };
                        var cd = new DropdownField(conds, Math.Min((int)block.assertCondition, conds.Count - 1));
                        cd.style.width = 60; cd.style.height = 16; cd.style.fontSize = 10;
                        cd.RegisterValueChangedCallback(e => { block.assertCondition = (AssertCondition)cd.index; OnBlockChanged(index); RefreshBlockList(); });
                        row.Add(cd);
                        if (block.assertCondition == AssertCondition.TextEquals || block.assertCondition == AssertCondition.TextContains)
                        {
                            var exp = new TextField { value = block.assertExpected ?? "" };
                            exp.style.width = 60; exp.style.height = 16; exp.style.fontSize = 10;
                            exp.RegisterValueChangedCallback(e => { block.assertExpected = e.newValue; OnBlockChanged(index); });
                            row.Add(exp);
                        }
                    }
                    else
                    {
                        var condText = block.assertCondition switch { AssertCondition.ElementExists => "exists", AssertCondition.ElementNotExists => "!exists", AssertCondition.TextEquals => $"=\"{block.assertExpected}\"", AssertCondition.TextContains => $"~\"{block.assertExpected}\"", _ => "?" };
                        row.Add(new Label(condText) { style = { fontSize = 10, color = new Color(0.7f, 0.7f, 0.7f) } });
                    }
                    break;

                case BlockType.Drag:
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    row.Add(new Label("→") { style = { marginLeft = 2, marginRight = 2 } });
                    if (block.dragTarget != null && block.dragTarget.IsValid())
                    {
                        var toBtn = new Button(() => ShowDragTargetPicker(block, index)) { text = block.dragTarget.GetDisplayText() };
                        toBtn.style.maxWidth = 100; toBtn.style.height = 16; toBtn.style.fontSize = 10;
                        toBtn.SetEnabled(canEditTarget);
                        row.Add(toBtn);
                    }
                    else
                    {
                        if (isEditing)
                        {
                            var dirs = new List<string> { "up", "down", "left", "right" };
                            var dd = new DropdownField(dirs, Math.Max(0, dirs.IndexOf(block.dragDirection ?? "down")));
                            dd.style.width = 50; dd.style.height = 16; dd.style.fontSize = 10;
                            dd.RegisterValueChangedCallback(e => { block.dragDirection = e.newValue; OnBlockChanged(index); });
                            row.Add(dd);
                            var dist = new FloatField { value = block.dragDistance };
                            dist.style.width = 35; dist.style.height = 16;
                            dist.RegisterValueChangedCallback(e => { block.dragDistance = e.newValue; OnBlockChanged(index); });
                            row.Add(dist);
                        }
                        else
                        {
                            row.Add(new Label($"{block.dragDirection} {block.dragDistance:P0}") { style = { fontSize = 10 } });
                        }
                    }
                    if (isEditing)
                    {
                        var dur = new FloatField { value = block.dragDuration };
                        dur.style.width = 30; dur.style.height = 16; dur.style.marginLeft = 4;
                        dur.RegisterValueChangedCallback(e => { block.dragDuration = e.newValue; OnBlockChanged(index); });
                        row.Add(dur);
                        row.Add(new Label("s") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.Scroll:
                    AddTargetButton(row, block, index, canEditTarget, 120, isEditing);
                    if (isEditing)
                    {
                        var dirs = new List<string> { "up", "down", "left", "right" };
                        var sd = new DropdownField(dirs, Math.Max(0, dirs.IndexOf(block.scrollDirection ?? "down")));
                        sd.style.width = 50; sd.style.height = 16; sd.style.fontSize = 10;
                        sd.RegisterValueChangedCallback(e => { block.scrollDirection = e.newValue; OnBlockChanged(index); });
                        row.Add(sd);
                    }
                    else
                    {
                        row.Add(new Label(block.scrollDirection ?? "down") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.DoubleClick:
                    AddTargetButton(row, block, index, canEditTarget, 200, isEditing);
                    break;

                case BlockType.SetSlider:
                    AddTargetButton(row, block, index, canEditTarget, 160, isEditing);
                    if (isEditing)
                    {
                        var sliderVal = new FloatField { value = block.sliderValue };
                        sliderVal.style.width = 40; sliderVal.style.height = 16;
                        sliderVal.RegisterValueChangedCallback(e => { block.sliderValue = Mathf.Clamp(e.newValue, 0f, 100f); OnBlockChanged(index); });
                        row.Add(sliderVal);
                    }
                    else
                    {
                        row.Add(new Label($"{block.sliderValue:F0}") { style = { fontSize = 10 } });
                    }
                    row.Add(new Label("%") { style = { fontSize = 10 } });
                    break;

                case BlockType.SetScrollbar:
                    AddTargetButton(row, block, index, canEditTarget, 160, isEditing);
                    if (isEditing)
                    {
                        var scrollbarVal = new FloatField { value = block.scrollbarValue };
                        scrollbarVal.style.width = 40; scrollbarVal.style.height = 16;
                        scrollbarVal.RegisterValueChangedCallback(e => { block.scrollbarValue = Mathf.Clamp(e.newValue, 0f, 100f); OnBlockChanged(index); });
                        row.Add(scrollbarVal);
                    }
                    else
                    {
                        row.Add(new Label($"{block.scrollbarValue:F0}") { style = { fontSize = 10 } });
                    }
                    row.Add(new Label("%") { style = { fontSize = 10 } });
                    break;

                case BlockType.Hold:
                    AddTargetButton(row, block, index, canEditTarget, 160, isEditing);
                    var hf = new FloatField { value = block.holdSeconds };
                    hf.style.width = 35; hf.style.height = 16;
                    hf.SetEnabled(isEditing);
                    hf.RegisterValueChangedCallback(e => { block.holdSeconds = e.newValue; OnBlockChanged(index); });
                    row.Add(hf);
                    row.Add(new Label("s") { style = { fontSize = 10 } });
                    break;

                case BlockType.KeyPress:
                    if (isEditing)
                    {
                        var keys = new List<string> { "Escape", "Enter", "Space", "Tab", "Backspace", "Delete", "UpArrow", "DownArrow", "LeftArrow", "RightArrow", "F1", "F5", "F11" };
                        var keyIdx = Math.Max(0, keys.IndexOf(block.keyName ?? "Escape"));
                        var kd = new DropdownField(keys, keyIdx);
                        kd.style.width = 80; kd.style.height = 16; kd.style.fontSize = 10;
                        kd.RegisterValueChangedCallback(e => { block.keyName = e.newValue; OnBlockChanged(index); });
                        row.Add(kd);
                    }
                    else
                    {
                        row.Add(new Label(block.keyName ?? "Escape") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.KeyHold:
                    if (isEditing)
                    {
                        // Key selection dropdown with movement keys
                        var holdKeys = new List<string> { "W", "A", "S", "D", "W,A", "W,D", "S,A", "S,D", "LeftShift,W", "Space", "UpArrow", "DownArrow", "LeftArrow", "RightArrow" };
                        var currentKey = block.keyHoldKeys ?? "W";
                        var khIdx = holdKeys.IndexOf(currentKey);
                        if (khIdx < 0) khIdx = 0;
                        var khd = new DropdownField(holdKeys, khIdx);
                        khd.style.width = 70; khd.style.height = 16; khd.style.fontSize = 10;
                        khd.RegisterValueChangedCallback(e => { block.keyHoldKeys = e.newValue; OnBlockChanged(index); });
                        row.Add(khd);
                        // Duration field
                        var khDur = new FloatField { value = block.keyHoldDuration };
                        khDur.style.width = 35; khDur.style.height = 16;
                        khDur.RegisterValueChangedCallback(e => { block.keyHoldDuration = e.newValue; OnBlockChanged(index); });
                        row.Add(khDur);
                        row.Add(new Label("s") { style = { fontSize = 10 } });
                    }
                    else
                    {
                        row.Add(new Label($"[{block.keyHoldKeys ?? "W"}] {block.keyHoldDuration}s") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.WaitForElement:
                    AddTargetButton(row, block, index, canEditTarget, 160, isEditing);
                    var tf = new FloatField { value = block.waitTimeout };
                    tf.style.width = 35; tf.style.height = 16;
                    tf.SetEnabled(isEditing);
                    tf.RegisterValueChangedCallback(e => { block.waitTimeout = e.newValue; OnBlockChanged(index); });
                    row.Add(tf);
                    row.Add(new Label("s") { style = { fontSize = 10 } });
                    break;

                case BlockType.ScrollUntil:
                    // Target element to find
                    row.Add(new Label("Find:") { style = { fontSize = 10 } });
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    // Scroll container (optional)
                    row.Add(new Label("In:") { style = { fontSize = 10, marginLeft = 4 } });
                    AddScrollContainerButton(row, block, index, canEditTarget, 80);
                    // Max attempts
                    var maxf = new IntegerField { value = block.scrollMaxAttempts };
                    maxf.style.width = 30; maxf.style.height = 16;
                    maxf.SetEnabled(isEditing);
                    maxf.RegisterValueChangedCallback(e => { block.scrollMaxAttempts = e.newValue; OnBlockChanged(index); });
                    row.Add(maxf);
                    row.Add(new Label("tries") { style = { fontSize = 10 } });
                    break;

                case BlockType.Screenshot:
                    var ssf = new TextField { value = block.screenshotName ?? "" };
                    ssf.style.width = 100; ssf.style.height = 16; ssf.style.fontSize = 10;
                    ssf.SetEnabled(isEditing);
                    ssf.RegisterValueChangedCallback(e => { block.screenshotName = e.newValue; OnBlockChanged(index); });
                    row.Add(ssf);
                    break;

                case BlockType.Log:
                    var logf = new TextField { value = block.logMessage ?? "" };
                    logf.style.width = 150; logf.style.height = 16; logf.style.fontSize = 10;
                    logf.SetEnabled(isEditing);
                    logf.RegisterValueChangedCallback(e => { block.logMessage = e.newValue; OnBlockChanged(index); });
                    row.Add(logf);
                    break;

                case BlockType.LoadScene:
                    var scnf = new TextField { value = block.sceneName ?? "" };
                    scnf.style.width = 100; scnf.style.height = 16; scnf.style.fontSize = 10;
                    scnf.SetEnabled(isEditing);
                    scnf.RegisterValueChangedCallback(e => { block.sceneName = e.newValue; OnBlockChanged(index); });
                    row.Add(scnf);
                    if (isEditing)
                    {
                        var addv = new Toggle("+") { value = block.additiveLoad, tooltip = "Additive load" };
                        addv.RegisterValueChangedCallback(e => { block.additiveLoad = e.newValue; OnBlockChanged(index); });
                        row.Add(addv);
                    }
                    break;

                case BlockType.Swipe:
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    if (isEditing)
                    {
                        var dirs = new List<string> { "up", "down", "left", "right" };
                        var sd = new DropdownField(dirs, Math.Max(0, dirs.IndexOf(block.swipeDirection ?? "up")));
                        sd.style.width = 50; sd.style.height = 16; sd.style.fontSize = 10;
                        sd.RegisterValueChangedCallback(e => { block.swipeDirection = e.newValue; OnBlockChanged(index); });
                        row.Add(sd);
                        var dist = new FloatField { value = block.swipeDistance };
                        dist.style.width = 35; dist.style.height = 16;
                        dist.RegisterValueChangedCallback(e => { block.swipeDistance = Mathf.Clamp01(e.newValue); OnBlockChanged(index); });
                        row.Add(dist);
                        row.Add(new Label("dist") { style = { fontSize = 9 } });
                    }
                    else
                    {
                        row.Add(new Label($"{block.swipeDirection} {block.swipeDistance:P0}") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.Pinch:
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    if (isEditing)
                    {
                        var scale = new FloatField { value = block.pinchScale };
                        scale.style.width = 40; scale.style.height = 16;
                        scale.RegisterValueChangedCallback(e => { block.pinchScale = Mathf.Max(0.1f, e.newValue); OnBlockChanged(index); });
                        row.Add(scale);
                        row.Add(new Label("scale") { style = { fontSize = 9 } });
                        var dur = new FloatField { value = block.pinchDuration };
                        dur.style.width = 35; dur.style.height = 16;
                        dur.RegisterValueChangedCallback(e => { block.pinchDuration = Mathf.Max(0.1f, e.newValue); OnBlockChanged(index); });
                        row.Add(dur);
                        row.Add(new Label("s") { style = { fontSize = 9 } });
                    }
                    else
                    {
                        row.Add(new Label($"{(block.pinchScale < 1 ? "in" : "out")} {block.pinchScale:F1}x") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.TwoFingerSwipe:
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    if (isEditing)
                    {
                        var dirs = new List<string> { "up", "down", "left", "right" };
                        var sd = new DropdownField(dirs, Math.Max(0, dirs.IndexOf(block.swipeDirection ?? "up")));
                        sd.style.width = 50; sd.style.height = 16; sd.style.fontSize = 10;
                        sd.RegisterValueChangedCallback(e => { block.swipeDirection = e.newValue; OnBlockChanged(index); });
                        row.Add(sd);
                        var dist = new FloatField { value = block.swipeDistance };
                        dist.style.width = 35; dist.style.height = 16;
                        dist.RegisterValueChangedCallback(e => { block.swipeDistance = Mathf.Clamp01(e.newValue); OnBlockChanged(index); });
                        row.Add(dist);
                        row.Add(new Label("dist") { style = { fontSize = 9 } });
                    }
                    else
                    {
                        row.Add(new Label($"{block.swipeDirection} {block.swipeDistance:P0}") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.Rotate:
                    AddTargetButton(row, block, index, canEditTarget, 100, isEditing);
                    if (isEditing)
                    {
                        var deg = new FloatField { value = block.rotateDegrees };
                        deg.style.width = 45; deg.style.height = 16;
                        deg.RegisterValueChangedCallback(e => { block.rotateDegrees = e.newValue; OnBlockChanged(index); });
                        row.Add(deg);
                        row.Add(new Label("°") { style = { fontSize = 9 } });
                        var dur = new FloatField { value = block.rotateDuration };
                        dur.style.width = 35; dur.style.height = 16;
                        dur.RegisterValueChangedCallback(e => { block.rotateDuration = Mathf.Max(0.1f, e.newValue); OnBlockChanged(index); });
                        row.Add(dur);
                        row.Add(new Label("s") { style = { fontSize = 9 } });
                    }
                    else
                    {
                        row.Add(new Label($"{(block.rotateDegrees >= 0 ? "CW" : "CCW")} {Mathf.Abs(block.rotateDegrees)}°") { style = { fontSize = 10 } });
                    }
                    break;

                case BlockType.ForEach:
                    AddTargetButton(row, block, index, canEditTarget, 120, isEditing);
                    if (isEditing)
                    {
                        row.Add(new Label("as") { style = { fontSize = 9, marginLeft = 2 } });
                        var varField = new TextField { value = block.forEachVariable ?? "item" };
                        varField.style.width = 50; varField.style.height = 16; varField.style.fontSize = 10;
                        varField.RegisterValueChangedCallback(e => { block.forEachVariable = e.newValue; OnBlockChanged(index); });
                        row.Add(varField);
                        row.Add(new Label("max") { style = { fontSize = 9, marginLeft = 4 } });
                        var maxField = new IntegerField { value = block.forEachMaxIterations };
                        maxField.style.width = 35; maxField.style.height = 16;
                        maxField.RegisterValueChangedCallback(e => { block.forEachMaxIterations = Math.Max(0, e.newValue); OnBlockChanged(index); });
                        row.Add(maxField);
                    }
                    else
                    {
                        row.Add(new Label($"as {block.forEachVariable ?? "item"}") { style = { fontSize = 10, marginLeft = 2 } });
                    }
                    break;

                case BlockType.EndForEach:
                    row.Add(new Label("End ForEach") { style = { fontSize = 10, color = new Color(0.75f, 0.5f, 0f) } });
                    break;
            }
        }

        private void AddTargetButton(VisualElement row, VisualBlock block, int index, bool canEdit, int maxWidth, bool canEditChain = false)
        {
            var hasValidTarget = block.target != null && block.target.IsValid();
            var chainEditable = canEdit || canEditChain;

            if (canEdit)
            {
                // Editing mode: show dropdown + text field inline
                AddTargetEditor(row, block, index, maxWidth, chainEditable && hasValidTarget);
            }
            else
            {
                // Display mode: show readonly label
                var displayText = hasValidTarget ? GetFullTargetDisplay(block.target) : "[target]";
                var targetLabel = new Label(displayText);
                targetLabel.style.fontSize = 10;
                targetLabel.style.color = hasValidTarget ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                targetLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                targetLabel.style.overflow = Overflow.Hidden;
                targetLabel.style.textOverflow = TextOverflow.Ellipsis;
                targetLabel.style.maxWidth = maxWidth;
                targetLabel.style.paddingLeft = 4;
                targetLabel.style.paddingRight = 4;
                row.Add(targetLabel);
            }
        }

        private void AddTargetEditor(VisualElement row, VisualBlock block, int index, int maxWidth, bool showChainButton)
        {
            var searchTypes = new List<string> { "Text", "Name", "Type", "Path", "Adjacent", "Near", "Sprite", "Tag", "Any" };

            // Get current search type
            var currentType = block.target?.query?.searchBase ?? "text";
            var typeIndex = searchTypes.FindIndex(t => t.ToLower() == currentType.ToLower());
            if (typeIndex < 0) typeIndex = 0;

            // Search type dropdown
            var typeDropdown = new DropdownField(searchTypes, typeIndex);
            typeDropdown.style.width = 65;
            typeDropdown.style.height = 16;
            typeDropdown.style.fontSize = 10;
            typeDropdown.style.marginRight = 2;
            row.Add(typeDropdown);

            // Value text field with suggestions dropdown button
            var valueContainer = new VisualElement();
            valueContainer.style.flexDirection = FlexDirection.Row;
            valueContainer.style.marginRight = 2;

            var valueField = new TextField { value = block.target?.query?.value ?? "" };
            valueField.style.width = maxWidth - 120;
            valueField.style.minWidth = 50;
            valueField.style.height = 16;
            valueField.style.fontSize = 10;
            valueField.style.marginRight = 0;
            valueContainer.Add(valueField);

            row.Add(valueContainer);

            // Direction dropdown (only for Adjacent/Near)
            var directions = new List<string> { "Right", "Left", "Above", "Below" };
            var currentDir = block.target?.query?.direction ?? "right";
            var dirIndex = directions.FindIndex(d => d.ToLower() == currentDir.ToLower());
            if (dirIndex < 0) dirIndex = 0;

            var dirDropdown = new DropdownField(directions, dirIndex);
            dirDropdown.style.width = 55;
            dirDropdown.style.height = 16;
            dirDropdown.style.fontSize = 10;
            dirDropdown.style.marginRight = 2;
            dirDropdown.style.display = (currentType == "adjacent" || currentType == "near") ? DisplayStyle.Flex : DisplayStyle.None;
            row.Add(dirDropdown);

            // Suggestions dropdown button (added after dirDropdown so we can reference it)
            var suggestBtn = new Button(() => ShowValueSuggestions(valueField, typeDropdown, dirDropdown, block, index))
            {
                text = "▼",
                tooltip = "Show suggestions from scene"
            };
            suggestBtn.style.width = 16;
            suggestBtn.style.height = 16;
            suggestBtn.style.fontSize = 8;
            suggestBtn.style.paddingLeft = 0;
            suggestBtn.style.paddingRight = 0;
            suggestBtn.style.marginLeft = 0;
            suggestBtn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f);
            suggestBtn.style.borderTopLeftRadius = 0;
            suggestBtn.style.borderBottomLeftRadius = 0;
            suggestBtn.style.borderTopRightRadius = 3;
            suggestBtn.style.borderBottomRightRadius = 3;
            valueContainer.Add(suggestBtn);

            // Update visibility when type changes - use SetValueWithoutNotify to avoid focus stealing
            typeDropdown.RegisterValueChangedCallback(e =>
            {
                var newType = e.newValue.ToLower();
                dirDropdown.style.display = (newType == "adjacent" || newType == "near") ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateTargetFromEditor(block, index, newType, valueField.value, dirDropdown.value.ToLower());
            });

            // Update target when value changes
            valueField.RegisterValueChangedCallback(e =>
            {
                var type = typeDropdown.value.ToLower();
                UpdateTargetFromEditor(block, index, type, e.newValue, dirDropdown.value.ToLower());
            });

            // Update target when direction changes
            dirDropdown.RegisterValueChangedCallback(e =>
            {
                var type = typeDropdown.value.ToLower();
                UpdateTargetFromEditor(block, index, type, valueField.value, e.newValue.ToLower());
            });

            // Chain display and add button
            var hasChain = block.target?.query?.chain != null && block.target.query.chain.Count > 0;
            if (hasChain)
            {
                var chainLabel = new Label(GetChainInlineDisplay(block.target.query.chain));
                chainLabel.style.fontSize = 9;
                chainLabel.style.color = new Color(0.6f, 0.8f, 0.6f);
                chainLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                chainLabel.style.overflow = Overflow.Hidden;
                chainLabel.style.textOverflow = TextOverflow.Ellipsis;
                chainLabel.style.maxWidth = 100;
                chainLabel.RegisterCallback<ClickEvent>(evt => ShowChainPicker(block, index));
                row.Add(chainLabel);
            }

            // Show "+" button when we have a valid target value
            if (showChainButton || !string.IsNullOrEmpty(valueField.value))
            {
                var chainBtn = new Button(() => ShowChainPicker(block, index))
                {
                    text = "+",
                    tooltip = "Add search chain filter"
                };
                chainBtn.style.width = 18;
                chainBtn.style.height = 16;
                chainBtn.style.fontSize = 11;
                chainBtn.style.marginLeft = 0;
                chainBtn.style.paddingLeft = 0;
                chainBtn.style.paddingRight = 0;
                chainBtn.style.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
                chainBtn.style.borderTopLeftRadius = 3;
                chainBtn.style.borderTopRightRadius = 3;
                chainBtn.style.borderBottomLeftRadius = 3;
                chainBtn.style.borderBottomRightRadius = 3;
                row.Add(chainBtn);
            }
        }

        private void UpdateTargetFromEditor(VisualBlock block, int index, string searchType, string value, string direction)
        {
            if (string.IsNullOrEmpty(value))
            {
                // Clear target if value is empty
                if (block.target != null)
                {
                    Undo.RecordObject(currentTest, "Clear target");
                    block.target = null;
                    OnBlockChanged(index);
                }
                return;
            }

            Undo.RecordObject(currentTest, "Set target");

            // Preserve existing chain
            var existingChain = block.target?.query?.chain;

            // Create new selector based on type
            block.target = searchType switch
            {
                "text" => ElementSelector.ByText(value),
                "name" => ElementSelector.ByName(value),
                "type" => ElementSelector.ByType(value),
                "path" => ElementSelector.ByPath(value),
                "adjacent" => ElementSelector.Adjacent(value, direction),
                "near" => ElementSelector.NearTo(value, direction),
                "texture" => ElementSelector.ByTexture(value),
                "tag" => ElementSelector.ByTag(value),
                "any" => ElementSelector.ByAny(value),
                _ => ElementSelector.ByText(value)
            };

            // Restore chain
            if (existingChain != null && existingChain.Count > 0)
            {
                block.target.query.chain = existingChain;
            }

            OnBlockChanged(index);
        }

        private void ShowValueSuggestions(TextField valueField, DropdownField typeDropdown, DropdownField dirDropdown, VisualBlock block, int index)
        {
            var searchType = typeDropdown.value.ToLower();
            var suggestions = GetSuggestionsForType(searchType);

            if (suggestions.Count == 0)
            {
                statusLabel.text = "No suggestions available - enter play mode to discover elements";
                return;
            }

            // Create popup menu with suggestions
            var menu = new GenericMenu();

            foreach (var suggestion in suggestions.Take(20)) // Limit to 20 items
            {
                var value = suggestion;
                menu.AddItem(new GUIContent(TruncateText(value, 40)), false, () =>
                {
                    valueField.SetValueWithoutNotify(value);
                    UpdateTargetFromEditor(block, index, searchType, value, dirDropdown.value.ToLower());
                });
            }

            if (suggestions.Count > 20)
            {
                menu.AddDisabledItem(new GUIContent($"... and {suggestions.Count - 20} more"));
            }

            menu.ShowAsContext();
        }

        private List<string> GetSuggestionsForType(string searchType)
        {
            var suggestions = new HashSet<string>();

            // Always refresh elements when in play mode to get latest scene state
            if (EditorApplication.isPlaying)
            {
                currentElements = ElementDiscovery.DiscoverElements();
            }

            if (currentElements == null || currentElements.Count == 0)
                return suggestions.ToList();

            // Only show enabled Selectables (buttons, toggles, sliders, dropdowns, etc.)
            foreach (var elem in currentElements)
            {
                if (!elem.isEnabled) continue;

                switch (searchType)
                {
                    case "text":
                        if (!string.IsNullOrEmpty(elem.text) && !elem.text.StartsWith("(placeholder"))
                            suggestions.Add(elem.text);
                        break;

                    case "name":
                        if (!string.IsNullOrEmpty(elem.name))
                            suggestions.Add(elem.name);
                        break;

                    case "type":
                        // Show element types: button, slider, toggle, dropdown, etc.
                        if (!string.IsNullOrEmpty(elem.type))
                            suggestions.Add(elem.type);
                        break;

                    case "adjacent":
                    case "near":
                    case "any":
                    default:
                        // Show text and names for everything else
                        if (!string.IsNullOrEmpty(elem.text) && !elem.text.StartsWith("(placeholder"))
                            suggestions.Add(elem.text);
                        if (!string.IsNullOrEmpty(elem.name))
                            suggestions.Add(elem.name);
                        break;
                }
            }

            return suggestions.OrderBy(s => s).ToList();
        }

        private string GetFullTargetDisplay(ElementSelector selector)
        {
            if (selector?.query == null) return "(no target)";

            var baseDisplay = GetTargetBaseDisplay(selector);
            var chain = selector.query.chain;

            if (chain == null || chain.Count == 0)
                return baseDisplay;

            // Append chain methods inline
            var chainDisplay = GetChainInlineDisplay(chain);
            return baseDisplay + chainDisplay;
        }

        private string GetChainInlineDisplay(List<SearchChainItem> chain)
        {
            if (chain == null || chain.Count == 0) return "";

            var parts = new List<string>();
            foreach (var item in chain)
            {
                if (item == null) continue;
                var display = item.method switch
                {
                    "near" => $".Near(\"{TruncateText(item.value, 12)}\")",
                    "hasParent" => $".HasParent(\"{TruncateText(item.value, 10)}\")",
                    "hasAncestor" => $".HasAncestor(\"{TruncateText(item.value, 8)}\")",
                    "hasChild" => $".HasChild(\"{TruncateText(item.value, 10)}\")",
                    "hasDescendant" => $".HasDescendant(\"{TruncateText(item.value, 8)}\")",
                    "hasSibling" => $".HasSibling(\"{TruncateText(item.value, 10)}\")",
                    "first" => ".First()",
                    "last" => ".Last()",
                    "skip" => $".Skip({item.count})",
                    "take" => $".Take({item.count})",
                    "visible" => ".Visible()",
                    "interactable" => ".Interactable()",
                    "includeInactive" => ".IncludeInactive()",
                    "includeDisabled" => ".IncludeDisabled()",
                    "inRegion" => $".InRegion(\"{TruncateText(item.value, 10)}\")",
                    "type" => $".Type(\"{item.value}\")",
                    "text" => $".Text(\"{TruncateText(item.value, 10)}\")",
                    "name" => $".Name(\"{TruncateText(item.value, 10)}\")",
                    "getParent" => ".GetParent()",
                    "getChild" => $".GetChild({item.index})",
                    "getSibling" => $".GetSibling({item.offset})",
                    _ => $".{item.method}()"
                };
                parts.Add(display);
            }
            return string.Join("", parts);
        }

        private string GetTargetBaseDisplay(ElementSelector selector)
        {
            if (selector?.query == null) return "(no target)";

            return selector.query.searchBase switch
            {
                "text" => $"Text(\"{TruncateText(selector.query.value, 25)}\")",
                "name" => $"Name(\"{TruncateText(selector.query.value, 25)}\")",
                "type" => $"Type<{selector.query.value}>",
                "adjacent" => $"Adjacent(\"{TruncateText(selector.query.value, 18)}\", {selector.query.direction ?? "?"})",
                "near" => $"Near(\"{TruncateText(selector.query.value, 20)}\")",
                "path" => $"Path(\"{TruncateText(selector.query.value, 25)}\")",
                "texture" => $"Texture(\"{TruncateText(selector.query.value, 18)}\")",
                "tag" => $"Tag(\"{selector.query.value}\")",
                "any" => $"Any(\"{TruncateText(selector.query.value, 20)}\")",
                _ => selector.query.value ?? "(unknown)"
            };
        }

        private string GetChainShortDisplay(SearchChainItem item)
        {
            return item.method switch
            {
                "near" => $"→\"{TruncateText(item.value, 6)}\"",
                "hasParent" => $"↑\"{TruncateText(item.value, 6)}\"",
                "hasAncestor" => $"⇑\"{TruncateText(item.value, 6)}\"",
                "hasChild" => $"↓\"{TruncateText(item.value, 6)}\"",
                "hasSibling" => $"↔\"{TruncateText(item.value, 6)}\"",
                "first" => "①",
                "last" => "⑨",
                "skip" => $"⊳{item.count}",
                "take" => $"#{item.count}",
                "visible" => "👁",
                "interactable" => "☑",
                "inRegion" => $"◫{TruncateText(item.value, 4)}",
                "type" => $"<{TruncateText(item.value, 4)}>",
                "text" => $"\"{TruncateText(item.value, 4)}\"",
                "name" => $"'{TruncateText(item.value, 4)}'",
                _ => $".{item.method}"
            };
        }

        private string GetChainFullDisplay(List<SearchChainItem> chain)
        {
            if (chain == null || chain.Count == 0) return "";
            var parts = new List<string>();
            foreach (var item in chain)
            {
                var display = item.method switch
                {
                    "near" => $".near(\"{item.value}\"{(string.IsNullOrEmpty(item.direction) ? "" : $", {item.direction}")})",
                    "hasParent" => $".hasParent(\"{item.value}\")",
                    "hasAncestor" => $".hasAncestor(\"{item.value}\")",
                    "hasChild" => $".hasChild(\"{item.value}\")",
                    "hasDescendant" => $".hasDescendant(\"{item.value}\")",
                    "hasSibling" => $".hasSibling(\"{item.value}\")",
                    "first" => ".first()",
                    "last" => ".last()",
                    "skip" => $".skip({item.count})",
                    "take" => $".take({item.count})",
                    "visible" => ".visible()",
                    "interactable" => ".interactable()",
                    "includeInactive" => ".includeInactive()",
                    "includeDisabled" => ".includeDisabled()",
                    "inRegion" => $".inRegion(\"{item.value}\")",
                    "type" => $".type(\"{item.value}\")",
                    "text" => $".text(\"{item.value}\")",
                    "name" => $".name(\"{item.value}\")",
                    "getParent" => ".getParent()",
                    "getChild" => $".getChild({item.index})",
                    "getSibling" => $".getSibling({item.offset})",
                    _ => $".{item.method}()"
                };
                parts.Add(display);
            }
            return string.Join("", parts);
        }

        private string TruncateText(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
        }

        private void ShowChainPicker(VisualBlock block, int blockIndex)
        {
            if (block.target?.query == null) return;

            var menu = new GenericMenu();
            var chain = block.target.query.chain ?? new List<SearchChainItem>();
            var popupPos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);

            // Current chain items - show with option to remove
            if (chain.Count > 0)
            {
                menu.AddDisabledItem(new GUIContent("— Current Chain —"));
                for (int i = 0; i < chain.Count; i++)
                {
                    var idx = i;
                    var item = chain[i];
                    var display = GetChainItemMenuDisplay(item);
                    menu.AddItem(new GUIContent($"  ✕ {display}"), false, () =>
                    {
                        Undo.RecordObject(currentTest, "Remove chain filter");
                        block.target.query.chain.RemoveAt(idx);
                        if (block.target.query.chain.Count == 0)
                            block.target.query.chain = null;
                        OnBlockChanged(blockIndex);
                        RefreshBlockList();
                    });
                }
                menu.AddSeparator("");
            }

            // Hierarchy filters
            menu.AddItem(new GUIContent("Add Filter/Hierarchy/hasParent..."), false, () =>
                ShowChainValueInput("hasParent", "Parent name pattern:", block, blockIndex, popupPos));
            menu.AddItem(new GUIContent("Add Filter/Hierarchy/hasAncestor..."), false, () =>
                ShowChainValueInput("hasAncestor", "Ancestor name pattern:", block, blockIndex, popupPos));
            menu.AddItem(new GUIContent("Add Filter/Hierarchy/hasChild..."), false, () =>
                ShowChainValueInput("hasChild", "Child name pattern:", block, blockIndex, popupPos));
            menu.AddItem(new GUIContent("Add Filter/Hierarchy/hasDescendant..."), false, () =>
                ShowChainValueInput("hasDescendant", "Descendant name pattern:", block, blockIndex, popupPos));
            menu.AddItem(new GUIContent("Add Filter/Hierarchy/hasSibling..."), false, () =>
                ShowChainValueInput("hasSibling", "Sibling name pattern:", block, blockIndex, popupPos));

            // Spatial filters
            menu.AddItem(new GUIContent("Add Filter/Spatial/near (any direction)..."), false, () =>
                ShowChainValueInput("near", "Text label to find element near:", block, blockIndex, popupPos));
            menu.AddItem(new GUIContent("Add Filter/Spatial/near right..."), false, () =>
                ShowChainValueInput("near", "Text label (look right):", block, blockIndex, popupPos, "right"));
            menu.AddItem(new GUIContent("Add Filter/Spatial/near left..."), false, () =>
                ShowChainValueInput("near", "Text label (look left):", block, blockIndex, popupPos, "left"));
            menu.AddItem(new GUIContent("Add Filter/Spatial/near above..."), false, () =>
                ShowChainValueInput("near", "Text label (look above):", block, blockIndex, popupPos, "above"));
            menu.AddItem(new GUIContent("Add Filter/Spatial/near below..."), false, () =>
                ShowChainValueInput("near", "Text label (look below):", block, blockIndex, popupPos, "below"));

            // Region filters
            menu.AddItem(new GUIContent("Add Filter/Region/TopLeft"), false, () => AddChainItem(block, blockIndex, "inRegion", "TopLeft"));
            menu.AddItem(new GUIContent("Add Filter/Region/Top"), false, () => AddChainItem(block, blockIndex, "inRegion", "TopCenter"));
            menu.AddItem(new GUIContent("Add Filter/Region/TopRight"), false, () => AddChainItem(block, blockIndex, "inRegion", "TopRight"));
            menu.AddItem(new GUIContent("Add Filter/Region/Left"), false, () => AddChainItem(block, blockIndex, "inRegion", "MiddleLeft"));
            menu.AddItem(new GUIContent("Add Filter/Region/Center"), false, () => AddChainItem(block, blockIndex, "inRegion", "Center"));
            menu.AddItem(new GUIContent("Add Filter/Region/Right"), false, () => AddChainItem(block, blockIndex, "inRegion", "MiddleRight"));
            menu.AddItem(new GUIContent("Add Filter/Region/BottomLeft"), false, () => AddChainItem(block, blockIndex, "inRegion", "BottomLeft"));
            menu.AddItem(new GUIContent("Add Filter/Region/Bottom"), false, () => AddChainItem(block, blockIndex, "inRegion", "BottomCenter"));
            menu.AddItem(new GUIContent("Add Filter/Region/BottomRight"), false, () => AddChainItem(block, blockIndex, "inRegion", "BottomRight"));

            // Selection filters
            menu.AddItem(new GUIContent("Add Filter/Selection/first()"), false, () => AddChainItem(block, blockIndex, "first", null));
            menu.AddItem(new GUIContent("Add Filter/Selection/last()"), false, () => AddChainItem(block, blockIndex, "last", null));
            menu.AddItem(new GUIContent("Add Filter/Selection/skip(1)"), false, () => AddChainItemWithCount(block, blockIndex, "skip", 1));
            menu.AddItem(new GUIContent("Add Filter/Selection/skip(2)"), false, () => AddChainItemWithCount(block, blockIndex, "skip", 2));
            menu.AddItem(new GUIContent("Add Filter/Selection/take(1)"), false, () => AddChainItemWithCount(block, blockIndex, "take", 1));
            menu.AddItem(new GUIContent("Add Filter/Selection/take(3)"), false, () => AddChainItemWithCount(block, blockIndex, "take", 3));

            // Visibility filters
            menu.AddItem(new GUIContent("Add Filter/Visibility/visible()"), false, () => AddChainItem(block, blockIndex, "visible", null));
            menu.AddItem(new GUIContent("Add Filter/Visibility/interactable()"), false, () => AddChainItem(block, blockIndex, "interactable", null));
            menu.AddItem(new GUIContent("Add Filter/Visibility/includeInactive()"), false, () => AddChainItem(block, blockIndex, "includeInactive", null));
            menu.AddItem(new GUIContent("Add Filter/Visibility/includeDisabled()"), false, () => AddChainItem(block, blockIndex, "includeDisabled", null));

            // Type filter
            menu.AddItem(new GUIContent("Add Filter/Type/Button"), false, () => AddChainItem(block, blockIndex, "type", "Button"));
            menu.AddItem(new GUIContent("Add Filter/Type/Toggle"), false, () => AddChainItem(block, blockIndex, "type", "Toggle"));
            menu.AddItem(new GUIContent("Add Filter/Type/Slider"), false, () => AddChainItem(block, blockIndex, "type", "Slider"));
            menu.AddItem(new GUIContent("Add Filter/Type/InputField"), false, () => AddChainItem(block, blockIndex, "type", "TMP_InputField"));
            menu.AddItem(new GUIContent("Add Filter/Type/Dropdown"), false, () => AddChainItem(block, blockIndex, "type", "TMP_Dropdown"));
            menu.AddItem(new GUIContent("Add Filter/Type/ScrollRect"), false, () => AddChainItem(block, blockIndex, "type", "ScrollRect"));
            menu.AddItem(new GUIContent("Add Filter/Type/Image"), false, () => AddChainItem(block, blockIndex, "type", "Image"));
            menu.AddItem(new GUIContent("Add Filter/Type/Other..."), false, () =>
                ShowChainValueInput("type", "Component type name:", block, blockIndex, popupPos));

            // Traversal
            menu.AddItem(new GUIContent("Add Filter/Traverse/getParent()"), false, () => AddChainItem(block, blockIndex, "getParent", null));
            menu.AddItem(new GUIContent("Add Filter/Traverse/getChild(0)"), false, () => AddChainItemWithIndex(block, blockIndex, "getChild", 0));
            menu.AddItem(new GUIContent("Add Filter/Traverse/getChild(1)"), false, () => AddChainItemWithIndex(block, blockIndex, "getChild", 1));
            menu.AddItem(new GUIContent("Add Filter/Traverse/getSibling(+1)"), false, () => AddChainItemWithOffset(block, blockIndex, "getSibling", 1));
            menu.AddItem(new GUIContent("Add Filter/Traverse/getSibling(-1)"), false, () => AddChainItemWithOffset(block, blockIndex, "getSibling", -1));

            // Clear all
            if (chain.Count > 0)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear All Filters"), false, () =>
                {
                    Undo.RecordObject(currentTest, "Clear chain filters");
                    block.target.query.chain = null;
                    OnBlockChanged(blockIndex);
                    RefreshBlockList();
                });
            }

            menu.ShowAsContext();
        }

        private string GetChainItemMenuDisplay(SearchChainItem item)
        {
            return item.method switch
            {
                "near" => $"near(\"{item.value}\"{(string.IsNullOrEmpty(item.direction) ? "" : $", {item.direction}")})",
                "hasParent" => $"hasParent(\"{item.value}\")",
                "hasAncestor" => $"hasAncestor(\"{item.value}\")",
                "hasChild" => $"hasChild(\"{item.value}\")",
                "hasDescendant" => $"hasDescendant(\"{item.value}\")",
                "hasSibling" => $"hasSibling(\"{item.value}\")",
                "first" => "first()",
                "last" => "last()",
                "skip" => $"skip({item.count})",
                "take" => $"take({item.count})",
                "visible" => "visible()",
                "interactable" => "interactable()",
                "includeInactive" => "includeInactive()",
                "includeDisabled" => "includeDisabled()",
                "inRegion" => $"inRegion(\"{item.value}\")",
                "type" => $"type(\"{item.value}\")",
                "text" => $"text(\"{item.value}\")",
                "name" => $"name(\"{item.value}\")",
                "getParent" => "getParent()",
                "getChild" => $"getChild({item.index})",
                "getSibling" => $"getSibling({item.offset})",
                _ => $"{item.method}()"
            };
        }

        private void ShowChainValueInput(string method, string prompt, VisualBlock block, int blockIndex, Vector2 popupPos, string direction = null)
        {
            var popup = CreateInstance<TextInputPopup>();
            popup.Init(prompt, "", val =>
            {
                if (string.IsNullOrEmpty(val)) return;
                AddChainItem(block, blockIndex, method, val, direction);
            });
            popup.ShowAsDropDown(new Rect(popupPos, Vector2.zero), new Vector2(250, 70));
        }

        private void AddChainItem(VisualBlock block, int blockIndex, string method, string value, string direction = null)
        {
            Undo.RecordObject(currentTest, "Add chain filter");
            block.target.query.chain ??= new List<SearchChainItem>();
            block.target.query.chain.Add(new SearchChainItem
            {
                method = method,
                value = value,
                direction = direction
            });
            OnBlockChanged(blockIndex);
            RefreshBlockList();
        }

        private void AddChainItemWithCount(VisualBlock block, int blockIndex, string method, int count)
        {
            Undo.RecordObject(currentTest, "Add chain filter");
            block.target.query.chain ??= new List<SearchChainItem>();
            block.target.query.chain.Add(new SearchChainItem
            {
                method = method,
                count = count
            });
            OnBlockChanged(blockIndex);
            RefreshBlockList();
        }

        private void AddChainItemWithIndex(VisualBlock block, int blockIndex, string method, int index)
        {
            Undo.RecordObject(currentTest, "Add chain filter");
            block.target.query.chain ??= new List<SearchChainItem>();
            block.target.query.chain.Add(new SearchChainItem
            {
                method = method,
                index = index
            });
            OnBlockChanged(blockIndex);
            RefreshBlockList();
        }

        private void AddChainItemWithOffset(VisualBlock block, int blockIndex, string method, int offset)
        {
            Undo.RecordObject(currentTest, "Add chain filter");
            block.target.query.chain ??= new List<SearchChainItem>();
            block.target.query.chain.Add(new SearchChainItem
            {
                method = method,
                offset = offset
            });
            OnBlockChanged(blockIndex);
            RefreshBlockList();
        }

        private void AddScrollContainerButton(VisualElement row, VisualBlock block, int index, bool canEdit, int maxWidth)
        {
            var btn = new Button(() => ShowScrollContainerPicker(block, index))
            {
                text = block.scrollContainer?.GetDisplayText() ?? "[auto]"
            };
            btn.style.maxWidth = maxWidth;
            btn.style.height = 16;
            btn.style.fontSize = 10;
            btn.SetEnabled(canEdit);
            row.Add(btn);
        }

        private void ShowScrollContainerPicker(VisualBlock block, int blockIndex)
        {
            // Simple popup to pick a scroll container - reuse target picker logic
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Auto-detect"), block.scrollContainer == null, () =>
            {
                block.scrollContainer = null;
                OnBlockChanged(blockIndex);
                RefreshBlockList();
            });
            menu.AddSeparator("");

            // Find all ScrollRects in scene
            var scrollRects = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.ScrollRect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var sr in scrollRects)
            {
                var name = sr.gameObject.name;
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    block.scrollContainer = ElementSelector.ByName(name, name);
                    OnBlockChanged(blockIndex);
                    RefreshBlockList();
                });
            }

            menu.ShowAsContext();
        }

        // === Scene GUI for Visual Picking ===

        private void OnSceneGUI(SceneView sceneView)
        {
            // Only active when picking a target
            if (!isPickingTarget && !isPickingDragTarget) return;
            if (!EditorApplication.isPlaying) { CancelPicking(); return; }

            // Change cursor to indicate picking mode
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.Link);

            // Handle escape to cancel
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                CancelPicking();
                Event.current.Use();
                Repaint();
                return;
            }

            // For now, just show overlay text - full implementation would raycast from Game view
            Handles.BeginGUI();
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = Color.yellow;
            var msg = isPickingTarget ? "Click an element in Game View (ESC to cancel)" : "Click drag target in Game View (ESC to cancel)";
            GUI.Box(new Rect(10, 10, 350, 30), msg, style);
            Handles.EndGUI();

            sceneView.Repaint();
        }

        private void ShowDragTargetPicker(VisualBlock block, int index)
        {
            if (!EditorApplication.isPlaying)
            {
                statusLabel.text = "Enter play mode to pick targets";
                return;
            }

            RefreshElements();

            // Capture mouse position before menu callback (Event.current will be null in callback)
            var popupPos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);

            var menu = new GenericMenu();

            if (currentElements.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No UI elements found"));
            }
            else
            {
                foreach (var elem in currentElements)
                {
                    var e = elem;
                    var label = !string.IsNullOrEmpty(e.text) ? e.text : e.name;
                    var centerX = (int)(e.bounds.x + e.bounds.width / 2);
                    var centerY = (int)(e.bounds.y + e.bounds.height / 2);
                    var display = $"{label} ({e.type}) @ ({centerX},{centerY})";

                    menu.AddItem(new GUIContent(display), false, () =>
                    {
                        Undo.RecordObject(currentTest, "Set drag target");
                        block.dragTarget = !string.IsNullOrEmpty(e.text)
                            ? ElementSelector.ByText(e.text, label)
                            : ElementSelector.ByName(e.name, label);
                        OnBlockChanged(index);
                        RefreshBlockList();
                    });
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter manually..."), false, () =>
            {
                var popup = CreateInstance<TextInputPopup>();
                popup.Init("Drag Target Name", block.dragTarget?.query?.value ?? "", val =>
                {
                    Undo.RecordObject(currentTest, "Set drag target");
                    block.dragTarget = ElementSelector.ByName(val, val);
                    OnBlockChanged(index);
                    RefreshBlockList();
                });
                popup.ShowAsDropDown(new Rect(popupPos, Vector2.zero), new Vector2(250, 70));
            });

            menu.ShowAsContext();
        }

        private void ShowTargetPicker(VisualBlock block, int index)
        {
            if (!EditorApplication.isPlaying)
            {
                statusLabel.text = "Enter play mode to pick targets";
                return;
            }

            RefreshElements();

            // Capture mouse position before menu callback (Event.current will be null in callback)
            var popupPos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);

            var menu = new GenericMenu();

            if (currentElements.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No UI elements found"));
            }
            else
            {
                foreach (var elem in currentElements)
                {
                    var e = elem;
                    // Show: Name/Text (type) @ position - this is the default quick-pick option
                    var label = !string.IsNullOrEmpty(e.text) ? e.text : e.name;
                    var centerX = (int)(e.bounds.x + e.bounds.width / 2);
                    var centerY = (int)(e.bounds.y + e.bounds.height / 2);
                    var display = $"{label} ({e.type}) @ ({centerX},{centerY})";

                    // Default: quick pick using best identifier (text or name)
                    menu.AddItem(new GUIContent(display), false, () =>
                    {
                        Undo.RecordObject(currentTest, "Set target");
                        block.target = !string.IsNullOrEmpty(e.text)
                            ? ElementSelector.ByText(e.text, label)
                            : ElementSelector.ByName(e.name, label);
                        OnBlockChanged(index);
                        RefreshBlockList();
                    });

                    // Add submenu with search options for this element
                    var submenuPath = $"{display}/";

                    // Search by Text (if element has text)
                    if (!string.IsNullOrEmpty(e.text))
                    {
                        var text = e.text;
                        menu.AddItem(new GUIContent($"{submenuPath}Text: \"{TruncateDisplay(text)}\""), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.ByText(text, $"Text(\"{TruncateDisplay(text)}\")");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }

                    // Search by Name
                    if (!string.IsNullOrEmpty(e.name))
                    {
                        var name = e.name;
                        menu.AddItem(new GUIContent($"{submenuPath}Name: \"{TruncateDisplay(name)}\""), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.ByName(name, $"Name(\"{TruncateDisplay(name)}\")");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }

                    // Search by Type
                    var typeName = GetSearchTypeName(e.type, e.componentType);
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        menu.AddItem(new GUIContent($"{submenuPath}Type: {typeName}"), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.ByType(typeName, $"Type<{typeName}>");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }

                    // Search by Path
                    if (!string.IsNullOrEmpty(e.path))
                    {
                        var path = e.path;
                        menu.AddItem(new GUIContent($"{submenuPath}Path: {TruncateDisplay(path, 40)}"), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.ByPath(path, $"Path(\"{TruncateDisplay(path, 30)}\")");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }

                    // Search by Sprite (if element has an Image with sprite)
                    var spriteName = GetSpriteName(e.gameObject);
                    if (!string.IsNullOrEmpty(spriteName))
                    {
                        menu.AddItem(new GUIContent($"{submenuPath}Texture: \"{spriteName}\""), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.ByTexture(spriteName, $"Texture(\"{spriteName}\")");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }

                    // Search by Adjacent label (if element has adjacent label)
                    if (!string.IsNullOrEmpty(e.adjacentLabel) && !string.IsNullOrEmpty(e.adjacentDirection))
                    {
                        var adjLabel = e.adjacentLabel;
                        var adjDir = e.adjacentDirection;
                        menu.AddItem(new GUIContent($"{submenuPath}Adjacent: \"{TruncateDisplay(adjLabel)}\" ({adjDir})"), false, () =>
                        {
                            Undo.RecordObject(currentTest, "Set target");
                            block.target = ElementSelector.Adjacent(adjLabel, adjDir, $"Adjacent(\"{TruncateDisplay(adjLabel)}\", {adjDir})");
                            OnBlockChanged(index);
                            RefreshBlockList();
                        });
                    }
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter manually..."), false, () =>
            {
                var popup = CreateInstance<TextInputPopup>();
                popup.Init("Target Name", block.target?.query?.value ?? "", val =>
                {
                    Undo.RecordObject(currentTest, "Set target");
                    block.target = ElementSelector.ByName(val, val);
                    OnBlockChanged(index);
                    RefreshBlockList();
                });
                popup.ShowAsDropDown(new Rect(popupPos, Vector2.zero), new Vector2(250, 70));
            });

            menu.ShowAsContext();
        }

        /// <summary>
        /// Truncates a string for display in menus.
        /// </summary>
        private static string TruncateDisplay(string text, int maxLength = 25)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Gets the Search API type name from element type and component type.
        /// </summary>
        private static string GetSearchTypeName(string elementType, string componentType)
        {
            // Map common element types to their Search API type names
            return elementType?.ToLower() switch
            {
                "button" => "Button",
                "toggle (on)" or "toggle (off)" => "Toggle",
                "slider" => "Slider",
                "input" => "InputField",
                "dropdown" => "Dropdown",
                "scrollview" => "ScrollRect",
                "selectable" => "Selectable",
                _ => !string.IsNullOrEmpty(componentType) ? GetShortTypeName(componentType) : null
            };
        }

        /// <summary>
        /// Gets the short type name from a full component type name.
        /// </summary>
        private static string GetShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;
            var lastDot = fullTypeName.LastIndexOf('.');
            return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
        }

        /// <summary>
        /// Gets the sprite name from a GameObject if it has an Image component with a sprite.
        /// </summary>
        private static string GetSpriteName(GameObject go)
        {
            if (go == null) return null;

            // Check for UI Image
            var image = go.GetComponentInChildren<UIImage>();
            if (image != null && image.sprite != null)
                return image.sprite.name;

            // Check for SpriteRenderer
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
                return sr.sprite.name;

            return null;
        }

        // === Block Operations ===

        private void AddBlock(BlockType type)
        {
            if (currentTest == null) OnNewTest();
            if (!EditorApplication.isPlaying)
            {
                statusLabel.text = "Enter play mode first";
                return;
            }

            Undo.RecordObject(currentTest, $"Add {type} block");

            var block = new VisualBlock
            {
                id = Guid.NewGuid().ToString(),
                type = type,
                waitSeconds = type == BlockType.Wait ? 1f : 0f,
                dragDistance = type == BlockType.Drag ? 0.2f : 0f,  // 20% of screen height
                scrollAmount = type == BlockType.Scroll ? 0.3f : 0f  // 30% of scrollable area
            };

            currentTest.blocks.Add(block);
            EditorUtility.SetDirty(currentTest);

            // Start editing this block
            editingBlockIndex = currentTest.blocks.Count - 1;
            blockHasUnsavedChanges = true;
            selectedBlockIndex = editingBlockIndex;

            RefreshBlockList();
            statusLabel.text = $"Added {type} block - configure and Save";
        }

        private void StartEditingBlock(int index)
        {
            if (editingBlockIndex >= 0 && blockHasUnsavedChanges)
            {
                if (!EditorUtility.DisplayDialog("Unsaved Changes",
                    "The current block has unsaved changes. Discard them?",
                    "Discard", "Cancel"))
                {
                    return;
                }
            }

            editingBlockIndex = index;
            blockHasUnsavedChanges = false;
            selectedBlockIndex = index;
            RefreshBlockList();
        }

        private void OnBlockChanged(int index, bool refreshList = false)
        {
            if (index == editingBlockIndex)
            {
                blockHasUnsavedChanges = true;
                // Only refresh if explicitly requested, to avoid focus stealing during typing
                if (refreshList)
                    RefreshBlockList();
            }
        }

        private async void SaveBlock(int index)
        {
            if (currentTest == null) return;

            EditorUtility.SetDirty(currentTest);
            AssetDatabase.SaveAssets();

            blockHasUnsavedChanges = false;
            RefreshBlockList();

            // Execute the block after saving
            if (EditorApplication.isPlaying && index < currentTest.blocks.Count)
            {
                try
                {
                    var block = currentTest.blocks[index];
                    Debug.Log($"[TestBuilder] SaveBlock - executing block {index + 1}: {block.type} target={block.target?.GetDisplayText() ?? "null"}");
                    statusLabel.text = $"Executing block {index + 1}...";
                    await ExecuteBlock(block);
                    statusLabel.text = $"Block {index + 1} saved and executed";
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[TestBuilder] SaveBlock execution failed: {ex}");
                    statusLabel.text = $"Failed: {ex.Message}";
                }
            }
            else
            {
                statusLabel.text = $"Block {index + 1} saved";
            }
        }

        private async void DeleteBlock(int index)
        {
            if (currentTest == null) return;

            if (!EditorUtility.DisplayDialog("Delete Block",
                $"Delete block {index + 1}?", "Delete", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(currentTest, "Delete block");
            currentTest.blocks.RemoveAt(index);
            EditorUtility.SetDirty(currentTest);

            if (editingBlockIndex == index)
            {
                editingBlockIndex = -1;
                blockHasUnsavedChanges = false;
            }
            else if (editingBlockIndex > index)
            {
                editingBlockIndex--;
            }

            if (selectedBlockIndex >= currentTest.blocks.Count)
                selectedBlockIndex = currentTest.blocks.Count - 1;

            RefreshBlockList();

            // Restart and run all remaining blocks after deletion
            if (EditorApplication.isPlaying && currentTest.blocks.Count > 0)
            {
                statusLabel.text = "Restarting after delete...";
                await RestartScene();

                // Run all blocks from start to end
                for (int i = 0; i < currentTest.blocks.Count; i++)
                {
                    if (!EditorApplication.isPlaying) break;

                    selectedBlockIndex = i;
                    RefreshBlockList();
                    statusLabel.text = $"Running block {i + 1}/{currentTest.blocks.Count}...";

                    await ExecuteBlock(currentTest.blocks[i]);
                    await UniTask.Delay(200);
                }

                statusLabel.text = $"Completed all {currentTest.blocks.Count} blocks";
            }
            else if (EditorApplication.isPlaying)
            {
                statusLabel.text = "Restarting after delete...";
                await RestartScene();
                statusLabel.text = "Scene restarted (no blocks)";
            }
        }

        private async void RunToBlock(int targetIndex)
        {
            if (!EditorApplication.isPlaying || currentTest == null) return;

            // Restart by reloading the scene
            statusLabel.text = "Restarting test...";
            await RestartScene();

            statusLabel.text = $"Running blocks 1-{targetIndex + 1}...";

            for (int i = 0; i <= targetIndex && i < currentTest.blocks.Count; i++)
            {
                if (!EditorApplication.isPlaying) break;

                selectedBlockIndex = i;
                RefreshBlockList();
                statusLabel.text = $"Running block {i + 1}/{targetIndex + 1}...";

                await ExecuteBlock(currentTest.blocks[i]);
                await UniTask.Delay(200);
            }

            statusLabel.text = $"Completed blocks 1-{targetIndex + 1}";
        }

        private async UniTask RestartScene()
        {
            if (currentTest == null || string.IsNullOrEmpty(currentTest.startScene)) return;

            // Reload the scene to reset state
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var asyncOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(scene.name);

            while (asyncOp != null && !asyncOp.isDone)
            {
                await UniTask.Yield();
            }

            // Wait a frame for scene to settle
            await UniTask.DelayFrame(2);
        }

        private async UniTask ExecuteBlock(VisualBlock block)
        {
            var result = await VisualTestRunner.ExecuteBlockAsync(block);
            if (!result.Success)
            {
                Debug.LogError($"[TestBuilder] Block execution failed: {result.Error}");
                statusLabel.text = $"Failed: {result.Error}";
            }
        }

        // === File Operations ===

        private void OnNewTest()
        {
            currentTest = CreateInstance<VisualTest>();
            currentTest.testName = "New Test";
            currentTest.blocks = new List<VisualBlock>();

            selectedBlockIndex = -1;
            editingBlockIndex = -1;
            blockHasUnsavedChanges = false;

            testNameField?.SetValueWithoutNotify("New Test");
            sceneDropdown?.SetValueWithoutNotify("-- Select Scene --");

            RefreshUI();
            statusLabel.text = "New test created - select a scene";
        }

        private void OnSaveTest()
        {
            if (currentTest == null) return;

            var path = AssetDatabase.GetAssetPath(currentTest);
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("Save Visual Test", currentTest.testName, "asset", "Save the visual test");
                if (string.IsNullOrEmpty(path)) return;
                AssetDatabase.CreateAsset(currentTest, path);
            }

            EditorUtility.SetDirty(currentTest);
            AssetDatabase.SaveAssets();
            EditorPrefs.SetString("TestBuilder.LastTest", path);
            statusLabel.text = $"Saved: {path}";
        }

        private void OnLoadTest()
        {
            var path = EditorUtility.OpenFilePanel("Load Visual Test", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            path = "Assets" + path.Substring(Application.dataPath.Length);
            var test = AssetDatabase.LoadAssetAtPath<VisualTest>(path);
            if (test != null) LoadTest(test);
        }

        public void LoadTest(VisualTest test)
        {
            currentTest = test;
            selectedBlockIndex = -1;
            editingBlockIndex = -1;
            blockHasUnsavedChanges = false;

            testNameField?.SetValueWithoutNotify(currentTest.testName ?? "");

            var sceneName = string.IsNullOrEmpty(currentTest.startScene) ? "-- Select Scene --" : currentTest.startScene;
            sceneDropdown?.SetValueWithoutNotify(sceneName);

            var assetPath = AssetDatabase.GetAssetPath(test);
            if (!string.IsNullOrEmpty(assetPath))
            {
                EditorPrefs.SetString("TestBuilder.LastTest", assetPath);
                // Also save to session state for domain reload persistence
                SessionState.SetString(kSessionTestPath, assetPath);
            }

            RefreshUI();
            statusLabel.text = $"Loaded: {test.testName}";
        }

        private void LoadLastTest()
        {
            // Try session state first (for domain reload), then EditorPrefs (for editor restart)
            var sessionPath = SessionState.GetString(kSessionTestPath, "");
            var lastPath = !string.IsNullOrEmpty(sessionPath) ? sessionPath : EditorPrefs.GetString("TestBuilder.LastTest", "");

            if (!string.IsNullOrEmpty(lastPath))
            {
                var test = AssetDatabase.LoadAssetAtPath<VisualTest>(lastPath);
                if (test != null) { LoadTest(test); return; }
            }
            OnNewTest();
        }

        private List<string> GetAllSceneNames()
        {
            var scenes = new List<string> { "-- Select Scene --" };

            // Find all scene files in the project
            var guids = AssetDatabase.FindAssets("t:Scene");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!scenes.Contains(sceneName))
                    scenes.Add(sceneName);
            }

            return scenes;
        }

        private string GetScenePath(string sceneName)
        {
            // First check build settings
            var buildScene = EditorBuildSettings.scenes
                .FirstOrDefault(s => System.IO.Path.GetFileNameWithoutExtension(s.path) == sceneName);
            if (buildScene != null)
                return buildScene.path;

            // Fall back to searching all scenes in project
            var guids = AssetDatabase.FindAssets($"t:Scene {sceneName}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    return path;
            }

            return null;
        }

        private void RefreshElements()
        {
            currentElements.Clear();
            if (!EditorApplication.isPlaying) return;
            currentElements = ElementDiscovery.DiscoverElements();
        }

        // === AI Mode (popup-based) ===

        private void ShowAIPromptPopup()
        {
            if (!EditorApplication.isPlaying)
            {
                statusLabel.text = "Enter play mode first";
                return;
            }

            // Check API key before showing popup - load settings asset directly
            var settingsPath = "Assets/Editor/AITestSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingsPath);

            var hasGemini = false;

            if (settings != null)
            {
                var geminiKeyField = settings.GetType().GetField("geminiApiKey");

                if (geminiKeyField != null)
                    hasGemini = !string.IsNullOrEmpty(geminiKeyField.GetValue(settings) as string);
            }

            if (!hasGemini)
            {
                statusLabel.text = "No AI provider configured!";
                UpdateAIStatusPanel(true, "Error", "No API key configured. Go to Project Settings > UI Test > AI Testing to add your Gemini API key.", -1);

                if (EditorUtility.DisplayDialog("AI Not Configured",
                    "No AI provider is configured.\n\nPlease add your Gemini API key in:\nProject Settings > UI Test > AI Testing",
                    "Open Settings", "Cancel"))
                {
                    SettingsService.OpenProjectSettings("Project/UI Test/AI Testing");
                }
                return;
            }

            var popup = CreateInstance<AIPromptPopup>();
            popup.Init(async (prompt) =>
            {
                if (string.IsNullOrWhiteSpace(prompt)) return;

                if (currentTest == null)
                    OnNewTest();

                // Clean up previous assistant
                aiAssistant?.Dispose();
                aiActionCount = 0;

                // Create new assistant
                aiAssistant = new AIAssistant(currentTest);

                // Show the AI status panel
                UpdateAIStatusPanel(true, "Initializing...", "Preparing to analyze screen...", 0);

                aiAssistant.OnBlockAdded += (block) =>
                {
                    aiActionCount++;
                    RefreshBlockList();
                    statusLabel.text = $"AI added: {block.GetDisplayText()}";
                    UpdateAIStatusPanel(true, $"Executing action {aiActionCount}...", block.GetDisplayText(), -1);
                };

                aiAssistant.OnReasoningReceived += (reasoning) =>
                {
                    UpdateAIStatusPanel(true, null, reasoning, -1);
                };

                aiAssistant.OnStatusChanged += (status) =>
                {
                    statusLabel.text = status;

                    // Map status to display state
                    var displayState = status switch
                    {
                        var s when s.Contains("Initializing") => "Initializing...",
                        var s when s.Contains("analyzing") || s.Contains("Analyzing") => "Analyzing...",
                        var s when s.Contains("Executing") => "Executing action...",
                        var s when s.Contains("Stopping") => "Stopping...",
                        var s when s.Contains("Completed") => "Completed",
                        var s when s.Contains("Failed") || s.Contains("Error") => "Failed",
                        _ => "Running"
                    };
                    UpdateAIStatusPanel(true, displayState, null, -1);
                };

                aiAssistant.OnStateChanged += (state) =>
                {
                    var displayState = state switch
                    {
                        AIAssistant.State.Idle => "Idle",
                        AIAssistant.State.Initializing => "Initializing...",
                        AIAssistant.State.Running => "Running",
                        AIAssistant.State.Paused => "Paused",
                        AIAssistant.State.Stopping => "Stopping...",
                        AIAssistant.State.Completed => "Completed",
                        AIAssistant.State.Failed => "Failed",
                        _ => "Unknown"
                    };
                    UpdateAIStatusPanel(true, displayState, null, -1);
                };

                aiAssistant.OnCompleted += (success, message) =>
                {
                    RefreshBlockList();
                    statusLabel.text = success ? $"AI done: {message}" : $"AI stopped: {message}";
                    UpdateAIStatusPanel(true, success ? "Completed" : "Stopped", message, 1f);

                    // Hide the panel after a delay
                    EditorApplication.delayCall += () =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            EditorApplication.delayCall += () =>
                            {
                                UpdateAIStatusPanel(false);
                            };
                        };
                    };
                };

                statusLabel.text = "AI starting...";
                UpdateAIStatusPanel(true, "Starting...", $"Prompt: {prompt}", 0);

                try
                {
                    await aiAssistant.StartAsync(prompt);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TestBuilder] AI error: {ex}");
                    statusLabel.text = $"AI error: {ex.Message}";
                    UpdateAIStatusPanel(true, "Error", ex.Message, -1);
                }
            });

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            popup.ShowAsDropDown(new Rect(mousePos, Vector2.zero), new Vector2(300, 100));
        }

        private void StopAI()
        {
            aiAssistant?.Stop();
            statusLabel.text = "AI stopping...";
            UpdateAIStatusPanel(true, "Stopping...", "Cancelling AI operation...", -1);
        }

        private void OpenAISettings()
        {
            SettingsService.OpenProjectSettings("Project/UI Test/AI Testing");
        }
    }

    /// <summary>
    /// Simple popup for AI prompt input.
    /// </summary>
    public class AIPromptPopup : EditorWindow
    {
        private string prompt = "";
        private Action<string> onSubmit;

        public void Init(Action<string> callback)
        {
            onSubmit = callback;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("What should the AI do?", EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            prompt = EditorGUILayout.TextArea(prompt, GUILayout.Height(40));

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Start", GUILayout.Height(24)))
            {
                onSubmit?.Invoke(prompt);
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(60), GUILayout.Height(24)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();

            // Handle Enter key
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !string.IsNullOrWhiteSpace(prompt))
            {
                onSubmit?.Invoke(prompt);
                Close();
                Event.current.Use();
            }
        }
    }
}
