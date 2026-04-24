using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    /// <summary>
    /// Entry point for <c>Bugpunch.RequestHelp()</c>. Presents a modal picker
    /// with three choices — Record a bug / Ask for help / Send feedback —
    /// giving games a single-button surface for all three flows.
    ///
    /// On-device, this delegates to the native dialog implementation (Android
    /// card via <see cref="AndroidDialog.ShowRequestHelp"/>, iOS view
    /// controller via <see cref="IOSDialog.ShowRequestHelp"/>) so the picker
    /// matches the rest of the SDK's native look. In the Editor and on
    /// standalone builds the UI Toolkit fallback below is used instead.
    /// </summary>
    public static class BugpunchRequestHelpPicker
    {
        // ─── Public entry point ──────────────────────────────────────────

        /// <summary>
        /// Show the picker. Safe to call while BugpunchClient is starting —
        /// if not initialized the button handlers will simply no-op and log
        /// a warning (mirrors the rest of the <see cref="Bugpunch"/> API).
        /// </summary>
        public static void Show()
        {
            var dialog = NativeDialogFactory.Create();
            if (dialog == null || !dialog.IsSupported)
            {
                // Very defensive — NativeDialogFactory always returns a
                // usable implementation today. Fall through to UIToolkit
                // anyway so the feature still works if someone shrinks the
                // factory later.
                ShowUIToolkitFallback(DispatchChoice, () => { });
                return;
            }

            dialog.ShowRequestHelp(DispatchChoice, () => { /* user cancelled — no-op */ });
        }

        /// <summary>
        /// Route a picker result to the appropriate follow-up flow.
        /// Exposed so native impls and the UI Toolkit fallback share the
        /// same dispatch.
        /// </summary>
        static void DispatchChoice(int choice)
        {
            switch (choice)
            {
                case 0: OnRecordBug(); break;
                case 1: OnAskForHelp(); break;
                case 2: OnSendFeedback(); break;
                default:
                    Debug.LogWarning($"[Bugpunch.RequestHelp] Unknown picker choice: {choice}");
                    break;
            }
        }

        static void OnRecordBug()
        {
            BugpunchNative.EnterDebugMode(false);
        }

        static void OnAskForHelp()
        {
            // v2 routes "Ask for help" into the live chat board (threads +
            // inline replies) instead of the v1 one-shot bug-report form.
            // The board handles its own "no config" / "hours disabled" /
            // "off-hours banner" UX — we just kick it off here.
            var dialog = NativeDialogFactory.Create();
            if (dialog == null || !dialog.IsSupported)
            {
                Debug.LogWarning("[Bugpunch.RequestHelp] No dialog surface available — falling back to UIToolkit chat board.");
                BugpunchChatBoard.Show();
                return;
            }
            dialog.ShowChatBoard();
        }

        static void OnSendFeedback()
        {
            BugpunchFeedbackBoard.Show();
        }

        // ─── UI Toolkit fallback (Editor + Standalone) ────────────────────

        static GameObject _hostGO;
        static UIDocument _doc;
        static StyleSheet _styleSheet;

        /// <summary>
        /// UI Toolkit implementation of the picker. This is the default on
        /// Editor / Standalone and is also what <see cref="UIToolkitDialog"/>
        /// delegates to so all surfaces present the same picker.
        /// Public/internal so the UIToolkitDialog bridge can reach it without
        /// re-implementing the card.
        /// </summary>
        internal static void ShowUIToolkitFallback(Action<int> onChoice, Action onCancel)
        {
            var doc = EnsureDocument();
            var ss = EnsureStyleSheet();
            var root = doc.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Position;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.right = 0; root.style.bottom = 0;
            if (ss != null) root.styleSheets.Add(ss);

            void Pick(int i)
            {
                Dismiss();
                onChoice?.Invoke(i);
            }

            void Cancel()
            {
                Dismiss();
                onCancel?.Invoke();
            }

            var backdrop = CreateBackdrop(Cancel);
            var card = CreateCard();
            BuildCard(card, Pick, Cancel);

            // Escape key closes without action.
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Cancel(); e.StopPropagation(); }
            });
            root.Focus();

            backdrop.Add(card);
            root.Add(backdrop);
        }

        static void BuildCard(VisualElement card, Action<int> pick, Action cancel)
        {
            // Title
            var title = new Label("What would you like to do?");
            title.style.color = Color.white;
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            title.style.whiteSpace = WhiteSpace.Normal;
            card.Add(title);

            var subtitle = new Label("Pick what fits — we'll only bother the dev team with what you send.");
            subtitle.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
            subtitle.style.fontSize = 13;
            subtitle.style.marginBottom = 16;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            card.Add(subtitle);

            card.Add(CreatePickerButton(
                "Record a bug",
                "Capture a video + report a problem",
                new Color(0.58f, 0.22f, 0.22f, 1),
                "bugpunch-help-bug",
                () => pick(0)));

            card.Add(CreatePickerButton(
                "Ask for help",
                "Short question to the dev team",
                new Color(0.20f, 0.38f, 0.60f, 1),
                "bugpunch-help-ask",
                () => pick(1)));

            card.Add(CreatePickerButton(
                "Send feedback",
                "Suggest / vote on features",
                new Color(0.25f, 0.49f, 0.30f, 1),
                "bugpunch-help-feedback",
                () => pick(2)));

            // Close link
            var closeBtn = new Button(() => cancel()) { text = "Cancel" };
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.color = new Color(0.6f, 0.6f, 0.6f, 1);
            closeBtn.style.marginTop = 12;
            closeBtn.style.paddingTop = 6; closeBtn.style.paddingBottom = 6;
            closeBtn.style.borderTopWidth = closeBtn.style.borderBottomWidth =
                closeBtn.style.borderLeftWidth = closeBtn.style.borderRightWidth = 0;
            closeBtn.style.fontSize = 13;
            closeBtn.style.alignSelf = Align.Center;
            card.Add(closeBtn);
        }

        static Button CreatePickerButton(string title, string caption, Color accent, string iconResource, Action onClick)
        {
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f, 1);
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.28f, 0.28f, 0.28f, 1);
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 8;
            btn.style.paddingTop = 12; btn.style.paddingBottom = 12;
            btn.style.paddingLeft = 14; btn.style.paddingRight = 14;
            btn.style.marginTop = 0; btn.style.marginBottom = 8;
            btn.style.marginLeft = 0; btn.style.marginRight = 0;

            // Icon on the left. Falls back to the coloured accent dot if the
            // Resources texture is missing (unusual, but avoids a broken row
            // in downstream projects that strip the PNGs).
            var icon = new VisualElement();
            var tex = Resources.Load<Texture2D>(iconResource);
            if (tex != null)
            {
                icon.style.width = 48; icon.style.height = 48;
                icon.style.backgroundImage = new StyleBackground(tex);
                icon.style.marginRight = 12;
            }
            else
            {
                icon.style.width = 10; icon.style.height = 10;
                icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius =
                    icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = 5;
                icon.style.backgroundColor = accent;
                icon.style.marginRight = 12;
            }
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.flexShrink = 1;
            textCol.style.flexDirection = FlexDirection.Column;
            textCol.pickingMode = PickingMode.Ignore;

            var titleLabel = new Label(title);
            titleLabel.style.color = Color.white;
            titleLabel.style.fontSize = 15;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 2;
            titleLabel.pickingMode = PickingMode.Ignore;
            textCol.Add(titleLabel);

            var captionLabel = new Label(caption);
            captionLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
            captionLabel.style.fontSize = 12;
            captionLabel.style.whiteSpace = WhiteSpace.Normal;
            captionLabel.pickingMode = PickingMode.Ignore;
            textCol.Add(captionLabel);

            btn.Add(textCol);

            // Right chevron
            var chev = new Label(">");
            chev.style.color = new Color(0.55f, 0.55f, 0.55f, 1);
            chev.style.fontSize = 14;
            chev.style.marginLeft = 8;
            chev.style.unityFontStyleAndWeight = FontStyle.Bold;
            chev.pickingMode = PickingMode.Ignore;
            btn.Add(chev);

            return btn;
        }

        static VisualElement CreateBackdrop(Action onClickOutside)
        {
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0;
            backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new Color(0, 0, 0, 0.65f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;

            backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == backdrop) onClickOutside?.Invoke();
            });
            return backdrop;
        }

        static VisualElement CreateCard()
        {
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 0.97f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 12;
            card.style.paddingTop = 24; card.style.paddingBottom = 20;
            card.style.paddingLeft = 24; card.style.paddingRight = 24;
            card.style.maxWidth = 420;
            card.style.minWidth = 320;
            card.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return card;
        }

        static UIDocument EnsureDocument()
        {
            if (_doc != null) return _doc;

            _hostGO = new GameObject("Bugpunch_RequestHelpPicker");
            _hostGO.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_hostGO);

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            // Sort below the main dialog host (30000) so if a picker choice
            // opens a dialog (bug report form), the form sits on top without
            // a frame of flicker.
            panelSettings.sortingOrder = 29000;

            _doc = _hostGO.AddComponent<UIDocument>();
            _doc.panelSettings = panelSettings;
            return _doc;
        }

        static StyleSheet EnsureStyleSheet()
        {
            if (_styleSheet != null) return _styleSheet;
            _styleSheet = Resources.Load<StyleSheet>("BugpunchDialogs");
#if UNITY_EDITOR
            if (_styleSheet == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("BugpunchDialogs t:StyleSheet");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _styleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }
#endif
            return _styleSheet;
        }

        static void Dismiss()
        {
            if (_doc != null) _doc.rootVisualElement.Clear();
        }
    }
}
