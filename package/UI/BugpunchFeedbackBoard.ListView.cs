using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.UI
{
    public static partial class BugpunchFeedbackBoard
    {
        // ─── List view ────────────────────────────────────────────────────

        static void BuildListView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            // Header row: title + close button.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 8;

            var theme = BugpunchTheme.Current;
            var title = new Label("Feedback");
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeTitle;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var close = new Button(Hide) { text = "×" };
            close.style.backgroundColor = Color.clear;
            close.style.color = theme.textSecondary;
            close.style.fontSize = 22;
            close.style.unityFontStyleAndWeight = FontStyle.Bold;
            close.style.paddingTop = 0; close.style.paddingBottom = 0;
            close.style.paddingLeft = 8; close.style.paddingRight = 8;
            close.style.borderTopWidth = close.style.borderBottomWidth =
                close.style.borderLeftWidth = close.style.borderRightWidth = 0;
            close.style.marginLeft = 0;
            header.Add(close);
            container.Add(header);

            var subtitle = new Label("Suggest what to build next, or vote on what others have asked for.");
            subtitle.style.color = theme.textSecondary;
            subtitle.style.fontSize = theme.fontSizeCaption + 1;
            subtitle.style.marginBottom = 10;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            container.Add(subtitle);

            // Search
            var search = new TextField();
            StyleInput(search, "Search feedback…");
            search.RegisterValueChangedCallback(e =>
            {
                _searchFilter = e.newValue ?? "";
                RefreshList();
            });
            search.style.marginBottom = 8;
            container.Add(search);

            // Scroll list
            _listScroll = new ScrollView(ScrollViewMode.Vertical);
            _listScroll.style.maxHeight = 420;
            _listScroll.style.minHeight = 160;
            _listScroll.style.marginBottom = 8;
            container.Add(_listScroll);

            _listEmptyLabel = new Label("Loading…");
            _listEmptyLabel.style.color = theme.textMuted;
            _listEmptyLabel.style.fontSize = theme.fontSizeCaption + 1;
            _listEmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _listEmptyLabel.style.marginTop = 40;
            _listEmptyLabel.style.marginBottom = 40;
            _listScroll.Add(_listEmptyLabel);

            // New-feedback button
            var newBtn = new Button(() =>
            {
                _submitTitleField.value = "";
                _submitDescField.value = "";
                SwitchTo(View.Submit);
                _submitTitleField.Focus();
            });
            newBtn.text = "+ New feedback";
            StylePrimaryButton(newBtn);
            newBtn.style.marginLeft = 0;
            container.Add(newBtn);
        }

        static void RefreshList()
        {
            _listScroll.Clear();

            var filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            var visible = new List<FeedbackItem>();
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(filter) && (item.title == null ||
                    item.title.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0))
                {
                    continue;
                }
                visible.Add(item);
            }

            if (visible.Count == 0)
            {
                _listEmptyLabel.text = _items.Count == 0
                    ? "No feedback yet. Be the first to suggest something!"
                    : "No feedback matches your search.";
                _listScroll.Add(_listEmptyLabel);
                return;
            }

            foreach (var item in visible)
                _listScroll.Add(CreateListRow(item));
        }

        static VisualElement CreateListRow(FeedbackItem item)
        {
            var theme = BugpunchTheme.Current;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.backgroundColor = theme.cardBorder;
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 6;
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            row.style.paddingLeft = 12; row.style.paddingRight = 8;
            row.style.marginBottom = 6;

            // Tapping the row body (not the vote pill) opens the detail view
            // — matches the dashboard's master-detail pattern. The vote pill
            // stops propagation so toggling a vote doesn't navigate away.
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                // Only respond to direct hits on the row container; children
                // (text labels) bubble through and work fine, but the vote
                // button stops its own propagation.
                ShowDetail(item);
                e.StopPropagation();
            });

            // Text column (title + body)
            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;

            var titleLabel = new Label(item.title ?? "(untitled)");
            titleLabel.style.color = theme.textPrimary;
            titleLabel.style.fontSize = theme.fontSizeBody;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginBottom = 2;
            text.Add(titleLabel);

            if (!string.IsNullOrEmpty(item.body))
            {
                // List rows show a plain preview — markdown markers are
                // stripped so **bold** / *italic* / links don't visually
                // clutter the summary. Full markdown renders in the detail.
                var bodyText = BugpunchMarkdownRenderer.StripForPreview(item.body);
                if (bodyText.Length > 160) bodyText = bodyText.Substring(0, 157) + "…";
                var bodyLabel = new Label(bodyText);
                bodyLabel.style.color = theme.textSecondary;
                bodyLabel.style.fontSize = theme.fontSizeCaption;
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                text.Add(bodyLabel);
            }

            if (item.attachments != null && item.attachments.Count > 0)
            {
                var badge = new Label($"📎 {item.attachments.Count} image{(item.attachments.Count == 1 ? "" : "s")}");
                badge.style.color = theme.textSecondary;
                badge.style.fontSize = theme.fontSizeCaption;
                badge.style.marginTop = 2;
                text.Add(badge);
            }

            row.Add(text);

            // Vote pill
            var voteBtn = new Button();
            voteBtn.style.flexDirection = FlexDirection.Column;
            voteBtn.style.alignItems = Align.Center;
            voteBtn.style.justifyContent = Justify.Center;
            voteBtn.style.marginLeft = 12;
            voteBtn.style.minWidth = 52;
            voteBtn.style.paddingTop = 6; voteBtn.style.paddingBottom = 6;
            voteBtn.style.paddingLeft = 10; voteBtn.style.paddingRight = 10;
            voteBtn.style.borderTopLeftRadius = voteBtn.style.borderTopRightRadius =
                voteBtn.style.borderBottomLeftRadius = voteBtn.style.borderBottomRightRadius = 6;
            voteBtn.style.borderTopWidth = voteBtn.style.borderBottomWidth =
                voteBtn.style.borderLeftWidth = voteBtn.style.borderRightWidth = 1;
            ApplyVoteStyle(voteBtn, item.hasMyVote);

            var arrow = new Label("▲");
            arrow.style.fontSize = theme.fontSizeCaption;
            arrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            arrow.style.color = item.hasMyVote ? theme.textPrimary : theme.textSecondary;
            arrow.pickingMode = PickingMode.Ignore;
            voteBtn.Add(arrow);

            var count = new Label(item.voteCount.ToString());
            count.style.fontSize = theme.fontSizeCaption;
            count.style.unityFontStyleAndWeight = FontStyle.Bold;
            count.style.color = item.hasMyVote ? theme.textPrimary : theme.textSecondary;
            count.pickingMode = PickingMode.Ignore;
            voteBtn.Add(count);

            voteBtn.clicked += () => _ = ToggleVoteWithEmailPrompt(item, voteBtn, arrow, count);
            // Prevent the row's PointerDownEvent handler from opening the
            // detail view when the player just wants to upvote.
            voteBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            row.Add(voteBtn);
            return row;
        }

        /// <summary>
        /// Wrap <see cref="ToggleVote"/> with a one-time email prompt so the
        /// first upvote of the session asks the player if they want to be
        /// notified of replies. The prompt is remembered via PlayerPrefs so it
        /// never fires a second time, regardless of answer.
        /// </summary>
        static async System.Threading.Tasks.Task ToggleVoteWithEmailPrompt(
            FeedbackItem item, VisualElement voteBtn, Label arrow, Label count)
        {
            await EnsureEmailPromptedIfUpvoting(item.hasMyVote);
            await ToggleVote(item, voteBtn, arrow, count);
        }
    }
}
