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
    /// <summary>
    /// Feedback board — list / submit / similarity-prompt views in a single
    /// UIDocument. Players can scan existing feedback, vote, and post new
    /// ideas. When a new submission looks semantically close to an existing
    /// item (score &gt; 0.85, resolved server-side by the LLM), the submit
    /// flow first offers to vote for that instead of posting a duplicate.
    ///
    /// All HTTP goes through a small local helper that pulls base URL + API
    /// key from <see cref="BugpunchConfig"/> on
    /// <see cref="BugpunchClient.Instance"/> and sends the anon voter identity
    /// as <c>X-Device-Id</c>. The server resolves projectId from the API key.
    /// </summary>
    public static partial class BugpunchFeedbackBoard
    {
        static GameObject _hostGO;
        static UIDocument _doc;
        static StyleSheet _styleSheet;

        // View containers — one stays visible at a time.
        static VisualElement _root;
        static VisualElement _card;
        static VisualElement _listContainer;
        static VisualElement _submitContainer;
        static VisualElement _similarityContainer;
        static VisualElement _detailContainer;

        // List state
        static readonly List<FeedbackItem> _items = new();
        static string _searchFilter = "";
        static ScrollView _listScroll;
        static Label _listEmptyLabel;

        // Submit state — kept so the similarity view can re-use the values.
        static TextField _submitTitleField;
        static TextField _submitDescField;
        static string _pendingSubmitTitle;
        static string _pendingSubmitDescription;

        // Similarity state
        static VisualElement _similarityBody;

        // Detail state
        static FeedbackItem _detailItem;
        static VisualElement _detailBody;
        static ScrollView _detailScroll;
        static VisualElement _commentsList;
        static Label _commentsEmptyLabel;
        static TextField _commentDraftField;
        static Button _commentPostButton;
        static TextField _commentEmailField;

        // Attachments — draft state for the submit form + the comment composer.
        // Each UI session has at most one active draft per surface, so a
        // single-element list is plenty. Texture cache keeps image GETs once
        // per URL regardless of how many rows mention it.
        static readonly List<PendingAttachment> _submitDraftAttachments = new();
        static readonly List<PendingAttachment> _commentDraftAttachments = new();
        static VisualElement _submitAttachmentPreview;
        static Button _submitAttachButton;
        static VisualElement _commentAttachmentPreview;
        static Button _commentAttachButton;
        static readonly Dictionary<string, Texture2D> _imageCache = new();

        // PlayerPrefs — remember whether we've asked for an email this install.
        // Stored across app launches so the prompt is a one-time-only nudge.
        const string PREF_EMAIL_ASKED = "bp_feedback_email_asked";
        const string PREF_EMAIL_VALUE = "bp_feedback_email";

        /// <summary>
        /// Show the feedback board. Entry point from
        /// <see cref="BugpunchRequestHelpPicker"/>; safe to call directly too.
        /// </summary>
        public static void Show()
        {
            EnsureDocument();
            var ss = EnsureStyleSheet();

            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0;
            _root.style.right = 0; _root.style.bottom = 0;
            if (ss != null) _root.styleSheets.Add(ss);

            var backdrop = CreateBackdrop(Hide);
            _card = CreateCard();
            backdrop.Add(_card);
            _root.Add(backdrop);

            _listContainer = new VisualElement();
            _submitContainer = new VisualElement();
            _similarityContainer = new VisualElement();
            _detailContainer = new VisualElement();

            BuildListView(_listContainer);
            BuildSubmitView(_submitContainer);
            BuildSimilarityView(_similarityContainer);
            BuildDetailView(_detailContainer);

            _card.Add(_listContainer);
            _card.Add(_submitContainer);
            _card.Add(_similarityContainer);
            _card.Add(_detailContainer);

            SwitchTo(View.List);

            // Escape closes the board (or returns to list if on a sub-view).
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _root.Focus();

            _ = FetchList();
        }

        static void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape) return;
            if (_submitContainer.style.display == DisplayStyle.Flex ||
                _similarityContainer.style.display == DisplayStyle.Flex ||
                _detailContainer.style.display == DisplayStyle.Flex)
            {
                SwitchTo(View.List);
            }
            else
            {
                Hide();
            }
            e.StopPropagation();
        }

        enum View { List, Submit, Similarity, Detail }

        static void SwitchTo(View v)
        {
            _listContainer.style.display = v == View.List ? DisplayStyle.Flex : DisplayStyle.None;
            _submitContainer.style.display = v == View.Submit ? DisplayStyle.Flex : DisplayStyle.None;
            _similarityContainer.style.display = v == View.Similarity ? DisplayStyle.Flex : DisplayStyle.None;
            _detailContainer.style.display = v == View.Detail ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Hide()
        {
            if (_doc != null) _doc.rootVisualElement.Clear();
        }

        static void ShowInlineError(string message)
        {
            // Simple inline error — appended to the similarity body if visible,
            // otherwise to the submit view. Avoids a full notification system.
            var theme = BugpunchTheme.Current;
            var err = new Label(message);
            // accentRecord = the red / destructive accent — matches native
            // error toasts and the record button tint.
            err.style.color = theme.accentRecord;
            err.style.fontSize = theme.fontSizeCaption;
            err.style.marginTop = 8;
            err.style.whiteSpace = WhiteSpace.Normal;
            if (_submitContainer.style.display == DisplayStyle.Flex)
                _submitContainer.Add(err);
            else if (_similarityContainer.style.display == DisplayStyle.Flex)
                _similarityContainer.Add(err);
        }

        // ─── Infra ────────────────────────────────────────────────────────

        static void EnsureDocument()
        {
            if (_doc != null) return;

            _hostGO = new GameObject("Bugpunch_FeedbackBoard");
            _hostGO.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_hostGO);

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 29500; // above picker, below main dialog host
            _doc = _hostGO.AddComponent<UIDocument>();
            _doc.panelSettings = panelSettings;
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

    }

    /// <summary>
    /// Feedback-board markdown renderer — converts the supported subset
    /// (<c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, bullet / numbered
    /// lists, <c>[text](http(s)://…)</c>, paragraphs with single-newline
    /// breaks) into a UI Toolkit tree of <see cref="Label"/>s inside the
    /// given container. No headings, no tables, no HTML, no code blocks.
    ///
    /// Links must start with <c>http://</c> or <c>https://</c>; anything
    /// else is emitted as plain text so <c>javascript:</c> / <c>data:</c>
    /// / relative-path attempts can't execute via <see cref="Application.OpenURL"/>.
    /// </summary>
    internal static class BugpunchMarkdownRenderer
    {
        enum InlineKind { Text, Code, Bold, Italic, Link, Br }

        struct InlineToken
        {
            public InlineKind Kind;
            public string Value;
            public string Href; // only for Link
        }

        /// <summary>
        /// Render markdown into <paramref name="container"/>. Paragraphs and
        /// list items become child Labels; bullets start with "• " and
        /// numbered items with "{n}. ".
        /// </summary>
        public static void RenderTo(
            VisualElement container, string source,
            Color textColor, int fontSize)
        {
            if (container == null || string.IsNullOrEmpty(source)) return;

            container.style.flexDirection = FlexDirection.Column;

            var lines = (source ?? "").Replace("\r\n", "\n").Split('\n');
            var paragraph = new StringBuilder();

            void FlushParagraph()
            {
                var text = paragraph.ToString().Trim();
                paragraph.Length = 0;
                if (string.IsNullOrEmpty(text)) return;
                container.Add(BuildInlineLabel(text, textColor, fontSize));
            }

            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    i++;
                    continue;
                }

                var bulletMatch = Regex.Match(line, @"^\s*[-*]\s+(.*)$");
                var numberMatch = Regex.Match(line, @"^\s*\d+\.\s+(.*)$");
                if (bulletMatch.Success || numberMatch.Success)
                {
                    FlushParagraph();
                    var isBullet = bulletMatch.Success;
                    int ordinal = 1;
                    while (i < lines.Length)
                    {
                        var l = lines[i];
                        if (string.IsNullOrWhiteSpace(l)) break;
                        var bm = Regex.Match(l, @"^\s*[-*]\s+(.*)$");
                        var nm = Regex.Match(l, @"^\s*\d+\.\s+(.*)$");
                        if (isBullet && bm.Success)
                        {
                            container.Add(BuildListItem("• " + bm.Groups[1].Value, textColor, fontSize));
                            i++;
                            continue;
                        }
                        if (!isBullet && nm.Success)
                        {
                            container.Add(BuildListItem($"{ordinal}. " + nm.Groups[1].Value, textColor, fontSize));
                            ordinal++;
                            i++;
                            continue;
                        }
                        break;
                    }
                    continue;
                }

                if (paragraph.Length > 0) paragraph.Append('\n');
                paragraph.Append(line);
                i++;
            }
            FlushParagraph();
        }

        /// <summary>
        /// Strip markdown markers for a plain-text list preview. Leaves a
        /// readable summary without <c>**</c> / <c>*</c> / <c>`</c> /
        /// <c>[text](url)</c> visual noise.
        /// </summary>
        public static string StripForPreview(string source)
        {
            if (string.IsNullOrEmpty(source)) return "";
            var s = source;
            s = Regex.Replace(s, @"`([^`]+)`", "$1");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "$1");
            s = Regex.Replace(s, @"\*([^*]+)\*", "$1");
            s = Regex.Replace(s, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1");
            s = Regex.Replace(s, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);
            return s;
        }

        // ── paragraph / list-item label builders ────────────────────────

        static VisualElement BuildInlineLabel(string text, Color color, int fontSize)
        {
            // Paragraphs are assembled as a single Label so UI Toolkit's
            // text wrapping does the right thing; the Label takes a rich
            // marker-like string we build by collapsing the token stream.
            // UI Toolkit's Label doesn't render nested links clickably, so
            // we emit links in square brackets with their URL appended and
            // wire up a tap handler over the whole label to open the URL.
            // That's a conscious trade-off — richer per-token styling would
            // mean dozens of sibling Labels and breaks wrapping.
            var tokens = TokenizeInline(text);

            // Collect link hrefs to wire up the first-link tap as a fallback.
            string firstHref = null;
            var buf = new StringBuilder();
            foreach (var t in tokens)
            {
                switch (t.Kind)
                {
                    case InlineKind.Br:     buf.Append('\n'); break;
                    case InlineKind.Text:   buf.Append(t.Value); break;
                    case InlineKind.Code:   buf.Append(t.Value); break;
                    case InlineKind.Bold:   buf.Append(t.Value); break;
                    case InlineKind.Italic: buf.Append(t.Value); break;
                    case InlineKind.Link:
                        buf.Append(t.Value);
                        if (firstHref == null) firstHref = t.Href;
                        break;
                }
            }

            var label = new Label(buf.ToString());
            label.style.color = color;
            label.style.fontSize = fontSize;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4;

            // If the paragraph contains any link, tapping it opens the
            // first URL. That's the simplest meaningful action without
            // introducing per-token hit testing in UI Toolkit.
            if (firstHref != null)
            {
                label.style.color = new Color(0.56f, 0.82f, 1f, 1);
                label.RegisterCallback<PointerDownEvent>(e =>
                {
                    Application.OpenURL(firstHref);
                    e.StopPropagation();
                });
            }
            return label;
        }

        static VisualElement BuildListItem(string text, Color color, int fontSize)
        {
            var tokens = TokenizeInline(text);
            string firstHref = null;
            var buf = new StringBuilder();
            foreach (var t in tokens)
            {
                switch (t.Kind)
                {
                    case InlineKind.Br:     buf.Append(' '); break;
                    case InlineKind.Text:   buf.Append(t.Value); break;
                    case InlineKind.Code:   buf.Append(t.Value); break;
                    case InlineKind.Bold:   buf.Append(t.Value); break;
                    case InlineKind.Italic: buf.Append(t.Value); break;
                    case InlineKind.Link:
                        buf.Append(t.Value);
                        if (firstHref == null) firstHref = t.Href;
                        break;
                }
            }
            var label = new Label(buf.ToString());
            label.style.color = color;
            label.style.fontSize = fontSize;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 8;
            if (firstHref != null)
            {
                label.style.color = new Color(0.56f, 0.82f, 1f, 1);
                label.RegisterCallback<PointerDownEvent>(e =>
                {
                    Application.OpenURL(firstHref);
                    e.StopPropagation();
                });
            }
            return label;
        }

        // ── inline tokenizer ────────────────────────────────────────────

        static List<InlineToken> TokenizeInline(string source)
        {
            var tokens = new List<InlineToken>();
            var buf = new StringBuilder();
            void Flush()
            {
                if (buf.Length == 0) return;
                var s = buf.ToString();
                buf.Length = 0;
                var parts = s.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0)
                        tokens.Add(new InlineToken { Kind = InlineKind.Text, Value = parts[i] });
                    if (i < parts.Length - 1)
                        tokens.Add(new InlineToken { Kind = InlineKind.Br });
                }
            }

            int p = 0;
            while (p < source.Length)
            {
                char ch = source[p];

                // `code`
                if (ch == '`')
                {
                    int end = source.IndexOf('`', p + 1);
                    if (end > p)
                    {
                        Flush();
                        tokens.Add(new InlineToken { Kind = InlineKind.Code, Value = source.Substring(p + 1, end - p - 1) });
                        p = end + 1;
                        continue;
                    }
                }

                // [text](http(s)://url)
                if (ch == '[')
                {
                    int closeText = source.IndexOf(']', p + 1);
                    if (closeText > p && closeText + 1 < source.Length && source[closeText + 1] == '(')
                    {
                        int closeUrl = source.IndexOf(')', closeText + 2);
                        if (closeUrl > closeText + 1)
                        {
                            var t = source.Substring(p + 1, closeText - p - 1);
                            var href = source.Substring(closeText + 2, closeUrl - closeText - 2).Trim();
                            if (Regex.IsMatch(href, @"^https?://", RegexOptions.IgnoreCase))
                            {
                                Flush();
                                tokens.Add(new InlineToken { Kind = InlineKind.Link, Value = t, Href = href });
                                p = closeUrl + 1;
                                continue;
                            }
                        }
                    }
                }

                // **bold**
                if (ch == '*' && p + 1 < source.Length && source[p + 1] == '*')
                {
                    int end = source.IndexOf("**", p + 2);
                    if (end > p + 1)
                    {
                        Flush();
                        tokens.Add(new InlineToken { Kind = InlineKind.Bold, Value = source.Substring(p + 2, end - p - 2) });
                        p = end + 2;
                        continue;
                    }
                }

                // *italic*
                if (ch == '*')
                {
                    int end = source.IndexOf('*', p + 1);
                    if (end > p + 1 && (p + 1 >= source.Length || source[p + 1] != '*'))
                    {
                        Flush();
                        tokens.Add(new InlineToken { Kind = InlineKind.Italic, Value = source.Substring(p + 1, end - p - 1) });
                        p = end + 1;
                        continue;
                    }
                }

                buf.Append(ch);
                p++;
            }
            Flush();
            return tokens;
        }
    }
}
