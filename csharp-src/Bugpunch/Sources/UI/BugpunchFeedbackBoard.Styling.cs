using UnityEngine;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.UI
{
    public static partial class BugpunchFeedbackBoard
    {
        static void ApplyVoteStyle(VisualElement el, bool active)
        {
            var theme = BugpunchTheme.Current;
            if (active)
            {
                // Voted = accent pill.
                el.style.backgroundColor = theme.accentPrimary;
                var accent = theme.accentPrimary;
                // Slightly lighter border on an active pill — lifts it off the bg.
                el.style.borderTopColor = el.style.borderBottomColor =
                    el.style.borderLeftColor = el.style.borderRightColor = new Color(
                        Mathf.Clamp01(accent.r + 0.10f),
                        Mathf.Clamp01(accent.g + 0.10f),
                        Mathf.Clamp01(accent.b + 0.10f),
                        accent.a);
            }
            else
            {
                el.style.backgroundColor = theme.cardBorder;
                el.style.borderTopColor = el.style.borderBottomColor =
                    el.style.borderLeftColor = el.style.borderRightColor = theme.cardBorder;
            }
        }

        // ─── Styling helpers (delegate to BugpunchUIToolkit) ──────────────
        //
        // These thin wrappers keep callers in the partial class
        // unchanged while the actual implementations live in the shared
        // BugpunchUIToolkit static class.

        static void StyleInput(TextField field, string placeholder, bool multiline = false)
            => BugpunchUIToolkit.StyleInput(field, placeholder, multiline, multilineMinHeight: 80f);

        static void StylePrimaryButton(Button btn)
            => BugpunchUIToolkit.StylePrimaryButton(btn, horizontalPadding: 20f, marginLeft: 8f);

        static void StyleSecondaryButton(Button btn)
            => BugpunchUIToolkit.StyleSecondaryButton(btn, horizontalPadding: 20f, marginLeft: 8f);

        static VisualElement CreateBackdrop(System.Action onClickOutside)
            => BugpunchUIToolkit.CreateBackdrop(onClickOutside);

        static VisualElement CreateCard()
            => BugpunchUIToolkit.CreateCard(maxWidth: 520f, minWidth: 360f, widthPercent: 90f);
    }
}
