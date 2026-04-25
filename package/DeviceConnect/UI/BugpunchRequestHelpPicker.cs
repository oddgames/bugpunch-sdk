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
    ///
    /// Layout: on wide containers (&gt;= 540px) the three options render as a
    /// horizontal row of equal-width cards; on narrow containers the row
    /// collapses to a vertical stack via a <see cref="GeometryChangedEvent"/>
    /// callback that swaps the container's flex direction.
    /// </summary>
    public static class BugpunchRequestHelpPicker
    {
        // Breakpoint below which the 3 option cards stack vertically instead
        // of sitting side-by-side. 540px covers most phone portrait widths.
        const float NarrowBreakpoint = 540f;

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
                case 0: OnAskForHelp(); break;
                case 1: OnRecordBug(); break;
                case 2: OnRequestFeature(); break;
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

        static void OnRequestFeature()
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
            var theme = BugpunchTheme.Current;
            var strings = BugpunchStrings.Current;

            // Title
            var title = new Label(strings.Text("pickerTitle", "What would you like to do?"));
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeTitle;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            title.style.whiteSpace = WhiteSpace.Normal;
            card.Add(title);

            var subtitle = new Label(strings.Text("pickerSubtitle",
                "Pick what fits — we'll only bother the dev team with what you send."));
            subtitle.style.color = theme.textSecondary;
            subtitle.style.fontSize = theme.fontSizeCaption + 1; // 13px — a touch above caption
            subtitle.style.marginBottom = 16;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            card.Add(subtitle);

            // Horizontal row of 3 equal-width option cards. GeometryChangedEvent
            // flips to Column when the container becomes narrower than the
            // breakpoint so the same 3 children reflow without rebuild.
            var options = new VisualElement();
            options.style.flexDirection = FlexDirection.Row;
            options.style.flexWrap = Wrap.NoWrap;
            options.style.alignItems = Align.Stretch;
            options.style.justifyContent = Justify.Center;

            var askCard      = CreateOptionCard(
                strings.Text("pickerAskTitle",     "Ask for help"),
                strings.Text("pickerAskCaption",   "Short question to the dev team"),
                theme.accentChat,     "ask",      () => pick(0), theme);
            var bugCard      = CreateOptionCard(
                strings.Text("pickerBugTitle",     "Record a bug"),
                strings.Text("pickerBugCaption",   "Capture a video + report a problem"),
                theme.accentBug,      "bug",      () => pick(1), theme);
            var featureCard  = CreateOptionCard(
                strings.Text("pickerFeatureTitle",   "Request a feature"),
                strings.Text("pickerFeatureCaption", "Suggest / vote on improvements"),
                theme.accentFeedback, "feedback", () => pick(2), theme);

            options.Add(askCard);
            options.Add(bugCard);
            options.Add(featureCard);
            card.Add(options);

            // Breakpoint reflow: Row ↔ Column driven by the container's
            // resolved width. We also swap each option's width/margin between
            // the two modes so a column stack doesn't leave right-side gaps.
            options.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                ApplyPickerLayout(options, evt.newRect.width);
            });
            // Prime the layout — GeometryChangedEvent won't fire on an
            // element with zero initial width reliably across editors.
            options.schedule.Execute(() => ApplyPickerLayout(options, options.resolvedStyle.width)).StartingIn(16);

            // Close link
            var closeBtn = new Button(() => cancel()) { text = strings.Text("pickerCancel", "Cancel") };
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.color = theme.textMuted;
            closeBtn.style.marginTop = 14;
            closeBtn.style.paddingTop = 6; closeBtn.style.paddingBottom = 6;
            closeBtn.style.borderTopWidth = closeBtn.style.borderBottomWidth =
                closeBtn.style.borderLeftWidth = closeBtn.style.borderRightWidth = 0;
            closeBtn.style.fontSize = theme.fontSizeCaption + 1;
            closeBtn.style.alignSelf = Align.Center;
            card.Add(closeBtn);
        }

        static void ApplyPickerLayout(VisualElement container, float width)
        {
            if (float.IsNaN(width) || width <= 0) return;
            bool narrow = width < NarrowBreakpoint;
            container.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;

            for (int i = 0; i < container.childCount; i++)
            {
                var c = container[i];
                if (narrow)
                {
                    c.style.width = Length.Percent(100);
                    c.style.marginLeft = 0;
                    c.style.marginRight = 0;
                    c.style.marginTop = i == 0 ? 0 : 10;
                    c.style.marginBottom = 0;
                }
                else
                {
                    // Three equal-width columns with a 12px gap between them.
                    c.style.width = Length.Percent(100f / 3f);
                    c.style.marginLeft = i == 0 ? 0 : 6;
                    c.style.marginRight = i == container.childCount - 1 ? 0 : 6;
                    c.style.marginTop = 0;
                    c.style.marginBottom = 0;
                }
            }
        }

        /// <summary>
        /// One option card: centred 96px icon, title, caption, accent bottom
        /// border. Entire card is tap-to-pick.
        /// </summary>
        static VisualElement CreateOptionCard(string title, string caption, Color accent, string iconSlot, Action onClick, BugpunchTheme theme)
        {
            var card = new VisualElement();
            card.style.backgroundColor = theme.cardBackground;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = theme.cardBorder;
            card.style.borderTopWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            // Accent bottom border (2px) — signals the follow-up action colour.
            card.style.borderBottomWidth = 2;
            card.style.borderBottomColor = accent;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = theme.cardRadius;
            card.style.paddingTop = 16; card.style.paddingBottom = 16;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.minHeight = 200;
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.FlexStart;
            card.style.flexDirection = FlexDirection.Column;

            // Whole card is the tap target.
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                onClick?.Invoke();
                e.StopPropagation();
            });

            // Icon — 96px square, centred. Uses the theme's override texture
            // when set, else the packaged Resources texture. Falls back to an
            // accent dot if everything's missing (same pattern as the old picker).
            var icon = new VisualElement();
            var tex = theme.ResolveIcon(iconSlot);
            if (tex != null)
            {
                icon.style.width = 96; icon.style.height = 96;
                icon.style.backgroundImage = new StyleBackground(tex);
            }
            else
            {
                icon.style.width = 48; icon.style.height = 48;
                icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius =
                    icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = 24;
                icon.style.backgroundColor = accent;
            }
            icon.style.marginBottom = 10;
            icon.pickingMode = PickingMode.Ignore;
            card.Add(icon);

            var titleLabel = new Label(title);
            titleLabel.style.color = theme.textPrimary;
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginBottom = 4;
            titleLabel.pickingMode = PickingMode.Ignore;
            card.Add(titleLabel);

            var captionLabel = new Label(caption);
            captionLabel.style.color = theme.textSecondary;
            captionLabel.style.fontSize = 13;
            captionLabel.style.whiteSpace = WhiteSpace.Normal;
            captionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            captionLabel.style.maxWidth = Length.Percent(100);
            captionLabel.pickingMode = PickingMode.Ignore;
            card.Add(captionLabel);

            return card;
        }

        static VisualElement CreateBackdrop(Action onClickOutside)
        {
            var theme = BugpunchTheme.Current;
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0;
            backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = theme.backdrop;
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
            var theme = BugpunchTheme.Current;
            var card = new VisualElement();
            card.style.backgroundColor = theme.cardBackground;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = theme.cardBorder;
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = theme.cardRadius;
            card.style.paddingTop = 24; card.style.paddingBottom = 20;
            card.style.paddingLeft = 24; card.style.paddingRight = 24;
            // Wider than the old stack-of-rows layout so three cards fit
            // side-by-side comfortably without squashing captions.
            card.style.maxWidth = 640;
            card.style.minWidth = 320;
            card.style.width = Length.Percent(92);
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
