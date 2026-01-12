using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using ODDGames.UITest.AI;

namespace ODDGames.UITest.VisualBuilder.Editor
{
    /// <summary>
    /// Visual Test Builder with Scratch-style blocks.
    /// Flow: Select scene → Enter play mode → Add/edit blocks → Save each block → Run
    /// </summary>
    public class TestBuilder : EditorWindow
    {
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
        private VisualElement blockControls;

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
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            aiAssistant?.Dispose();
            CancelPicking();
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

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                RefreshUI();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
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

            LoadLastTest();
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

            // Start/Stop buttons
            startEditBtn = new Button(OnStartEdit) { text = "▶ Edit" };
            startEditBtn.style.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
            toolbar.Add(startEditBtn);

            stopBtn = new Button(OnStopEdit) { text = "■" };
            stopBtn.style.backgroundColor = new Color(0.5f, 0.3f, 0.3f);
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

        private void BuildMainContent()
        {
            blockPanel = new VisualElement();
            blockPanel.style.flexGrow = 1;
            blockPanel.style.flexDirection = FlexDirection.Column;

            // Block type buttons row
            blockControls = new VisualElement();
            blockControls.style.flexDirection = FlexDirection.Row;
            blockControls.style.paddingLeft = 4;
            blockControls.style.paddingTop = 2;
            blockControls.style.paddingBottom = 2;
            blockControls.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            // Row 1: Input actions
            AddBlockTypeButton(blockControls, "+Click", BlockType.Click, new Color(0.3f, 0.6f, 1f));
            AddBlockTypeButton(blockControls, "+2xClk", BlockType.DoubleClick, new Color(0.24f, 0.53f, 0.94f));
            AddBlockTypeButton(blockControls, "+Hold", BlockType.Hold, new Color(0.42f, 0.65f, 1f));
            AddBlockTypeButton(blockControls, "+Type", BlockType.Type, new Color(0.6f, 0.4f, 1f));
            AddBlockTypeButton(blockControls, "+Key", BlockType.KeyPress, new Color(0.63f, 0.5f, 1f));
            AddBlockTypeButton(blockControls, "+KeyH", BlockType.KeyHold, new Color(0.56f, 0.44f, 0.94f));
            AddBlockTypeButton(blockControls, "+Drag", BlockType.Drag, new Color(1f, 0.55f, 0.1f));
            AddBlockTypeButton(blockControls, "+Scroll", BlockType.Scroll, new Color(0.35f, 0.75f, 0.35f));

            // Row 2 container
            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.paddingLeft = 4;
            row2.style.paddingBottom = 2;
            row2.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            AddBlockTypeButton(row2, "+Wait", BlockType.Wait, new Color(1f, 0.75f, 0f));
            AddBlockTypeButton(row2, "+WaitEl", BlockType.WaitForElement, new Color(0.88f, 0.63f, 0f));
            AddBlockTypeButton(row2, "+Assert", BlockType.Assert, new Color(1f, 0.4f, 0.5f));
            AddBlockTypeButton(row2, "+Log", BlockType.Log, new Color(0.5f, 0.5f, 0.5f));
            AddBlockTypeButton(row2, "+Shot", BlockType.Screenshot, new Color(0.25f, 0.75f, 0.75f));
            AddBlockTypeButton(row2, "+Scene", BlockType.LoadScene, new Color(0.63f, 0.5f, 0.75f));

            // AI button
            var aiBtn = new Button(ShowAIPromptPopup) { text = "AI" };
            aiBtn.style.backgroundColor = new Color(0.4f, 0.3f, 0.7f);
            aiBtn.style.color = Color.white;
            aiBtn.style.height = 18;
            aiBtn.style.fontSize = 10;
            aiBtn.style.marginLeft = 8;
            aiBtn.style.paddingLeft = 6;
            aiBtn.style.paddingRight = 6;
            aiBtn.style.borderTopLeftRadius = 3;
            aiBtn.style.borderTopRightRadius = 3;
            aiBtn.style.borderBottomLeftRadius = 3;
            aiBtn.style.borderBottomRightRadius = 3;
            row2.Add(aiBtn);

            blockPanel.Add(row2);

            blockPanel.Add(blockControls);

            // Block list
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            blockListContainer = new VisualElement();
            scroll.Add(blockListContainer);
            blockPanel.Add(scroll);

            root.Add(blockPanel);
        }

