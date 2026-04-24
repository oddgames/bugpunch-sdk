using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    /// <summary>
    /// UI Toolkit implementation of INativeDialog for Editor and desktop standalone builds.
    /// Replaces the IMGUI FallbackDialog and the basic EditorUtility dialogs with a proper
    /// dark-themed overlay that supports all dialog types including the recording overlay.
    /// </summary>
    public class UIToolkitDialog : INativeDialog
    {
        public bool IsSupported => true;

        static GameObject _hostGO;
        static UIDocument _doc;
        static StyleSheet _styleSheet;
        static float _recordStartTime;
        static IVisualElementScheduledItem _timerSchedule;
        static Action _recordingCallback;

        // Cached icon textures
        static Texture2D _iconCamera;
        static Texture2D _iconVideocam;
        static Texture2D _iconStopCircle;
        static Texture2D _iconLogo;

        static UIDocument EnsureDocument()
        {
            if (_doc != null) return _doc;

            _hostGO = new GameObject("Bugpunch_UIToolkit");
            _hostGO.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_hostGO);

            // PanelSettings asset is required — create one at runtime
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 30000; // On top of everything

            _doc = _hostGO.AddComponent<UIDocument>();
            _doc.panelSettings = panelSettings;

            return _doc;
        }

        static StyleSheet EnsureStyleSheet()
        {
            if (_styleSheet != null) return _styleSheet;

            // Load the USS from Resources or from the package path
            _styleSheet = Resources.Load<StyleSheet>("BugpunchDialogs");

            if (_styleSheet == null)
            {
                // Try loading from package path (Editor + standalone with addressables)
                #if UNITY_EDITOR
                _styleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    FindUSSAssetPath());
                #endif
            }

            // If still null, create styles programmatically as ultimate fallback
            if (_styleSheet == null)
                _styleSheet = CreateFallbackStyles();

            return _styleSheet;
        }

        #if UNITY_EDITOR
        static string FindUSSAssetPath()
        {
            // Search for our USS file in the package
            var guids = UnityEditor.AssetDatabase.FindAssets("BugpunchDialogs t:StyleSheet");
            if (guids.Length > 0)
                return UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            return "";
        }
        #endif

        static StyleSheet CreateFallbackStyles()
        {
            // Programmatic fallback if USS file isn't loadable at runtime.
            // This covers standalone builds where the USS isn't in Resources.
            var ss = ScriptableObject.CreateInstance<StyleSheet>();
            // The StyleSheet API doesn't allow programmatic rule creation,
            // so we apply inline styles in the builder methods instead.
            return ss;
        }

        // ─── Icon Loading ──────────────────────────────────────────────

        static string FindIconsDir()
        {
            // In Editor, find via AssetDatabase; at runtime, relative to data path
            #if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("bugpunch_logo t:Texture2D");
            if (guids.Length > 0)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                return Path.GetDirectoryName(path).Replace('\\', '/');
            }
            #endif
            // Fallback: search common package locations
            string[] candidates = {
                "Packages/au.com.oddgames.bugpunch/Bugpunch/DeviceConnect/UI/Icons",
                Application.dataPath + "/../Packages/au.com.oddgames.bugpunch/Bugpunch/DeviceConnect/UI/Icons",
            };
            foreach (var c in candidates)
                if (Directory.Exists(c)) return c;
            return null;
        }

        static Texture2D LoadIcon(string filename)
        {
            #if UNITY_EDITOR
            var dir = FindIconsDir();
            if (dir != null)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{filename}");
                if (asset != null) return asset;
            }
            #endif
            // Runtime fallback: load from bytes on disk
            string[] searchPaths = {
                Path.Combine(Application.dataPath, "Plugins", "Bugpunch", "DeviceConnect", "UI", "Icons", filename),
                Path.Combine(Application.streamingAssetsPath, "Bugpunch", "Icons", filename),
            };
            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(p));
                    return tex;
                }
            }
            return null;
        }

        static Texture2D GetIcon(ref Texture2D cache, string filename)
        {
            if (cache != null) return cache;
            cache = LoadIcon(filename);
            return cache;
        }

        static Texture2D CameraIcon => GetIcon(ref _iconCamera, "photo_camera_white.png");
        static Texture2D VideocamIcon => GetIcon(ref _iconVideocam, "videocam_white.png");
        static Texture2D StopCircleIcon => GetIcon(ref _iconStopCircle, "stop_circle_white.png");
        static Texture2D LogoIcon => GetIcon(ref _iconLogo, "bugpunch_logo.png");

        static VisualElement CreateIconElement(Texture2D icon, int size, Color? tint = null)
        {
            var el = new VisualElement();
            el.style.width = size;
            el.style.height = size;
            if (icon != null)
            {
                el.style.backgroundImage = icon;
                el.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                if (tint.HasValue)
                    el.style.unityBackgroundImageTintColor = tint.Value;
            }
            el.pickingMode = PickingMode.Ignore;
            return el;
        }

        // ─── Logo Header ──────────────────────────────────────────────

        static VisualElement CreateLogoHeader()
        {
            var logo = LogoIcon;
            if (logo == null) return null;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 12;

            var img = CreateIconElement(logo, 28);
            img.style.marginRight = 8;
            row.Add(img);

            var name = new Label("Bugpunch");
            name.style.fontSize = 14;
            name.style.color = new Color(0.6f, 0.6f, 0.6f, 1);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(name);

            return row;
        }

        static VisualElement CreateBackdrop(Action onClickOutside = null)
        {
            var backdrop = new VisualElement();
            backdrop.AddToClassList("bp-backdrop");
            // Inline fallback styles in case USS didn't load
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0;
            backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new Color(0, 0, 0, 0.7f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;

            if (onClickOutside != null)
            {
                backdrop.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.target == backdrop)
                        onClickOutside();
                });
            }

            return backdrop;
        }

        static VisualElement CreateCard(bool wide = false)
        {
            var card = new VisualElement();
            card.AddToClassList("bp-card");
            if (wide) card.AddToClassList("bp-card-wide");
            // Inline fallback
            card.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 0.96f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.paddingTop = 24; card.style.paddingBottom = 24;
            card.style.paddingLeft = 24; card.style.paddingRight = 24;
            card.style.maxWidth = wide ? 700 : 420;
            card.style.minWidth = wide ? 480 : 320;
            // Stop clicks on card from propagating to backdrop
            card.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return card;
        }

        static Label CreateLabel(string text, string className, Color? color = null, int fontSize = 13, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.style.color = color ?? Color.white;
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        static TextField CreateTextField(string placeholder, bool multiline = false)
        {
            var field = new TextField();
            field.multiline = multiline;
            field.AddToClassList(multiline ? "bp-textarea" : "bp-input");

            // Style the inner input
            var input = field.Q<VisualElement>(className: "unity-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.19f, 0.19f, 0.19f, 1);
                input.style.borderTopColor = input.style.borderBottomColor =
                    input.style.borderLeftColor = input.style.borderRightColor = new Color(0.31f, 0.31f, 0.31f, 1);
                input.style.borderTopWidth = input.style.borderBottomWidth =
                    input.style.borderLeftWidth = input.style.borderRightWidth = 1;
                input.style.borderTopLeftRadius = input.style.borderTopRightRadius =
                    input.style.borderBottomLeftRadius = input.style.borderBottomRightRadius = 6;
                input.style.color = Color.white;
                input.style.paddingTop = 6; input.style.paddingBottom = 6;
                input.style.paddingLeft = 8; input.style.paddingRight = 8;
                if (multiline) input.style.minHeight = 60;
            }

            // Placeholder via a label overlay
            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = new Label(placeholder);
                ph.style.position = Position.Absolute;
                ph.style.left = 8; ph.style.top = 6;
                ph.style.color = new Color(0.45f, 0.45f, 0.45f, 1);
                ph.style.fontSize = 13;
                ph.pickingMode = PickingMode.Ignore;
                field.Add(ph);

                field.RegisterValueChangedCallback(e =>
                    ph.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);
            }

            field.style.marginBottom = 4;
            return field;
        }

        static Button CreateButton(string text, string className, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.AddToClassList("bp-btn");
            btn.AddToClassList(className);
            // Inline fallback
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.paddingTop = 8; btn.style.paddingBottom = 8;
            btn.style.paddingLeft = 20; btn.style.paddingRight = 20;
            btn.style.fontSize = 14;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = 8;

            switch (className)
            {
                case "bp-btn-primary":
                    btn.style.backgroundColor = new Color(0.18f, 0.49f, 0.20f, 1);
                    btn.style.color = Color.white;
                    break;
                case "bp-btn-danger":
                    btn.style.backgroundColor = new Color(0.78f, 0.16f, 0.16f, 1);
                    btn.style.color = Color.white;
                    break;
                case "bp-btn-secondary":
                    btn.style.backgroundColor = new Color(0.26f, 0.26f, 0.26f, 1);
                    btn.style.color = new Color(0.78f, 0.78f, 0.78f, 1);
                    break;
                case "bp-btn-link":
                    btn.style.backgroundColor = Color.clear;
                    btn.style.color = new Color(0.55f, 0.55f, 0.55f, 1);
                    btn.style.paddingTop = 4; btn.style.paddingBottom = 4;
                    btn.style.paddingLeft = 8; btn.style.paddingRight = 8;
                    btn.style.unityFontStyleAndWeight = FontStyle.Normal;
                    btn.style.fontSize = 13;
                    break;
            }

            return btn;
        }

        static VisualElement CreateSeveritySelector(int defaultIndex, Action<int> onChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-severity-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;
            row.style.marginBottom = 8;

            string[] labels = { "Low", "Medium", "High", "Critical" };
            Button[] buttons = new Button[4];
            int selected = defaultIndex;

            void Refresh()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    bool active = i == selected;
                    buttons[i].style.backgroundColor = active
                        ? new Color(0.24f, 0.39f, 0.63f, 1)
                        : new Color(0.22f, 0.22f, 0.22f, 1);
                    buttons[i].style.borderTopColor = buttons[i].style.borderBottomColor =
                        buttons[i].style.borderLeftColor = buttons[i].style.borderRightColor =
                            active ? new Color(0.39f, 0.63f, 0.96f, 1) : new Color(0.31f, 0.31f, 0.31f, 1);
                    buttons[i].style.color = active ? Color.white : new Color(0.78f, 0.78f, 0.78f, 1);
                }
            }

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var btn = new Button(() => { selected = idx; Refresh(); onChanged(idx); });
                btn.text = labels[i];
                btn.AddToClassList("bp-severity-btn");
                btn.style.flexGrow = 1;
                btn.style.borderTopWidth = btn.style.borderBottomWidth =
                    btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
                btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                    btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 4;
                btn.style.paddingTop = 6; btn.style.paddingBottom = 6;
                btn.style.fontSize = 12;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;
                if (i < labels.Length - 1) btn.style.marginRight = 4;
                buttons[i] = btn;
                row.Add(btn);
            }

            Refresh();
            return row;
        }

        static VisualElement CreateButtonRow(params Button[] btns)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-button-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 12;
            foreach (var b in btns) row.Add(b);
            return row;
        }

        void ShowDialog(VisualElement content)
        {
            var doc = EnsureDocument();
            var ss = EnsureStyleSheet();
            doc.rootVisualElement.Clear();
            if (ss != null) doc.rootVisualElement.styleSheets.Add(ss);
            doc.rootVisualElement.Add(content);
        }

        void DismissDialog()
        {
            if (_doc != null)
                _doc.rootVisualElement.Clear();
        }

        // ─── INativeDialog Implementation ──────────────────────────────

        public void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback)
        {
            var backdrop = CreateBackdrop();
            var card = CreateCard();

            var logo = CreateLogoHeader();
            if (logo != null) card.Add(logo);
            card.Add(CreateLabel("Script Permission", "bp-title", Color.white, 20, FontStyle.Bold));
            card.Add(CreateLabel($"The server wants to run a script on this device:\n\n{scriptName}",
                "bp-subtitle"));
            if (!string.IsNullOrEmpty(scriptDescription))
                card.Add(CreateLabel(scriptDescription, "bp-body"));

            card.Add(CreateButtonRow(
                CreateButton("Deny", "bp-btn-secondary", () => { DismissDialog(); callback?.Invoke(PermissionResult.Deny); }),
                CreateButton("Always Allow", "bp-btn-secondary", () => { DismissDialog(); callback?.Invoke(PermissionResult.AllowAlways); }),
                CreateButton("Allow Once", "bp-btn-primary", () => { DismissDialog(); callback?.Invoke(PermissionResult.AllowOnce); })
            ));

            backdrop.Add(card);
            ShowDialog(backdrop);
        }

        public void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel)
        {
            var backdrop = CreateBackdrop(() => { DismissDialog(); onCancel?.Invoke(); });
            var card = CreateCard();

            var logo = CreateLogoHeader();
            if (logo != null) card.Add(logo);
            card.Add(CreateLabel("Bug Report", "bp-title", Color.white, 20, FontStyle.Bold));
            card.Add(CreateLabel("Describe the issue you encountered.", "bp-subtitle"));

            card.Add(CreateLabel("Title", "bp-label"));
            var titleField = CreateTextField("Brief description of the bug");
            card.Add(titleField);

            card.Add(CreateLabel("Description", "bp-label"));
            var descField = CreateTextField("Steps to reproduce, expected vs actual behavior", multiline: true);
            card.Add(descField);

            card.Add(CreateLabel("Severity", "bp-label"));
            int severity = 1;
            card.Add(CreateSeveritySelector(1, i => severity = i));

            string[] sevNames = { "low", "medium", "high", "critical" };

            var submitBtn = CreateButton("Submit", "bp-btn-primary", () =>
            {
                DismissDialog();
                onSubmit?.Invoke(new BugReportData
                {
                    title = titleField.value,
                    description = descField.value,
                    severity = sevNames[severity],
                    includeScreenshot = true,
                    includeLogs = true
                });
            });

            // Disable submit until title is filled
            submitBtn.SetEnabled(false);
            titleField.RegisterValueChangedCallback(e =>
                submitBtn.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));

            card.Add(CreateButtonRow(
                CreateButton("Cancel", "bp-btn-secondary", () => { DismissDialog(); onCancel?.Invoke(); }),
                submitBtn
            ));

            backdrop.Add(card);
            ShowDialog(backdrop);
        }

        public void ShowCrashReport(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss)
        {
            var backdrop = CreateBackdrop();
            var card = CreateCard(wide: true);

            // Header with logo
            var logo = CreateLogoHeader();
            if (logo != null) card.Add(logo);
            card.Add(CreateLabel("Crash Report", "bp-title bp-title-error",
                new Color(1f, 0.3f, 0.3f), 20, FontStyle.Bold));

            // Top row: thumbnail + exception
            var topRow = new VisualElement();
            topRow.AddToClassList("bp-crash-top");
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.marginBottom = 8;

            // Screenshot thumbnail
            if (context.screenshotJpg != null && context.screenshotJpg.Length > 0)
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(context.screenshotJpg);
                var thumb = new VisualElement();
                thumb.AddToClassList("bp-thumbnail");
                thumb.style.width = 180;
                thumb.style.height = 140;
                thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                    thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 4;
                thumb.style.marginRight = 12;
                thumb.style.backgroundImage = tex;
                thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                topRow.Add(thumb);
            }

            // Exception + stack trace
            var infoCol = new VisualElement();
            infoCol.AddToClassList("bp-crash-info");
            infoCol.style.flexGrow = 1;
            infoCol.style.flexShrink = 1;

            if (!string.IsNullOrEmpty(context.exceptionMessage))
            {
                var excLabel = CreateLabel(context.exceptionMessage, "bp-exception-text",
                    new Color(1f, 0.5f, 0.5f), 13, FontStyle.Bold);
                excLabel.style.marginBottom = 6;
                infoCol.Add(excLabel);
            }

            if (!string.IsNullOrEmpty(context.stackTrace))
            {
                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.AddToClassList("bp-stack-scroll");
                scrollView.style.maxHeight = 120;
                scrollView.style.backgroundColor = new Color(0.11f, 0.11f, 0.11f, 1);
                scrollView.style.borderTopLeftRadius = scrollView.style.borderTopRightRadius =
                    scrollView.style.borderBottomLeftRadius = scrollView.style.borderBottomRightRadius = 4;
                scrollView.style.paddingTop = 6; scrollView.style.paddingBottom = 6;
                scrollView.style.paddingLeft = 6; scrollView.style.paddingRight = 6;

                var stackLabel = CreateLabel(context.stackTrace, "bp-stack-text",
                    new Color(0.67f, 0.67f, 0.67f), 11);
                scrollView.Add(stackLabel);
                infoCol.Add(scrollView);
            }

            topRow.Add(infoCol);
            card.Add(topRow);

            // Title
            card.Add(CreateLabel("Title", "bp-label"));
            var titleField = CreateTextField("Crash summary");
            // Pre-fill from exception
            if (!string.IsNullOrEmpty(context.exceptionMessage))
            {
                var msg = context.exceptionMessage;
                titleField.value = msg.Length > 120 ? msg.Substring(0, 120) : msg;
            }
            card.Add(titleField);

            // Description
            card.Add(CreateLabel("Description (what were you doing?)", "bp-label"));
            var descField = CreateTextField("Steps leading up to the crash", multiline: true);
            card.Add(descField);

            // Severity
            card.Add(CreateLabel("Severity", "bp-label"));
            int severity = 2; // default "high" for crashes
            card.Add(CreateSeveritySelector(2, i => severity = i));

            // Toggles
            bool includeVideo = !string.IsNullOrEmpty(context.videoPath);
            bool includeLogs = true;

            var toggleRow = new VisualElement();
            toggleRow.AddToClassList("bp-toggle-row");
            toggleRow.style.flexDirection = FlexDirection.Row;
            toggleRow.style.marginTop = 4;
            toggleRow.style.marginBottom = 8;

            var videoToggle = new Toggle("Include video");
            videoToggle.value = includeVideo;
            videoToggle.SetEnabled(!string.IsNullOrEmpty(context.videoPath));
            videoToggle.RegisterValueChangedCallback(e => includeVideo = e.newValue);
            videoToggle.style.marginRight = 16;
            videoToggle.style.color = new Color(0.78f, 0.78f, 0.78f, 1);
            toggleRow.Add(videoToggle);

            var logsToggle = new Toggle("Include logs");
            logsToggle.value = true;
            logsToggle.RegisterValueChangedCallback(e => includeLogs = e.newValue);
            logsToggle.style.color = new Color(0.78f, 0.78f, 0.78f, 1);
            toggleRow.Add(logsToggle);

            card.Add(toggleRow);

            // Buttons
            string[] sevNames = { "low", "medium", "high", "critical" };

            var submitBtn = CreateButton("Submit Report", "bp-btn-primary", () =>
            {
                DismissDialog();
                onSubmit?.Invoke(new CrashReportResult
                {
                    title = titleField.value,
                    description = descField.value,
                    severity = sevNames[severity],
                    includeVideo = includeVideo,
                    includeLogs = includeLogs
                });
            });
            submitBtn.SetEnabled(!string.IsNullOrEmpty(titleField.value));
            titleField.RegisterValueChangedCallback(e =>
                submitBtn.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));

            card.Add(CreateButtonRow(
                CreateButton("Dismiss", "bp-btn-secondary", () => { DismissDialog(); onDismiss?.Invoke(); }),
                submitBtn
            ));

            backdrop.Add(card);
            ShowDialog(backdrop);
        }

        public void ShowReportWelcome(Action onConfirm, Action onCancel)
        {
            var backdrop = CreateBackdrop(() => { DismissDialog(); onCancel?.Invoke(); });

            var card = CreateCard();
            card.AddToClassList("bp-welcome-card");
            card.style.alignItems = Align.Center;
            card.style.maxWidth = 340;

            // Bugpunch logo at the top
            var logoImg = LogoIcon;
            if (logoImg != null)
            {
                var logoEl = CreateIconElement(logoImg, 48);
                logoEl.style.marginBottom = 12;
                card.Add(logoEl);
            }

            // Icons row (camera + video)
            var iconsRow = new VisualElement();
            iconsRow.AddToClassList("bp-welcome-icons");
            iconsRow.style.flexDirection = FlexDirection.Row;
            iconsRow.style.justifyContent = Justify.Center;
            iconsRow.style.marginBottom = 12;

            // Camera icon with tinted background
            var camBg = new VisualElement();
            camBg.style.width = 48; camBg.style.height = 48;
            camBg.style.marginLeft = 8; camBg.style.marginRight = 8;
            camBg.style.borderTopLeftRadius = camBg.style.borderTopRightRadius =
                camBg.style.borderBottomLeftRadius = camBg.style.borderBottomRightRadius = 8;
            camBg.style.backgroundColor = new Color(0.15f, 0.28f, 0.4f, 1);
            camBg.style.alignItems = Align.Center;
            camBg.style.justifyContent = Justify.Center;
            camBg.Add(CreateIconElement(CameraIcon, 28, new Color(0.39f, 0.71f, 0.96f)));
            iconsRow.Add(camBg);

            // Video icon with tinted background
            var vidBg = new VisualElement();
            vidBg.style.width = 48; vidBg.style.height = 48;
            vidBg.style.marginLeft = 8; vidBg.style.marginRight = 8;
            vidBg.style.borderTopLeftRadius = vidBg.style.borderTopRightRadius =
                vidBg.style.borderBottomLeftRadius = vidBg.style.borderBottomRightRadius = 8;
            vidBg.style.backgroundColor = new Color(0.35f, 0.15f, 0.15f, 1);
            vidBg.style.alignItems = Align.Center;
            vidBg.style.justifyContent = Justify.Center;
            vidBg.Add(CreateIconElement(VideocamIcon, 28, new Color(0.94f, 0.33f, 0.31f)));
            iconsRow.Add(vidBg);

            card.Add(iconsRow);

            // Title
            card.Add(CreateLabel("Report a Bug", "bp-welcome-title", Color.white, 20, FontStyle.Bold));

            // Body
            var body = CreateLabel(
                "We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details.",
                "bp-welcome-body", new Color(0.73f, 0.73f, 0.73f), 14);
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginBottom = 20;
            body.style.paddingLeft = 8; body.style.paddingRight = 8;
            card.Add(body);

            // Got it button (full width)
            var gotItBtn = CreateButton("Got it", "bp-btn-primary", () =>
            {
                DismissDialog();
                onConfirm?.Invoke();
            });
            gotItBtn.style.width = Length.Percent(100);
            gotItBtn.style.paddingTop = 12; gotItBtn.style.paddingBottom = 12;
            gotItBtn.style.fontSize = 16;
            gotItBtn.style.marginLeft = 0;
            gotItBtn.style.marginBottom = 8;
            card.Add(gotItBtn);

            // Cancel link
            var cancelBtn = CreateButton("Cancel", "bp-btn-link", () =>
            {
                DismissDialog();
                onCancel?.Invoke();
            });
            cancelBtn.style.marginLeft = 0;
            cancelBtn.style.alignSelf = Align.Center;
            card.Add(cancelBtn);

            backdrop.Add(card);
            ShowDialog(backdrop);
        }

        public void ShowRecordingOverlay(Action onStopRecording)
        {
            _recordingCallback = onStopRecording;

            var doc = EnsureDocument();
            var root = doc.rootVisualElement;
            root.Clear();

            var ss = EnsureStyleSheet();
            if (ss != null) root.styleSheets.Add(ss);

            // Floating container in the bottom-right
            var container = new VisualElement();
            container.AddToClassList("bp-recording-container");
            container.style.position = Position.Absolute;
            container.style.bottom = 56;
            container.style.right = 20;
            container.style.alignItems = Align.Center;

            // Record/stop button (red circle with stop icon)
            var btn = new Button(() => { _recordingCallback?.Invoke(); });
            btn.AddToClassList("bp-record-btn");
            btn.style.width = 56; btn.style.height = 56;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 28;
            btn.style.backgroundColor = new Color(0.83f, 0.18f, 0.18f, 1);
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.paddingTop = 0; btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0; btn.style.paddingRight = 0;

            // Stop icon (use PNG if available, otherwise white square)
            var stopTex = StopCircleIcon;
            if (stopTex != null)
            {
                btn.Add(CreateIconElement(stopTex, 28, Color.white));
            }
            else
            {
                var stopSquare = new VisualElement();
                stopSquare.style.width = 20; stopSquare.style.height = 20;
                stopSquare.style.borderTopLeftRadius = stopSquare.style.borderTopRightRadius =
                    stopSquare.style.borderBottomLeftRadius = stopSquare.style.borderBottomRightRadius = 3;
                stopSquare.style.backgroundColor = Color.white;
                stopSquare.pickingMode = PickingMode.Ignore;
                btn.Add(stopSquare);
            }

            container.Add(btn);

            // Timer label
            var timer = new Label("0:00");
            timer.AddToClassList("bp-record-timer");
            timer.style.fontSize = 11;
            timer.style.unityFontStyleAndWeight = FontStyle.Bold;
            timer.style.color = Color.white;
            timer.style.unityTextAlign = TextAnchor.MiddleCenter;
            timer.style.marginTop = 4;
            timer.pickingMode = PickingMode.Ignore;
            container.Add(timer);

            // ── Drag support ──
            // Track drag state to distinguish click (tap to stop) from drag (reposition)
            bool isDragging = false;
            Vector2 dragStartPos = Vector2.zero;

            container.RegisterCallback<PointerDownEvent>(e =>
            {
                isDragging = false;
                dragStartPos = e.position;
                container.CapturePointer(e.pointerId);
                e.StopPropagation();
            });

            container.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!container.HasPointerCapture(e.pointerId)) return;
                var delta = (Vector2)e.position - dragStartPos;
                if (!isDragging && delta.magnitude > 5f)
                    isDragging = true;
                if (isDragging)
                {
                    container.style.right = StyleKeyword.Auto;
                    container.style.bottom = StyleKeyword.Auto;
                    container.style.left = container.resolvedStyle.left + delta.x;
                    container.style.top = container.resolvedStyle.top + delta.y;
                    dragStartPos = e.position;
                }
                e.StopPropagation();
            });

            container.RegisterCallback<PointerUpEvent>(e =>
            {
                container.ReleasePointer(e.pointerId);
                // Only fire the callback if it was a tap, not a drag
                if (!isDragging)
                    _recordingCallback?.Invoke();
                e.StopPropagation();
            });

            // Override the button's own click since we handle it via pointer events on the container
            btn.clickable = null;

            // Make the whole root pass-through except our container
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.right = 0; root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            root.Add(container);

            // Start the timer using UI Toolkit's scheduler
            _recordStartTime = Time.realtimeSinceStartup;
            _timerSchedule = timer.schedule.Execute(() =>
            {
                float elapsed = Time.realtimeSinceStartup - _recordStartTime;
                int m = (int)elapsed / 60;
                int s = (int)elapsed % 60;
                timer.text = $"{m}:{s:D2}";
            }).Every(1000);
        }

        public void HideRecordingOverlay()
        {
            if (_timerSchedule != null)
            {
                _timerSchedule.Pause();
                _timerSchedule = null;
            }
            _recordingCallback = null;

            if (_doc != null)
                _doc.rootVisualElement.Clear();
        }

        public void ShowRequestHelp(Action<int> onChoice, Action onCancel)
        {
            // Editor + standalone fall back to the existing UI Toolkit picker —
            // it already owns its own UIDocument host so it won't clobber this
            // dialog's root.
            BugpunchRequestHelpPicker.ShowUIToolkitFallback(onChoice, onCancel);
        }

        public void ShowChatBoard()
        {
            // The chat board owns its own UIDocument host; calling directly
            // won't clobber other dialogs and lands on the right sub-view
            // based on server state (threads exist vs empty vs disabled).
            BugpunchChatBoard.Show();
        }
    }
}
