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
        // ─── Submit view ──────────────────────────────────────────────────

        static void BuildSubmitView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;
            var theme = BugpunchTheme.Current;

            var title = new Label("New feedback");
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeTitle;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            container.Add(title);

            var subtitle = new Label("Write a short title and (optionally) describe what you'd like.");
            subtitle.style.color = theme.textSecondary;
            subtitle.style.fontSize = theme.fontSizeCaption + 1;
            subtitle.style.marginBottom = 12;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            container.Add(subtitle);

            var titleLabel = new Label("Title");
            titleLabel.style.color = theme.textSecondary;
            titleLabel.style.fontSize = theme.fontSizeCaption + 1;
            titleLabel.style.marginBottom = 4;
            container.Add(titleLabel);

            _submitTitleField = new TextField();
            _submitTitleField.maxLength = 200;
            StyleInput(_submitTitleField, "One-line summary");
            container.Add(_submitTitleField);

            var descLabel = new Label("Description (optional)");
            descLabel.style.color = theme.textSecondary;
            descLabel.style.fontSize = theme.fontSizeCaption + 1;
            descLabel.style.marginTop = 8;
            descLabel.style.marginBottom = 4;
            container.Add(descLabel);

            _submitDescField = new TextField();
            _submitDescField.multiline = true;
            _submitDescField.maxLength = 4000;
            StyleInput(_submitDescField, "More detail — how it would work, why it matters…", multiline: true);
            container.Add(_submitDescField);

            // Attachment preview strip sits above the action row so the
            // thumbnails are visually associated with the draft above.
            _submitAttachmentPreview = new VisualElement();
            _submitAttachmentPreview.style.flexDirection = FlexDirection.Row;
            _submitAttachmentPreview.style.flexWrap = Wrap.Wrap;
            _submitAttachmentPreview.style.marginTop = 6;
            container.Add(_submitAttachmentPreview);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.alignItems = Align.Center;
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.style.marginTop = 12;

            _submitAttachButton = new Button(() => OnAttachTapped(isComment: false)) { text = "📎" };
            StyleSecondaryButton(_submitAttachButton);
            _submitAttachButton.style.marginLeft = 0;
            _submitAttachButton.style.marginRight = 0;
            btnRow.Add(_submitAttachButton);

            btnRow.Add(new VisualElement { style = { flexGrow = 1 } });

            var cancelBtn = new Button(() => SwitchTo(View.List)) { text = "Cancel" };
            StyleSecondaryButton(cancelBtn);
            btnRow.Add(cancelBtn);

            var submitBtn = new Button() { text = "Submit" };
            StylePrimaryButton(submitBtn);
            submitBtn.SetEnabled(false);

            _submitTitleField.RegisterValueChangedCallback(e =>
                submitBtn.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));

            submitBtn.clicked += () =>
            {
                var t = _submitTitleField.value?.Trim() ?? "";
                var d = _submitDescField.value?.Trim() ?? "";
                if (string.IsNullOrEmpty(t)) return;
                _ = SubmitWithSimilarityCheck(t, d, submitBtn);
            };
            btnRow.Add(submitBtn);
            container.Add(btnRow);
        }

        // ─── Similarity prompt view ───────────────────────────────────────

        static void BuildSimilarityView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;
            var theme = BugpunchTheme.Current;

            var title = new Label("Similar feedback already exists");
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeTitle - 1;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            title.style.whiteSpace = WhiteSpace.Normal;
            container.Add(title);

            _similarityBody = new VisualElement();
            _similarityBody.style.marginBottom = 12;
            container.Add(_similarityBody);
        }

        static void ShowSimilarity(SimilarityMatch match)
        {
            _similarityBody.Clear();
            var theme = BugpunchTheme.Current;

            var intro = new Label("Sounds similar to:");
            intro.style.color = theme.textSecondary;
            intro.style.fontSize = theme.fontSizeCaption + 1;
            intro.style.marginBottom = 6;
            _similarityBody.Add(intro);

            var card = new VisualElement();
            card.style.backgroundColor = theme.cardBorder;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.paddingTop = 10; card.style.paddingBottom = 10;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.marginBottom = 10;

            var matchTitle = new Label(match.title ?? "(untitled)");
            matchTitle.style.color = theme.textPrimary;
            matchTitle.style.fontSize = theme.fontSizeBody + 1;
            matchTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            matchTitle.style.whiteSpace = WhiteSpace.Normal;
            matchTitle.style.marginBottom = 4;
            card.Add(matchTitle);

            var votes = new Label($"{match.voteCount} vote{(match.voteCount == 1 ? "" : "s")}");
            votes.style.color = theme.textMuted;
            votes.style.fontSize = theme.fontSizeCaption;
            card.Add(votes);

            if (!string.IsNullOrEmpty(match.body))
            {
                var body = match.body.Length > 240 ? match.body.Substring(0, 237) + "…" : match.body;
                var bodyLabel = new Label(body);
                bodyLabel.style.color = theme.textSecondary;
                bodyLabel.style.fontSize = theme.fontSizeCaption;
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                bodyLabel.style.marginTop = 6;
                card.Add(bodyLabel);
            }

            _similarityBody.Add(card);

            var question = new Label("Want to vote for that instead?");
            question.style.color = theme.textSecondary;
            question.style.fontSize = theme.fontSizeCaption + 1;
            question.style.whiteSpace = WhiteSpace.Normal;
            _similarityBody.Add(question);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 12;

            var postMineBtn = new Button(() => _ = CreateFeedback(_pendingSubmitTitle, _pendingSubmitDescription, bypassSimilarity: true, closeOnSuccess: true))
            {
                text = "Post mine anyway"
            };
            StyleSecondaryButton(postMineBtn);
            row.Add(postMineBtn);

            var voteBtn = new Button(() => _ = VoteFor(match.id))
            {
                text = "Vote for that"
            };
            StylePrimaryButton(voteBtn);
            row.Add(voteBtn);

            _similarityBody.Add(row);

            SwitchTo(View.Similarity);
        }

        static async System.Threading.Tasks.Task SubmitWithSimilarityCheck(string title, string description, Button submitBtn)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            submitBtn.SetEnabled(false);
            submitBtn.text = "Checking…";
            _pendingSubmitTitle = title;
            _pendingSubmitDescription = description;

            try
            {
                var simUrl = baseUrl + "/api/feedback/similarity";
                var simBody = BuildJson(new Dictionary<string, string>
                {
                    ["title"] = title,
                    ["description"] = description,
                });
                var (simOk, _, simResp) = await HttpRequest("POST", simUrl, apiKey, simBody);
                if (simOk)
                {
                    SimilarityMatch top = null;
                    try
                    {
                        var obj = JObject.Parse(string.IsNullOrEmpty(simResp) ? "{}" : simResp);
                        var matches = obj["matches"] as JArray;
                        if (matches != null)
                        {
                            foreach (var m in matches)
                            {
                                var match = SimilarityMatch.FromJson(m);
                                if (match.score > 0.85f && (top == null || match.score > top.score))
                                    top = match;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bugpunch.FeedbackBoard] Similarity parse failed: {ex.Message}");
                    }

                    if (top != null)
                    {
                        ShowSimilarity(top);
                        return;
                    }
                }
                // Similarity either failed or found nothing — just post it.
                await CreateFeedback(title, description, bypassSimilarity: false, closeOnSuccess: true);
            }
            finally
            {
                submitBtn.text = "Submit";
                submitBtn.SetEnabled(true);
            }
        }
    }
}
