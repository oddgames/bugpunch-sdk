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
    /// Chat board — the "Ask for help" flow surface. Players open threads with
    /// the dev/QA team, ping back and forth, and are auto-routed here when QA
    /// replies between sessions. Mirrors the list / detail pattern of
    /// <see cref="BugpunchFeedbackBoard"/> — one <see cref="UIDocument"/>
    /// cycles between three sub-views (List / New thread / Thread detail).
    ///
    /// All HTTP goes through a small local helper that pulls base URL + API
    /// key from <see cref="BugpunchConfig"/> on
    /// <see cref="BugpunchClient.Instance"/> and sends the anon device
    /// identity as <c>X-Device-Id</c>. The server resolves projectId from
    /// the API key.
    ///
    /// Fallback vs. native: the native shells on Android + iOS both
    /// UnitySendMessage back into <see cref="BugpunchClient"/> which calls
    /// <see cref="Show"/> — the full chat UI stays in C# for v2 (see
    /// native "shell" plan). Editor + Standalone reach this through the
    /// <see cref="UIToolkitDialog"/> directly.
    /// </summary>
    public static class BugpunchChatBoard
    {
        static GameObject _hostGO;
        static UIDocument _doc;
        static StyleSheet _styleSheet;

        // View containers — one stays visible at a time.
        static VisualElement _root;
        static VisualElement _card;
        static VisualElement _listContainer;
        static VisualElement _newThreadContainer;
        static VisualElement _threadContainer;
        static VisualElement _disabledContainer;

        // Fetched hours status (populated on Show)
        static HoursInfo _hours;

        // List state
        static readonly List<ChatThread> _threads = new();
        static ScrollView _listScroll;
        static Label _listEmptyLabel;

        // New-thread fields
        static TextField _newSubjectField;
        static TextField _newMessageField;
        static TextField _newPlayerNameField;
        static Button _newSendBtn;

        // Thread-detail state
        static ChatThread _activeThread;
        static readonly List<ChatMessage> _activeMessages = new();
        static ScrollView _threadScroll;
        static TextField _composeField;
        static Button _composeSendBtn;
        static Label _threadTitle;
        static VisualElement _threadHoursBanner;
        static IVisualElementScheduledItem _pollSchedule;
        static string _lastMessageAt = null;
        static bool _markedRead;

        /// <summary>
        /// Show the chat board. Entry point from <see cref="BugpunchRequestHelpPicker"/>
        /// (picker choice 1 = Ask for help) and from the reply-popup auto-open
        /// on <see cref="BugpunchClient"/>. Safe to call while the client is
        /// starting — if not configured, the card surfaces a graceful error.
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
            _newThreadContainer = new VisualElement();
            _threadContainer = new VisualElement();
            _disabledContainer = new VisualElement();

            BuildListView(_listContainer);
            BuildNewThreadView(_newThreadContainer);
            BuildThreadView(_threadContainer);
            BuildDisabledView(_disabledContainer);

            _card.Add(_listContainer);
            _card.Add(_newThreadContainer);
            _card.Add(_threadContainer);
            _card.Add(_disabledContainer);

            // Escape closes the board (or backs out of a sub-view).
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _root.Focus();

            // Show a neutral loading state while we fetch hours.
            _listEmptyLabel.text = "Loading…";
            SwitchTo(View.List);

            _ = BootstrapAsync();
        }

        static void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape) return;
            if (_threadContainer.style.display == DisplayStyle.Flex ||
                _newThreadContainer.style.display == DisplayStyle.Flex)
            {
                SwitchTo(View.List);
                StopPolling();
            }
            else
            {
                Hide();
            }
            e.StopPropagation();
        }

        enum View { List, NewThread, Thread, Disabled }

        static void SwitchTo(View v)
        {
            _listContainer.style.display = v == View.List ? DisplayStyle.Flex : DisplayStyle.None;
            _newThreadContainer.style.display = v == View.NewThread ? DisplayStyle.Flex : DisplayStyle.None;
            _threadContainer.style.display = v == View.Thread ? DisplayStyle.Flex : DisplayStyle.None;
            _disabledContainer.style.display = v == View.Disabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Hide()
        {
            StopPolling();
            _activeThread = null;
            _markedRead = false;
            if (_doc != null) _doc.rootVisualElement.Clear();
        }

        // ─── Bootstrap flow ───────────────────────────────────────────────
        //
        // 1. Fetch /hours — if disabled, swap to the "unavailable" card.
        // 2. Fetch /threads/mine — decide between list (threads exist) and
        //    empty-state "new thread" view.

        static async System.Threading.Tasks.Task BootstrapAsync()
        {
            if (!TryGetBaseUrl(out _, out _))
            {
                _listEmptyLabel.text = "Bugpunch isn't configured — chat unavailable.";
                return;
            }

            await FetchHours();
            if (_hours != null && _hours.IsDisabled)
            {
                SwitchTo(View.Disabled);
                return;
            }

            await FetchThreads();
            if (_threads.Count == 0)
            {
                // Empty state → drop straight into the new-thread composer,
                // which is less clicks for first-time users and matches the
                // old bug-report modal where you land on a form.
                SwitchTo(View.NewThread);
                _newSubjectField.Focus();
            }
            else
            {
                RefreshList();
                SwitchTo(View.List);
            }
        }

        // ─── Disabled view ────────────────────────────────────────────────

        static void BuildDisabledView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            var title = new Label("Chat");
            title.style.color = Color.white;
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            container.Add(title);

            var body = new Label("Chat is unavailable right now.");
            body.style.color = new Color(0.78f, 0.78f, 0.78f, 1);
            body.style.fontSize = 14;
            body.style.marginBottom = 14;
            body.style.whiteSpace = WhiteSpace.Normal;
            container.Add(body);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            var closeBtn = new Button(Hide) { text = "Close" };
            StylePrimaryButton(closeBtn);
            row.Add(closeBtn);
            container.Add(row);
        }

        // ─── List view ────────────────────────────────────────────────────

        static void BuildListView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            // Header: title + close
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 8;

            var title = new Label("Chat with the team");
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

            var subtitle = new Label("Ask a question, report a glitch, or chat with the dev team.");
            subtitle.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
            subtitle.style.fontSize = 13;
            subtitle.style.marginBottom = 10;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            container.Add(subtitle);

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

            var newBtn = new Button(() =>
            {
                _newSubjectField.value = "";
                _newMessageField.value = "";
                _newPlayerNameField.value = "";
                SwitchTo(View.NewThread);
                _newSubjectField.Focus();
            });
            newBtn.text = "+ New question";
            StylePrimaryButton(newBtn);
            newBtn.style.marginLeft = 0;
            container.Add(newBtn);
        }

        static void RefreshList()
        {
            _listScroll.Clear();

            if (_threads.Count == 0)
            {
                _listEmptyLabel.text = "No threads yet. Start a new question!";
                _listScroll.Add(_listEmptyLabel);
                return;
            }

            foreach (var t in _threads)
                _listScroll.Add(CreateThreadRow(t));
        }

        static VisualElement CreateThreadRow(ChatThread t)
        {
            var row = new Button(() => OpenThread(t));
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1);
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 6;
            row.style.borderTopWidth = row.style.borderBottomWidth =
                row.style.borderLeftWidth = row.style.borderRightWidth = 0;
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            row.style.paddingLeft = 12; row.style.paddingRight = 10;
            row.style.marginTop = 0; row.style.marginBottom = 6;
            row.style.marginLeft = 0; row.style.marginRight = 0;

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.flexShrink = 1;
            textCol.pickingMode = PickingMode.Ignore;

            var subject = new Label(string.IsNullOrEmpty(t.Subject) ? "(no subject)" : t.Subject);
            subject.style.color = Color.white;
            subject.style.fontSize = 14;
            subject.style.unityFontStyleAndWeight = FontStyle.Bold;
            subject.style.whiteSpace = WhiteSpace.Normal;
            subject.pickingMode = PickingMode.Ignore;
            textCol.Add(subject);

            var meta = new Label(FormatRelativeTime(t.LastMessageAt) + (t.Status == "closed" ? " · closed" : ""));
            meta.style.color = new Color(0.6f, 0.6f, 0.6f, 1);
            meta.style.fontSize = 11;
            meta.style.marginTop = 2;
            meta.pickingMode = PickingMode.Ignore;
            textCol.Add(meta);

            row.Add(textCol);

            if (t.UnreadCount > 0)
            {
                var badge = new Label(t.UnreadCount.ToString());
                badge.style.backgroundColor = new Color(0.78f, 0.16f, 0.16f, 1);
                badge.style.color = Color.white;
                badge.style.fontSize = 11;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.paddingTop = 2; badge.style.paddingBottom = 2;
                badge.style.paddingLeft = 7; badge.style.paddingRight = 7;
                badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
                    badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 9;
                badge.style.marginLeft = 8;
                badge.pickingMode = PickingMode.Ignore;
                row.Add(badge);
            }

            return row;
        }

        // ─── New-thread view ──────────────────────────────────────────────

        static void BuildNewThreadView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 4;

            var title = new Label("New question");
            title.style.color = Color.white;
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var back = new Button(() => SwitchTo(View.List)) { text = "Back" };
            StyleSecondaryButton(back);
            back.style.marginLeft = 0;
            header.Add(back);
            container.Add(header);

            var subtitle = new Label("Tell us what's going on. We'll reply here — and you'll see the answer next time you open the game.");
            subtitle.style.color = new Color(0.72f, 0.72f, 0.72f, 1);
            subtitle.style.fontSize = 13;
            subtitle.style.marginBottom = 12;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            container.Add(subtitle);

            var subjectLabel = new Label("Subject");
            subjectLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            subjectLabel.style.fontSize = 13;
            subjectLabel.style.marginBottom = 4;
            container.Add(subjectLabel);

            _newSubjectField = new TextField();
            _newSubjectField.maxLength = 200;
            StyleInput(_newSubjectField, "Short summary");
            container.Add(_newSubjectField);

            var msgLabel = new Label("Message");
            msgLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            msgLabel.style.fontSize = 13;
            msgLabel.style.marginTop = 8;
            msgLabel.style.marginBottom = 4;
            container.Add(msgLabel);

            _newMessageField = new TextField();
            _newMessageField.multiline = true;
            _newMessageField.maxLength = 4000;
            StyleInput(_newMessageField, "Describe what's happening…", multiline: true);
            container.Add(_newMessageField);

            // Auto-fill subject from the first line of the message if subject
            // is still blank — saves the tester a field on short questions.
            _newMessageField.RegisterValueChangedCallback(e =>
            {
                var v = e.newValue ?? "";
                if (string.IsNullOrWhiteSpace(_newSubjectField.value) && !string.IsNullOrWhiteSpace(v))
                {
                    var line = v.Split('\n')[0].Trim();
                    if (line.Length > 80) line = line.Substring(0, 77) + "…";
                    _newSubjectField.value = line;
                }
                UpdateNewSendEnabled();
            });
            _newSubjectField.RegisterValueChangedCallback(_ => UpdateNewSendEnabled());

            var nameLabel = new Label("Your name (optional)");
            nameLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1);
            nameLabel.style.fontSize = 13;
            nameLabel.style.marginTop = 8;
            nameLabel.style.marginBottom = 4;
            container.Add(nameLabel);

            _newPlayerNameField = new TextField();
            _newPlayerNameField.maxLength = 80;
            StyleInput(_newPlayerNameField, "So we can address you by name");
            container.Add(_newPlayerNameField);

            // Hours banner for new-thread view — helps set expectations on
            // how fast the player will hear back.
            var banner = BuildHoursBannerIfApplicable();
            if (banner != null)
            {
                banner.style.marginTop = 12;
                container.Add(banner);
            }

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.style.marginTop = 12;

            var cancel = new Button(() =>
            {
                // If there are existing threads, back to list; otherwise close.
                if (_threads.Count > 0) SwitchTo(View.List);
                else Hide();
            })
            { text = "Cancel" };
            StyleSecondaryButton(cancel);
            btnRow.Add(cancel);

            _newSendBtn = new Button(() => _ = SubmitNewThread()) { text = "Send" };
            StylePrimaryButton(_newSendBtn);
            _newSendBtn.SetEnabled(false);
            btnRow.Add(_newSendBtn);
            container.Add(btnRow);
        }

        static void UpdateNewSendEnabled()
        {
            bool hasSubject = !string.IsNullOrWhiteSpace(_newSubjectField?.value);
            bool hasMsg = !string.IsNullOrWhiteSpace(_newMessageField?.value);
            _newSendBtn?.SetEnabled(hasSubject && hasMsg);
        }

        // ─── Thread view ──────────────────────────────────────────────────

        static void BuildThreadView(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 6;

            var back = new Button(() =>
            {
                StopPolling();
                _activeThread = null;
                _markedRead = false;
                // Refresh the list so any new message hits for other threads land.
                _ = FetchThreadsAndSwitchToList();
            })
            { text = "← Back" };
            StyleSecondaryButton(back);
            back.style.marginLeft = 0;
            header.Add(back);

            _threadTitle = new Label("");
            _threadTitle.style.color = Color.white;
            _threadTitle.style.fontSize = 16;
            _threadTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _threadTitle.style.flexGrow = 1;
            _threadTitle.style.flexShrink = 1;
            _threadTitle.style.marginLeft = 8;
            _threadTitle.style.marginRight = 8;
            _threadTitle.style.whiteSpace = WhiteSpace.Normal;
            header.Add(_threadTitle);

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

            _threadHoursBanner = new VisualElement();
            container.Add(_threadHoursBanner);

            _threadScroll = new ScrollView(ScrollViewMode.Vertical);
            _threadScroll.style.maxHeight = 380;
            _threadScroll.style.minHeight = 200;
            _threadScroll.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 1);
            _threadScroll.style.borderTopLeftRadius = _threadScroll.style.borderTopRightRadius =
                _threadScroll.style.borderBottomLeftRadius = _threadScroll.style.borderBottomRightRadius = 6;
            _threadScroll.style.paddingTop = 8; _threadScroll.style.paddingBottom = 8;
            _threadScroll.style.paddingLeft = 8; _threadScroll.style.paddingRight = 8;
            _threadScroll.style.marginBottom = 8;
            container.Add(_threadScroll);

            // Compose row
            var compose = new VisualElement();
            compose.style.flexDirection = FlexDirection.Row;
            compose.style.alignItems = Align.FlexStart;

            _composeField = new TextField();
            _composeField.multiline = true;
            _composeField.maxLength = 4000;
            StyleInput(_composeField, "Write a reply…", multiline: true);
            _composeField.style.flexGrow = 1;
            _composeField.style.flexShrink = 1;
            _composeField.style.marginBottom = 0;
            compose.Add(_composeField);

            _composeSendBtn = new Button(() => _ = SendReply()) { text = "Send" };
            StylePrimaryButton(_composeSendBtn);
            _composeSendBtn.style.alignSelf = Align.FlexEnd;
            _composeSendBtn.SetEnabled(false);
            _composeField.RegisterValueChangedCallback(e =>
                _composeSendBtn.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));
            compose.Add(_composeSendBtn);
            container.Add(compose);
        }

        static async System.Threading.Tasks.Task FetchThreadsAndSwitchToList()
        {
            await FetchThreads();
            RefreshList();
            SwitchTo(View.List);
        }

        static void OpenThread(ChatThread t)
        {
            _activeThread = t;
            _activeMessages.Clear();
            _markedRead = false;
            _threadTitle.text = string.IsNullOrEmpty(t.Subject) ? "(no subject)" : t.Subject;
            _threadScroll.Clear();

            // Hours banner inside the thread view
            _threadHoursBanner.Clear();
            var banner = BuildHoursBannerIfApplicable();
            if (banner != null)
            {
                banner.style.marginBottom = 8;
                _threadHoursBanner.Add(banner);
            }

            _composeField.value = "";
            SwitchTo(View.Thread);

            _ = LoadThreadAsync(t.Id);
            StartPolling(t.Id);
        }

        static async System.Threading.Tasks.Task LoadThreadAsync(string threadId)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var url = baseUrl + "/api/v1/chat/threads/" + UnityWebRequest.EscapeURL(threadId);
            var (ok, status, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok)
            {
                var err = new Label($"Could not load thread (HTTP {status}).");
                err.style.color = new Color(1f, 0.5f, 0.5f, 1);
                err.style.fontSize = 12;
                _threadScroll.Add(err);
                return;
            }

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var messages = obj["messages"] as JArray;
                _activeMessages.Clear();
                if (messages != null)
                {
                    foreach (var m in messages)
                        _activeMessages.Add(ChatMessage.FromJson(m));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Load parse failed: {ex.Message}");
            }

            RenderThread();

            // Track cursor for incremental polling.
            if (_activeMessages.Count > 0)
                _lastMessageAt = _activeMessages[_activeMessages.Count - 1].CreatedAt;

            // Mark read — only once per open, and only if the thread arrived with unread QA messages.
            if (!_markedRead && _activeThread != null && _activeThread.UnreadCount > 0)
            {
                _markedRead = true;
                _ = MarkRead(threadId);
                _activeThread.UnreadCount = 0;
            }
        }

        static void RenderThread()
        {
            _threadScroll.Clear();

            if (_activeMessages.Count == 0)
            {
                var empty = new Label("No messages yet.");
                empty.style.color = new Color(0.6f, 0.6f, 0.6f, 1);
                empty.style.fontSize = 12;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 20;
                _threadScroll.Add(empty);
                return;
            }

            foreach (var m in _activeMessages)
                _threadScroll.Add(CreateMessageBubble(m));

            // Scroll to bottom on next layout pass.
            _threadScroll.schedule.Execute(() =>
            {
                if (_threadScroll.contentContainer != null)
                    _threadScroll.scrollOffset = new Vector2(0, float.MaxValue);
            }).StartingIn(16);
        }

        static VisualElement CreateMessageBubble(ChatMessage m)
        {
            bool isPlayer = m.Sender == "sdk";
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.justifyContent = isPlayer ? Justify.FlexEnd : Justify.FlexStart;
            wrapper.style.marginBottom = 6;

            var bubble = new VisualElement();
            bubble.style.backgroundColor = isPlayer
                ? new Color(0.24f, 0.50f, 0.30f, 1)
                : new Color(0.20f, 0.22f, 0.26f, 1);
            bubble.style.borderTopLeftRadius = bubble.style.borderTopRightRadius =
                bubble.style.borderBottomLeftRadius = bubble.style.borderBottomRightRadius = 8;
            bubble.style.paddingTop = 6; bubble.style.paddingBottom = 6;
            bubble.style.paddingLeft = 10; bubble.style.paddingRight = 10;
            bubble.style.maxWidth = Length.Percent(80);

            if (!isPlayer && !string.IsNullOrEmpty(m.UserName))
            {
                var name = new Label(m.UserName);
                name.style.color = new Color(0.7f, 0.85f, 1f, 1);
                name.style.fontSize = 11;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.marginBottom = 2;
                bubble.Add(name);
            }

            var body = new Label(m.Body ?? "");
            body.style.color = Color.white;
            body.style.fontSize = 13;
            body.style.whiteSpace = WhiteSpace.Normal;
            bubble.Add(body);

            var time = new Label(FormatRelativeTime(m.CreatedAt));
            time.style.color = new Color(1f, 1f, 1f, 0.55f);
            time.style.fontSize = 10;
            time.style.marginTop = 2;
            time.style.unityTextAlign = isPlayer ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            bubble.Add(time);

            wrapper.Add(bubble);
            return wrapper;
        }

        static async System.Threading.Tasks.Task SendReply()
        {
            if (_activeThread == null) return;
            var text = _composeField.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            _composeSendBtn.SetEnabled(false);
            _composeSendBtn.text = "…";

            var url = baseUrl + "/api/v1/chat/threads/" + UnityWebRequest.EscapeURL(_activeThread.Id) + "/messages";
            var payload = BuildJson(new Dictionary<string, string> { ["message"] = text });
            var (ok, status, body) = await HttpRequest("POST", url, apiKey, payload);

            _composeSendBtn.text = "Send";
            _composeSendBtn.SetEnabled(true);

            if (!ok)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] SendReply failed (HTTP {status}).");
                return;
            }

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var msg = obj["message"];
                if (msg != null)
                {
                    var parsed = ChatMessage.FromJson(msg);
                    _activeMessages.Add(parsed);
                    _lastMessageAt = parsed.CreatedAt;
                    RenderThread();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] SendReply parse failed: {ex.Message}");
            }

            _composeField.value = "";
        }

        static async System.Threading.Tasks.Task SubmitNewThread()
        {
            var subject = _newSubjectField.value?.Trim() ?? "";
            var message = _newMessageField.value?.Trim() ?? "";
            var playerName = _newPlayerNameField.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(message)) return;
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            _newSendBtn.SetEnabled(false);
            _newSendBtn.text = "Sending…";

            var fields = new Dictionary<string, string>
            {
                ["subject"] = subject,
                ["message"] = message,
            };
            if (!string.IsNullOrEmpty(playerName)) fields["playerName"] = playerName;
            var payload = BuildJson(fields);

            var url = baseUrl + "/api/v1/chat/threads";
            var (ok, status, body) = await HttpRequest("POST", url, apiKey, payload);

            _newSendBtn.text = "Send";
            _newSendBtn.SetEnabled(true);

            if (!ok)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Create thread failed (HTTP {status}).");
                return;
            }

            string threadId = null;
            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                threadId = (string)obj["threadId"];
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Create thread parse failed: {ex.Message}");
            }

            // Refresh list and jump into the new thread (if id returned).
            await FetchThreads();
            if (!string.IsNullOrEmpty(threadId))
            {
                var t = _threads.Find(x => x.Id == threadId) ?? new ChatThread
                {
                    Id = threadId,
                    Subject = subject,
                    Status = "open",
                    LastMessageAt = DateTime.UtcNow.ToString("o"),
                };
                OpenThread(t);
            }
            else
            {
                RefreshList();
                SwitchTo(View.List);
            }
        }

        static async System.Threading.Tasks.Task MarkRead(string threadId)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/v1/chat/threads/" + UnityWebRequest.EscapeURL(threadId) + "/read";
            await HttpRequest("POST", url, apiKey, "{}");
        }

        // ─── Polling ─────────────────────────────────────────────────────

        static void StartPolling(string threadId)
        {
            StopPolling();
            // 5s cadence for "while thread is open" — matches email/chat app
            // defaults for desktop-class clients and is cheap on the server.
            _pollSchedule = _root.schedule.Execute(() => _ = PollThread(threadId)).Every(5000);
        }

        static void StopPolling()
        {
            if (_pollSchedule != null)
            {
                _pollSchedule.Pause();
                _pollSchedule = null;
            }
        }

        static async System.Threading.Tasks.Task PollThread(string threadId)
        {
            if (_activeThread == null || _activeThread.Id != threadId) return;
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var url = baseUrl + "/api/v1/chat/threads/" + UnityWebRequest.EscapeURL(threadId) + "/messages";
            if (!string.IsNullOrEmpty(_lastMessageAt))
                url += "?since=" + UnityWebRequest.EscapeURL(_lastMessageAt);

            var (ok, _, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok) return;

            bool gotNewQaMessage = false;
            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var messages = obj["messages"] as JArray;
                if (messages != null)
                {
                    foreach (var m in messages)
                    {
                        var parsed = ChatMessage.FromJson(m);
                        // De-dupe — a slow server clock could echo the last one.
                        if (_activeMessages.Exists(x => x.Id == parsed.Id)) continue;
                        _activeMessages.Add(parsed);
                        _lastMessageAt = parsed.CreatedAt;
                        if (parsed.Sender != "sdk") gotNewQaMessage = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Poll parse failed: {ex.Message}");
                return;
            }

            if (gotNewQaMessage)
            {
                RenderThread();
                // New QA message while open counts as read — flag it so we
                // don't re-open automatically next heartbeat tick.
                _ = MarkRead(threadId);
            }
        }

        // ─── HTTP ─────────────────────────────────────────────────────────

        static async System.Threading.Tasks.Task FetchHours()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/v1/chat/hours";
            var (ok, _, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok) return;

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                _hours = new HoursInfo
                {
                    Status = (string)obj["status"] ?? "",
                    NextOpen = (string)obj["nextOpen"],
                    Message = (string)obj["message"],
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Hours parse failed: {ex.Message}");
            }
        }

        static async System.Threading.Tasks.Task FetchThreads()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey))
            {
                _listEmptyLabel.text = "Bugpunch isn't configured — chat unavailable.";
                return;
            }

            var url = baseUrl + "/api/v1/chat/threads/mine";
            var (ok, status, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok)
            {
                _listEmptyLabel.text = $"Could not load threads (HTTP {status}).";
                return;
            }

            _threads.Clear();
            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var arr = obj["threads"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                        _threads.Add(ChatThread.FromJson(t));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Threads parse failed: {ex.Message}");
                _listEmptyLabel.text = "Could not parse thread list.";
            }
        }

        /// <summary>
        /// Return true if any thread in <paramref name="threads"/> currently
        /// has an unread QA message. Used by <see cref="BugpunchClient"/>'s
        /// reply-poll heartbeat — it calls the same endpoint separately so
        /// the chat board itself doesn't need to be open for the auto-popup
        /// to decide.
        /// </summary>
        internal static int CountUnread(IEnumerable<ChatThread> threads)
        {
            if (threads == null) return 0;
            int n = 0;
            foreach (var t in threads)
                if (t != null && t.UnreadCount > 0) n++;
            return n;
        }

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
                Debug.LogWarning("[Bugpunch.ChatBoard] BugpunchClient not initialized — chat unavailable.");
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
                sb.Append(JsonConvert.ToString(kv.Key));
                sb.Append(':');
                sb.Append(JsonConvert.ToString(kv.Value ?? ""));
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ─── Hours banner ────────────────────────────────────────────────

        static VisualElement BuildHoursBannerIfApplicable()
        {
            if (_hours == null || !_hours.IsOffHours) return null;

            var banner = new VisualElement();
            banner.style.flexDirection = FlexDirection.Column;
            banner.style.backgroundColor = new Color(0.25f, 0.20f, 0.10f, 1);
            banner.style.borderTopColor = banner.style.borderBottomColor =
                banner.style.borderLeftColor = banner.style.borderRightColor = new Color(0.55f, 0.44f, 0.18f, 1);
            banner.style.borderTopWidth = banner.style.borderBottomWidth =
                banner.style.borderLeftWidth = banner.style.borderRightWidth = 1;
            banner.style.borderTopLeftRadius = banner.style.borderTopRightRadius =
                banner.style.borderBottomLeftRadius = banner.style.borderBottomRightRadius = 6;
            banner.style.paddingTop = 8; banner.style.paddingBottom = 8;
            banner.style.paddingLeft = 10; banner.style.paddingRight = 10;

            var msg = !string.IsNullOrEmpty(_hours.Message)
                ? _hours.Message
                : "The team's offline right now — we'll reply when we're back.";
            var msgLabel = new Label(msg);
            msgLabel.style.color = new Color(1f, 0.88f, 0.72f, 1);
            msgLabel.style.fontSize = 12;
            msgLabel.style.whiteSpace = WhiteSpace.Normal;
            banner.Add(msgLabel);

            if (!string.IsNullOrEmpty(_hours.NextOpen))
            {
                var next = new Label("Next available: " + FormatAbsoluteTime(_hours.NextOpen));
                next.style.color = new Color(1f, 0.78f, 0.58f, 1);
                next.style.fontSize = 11;
                next.style.marginTop = 4;
                banner.Add(next);
            }
            return banner;
        }

        // ─── Styling ──────────────────────────────────────────────────────

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
            btn.style.paddingLeft = 16; btn.style.paddingRight = 16;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.marginLeft = 8;
        }

        // ─── Formatting ───────────────────────────────────────────────────

        static string FormatRelativeTime(string isoTime)
        {
            if (string.IsNullOrEmpty(isoTime)) return "";
            if (!DateTime.TryParse(isoTime, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
                return "";
            var delta = DateTime.UtcNow - t;
            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
            return t.ToLocalTime().ToString("MMM d");
        }

        static string FormatAbsoluteTime(string isoTime)
        {
            if (string.IsNullOrEmpty(isoTime)) return "";
            if (!DateTime.TryParse(isoTime, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
                return isoTime;
            return t.ToLocalTime().ToString("MMM d, h:mm tt");
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
            card.style.maxWidth = 560;
            card.style.minWidth = 360;
            card.style.width = Length.Percent(92);
            card.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return card;
        }

        static void EnsureDocument()
        {
            if (_doc != null) return;

            _hostGO = new GameObject("Bugpunch_ChatBoard");
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

        internal class ChatThread
        {
            public string Id;
            public string Subject;
            public string Status;          // "open" | "closed"
            public string LastMessageAt;
            public string CreatedAt;
            public int UnreadCount;

            public static ChatThread FromJson(JToken t)
            {
                return new ChatThread
                {
                    Id = (string)t["Id"] ?? (string)t["id"] ?? "",
                    Subject = (string)t["Subject"] ?? (string)t["subject"] ?? "",
                    Status = (string)t["Status"] ?? (string)t["status"] ?? "open",
                    LastMessageAt = (string)t["LastMessageAt"] ?? (string)t["lastMessageAt"] ?? "",
                    CreatedAt = (string)t["CreatedAt"] ?? (string)t["createdAt"] ?? "",
                    UnreadCount = (int?)t["UnreadCount"] ?? (int?)t["unreadCount"] ?? 0,
                };
            }
        }

        internal class ChatMessage
        {
            public string Id;
            public string ThreadId;
            public string Sender;          // "sdk" | "qa"
            public string Body;
            public string CreatedAt;
            public string UserName;

            public static ChatMessage FromJson(JToken t)
            {
                return new ChatMessage
                {
                    Id = (string)t["Id"] ?? (string)t["id"] ?? "",
                    ThreadId = (string)t["ThreadId"] ?? (string)t["threadId"] ?? "",
                    Sender = (string)t["Sender"] ?? (string)t["sender"] ?? "sdk",
                    Body = (string)t["Body"] ?? (string)t["body"] ?? "",
                    CreatedAt = (string)t["CreatedAt"] ?? (string)t["createdAt"] ?? "",
                    UserName = (string)t["UserName"] ?? (string)t["userName"],
                };
            }
        }

        class HoursInfo
        {
            public string Status;          // "on_hours" | "off_hours" | "always_on" | "disabled"
            public string NextOpen;
            public string Message;

            public bool IsDisabled => Status == "disabled";
            public bool IsOffHours => Status == "off_hours";
        }
    }
}
