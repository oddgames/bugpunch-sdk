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

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public static partial class BugpunchFeedbackBoard
    {
        // ─── Detail view ──────────────────────────────────────────────────

        static void BuildDetailView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            // Back button + title row
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;

            var theme = BugpunchTheme.Current;
            var backBtn = new Button(() => SwitchTo(View.List)) { text = "‹ Back" };
            backBtn.style.backgroundColor = Color.clear;
            backBtn.style.color = theme.accentChat;
            backBtn.style.fontSize = theme.fontSizeCaption + 1;
            backBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            backBtn.style.paddingTop = 4; backBtn.style.paddingBottom = 4;
            backBtn.style.paddingLeft = 0; backBtn.style.paddingRight = 8;
            backBtn.style.borderTopWidth = backBtn.style.borderBottomWidth =
                backBtn.style.borderLeftWidth = backBtn.style.borderRightWidth = 0;
            backBtn.style.marginLeft = 0;
            header.Add(backBtn);

            header.Add(new VisualElement { style = { flexGrow = 1 } });

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

            // Scrollable body — title/description/vote/comments stack vertically
            // and can be tall on long threads.
            _detailScroll = new ScrollView(ScrollViewMode.Vertical);
            _detailScroll.style.maxHeight = 520;
            _detailScroll.style.minHeight = 200;
            container.Add(_detailScroll);

            _detailBody = new VisualElement();
            _detailBody.style.flexDirection = FlexDirection.Column;
            _detailScroll.Add(_detailBody);
        }

        /// <summary>
        /// Switch to the detail view for the given item. Rebuilds the detail
        /// body every call so the client reflects the latest vote state and
        /// comment thread without stale caching.
        /// </summary>
        static void ShowDetail(FeedbackItem item)
        {
            _detailItem = item;
            _detailBody.Clear();
            var theme = BugpunchTheme.Current;

            // Title
            var titleLabel = new Label(item.title ?? "(untitled)");
            titleLabel.style.color = theme.textPrimary;
            titleLabel.style.fontSize = theme.fontSizeTitle - 2;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginBottom = 6;
            _detailBody.Add(titleLabel);

            // Meta row: vote pill + count
            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.alignItems = Align.Center;
            metaRow.style.marginBottom = 10;

            var voteBtn = new Button();
            voteBtn.style.flexDirection = FlexDirection.Row;
            voteBtn.style.alignItems = Align.Center;
            voteBtn.style.paddingTop = 6; voteBtn.style.paddingBottom = 6;
            voteBtn.style.paddingLeft = 12; voteBtn.style.paddingRight = 12;
            voteBtn.style.borderTopLeftRadius = voteBtn.style.borderTopRightRadius =
                voteBtn.style.borderBottomLeftRadius = voteBtn.style.borderBottomRightRadius = 6;
            voteBtn.style.borderTopWidth = voteBtn.style.borderBottomWidth =
                voteBtn.style.borderLeftWidth = voteBtn.style.borderRightWidth = 1;
            voteBtn.style.marginLeft = 0;
            ApplyVoteStyle(voteBtn, item.hasMyVote);

            var arrow = new Label("▲");
            arrow.style.fontSize = theme.fontSizeCaption;
            arrow.style.marginRight = 6;
            arrow.style.color = item.hasMyVote ? theme.textPrimary : theme.textSecondary;
            arrow.pickingMode = PickingMode.Ignore;
            voteBtn.Add(arrow);

            var count = new Label($"{item.voteCount} vote{(item.voteCount == 1 ? "" : "s")}");
            count.style.fontSize = theme.fontSizeCaption + 1;
            count.style.unityFontStyleAndWeight = FontStyle.Bold;
            count.style.color = item.hasMyVote ? theme.textPrimary : theme.textSecondary;
            count.pickingMode = PickingMode.Ignore;
            voteBtn.Add(count);

            voteBtn.clicked += () => _ = ToggleVoteWithEmailPromptDetail(item, voteBtn, arrow, count);
            metaRow.Add(voteBtn);
            _detailBody.Add(metaRow);

            // Description — markdown subset rendered as UI Toolkit elements.
            if (!string.IsNullOrEmpty(item.body))
            {
                var bodyContainer = new VisualElement();
                bodyContainer.style.marginBottom = 10;
                BugpunchMarkdownRenderer.RenderTo(
                    bodyContainer, item.body,
                    textColor: theme.textSecondary,
                    fontSize: theme.fontSizeCaption + 1);
                _detailBody.Add(bodyContainer);
            }

            // Body attachments — inline images under the description. Tap to
            // open in the system browser / photos app (full-size).
            if (item.attachments != null && item.attachments.Count > 0)
            {
                var attWrap = new VisualElement();
                attWrap.style.flexDirection = FlexDirection.Column;
                attWrap.style.marginBottom = 14;
                foreach (var a in item.attachments)
                    attWrap.Add(CreateImageAttachment(a));
                _detailBody.Add(attWrap);
            }

            // Comments header
            var commentsHeader = new Label("Comments");
            commentsHeader.style.color = theme.textPrimary;
            commentsHeader.style.fontSize = theme.fontSizeBody;
            commentsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            commentsHeader.style.marginTop = 4;
            commentsHeader.style.marginBottom = 6;
            _detailBody.Add(commentsHeader);

            _commentsList = new VisualElement();
            _commentsList.style.flexDirection = FlexDirection.Column;
            _detailBody.Add(_commentsList);

            _commentsEmptyLabel = new Label("No comments yet.");
            _commentsEmptyLabel.style.color = theme.textMuted;
            _commentsEmptyLabel.style.fontSize = theme.fontSizeCaption;
            _commentsEmptyLabel.style.marginTop = 4; _commentsEmptyLabel.style.marginBottom = 8;
            _commentsList.Add(_commentsEmptyLabel);

            // Composer: optional email + body + post button.
            var composer = new VisualElement();
            composer.style.marginTop = 10;
            composer.style.paddingTop = 10;
            composer.style.borderTopColor = theme.cardBorder;
            composer.style.borderTopWidth = 1;

            var emailNote = new Label("Add your email to get notified of replies (optional)");
            emailNote.style.color = theme.textMuted;
            emailNote.style.fontSize = theme.fontSizeCaption - 1;
            emailNote.style.marginBottom = 4;
            composer.Add(emailNote);

            _commentEmailField = new TextField();
            _commentEmailField.maxLength = 320;
            _commentEmailField.value = PlayerPrefs.GetString(PREF_EMAIL_VALUE, "");
            StyleInput(_commentEmailField, "you@example.com");
            _commentEmailField.style.marginBottom = 6;
            composer.Add(_commentEmailField);

            _commentDraftField = new TextField();
            _commentDraftField.multiline = true;
            _commentDraftField.maxLength = 4000;
            StyleInput(_commentDraftField, "Reply to this thread…", multiline: true);
            composer.Add(_commentDraftField);

            // Fresh-per-detail-view composer — drop any attachments a prior
            // detail session had queued so we don't accidentally post stale
            // screenshots against a different item.
            _commentDraftAttachments.Clear();
            _commentAttachmentPreview = new VisualElement();
            _commentAttachmentPreview.style.flexDirection = FlexDirection.Row;
            _commentAttachmentPreview.style.flexWrap = Wrap.Wrap;
            _commentAttachmentPreview.style.marginTop = 6;
            composer.Add(_commentAttachmentPreview);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.alignItems = Align.Center;
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.style.marginTop = 8;

            _commentAttachButton = new Button(() => OnAttachTapped(isComment: true)) { text = "📎" };
            StyleSecondaryButton(_commentAttachButton);
            _commentAttachButton.style.marginLeft = 0;
            _commentAttachButton.style.marginRight = 0;
            btnRow.Add(_commentAttachButton);

            btnRow.Add(new VisualElement { style = { flexGrow = 1 } });

            _commentPostButton = new Button() { text = "Post" };
            StylePrimaryButton(_commentPostButton);
            _commentPostButton.SetEnabled(false);
            _commentDraftField.RegisterValueChangedCallback(e =>
                _commentPostButton.SetEnabled(
                    !string.IsNullOrWhiteSpace(e.newValue) || _commentDraftAttachments.Count > 0));
            _commentPostButton.clicked += () => _ = PostComment();
            btnRow.Add(_commentPostButton);
            composer.Add(btnRow);

            _detailBody.Add(composer);

            SwitchTo(View.Detail);

            _ = FetchComments(item.id);
        }

        static VisualElement CreateCommentRow(CommentItem c)
        {
            var theme = BugpunchTheme.Current;
            var card = new VisualElement();
            card.style.backgroundColor = theme.cardBorder;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.marginBottom = 6;
            // Subtle border accent for staff comments so players can tell at a
            // glance which messages came from the dev team.
            if (c.authorIsStaff)
            {
                card.style.borderLeftColor = theme.accentPrimary;
                card.style.borderLeftWidth = 3;
                card.style.paddingLeft = 10;
            }

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var name = new Label(string.IsNullOrEmpty(c.authorName) ? "Anonymous" : c.authorName);
            name.style.color = theme.textPrimary;
            name.style.fontSize = theme.fontSizeCaption;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(name);

            if (c.authorIsStaff)
            {
                var badge = new Label("staff");
                badge.style.color = theme.textPrimary;
                badge.style.fontSize = theme.fontSizeCaption - 2;
                badge.style.marginLeft = 6;
                badge.style.paddingLeft = 4; badge.style.paddingRight = 4;
                badge.style.paddingTop = 1; badge.style.paddingBottom = 1;
                badge.style.backgroundColor = theme.accentPrimary;
                badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
                    badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
                header.Add(badge);
            }

            card.Add(header);

            if (c.deleted)
            {
                var del = new Label("[deleted]");
                del.style.color = theme.textMuted;
                del.style.fontSize = theme.fontSizeCaption;
                del.style.unityFontStyleAndWeight = FontStyle.Italic;
                card.Add(del);
            }
            else
            {
                if (!string.IsNullOrEmpty(c.body))
                {
                    var bodyContainer = new VisualElement();
                    BugpunchMarkdownRenderer.RenderTo(
                        bodyContainer, c.body,
                        textColor: theme.textSecondary,
                        fontSize: theme.fontSizeCaption);
                    card.Add(bodyContainer);
                }

                if (c.attachments != null && c.attachments.Count > 0)
                {
                    foreach (var a in c.attachments)
                        card.Add(CreateImageAttachment(a));
                }
            }

            return card;
        }

        /// <summary>
        /// Detail-view variant — identical behaviour, but also updates the
        /// pill label (which includes the word "vote"/"votes").
        /// </summary>
        static async System.Threading.Tasks.Task ToggleVoteWithEmailPromptDetail(
            FeedbackItem item, VisualElement voteBtn, Label arrow, Label count)
        {
            await EnsureEmailPromptedIfUpvoting(item.hasMyVote);

            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(item.id) + "/vote";
            var (ok, _, body) = await HttpRequest("POST", url, apiKey, BuildVotePayload());
            if (!ok) return;

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var voteCount = (int?)obj["voteCount"] ?? item.voteCount;
                var hasMyVote = (bool?)obj["hasMyVote"] ?? item.hasMyVote;
                item.voteCount = voteCount;
                item.hasMyVote = hasMyVote;
                count.text = $"{voteCount} vote{(voteCount == 1 ? "" : "s")}";
                ApplyVoteStyle(voteBtn, hasMyVote);
                var theme = BugpunchTheme.Current;
                arrow.style.color = hasMyVote ? theme.textPrimary : theme.textSecondary;
                count.style.color = hasMyVote ? theme.textPrimary : theme.textSecondary;
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("FeedbackBoard", $"Detail vote parse failed: {ex.Message}");
            }
        }

        /// <summary>
        /// One-time-only session email prompt. Only fires when the player is
        /// upvoting (hasMyVote==false → about to become true) and hasn't been
        /// asked before. On "Yes" we cache the typed email for future vote
        /// POSTs; on "No" we just mark PREF_EMAIL_ASKED so we never re-ask.
        /// </summary>
        static async System.Threading.Tasks.Task EnsureEmailPromptedIfUpvoting(bool hasMyVoteBefore)
        {
            if (hasMyVoteBefore) return; // un-voting — no prompt
            if (PlayerPrefs.GetInt(PREF_EMAIL_ASKED, 0) == 1) return;

            // Mark before showing — guarantees we only ever ask once, even if
            // the user dismisses the prompt without interacting with it.
            PlayerPrefs.SetInt(PREF_EMAIL_ASKED, 1);
            PlayerPrefs.Save();

            await ShowEmailCapturePrompt();
        }

        /// <summary>
        /// Inline modal over the feedback card that collects an optional
        /// email. Uses the existing UIToolkit surface so we don't introduce
        /// a new native dialog path.
        /// </summary>
        static System.Threading.Tasks.Task ShowEmailCapturePrompt()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var theme = BugpunchTheme.Current;

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.top = 0;
            overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = theme.backdrop;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            var card = new VisualElement();
            card.style.backgroundColor = theme.cardBackground;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = theme.cardBorder;
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = theme.cardRadius;
            card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.paddingLeft = 20; card.style.paddingRight = 20;
            card.style.maxWidth = 360; card.style.width = Length.Percent(92);

            var title = new Label("Want updates when the team replies?");
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeBody + 1;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginBottom = 6;
            card.Add(title);

            var hint = new Label("Optional — we'll only email you when someone posts a reply.");
            hint.style.color = theme.textSecondary;
            hint.style.fontSize = theme.fontSizeCaption;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 10;
            card.Add(hint);

            var input = new TextField();
            input.maxLength = 320;
            StyleInput(input, "you@example.com");
            card.Add(input);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.style.marginTop = 12;

            void Close()
            {
                if (overlay.parent != null) overlay.parent.Remove(overlay);
                tcs.TrySetResult(true);
            }

            var skip = new Button(() => Close()) { text = "Skip" };
            StyleSecondaryButton(skip);
            btnRow.Add(skip);

            var save = new Button(() =>
            {
                var email = (input.value ?? "").Trim();
                if (!string.IsNullOrEmpty(email))
                {
                    PlayerPrefs.SetString(PREF_EMAIL_VALUE, email);
                    PlayerPrefs.Save();
                }
                Close();
            })
            { text = "Save" };
            StylePrimaryButton(save);
            btnRow.Add(save);

            card.Add(btnRow);
            overlay.Add(card);
            _root.Add(overlay);

            return tcs.Task;
        }
    }
}
