using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.DeviceConnect.UI
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
    public static class BugpunchFeedbackBoard
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

            BuildListView(_listContainer);
            BuildSubmitView(_submitContainer);
            BuildSimilarityView(_similarityContainer);

            _card.Add(_listContainer);
            _card.Add(_submitContainer);
            _card.Add(_similarityContainer);

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
                _similarityContainer.style.display == DisplayStyle.Flex)
            {
                SwitchTo(View.List);
            }
            else
            {
                Hide();
            }
            e.StopPropagation();
        }

        enum View { List, Submit, Similarity }

        static void SwitchTo(View v)
        {
            _listContainer.style.display = v == View.List ? DisplayStyle.Flex : DisplayStyle.None;
            _submitContainer.style.display = v == View.Submit ? DisplayStyle.Flex : DisplayStyle.None;
            _similarityContainer.style.display = v == View.Similarity ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Hide()
        {
            if (_doc != null) _doc.rootVisualElement.Clear();
        }

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

            var title = new Label("Feedback");
            title.style.color = Color.white;
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var close = new Button(Hide) { text = "×" };
            close.style.backgroundColor = Color.clear;
            close.style.color = new Color(0.7f, 0.7f, 0.7f, 1);
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
            subtitle.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
            subtitle.style.fontSize = 13;
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
            _listEmptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1);
            _listEmptyLabel.style.fontSize = 13;
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
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1);
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 6;
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            row.style.paddingLeft = 12; row.style.paddingRight = 8;
            row.style.marginBottom = 6;

            // Text column (title + body)
            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;

            var titleLabel = new Label(item.title ?? "(untitled)");
            titleLabel.style.color = Color.white;
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginBottom = 2;
            text.Add(titleLabel);

            if (!string.IsNullOrEmpty(item.body))
            {
                var bodyText = item.body;
                if (bodyText.Length > 160) bodyText = bodyText.Substring(0, 157) + "…";
                var bodyLabel = new Label(bodyText);
                bodyLabel.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
                bodyLabel.style.fontSize = 12;
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                text.Add(bodyLabel);
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
            arrow.style.fontSize = 12;
            arrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            arrow.style.color = item.hasMyVote ? Color.white : new Color(0.78f, 0.78f, 0.78f, 1);
            arrow.pickingMode = PickingMode.Ignore;
            voteBtn.Add(arrow);

            var count = new Label(item.voteCount.ToString());
            count.style.fontSize = 12;
            count.style.unityFontStyleAndWeight = FontStyle.Bold;
            count.style.color = item.hasMyVote ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1);
            count.pickingMode = PickingMode.Ignore;
            voteBtn.Add(count);

            voteBtn.clicked += () => _ = ToggleVote(item, voteBtn, arrow, count);

            row.Add(voteBtn);
            return row;
        }

        static void ApplyVoteStyle(VisualElement el, bool active)
        {
            if (active)
            {
                el.style.backgroundColor = new Color(0.24f, 0.5f, 0.30f, 1);
                el.style.borderTopColor = el.style.borderBottomColor =
                    el.style.borderLeftColor = el.style.borderRightColor = new Color(0.35f, 0.70f, 0.42f, 1);
            }
            else
            {
                el.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1);
                el.style.borderTopColor = el.style.borderBottomColor =
                    el.style.borderLeftColor = el.style.borderRightColor = new Color(0.36f, 0.36f, 0.36f, 1);
            }
        }

        // ─── Submit view ──────────────────────────────────────────────────

        static void BuildSubmitView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            var title = new Label("New feedback");
            title.style.color = Color.white;
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            container.Add(title);

            var subtitle = new Label("Write a short title and (optionally) describe what you'd like.");
            subtitle.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
            subtitle.style.fontSize = 13;
            subtitle.style.marginBottom = 12;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            container.Add(subtitle);

            var titleLabel = new Label("Title");
            titleLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            titleLabel.style.fontSize = 13;
            titleLabel.style.marginBottom = 4;
            container.Add(titleLabel);

            _submitTitleField = new TextField();
            _submitTitleField.maxLength = 200;
            StyleInput(_submitTitleField, "One-line summary");
            container.Add(_submitTitleField);

            var descLabel = new Label("Description (optional)");
            descLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            descLabel.style.fontSize = 13;
            descLabel.style.marginTop = 8;
            descLabel.style.marginBottom = 4;
            container.Add(descLabel);

            _submitDescField = new TextField();
            _submitDescField.multiline = true;
            _submitDescField.maxLength = 4000;
            StyleInput(_submitDescField, "More detail — how it would work, why it matters…", multiline: true);
            container.Add(_submitDescField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.style.marginTop = 12;

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

            var title = new Label("Similar feedback already exists");
            title.style.color = Color.white;
            title.style.fontSize = 19;
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

            var intro = new Label("Sounds similar to:");
            intro.style.color = new Color(0.78f, 0.78f, 0.78f, 1);
            intro.style.fontSize = 13;
            intro.style.marginBottom = 6;
            _similarityBody.Add(intro);

            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f, 1);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.paddingTop = 10; card.style.paddingBottom = 10;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.marginBottom = 10;

            var matchTitle = new Label(match.title ?? "(untitled)");
            matchTitle.style.color = Color.white;
            matchTitle.style.fontSize = 15;
            matchTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            matchTitle.style.whiteSpace = WhiteSpace.Normal;
            matchTitle.style.marginBottom = 4;
            card.Add(matchTitle);

            var votes = new Label($"{match.voteCount} vote{(match.voteCount == 1 ? "" : "s")}");
            votes.style.color = new Color(0.65f, 0.65f, 0.65f, 1);
            votes.style.fontSize = 12;
            card.Add(votes);

            if (!string.IsNullOrEmpty(match.body))
            {
                var body = match.body.Length > 240 ? match.body.Substring(0, 237) + "…" : match.body;
                var bodyLabel = new Label(body);
                bodyLabel.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
                bodyLabel.style.fontSize = 12;
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                bodyLabel.style.marginTop = 6;
                card.Add(bodyLabel);
            }

            _similarityBody.Add(card);

            var question = new Label("Want to vote for that instead?");
            question.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            question.style.fontSize = 13;
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

        // ─── HTTP ─────────────────────────────────────────────────────────

        static async System.Threading.Tasks.Task FetchList()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey))
            {
                _listEmptyLabel.text = "Bugpunch isn't configured — feedback unavailable.";
                return;
            }

            var url = baseUrl + "/api/feedback?sort=votes";
            var (ok, status, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok)
            {
                _listEmptyLabel.text = $"Could not load feedback (HTTP {status}).";
                return;
            }

            _items.Clear();
            try
            {
                var arr = JArray.Parse(string.IsNullOrEmpty(body) ? "[]" : body);
                foreach (var t in arr)
                    _items.Add(FeedbackItem.FromJson(t));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.FeedbackBoard] Parse failed: {ex.Message}");
                _listEmptyLabel.text = "Could not parse feedback list.";
                return;
            }

            RefreshList();
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

        static async System.Threading.Tasks.Task CreateFeedback(string title, string description, bool bypassSimilarity, bool closeOnSuccess)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var url = baseUrl + "/api/feedback";
            var payload = new Dictionary<string, string>
            {
                ["title"] = title ?? "",
                ["description"] = description ?? "",
            };
            if (bypassSimilarity) payload["bypassSimilarity"] = "true";
            var body = BuildJson(payload);

            var (ok, status, _) = await HttpRequest("POST", url, apiKey, body);
            if (!ok)
            {
                Debug.LogWarning($"[Bugpunch.FeedbackBoard] Create failed (HTTP {status}).");
                // Surface failure inline — lightweight toast on the card.
                ShowInlineError("Could not submit feedback. Please try again.");
                return;
            }

            // Refresh and return to list.
            await FetchList();
            if (closeOnSuccess) SwitchTo(View.List);
        }

        static async System.Threading.Tasks.Task ToggleVote(FeedbackItem item, VisualElement voteBtn, Label arrow, Label count)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(item.id) + "/vote";
            var (ok, _, body) = await HttpRequest("POST", url, apiKey, "{}");
            if (!ok) return;

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var voteCount = (int?)obj["voteCount"] ?? item.voteCount;
                var hasMyVote = (bool?)obj["hasMyVote"] ?? item.hasMyVote;
                item.voteCount = voteCount;
                item.hasMyVote = hasMyVote;
                count.text = voteCount.ToString();
                ApplyVoteStyle(voteBtn, hasMyVote);
                arrow.style.color = hasMyVote ? Color.white : new Color(0.78f, 0.78f, 0.78f, 1);
                count.style.color = hasMyVote ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.FeedbackBoard] Vote parse failed: {ex.Message}");
            }
        }

        static async System.Threading.Tasks.Task VoteFor(string itemId)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(itemId) + "/vote";
            await HttpRequest("POST", url, apiKey, "{}");
            await FetchList();
            SwitchTo(View.List);
        }

        static void ShowInlineError(string message)
        {
            // Simple inline error — appended to the similarity body if visible,
            // otherwise to the submit view. Avoids a full notification system.
            var err = new Label(message);
            err.style.color = new Color(1f, 0.5f, 0.5f, 1);
            err.style.fontSize = 12;
            err.style.marginTop = 8;
            err.style.whiteSpace = WhiteSpace.Normal;
            if (_submitContainer.style.display == DisplayStyle.Flex)
                _submitContainer.Add(err);
            else if (_similarityContainer.style.display == DisplayStyle.Flex)
                _similarityContainer.Add(err);
        }

        // Tiny HTTP wrapper — lives inside the feedback board since the SDK
        // doesn't currently have a shared authed-JSON helper. Uses
        // UnityWebRequest.SendWebRequest() with async/await.
        static async System.Threading.Tasks.Task<(bool ok, long status, string body)> HttpRequest(string method, string url, string apiKey, string jsonBody)
        {
            UnityWebRequest req;
            if (method == "GET")
            {
                req = UnityWebRequest.Get(url);
            }
            else
            {
                req = new UnityWebRequest(url, method);
                req.downloadHandler = new DownloadHandlerBuffer();
                if (jsonBody != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(jsonBody);
                    req.uploadHandler = new UploadHandlerRaw(bytes) { contentType = "application/json" };
                    req.SetRequestHeader("Content-Type", "application/json");
                }
            }
            req.SetRequestHeader("Authorization", "Bearer " + (apiKey ?? ""));
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone)
                await System.Threading.Tasks.Task.Yield();

            var ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
            var body = req.downloadHandler != null ? req.downloadHandler.text : "";
            var code = req.responseCode;
            req.Dispose();
            return (ok, code, body);
        }

        static bool TryGetBaseUrl(out string baseUrl, out string apiKey)
        {
            baseUrl = null; apiKey = null;
            var client = BugpunchClient.Instance;
            if (client == null || client.Config == null)
            {
                Debug.LogWarning("[Bugpunch.FeedbackBoard] BugpunchClient not initialized — feedback unavailable.");
                return false;
            }
            baseUrl = client.Config.HttpBaseUrl;
            apiKey = client.Config.apiKey;
            return !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey);
        }

        static string BuildJson(Dictionary<string, string> fields)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in fields)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsonConvert.ToString(kv.Key).Trim('"')).Append("\":");
                if (kv.Key == "bypassSimilarity")
                {
                    // Simple bool — stored as "true" string in the dict.
                    sb.Append(kv.Value == "true" ? "true" : "false");
                }
                else
                {
                    sb.Append(JsonConvert.ToString(kv.Value ?? ""));
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ─── Styling helpers ──────────────────────────────────────────────

        static void StyleInput(TextField field, string placeholder, bool multiline = false)
        {
            field.multiline = multiline;
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
                if (multiline) input.style.minHeight = 80;
            }

            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = new Label(placeholder);
                ph.style.position = Position.Absolute;
                ph.style.left = 10; ph.style.top = 7;
                ph.style.color = new Color(0.45f, 0.45f, 0.45f, 1);
                ph.style.fontSize = 13;
                ph.pickingMode = PickingMode.Ignore;
                field.Add(ph);
                field.RegisterValueChangedCallback(e =>
                    ph.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);
            }
        }

        static void StylePrimaryButton(Button btn)
        {
            btn.style.backgroundColor = new Color(0.18f, 0.49f, 0.20f, 1);
            btn.style.color = Color.white;
            btn.style.fontSize = 14;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.paddingTop = 8; btn.style.paddingBottom = 8;
            btn.style.paddingLeft = 20; btn.style.paddingRight = 20;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = 8;
        }

        static void StyleSecondaryButton(Button btn)
        {
            btn.style.backgroundColor = new Color(0.26f, 0.26f, 0.26f, 1);
            btn.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            btn.style.fontSize = 14;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.paddingTop = 8; btn.style.paddingBottom = 8;
            btn.style.paddingLeft = 20; btn.style.paddingRight = 20;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = 8;
        }

        // ─── Infra ────────────────────────────────────────────────────────

        static VisualElement CreateBackdrop(Action onClickOutside)
        {
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0;
            backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new Color(0, 0, 0, 0.7f);
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
            card.style.paddingTop = 22; card.style.paddingBottom = 22;
            card.style.paddingLeft = 24; card.style.paddingRight = 24;
            card.style.maxWidth = 520;
            card.style.minWidth = 360;
            card.style.width = Length.Percent(90);
            card.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return card;
        }

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

        // ─── Data types ───────────────────────────────────────────────────

        class FeedbackItem
        {
            public string id;
            public string projectId;
            public string title;
            public string body;
            public int voteCount;
            public string status;
            public string createdAt;
            public string createdBy;
            public bool hasMyVote;

            public static FeedbackItem FromJson(JToken t)
            {
                return new FeedbackItem
                {
                    id = (string)t["id"] ?? "",
                    projectId = (string)t["projectId"] ?? "",
                    title = (string)t["title"] ?? "",
                    body = (string)t["body"] ?? "",
                    voteCount = (int?)t["voteCount"] ?? 0,
                    status = (string)t["status"] ?? "",
                    createdAt = (string)t["createdAt"] ?? "",
                    createdBy = (string)t["createdBy"] ?? "",
                    hasMyVote = (bool?)t["hasMyVote"] ?? false,
                };
            }
        }

        class SimilarityMatch
        {
            public string id;
            public string title;
            public string body;
            public int voteCount;
            public float score;

            public static SimilarityMatch FromJson(JToken t)
            {
                return new SimilarityMatch
                {
                    id = (string)t["id"] ?? "",
                    title = (string)t["title"] ?? "",
                    body = (string)t["body"] ?? "",
                    voteCount = (int?)t["voteCount"] ?? 0,
                    score = (float?)t["score"] ?? 0f,
                };
            }
        }
    }
}
