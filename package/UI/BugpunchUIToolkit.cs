using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.UI
{
    /// <summary>
    /// Shared UI Toolkit styling helpers used by BugpunchFeedbackBoard,
    /// BugpunchChatBoard, and UIToolkitDialog. All styling reads from
    /// BugpunchTheme.Current so callers stay theme-agnostic.
    ///
    /// Divergent numeric tweaks across callers are exposed as parameters
    /// (card width/padding, input minHeight, button padding/marginLeft).
    /// Defaults match the most common caller (FeedbackBoard / generic
    /// dialog) so most call sites remain one-liners.
    /// </summary>
    internal static class BugpunchUIToolkit
    {
        // ─── Backdrop ─────────────────────────────────────────────────────

        /// <summary>
        /// Full-screen modal backdrop. Click on the backdrop itself (not
        /// children) invokes <paramref name="onClickOutside"/>.
        /// </summary>
        public static VisualElement CreateBackdrop(Action onClickOutside = null)
        {
            var theme = BugpunchTheme.Current;
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0;
            backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = theme.backdrop;
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            if (onClickOutside != null)
            {
                backdrop.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.target == backdrop) onClickOutside();
                });
            }
            return backdrop;
        }

        // ─── Card ─────────────────────────────────────────────────────────

        /// <summary>
        /// Centered modal card. Caller passes width and padding params to
        /// preserve per-dialog visual divergences.
        /// </summary>
        public static VisualElement CreateCard(
            float maxWidth = 520f,
            float minWidth = 360f,
            float widthPercent = 90f,
            float verticalPadding = 22f,
            float horizontalPadding = 24f,
            bool relativePosition = false)
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
            card.style.paddingTop = verticalPadding; card.style.paddingBottom = verticalPadding;
            card.style.paddingLeft = horizontalPadding; card.style.paddingRight = horizontalPadding;
            card.style.maxWidth = maxWidth;
            card.style.minWidth = minWidth;
            // widthPercent <= 0 means "don't set width" — UIToolkitDialog's
            // standard card relies on max/min only and doesn't pin a percent.
            if (widthPercent > 0f) card.style.width = Length.Percent(widthPercent);
            if (relativePosition) card.style.position = Position.Relative;
            card.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return card;
        }

        // ─── Text field ───────────────────────────────────────────────────

        /// <summary>
        /// Style a TextField with the dark theme + an optional placeholder
        /// label overlay. <paramref name="multilineMinHeight"/> only takes
        /// effect when multiline is true.
        /// </summary>
        public static void StyleInput(
            TextField field,
            string placeholder,
            bool multiline = false,
            float multilineMinHeight = 44f,
            float placeholderLeft = 10f,
            float placeholderTop = 7f)
        {
            var theme = BugpunchTheme.Current;
            field.multiline = multiline;
            var input = field.Q<VisualElement>(className: "unity-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = theme.cardBackground;
                input.style.borderTopColor = input.style.borderBottomColor =
                    input.style.borderLeftColor = input.style.borderRightColor = theme.cardBorder;
                input.style.borderTopWidth = input.style.borderBottomWidth =
                    input.style.borderLeftWidth = input.style.borderRightWidth = 1;
                input.style.borderTopLeftRadius = input.style.borderTopRightRadius =
                    input.style.borderBottomLeftRadius = input.style.borderBottomRightRadius = 6;
                input.style.color = theme.textPrimary;
                input.style.paddingTop = 6; input.style.paddingBottom = 6;
                input.style.paddingLeft = 8; input.style.paddingRight = 8;
                if (multiline) input.style.minHeight = multilineMinHeight;
            }

            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = new Label(placeholder);
                ph.style.position = Position.Absolute;
                ph.style.left = placeholderLeft; ph.style.top = placeholderTop;
                ph.style.color = theme.textMuted;
                ph.style.fontSize = theme.fontSizeBody - 1;
                ph.pickingMode = PickingMode.Ignore;
                field.Add(ph);
                field.RegisterValueChangedCallback(e =>
                    ph.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);
            }
        }

        // ─── Buttons ──────────────────────────────────────────────────────

        /// <summary>
        /// Accent-coloured CTA. Per-caller padding/marginLeft preserved as
        /// params (Chat composer is denser than the feedback board).
        /// </summary>
        public static void StylePrimaryButton(
            Button btn,
            float horizontalPadding = 20f,
            float marginLeft = 8f)
        {
            var theme = BugpunchTheme.Current;
            btn.style.backgroundColor = theme.accentPrimary;
            btn.style.color = theme.textPrimary;
            btn.style.fontSize = theme.fontSizeBody;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.paddingTop = 8; btn.style.paddingBottom = 8;
            btn.style.paddingLeft = horizontalPadding; btn.style.paddingRight = horizontalPadding;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = marginLeft;
        }

        /// <summary>
        /// Subdued button — same shape as primary, muted card-border bg.
        /// </summary>
        public static void StyleSecondaryButton(
            Button btn,
            float horizontalPadding = 20f,
            float marginLeft = 8f)
        {
            var theme = BugpunchTheme.Current;
            btn.style.backgroundColor = theme.cardBorder;
            btn.style.color = theme.textSecondary;
            btn.style.fontSize = theme.fontSizeBody;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.paddingTop = 8; btn.style.paddingBottom = 8;
            btn.style.paddingLeft = horizontalPadding; btn.style.paddingRight = horizontalPadding;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = marginLeft;
        }

        /// <summary>
        /// Square-ish icon-only button. Smaller padding than text buttons.
        /// </summary>
        public static void StyleIconButton(Button btn)
        {
            var theme = BugpunchTheme.Current;
            btn.style.backgroundColor = theme.cardBorder;
            btn.style.color = theme.textPrimary;
            btn.style.fontSize = 16;
            btn.style.paddingTop = 6; btn.style.paddingBottom = 6;
            btn.style.paddingLeft = 10; btn.style.paddingRight = 10;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = 6;
        }
    }
}