        private void AddBlockTypeButton(VisualElement parent, string label, BlockType type, Color color)
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
            parent.Add(btn);
        }

        private void BuildStatusBar()
        {
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

        // === UI State Management ===

        private void RefreshUI()
        {
            // Guard against being called before CreateGUI
            if (root == null || statusLabel == null) return;

            var hasScene = currentTest != null && !string.IsNullOrEmpty(currentTest.startScene);
            var inPlayMode = EditorApplication.isPlaying;

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

            // Update status
            if (!hasScene)
                statusLabel.text = "Select a scene to begin";
            else if (!inPlayMode)
                statusLabel.text = "Click 'Start Editing' to enter play mode";
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

            for (int i = 0; i < currentTest.blocks.Count; i++)
                blockListContainer.Add(CreateBlockRow(currentTest.blocks[i], i));
        }

        private VisualElement CreateBlockRow(VisualBlock block, int index)
        {
            var isEditing = (index == editingBlockIndex);
            var c = block.GetBlockColor();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 20;
            row.style.marginBottom = 1;
            row.style.paddingLeft = 2;
            row.style.paddingRight = 2;
            row.style.backgroundColor = isEditing ? new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f, 1f) : new Color(0.22f, 0.22f, 0.22f);
            row.style.borderLeftWidth = 3;
            row.style.borderLeftColor = c;

            // Index
            row.Add(new Label($"{index + 1}") { style = { width = 14, fontSize = 9, color = new Color(0.5f, 0.5f, 0.5f) } });

            // Type label (short)
            var typeNames = new Dictionary<BlockType, string> {
                { BlockType.Click, "CLK" }, { BlockType.DoubleClick, "2xC" }, { BlockType.Hold, "HLD" },
                { BlockType.Type, "TYP" }, { BlockType.KeyPress, "KEY" }, { BlockType.KeyHold, "KyH" },
                { BlockType.Drag, "DRG" }, { BlockType.Scroll, "SCR" },
                { BlockType.Wait, "W" }, { BlockType.WaitForElement, "W?" },
                { BlockType.Assert, "AST" }, { BlockType.Log, "LOG" },
                { BlockType.Screenshot, "SS" }, { BlockType.LoadScene, "SCN" }
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

            return row;
        }

        private void BuildRowContent(VisualElement row, VisualBlock block, int index, bool isEditing)
        {
            var canEdit = EditorApplication.isPlaying && isEditing;

            switch (block.type)
            {
                case BlockType.Click:
                    AddTargetButton(row, block, index, canEdit, 150);
                    break;

                case BlockType.Type:
                    AddTargetButton(row, block, index, canEdit, 80);
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
                    AddTargetButton(row, block, index, canEdit, 80);
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
                    AddTargetButton(row, block, index, canEdit, 60);
                    row.Add(new Label("→") { style = { marginLeft = 2, marginRight = 2 } });
                    if (block.dragTarget != null && block.dragTarget.IsValid())
                    {
                        var toBtn = new Button(() => ShowDragTargetPicker(block, index)) { text = block.dragTarget.GetDisplayText() };
                        toBtn.style.maxWidth = 60; toBtn.style.height = 16; toBtn.style.fontSize = 10;
                        toBtn.SetEnabled(canEdit);
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
                            row.Add(new Label($"{block.dragDirection} {block.dragDistance}px") { style = { fontSize = 10 } });
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
                    AddTargetButton(row, block, index, canEdit, 80);
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
                    AddTargetButton(row, block, index, canEdit, 150);
                    break;

                case BlockType.Hold:
                    AddTargetButton(row, block, index, canEdit, 100);
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
                    AddTargetButton(row, block, index, canEdit, 100);
                    var tf = new FloatField { value = block.waitTimeout };
                    tf.style.width = 35; tf.style.height = 16;
                    tf.SetEnabled(isEditing);
                    tf.RegisterValueChangedCallback(e => { block.waitTimeout = e.newValue; OnBlockChanged(index); });
                    row.Add(tf);
                    row.Add(new Label("s") { style = { fontSize = 10 } });
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
            }
        }

        private void AddTargetButton(VisualElement row, VisualBlock block, int index, bool canEdit, int maxWidth)
        {
            var btn = new Button(() => ShowTargetPicker(block, index))
            {
                text = block.target?.GetDisplayText() ?? "[target]"
            };
            btn.style.maxWidth = maxWidth;
            btn.style.height = 16;
            btn.style.fontSize = 10;
            btn.SetEnabled(canEdit);
            row.Add(btn);
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
                        block.dragTarget = new ElementSelector
                        {
                            type = !string.IsNullOrEmpty(e.text) ? SelectorType.ByText : SelectorType.ByName,
                            pattern = !string.IsNullOrEmpty(e.text) ? e.text : e.name,
                            displayName = label
                        };
                        OnBlockChanged(index);
                        RefreshBlockList();
                    });
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter manually..."), false, () =>
            {
                var popup = CreateInstance<TextInputPopup>();
                popup.Init("Drag Target Name", block.dragTarget?.pattern ?? "", val =>
                {
                    Undo.RecordObject(currentTest, "Set drag target");
                    block.dragTarget = new ElementSelector { type = SelectorType.ByName, pattern = val, displayName = val };
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
                    // Show: Name/Text (type) @ position
                    var label = !string.IsNullOrEmpty(e.text) ? e.text : e.name;
                    var centerX = (int)(e.bounds.x + e.bounds.width / 2);
                    var centerY = (int)(e.bounds.y + e.bounds.height / 2);
                    var display = $"{label} ({e.type}) @ ({centerX},{centerY})";

                    menu.AddItem(new GUIContent(display), false, () =>
                    {
                        Undo.RecordObject(currentTest, "Set target");
                        block.target = new ElementSelector
                        {
                            type = !string.IsNullOrEmpty(e.text) ? SelectorType.ByText : SelectorType.ByName,
                            pattern = !string.IsNullOrEmpty(e.text) ? e.text : e.name,
                            displayName = label
                        };
                        OnBlockChanged(index);
                        RefreshBlockList();
                    });
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter manually..."), false, () =>
            {
                var popup = CreateInstance<TextInputPopup>();
                popup.Init("Target Name", block.target?.pattern ?? "", val =>
                {
                    Undo.RecordObject(currentTest, "Set target");
                    block.target = new ElementSelector { type = SelectorType.ByName, pattern = val, displayName = val };
                    OnBlockChanged(index);
                    RefreshBlockList();
                });
                popup.ShowAsDropDown(new Rect(popupPos, Vector2.zero), new Vector2(250, 70));
            });

            menu.ShowAsContext();
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
                dragDistance = type == BlockType.Drag ? 100f : 0f,
                scrollAmount = type == BlockType.Scroll ? 100f : 0f
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

        private void OnBlockChanged(int index)
        {
            if (index == editingBlockIndex)
            {
                blockHasUnsavedChanges = true;
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
                EditorPrefs.SetString("TestBuilder.LastTest", assetPath);

            RefreshUI();
            statusLabel.text = $"Loaded: {test.testName}";
        }

        private void LoadLastTest()
        {
            var lastPath = EditorPrefs.GetString("TestBuilder.LastTest", "");
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

            var popup = CreateInstance<AIPromptPopup>();
            popup.Init(async (prompt) =>
            {
                if (string.IsNullOrWhiteSpace(prompt)) return;

                if (currentTest == null)
                    OnNewTest();

                // Clean up previous assistant
                aiAssistant?.Dispose();

                // Create new assistant
                aiAssistant = new AIAssistant(currentTest);
                aiAssistant.OnBlockAdded += (block) =>
                {
                    RefreshBlockList();
                    statusLabel.text = $"AI: {block.GetDisplayText()}";
                };
                aiAssistant.OnStatusChanged += (status) =>
                {
                    statusLabel.text = status;
                };
                aiAssistant.OnCompleted += (success, message) =>
                {
                    RefreshBlockList();
                    statusLabel.text = success ? $"AI done: {message}" : $"AI stopped: {message}";
                };

                statusLabel.text = "AI starting...";

                try
                {
                    await aiAssistant.StartAsync(prompt, "");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TestBuilder] AI error: {ex}");
                    statusLabel.text = $"AI error: {ex.Message}";
                }
            });

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            popup.ShowAsDropDown(new Rect(mousePos, Vector2.zero), new Vector2(300, 100));
        }

        private void StopAI()
        {
            aiAssistant?.Stop();
            statusLabel.text = "AI stopped";
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
