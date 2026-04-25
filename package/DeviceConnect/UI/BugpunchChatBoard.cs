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
    /// <summary>
    /// Chat board — the "Ask for help" surface. Messenger-style single
    /// conversation: one persistent thread per device, render as chat bubbles
    /// with the composer pinned to the bottom. All HTTP goes through a small
    /// local helper that pulls base URL + API key from <see cref="BugpunchConfig"/>
    /// on <see cref="BugpunchClient.Instance"/> and sends the anon device
    /// identity as <c>X-Device-Id</c>. The server resolves projectId from the
    /// API key.
    ///
    /// Fallback vs. native: the native shells on Android + iOS both
    /// UnitySendMessage back into <see cref="BugpunchClient"/> which calls
    /// <see cref="Show"/> — the full chat UI stays in C#. Editor + Standalone
    /// reach this through the <see cref="UIToolkitDialog"/> directly.
    /// </summary>
    public static partial class BugpunchChatBoard
    {
        static GameObject _hostGO;
        static UIDocument _doc;
        static StyleSheet _styleSheet;

        // Root + surfaces
        static VisualElement _root;
        static VisualElement _card;
        static VisualElement _messagesContainer;   // message bubble list (inside _scroll)
        static ScrollView _scroll;
        static VisualElement _hoursBanner;
        static VisualElement _disabledCard;        // shown when chat is disabled
        static VisualElement _newMsgPill;          // "↓ New messages" floating pill

        // Composer
        static TextField _composeField;
        static Button _sendBtn;
        static Button _emojiBtn;
        static Button _attachBtn;
        static VisualElement _attachmentPreview;
        static VisualElement _emojiPopover;

        // Draft state
        static PendingAttachment _pendingAttachment;

        // Messages + polling
        static readonly List<ChatMessage> _messages = new();
        static string _threadId;          // may be null before first user message
        static HoursInfo _hours;
        static IVisualElementScheduledItem _pollSchedule;
        static IVisualElementScheduledItem _typingSchedule;
        static string _lastMessageAt;
        static bool _qaTyping;
        static VisualElement _qaTypingBubble;

        // Cache: image URL → Texture2D (so polling doesn't re-download)
        static readonly Dictionary<string, Texture2D> _imageCache = new();
        // Messages with their "tap to reveal time" toggled on
        static readonly HashSet<string> _revealedTimestamps = new();

        // Typing debounce — last time the composer was mutated by the user
        static float _lastComposerChangeTime = -999f;
        static float _lastTypingSentTime = -999f;
        static bool _composerFocused;

        // Pending picks from native image pickers keyed by call nonce.
        static Action<string> _pendingPickCallback;

        // URL linkify regex — conservative (avoid capturing trailing punctuation).
        static readonly Regex UrlRegex = new Regex(
            @"https?://[^\s<>""']+",
            RegexOptions.Compiled);

        /// <summary>
        /// Show the chat board. Entry point from
        /// <see cref="BugpunchRequestHelpPicker"/> (picker choice 1 = Ask for
        /// help) and from the reply-popup auto-open on
        /// <see cref="BugpunchClient"/>. Safe to call while the client is
        /// starting — if not configured the card surfaces a graceful error.
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

            // Reset per-open state.
            _messages.Clear();
            _threadId = null;
            _hours = null;
            _lastMessageAt = null;
            _qaTyping = false;
            _qaTypingBubble = null;
            _revealedTimestamps.Clear();
            _pendingAttachment = null;
            _lastComposerChangeTime = -999f;
            _lastTypingSentTime = -999f;
            _composerFocused = false;
            StopPolling();

            var backdrop = CreateBackdrop(Hide);
            _card = CreateCard();
            backdrop.Add(_card);
            _root.Add(backdrop);

            BuildChatSurface(_card);

            // Disabled card lives on top of everything — hidden unless hours=disabled.
            _disabledCard = BuildDisabledCard();
            _disabledCard.style.display = DisplayStyle.None;
            _card.Add(_disabledCard);

            // Escape closes the board (no sub-views to back out of anymore).
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                Hide();
                e.StopPropagation();
            });
            _root.Focus();

            _ = BootstrapAsync();
        }

        static void Hide()
        {
            StopPolling();
            if (_doc != null) _doc.rootVisualElement.Clear();
        }

        static void StopPolling()
        {
            if (_pollSchedule != null) { _pollSchedule.Pause(); _pollSchedule = null; }
            if (_typingSchedule != null) { _typingSchedule.Pause(); _typingSchedule = null; }
        }

        // ─── Bootstrap flow ───────────────────────────────────────────────

        static async System.Threading.Tasks.Task BootstrapAsync()
        {
            if (!TryGetBaseUrl(out _, out _))
            {
                ShowEmptyState("Bugpunch isn't configured — chat unavailable.");
                return;
            }

            await FetchHours();
            if (_hours != null && _hours.IsDisabled)
            {
                _disabledCard.style.display = DisplayStyle.Flex;
                return;
            }

            RenderHoursBanner();

            // Fetch the single thread (may 404 = no thread yet).
            await LoadThreadAsync();
            RenderMessages();
            MaybeAutoMarkRead();

            StartPolling();
            StartTypingTimers();
        }

        // ─── Chat surface ────────────────────────────────────────────────

        static void BuildChatSurface(VisualElement container)
        {
            container.style.flexDirection = FlexDirection.Column;

            // Header: title + close
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 8;

            var theme = BugpunchTheme.Current;

            var title = new Label("Chat with the team");
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

            _hoursBanner = new VisualElement();
            container.Add(_hoursBanner);

            // Message scroll area. Relative-positioned wrapper so the
            // "↓ New messages" pill can float inside it.
            var scrollWrap = new VisualElement();
            scrollWrap.style.position = Position.Relative;
            scrollWrap.style.marginBottom = 8;
            container.Add(scrollWrap);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.maxHeight = 440;
            _scroll.style.minHeight = 260;
            // Subtly darker than the card so the scroll well reads as inset.
            var bg = theme.cardBackground;
            _scroll.style.backgroundColor = new Color(bg.r * 0.75f, bg.g * 0.75f, bg.b * 0.75f, bg.a);
            _scroll.style.borderTopLeftRadius = _scroll.style.borderTopRightRadius =
                _scroll.style.borderBottomLeftRadius = _scroll.style.borderBottomRightRadius = 8;
            _scroll.style.paddingTop = 10; _scroll.style.paddingBottom = 10;
            _scroll.style.paddingLeft = 10; _scroll.style.paddingRight = 10;
            scrollWrap.Add(_scroll);

            _messagesContainer = new VisualElement();
            _messagesContainer.style.flexDirection = FlexDirection.Column;
            _scroll.Add(_messagesContainer);

            _newMsgPill = BuildNewMessagesPill();
            _newMsgPill.style.display = DisplayStyle.None;
            scrollWrap.Add(_newMsgPill);

            // Composer row
            BuildComposer(container);

            // Emoji popover sits on top of the composer, hidden until toggled.
            _emojiPopover = new VisualElement();
            _emojiPopover.style.display = DisplayStyle.None;
            container.Add(_emojiPopover);
        }

        static VisualElement BuildNewMessagesPill()
        {
            var theme = BugpunchTheme.Current;
            var pill = new Button(ScrollToBottom) { text = "↓ New messages" };
            pill.style.position = Position.Absolute;
            pill.style.right = 12; pill.style.bottom = 10;
            pill.style.backgroundColor = theme.accentPrimary;
            pill.style.color = theme.textPrimary;
            pill.style.fontSize = theme.fontSizeCaption;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.paddingTop = 6; pill.style.paddingBottom = 6;
            pill.style.paddingLeft = 12; pill.style.paddingRight = 12;
            pill.style.borderTopLeftRadius = pill.style.borderTopRightRadius =
                pill.style.borderBottomLeftRadius = pill.style.borderBottomRightRadius = 12;
            pill.style.borderTopWidth = pill.style.borderBottomWidth =
                pill.style.borderLeftWidth = pill.style.borderRightWidth = 0;
            return pill;
        }

        // ─── Rendering ───────────────────────────────────────────────────

        static void RenderMessages()
        {
            // Capture "was at bottom" before re-rendering so auto-scroll only
            // kicks in when the user hadn't scrolled up.
            bool wasAtBottom = IsNearBottom(80);

            _messagesContainer.Clear();
            _qaTypingBubble = null;

            if (_messages.Count == 0)
            {
                var empty = new Label("Start a conversation with the dev team.\nAsk a question, report a glitch, or just say hi.");
                empty.style.color = BugpunchTheme.Current.textMuted;
                empty.style.fontSize = BugpunchTheme.Current.fontSizeCaption + 1;
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 40;
                empty.style.marginBottom = 40;
                _messagesContainer.Add(empty);
            }
            else
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    var m = _messages[i];
                    var prev = i > 0 ? _messages[i - 1] : null;
                    bool collapseHeader = ShouldCollapseHeader(m, prev);
                    bool showAvatar = !IsSdk(m) && (prev == null || IsSdk(prev) || !WithinRun(prev, m));
                    _messagesContainer.Add(CreateMessageBubble(m, collapseHeader, showAvatar));
                }
            }

            // Typing indicator lives at the bottom, below the last bubble.
            if (_qaTyping)
                AppendTypingIndicator();

            // Auto-scroll to bottom if the user was already there. Use the
            // panel scheduler so layout has settled before we measure.
            _scroll.schedule.Execute(() =>
            {
                if (wasAtBottom) ScrollToBottom();
                UpdateNewMessagesPill();
            }).StartingIn(16);
        }

        static void ShowEmptyState(string msg)
        {
            _messagesContainer.Clear();
            var theme = BugpunchTheme.Current;
            var empty = new Label(msg);
            empty.style.color = theme.textMuted;
            empty.style.fontSize = theme.fontSizeCaption + 1;
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.marginTop = 40; empty.style.marginBottom = 40;
            _messagesContainer.Add(empty);
        }

        // ─── Scroll helpers ──────────────────────────────────────────────

        static bool IsNearBottom(float slack)
        {
            if (_scroll == null || _scroll.contentContainer == null) return true;
            var offset = _scroll.scrollOffset.y;
            var contentH = _scroll.contentContainer.resolvedStyle.height;
            var viewH = _scroll.resolvedStyle.height;
            if (float.IsNaN(contentH) || float.IsNaN(viewH) || contentH <= 0) return true;
            return offset + viewH >= contentH - slack;
        }

        static void ScrollToBottom()
        {
            if (_scroll == null) return;
            _scroll.scrollOffset = new Vector2(0, float.MaxValue);
            if (_newMsgPill != null) _newMsgPill.style.display = DisplayStyle.None;
        }

        static void UpdateNewMessagesPill()
        {
            if (_newMsgPill == null) return;
            // Only meaningful if there is overflow — otherwise always "at bottom".
            var viewH = _scroll?.resolvedStyle.height ?? 0;
            var contentH = _scroll?.contentContainer?.resolvedStyle.height ?? 0;
            bool overflow = contentH > viewH + 1;
            _newMsgPill.style.display = (overflow && !IsNearBottom(80)) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ─── Read / auto-open ────────────────────────────────────────────

        static void MaybeAutoMarkRead()
        {
            // Any unread QA message should be marked read once the board opens.
            bool hasUnreadQa = false;
            foreach (var m in _messages)
            {
                if (m.Sender == "qa" && string.IsNullOrEmpty(m.ReadBySdkAt))
                {
                    hasUnreadQa = true; break;
                }
            }
            if (hasUnreadQa) _ = PostRead();
        }

        // ─── Polling ─────────────────────────────────────────────────────

        static void StartPolling()
        {
            if (_root == null) return;
            _pollSchedule = _root.schedule.Execute(() => _ = PollTick()).Every(5000);
        }

        static void StartTypingTimers()
        {
            if (_root == null) return;
            // Typing outgoing: every 1s, if composer focused AND text changed
            // in the last 3s AND we haven't posted /typing in the last 3s,
            // send a typing ping. Debounces to at most once per 3s.
            _typingSchedule = _root.schedule.Execute(() =>
            {
                var now = Time.realtimeSinceStartup;
                if (!_composerFocused) return;
                if (now - _lastComposerChangeTime > 3f) return;
                if (now - _lastTypingSentTime < 3f) return;
                _lastTypingSentTime = now;
                _ = PostTyping();
            }).Every(1000);
        }

        static async System.Threading.Tasks.Task PollTick()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            // 1. Fetch new messages since cursor.
            if (!string.IsNullOrEmpty(_threadId))
            {
                var url = baseUrl + "/api/v1/chat/messages";
                if (!string.IsNullOrEmpty(_lastMessageAt))
                    url += "?since=" + UnityWebRequest.EscapeURL(_lastMessageAt);
                var (ok, _, body) = await HttpRequest("GET", url, apiKey, null);
                if (ok)
                {
                    try
                    {
                        var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                        var messages = obj["messages"] as JArray;
                        bool gotNewQa = false;
                        if (messages != null)
                        {
                            foreach (var m in messages)
                            {
                                var parsed = ChatMessage.FromJson(m);
                                if (_messages.Exists(x => !string.IsNullOrEmpty(x.Id) && x.Id == parsed.Id)) continue;
                                _messages.Add(parsed);
                                _lastMessageAt = parsed.CreatedAt;
                                if (parsed.Sender == "qa") gotNewQa = true;
                            }
                        }
                        if (gotNewQa)
                        {
                            _qaTyping = false; // typing ends when a message lands
                            RenderMessages();
                            _ = PostRead();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bugpunch.ChatBoard] Poll parse failed: {ex.Message}");
                    }
                }
            }

            // 2. Fetch QA typing flag.
            {
                var url = baseUrl + "/api/v1/chat/typing";
                var (ok, _, body) = await HttpRequest("GET", url, apiKey, null);
                if (ok)
                {
                    try
                    {
                        var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                        var qaTyping = (bool?)obj["qaTyping"] ?? false;
                        if (qaTyping != _qaTyping)
                        {
                            _qaTyping = qaTyping;
                            RenderMessages();
                        }
                    }
                    catch { /* ignore */ }
                }
            }
        }

        // ─── HTTP — hours / thread / message / typing / read ─────────────

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

        static async System.Threading.Tasks.Task LoadThreadAsync()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/v1/chat/thread";
            var (ok, status, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok)
            {
                // 404 = no thread yet — fine, composer creates it on first send.
                if (status != 404) Debug.LogWarning($"[Bugpunch.ChatBoard] thread fetch HTTP {status}");
                return;
            }
            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var thread = obj["thread"];
                if (thread != null) _threadId = (string)thread["Id"] ?? (string)thread["id"];
                var msgs = obj["messages"] as JArray;
                if (msgs != null)
                {
                    foreach (var m in msgs)
                        _messages.Add(ChatMessage.FromJson(m));
                }
                if (_messages.Count > 0)
                    _lastMessageAt = _messages[_messages.Count - 1].CreatedAt;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] thread parse failed: {ex.Message}");
            }
        }

        static async System.Threading.Tasks.Task SendMessage()
        {
            var text = _composeField.value?.Trim() ?? "";
            var hasAttachment = _pendingAttachment != null;
            if (string.IsNullOrEmpty(text) && !hasAttachment) return;
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            _sendBtn.SetEnabled(false);
            _sendBtn.text = "…";

            // Build JSON via Newtonsoft so nested attachments serialize correctly.
            var payload = new JObject { ["body"] = text };
            if (hasAttachment)
            {
                var arr = new JArray();
                var ao = new JObject
                {
                    ["type"] = "image",
                    ["url"] = _pendingAttachment.Url,
                    ["mime"] = _pendingAttachment.Mime ?? "image/*",
                };
                if (_pendingAttachment.Width > 0) ao["width"] = _pendingAttachment.Width;
                if (_pendingAttachment.Height > 0) ao["height"] = _pendingAttachment.Height;
                arr.Add(ao);
                payload["attachments"] = arr;
            }

            var (ok, status, body) = await HttpRequest("POST", baseUrl + "/api/v1/chat/message",
                apiKey, payload.ToString(Formatting.None));

            _sendBtn.text = "Send";
            UpdateSendEnabled();

            if (!ok)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] send failed HTTP {status}");
                return;
            }

            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var thread = obj["thread"];
                if (thread != null)
                    _threadId = (string)thread["Id"] ?? (string)thread["id"] ?? _threadId;
                var msg = obj["message"];
                if (msg != null)
                {
                    var parsed = ChatMessage.FromJson(msg);
                    _messages.Add(parsed);
                    _lastMessageAt = parsed.CreatedAt;
                }
                // hoursStatus may have changed (game just went off-hours) — refresh.
                var hoursTok = obj["hoursStatus"];
                if (hoursTok != null)
                {
                    _hours = new HoursInfo
                    {
                        Status = (string)hoursTok["status"] ?? "",
                        NextOpen = (string)hoursTok["nextOpen"],
                        Message = (string)hoursTok["message"],
                    };
                    RenderHoursBanner();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] send parse failed: {ex.Message}");
            }

            _composeField.value = "";
            ClearPendingAttachment();
            RenderMessages();
            // After a send, force-scroll regardless of previous position —
            // user just sent a message, always show it.
            _scroll.schedule.Execute(ScrollToBottom).StartingIn(16);
        }

        static async System.Threading.Tasks.Task PostRead()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            await HttpRequest("POST", baseUrl + "/api/v1/chat/read", apiKey, "{}");
            // Flip local ReadBySdkAt so the Seen indicator updates locally.
            foreach (var m in _messages)
                if (m.Sender == "qa" && string.IsNullOrEmpty(m.ReadBySdkAt))
                    m.ReadBySdkAt = DateTime.UtcNow.ToString("o");
        }

        static async System.Threading.Tasks.Task PostTyping()
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            await HttpRequest("POST", baseUrl + "/api/v1/chat/typing", apiKey, "{}");
        }

        // ─── Hours banner / disabled card ────────────────────────────────

        static void RenderHoursBanner()
        {
            if (_hoursBanner == null) return;
            _hoursBanner.Clear();
            if (_hours == null || !_hours.IsOffHours) return;

            // Off-hours banner uses the neutral info palette (cardBorder
            // background, textSecondary text). Keeps the whole chat surface
            // theme-consistent — no orange "warning" colour competes with the
            // accents used elsewhere.
            var theme = BugpunchTheme.Current;
            var banner = new VisualElement();
            banner.style.flexDirection = FlexDirection.Column;
            banner.style.backgroundColor = theme.cardBorder;
            banner.style.borderTopColor = banner.style.borderBottomColor =
                banner.style.borderLeftColor = banner.style.borderRightColor = theme.cardBorder;
            banner.style.borderTopWidth = banner.style.borderBottomWidth =
                banner.style.borderLeftWidth = banner.style.borderRightWidth = 1;
            banner.style.borderTopLeftRadius = banner.style.borderTopRightRadius =
                banner.style.borderBottomLeftRadius = banner.style.borderBottomRightRadius = 6;
            banner.style.paddingTop = 8; banner.style.paddingBottom = 8;
            banner.style.paddingLeft = 10; banner.style.paddingRight = 10;
            banner.style.marginBottom = 6;

            var msg = !string.IsNullOrEmpty(_hours.Message)
                ? _hours.Message
                : "The team's offline right now — we'll reply when we're back.";
            var msgLabel = new Label(msg);
            msgLabel.style.color = theme.textSecondary;
            msgLabel.style.fontSize = theme.fontSizeCaption;
            msgLabel.style.whiteSpace = WhiteSpace.Normal;
            banner.Add(msgLabel);

            if (!string.IsNullOrEmpty(_hours.NextOpen))
            {
                var next = new Label("Next available: " + FormatAbsoluteTime(_hours.NextOpen));
                next.style.color = theme.textMuted;
                next.style.fontSize = theme.fontSizeCaption - 1;
                next.style.marginTop = 4;
                banner.Add(next);
            }
            _hoursBanner.Add(banner);
        }

        static VisualElement BuildDisabledCard()
        {
            var theme = BugpunchTheme.Current;
            var wrap = new VisualElement();
            wrap.style.position = Position.Absolute;
            wrap.style.left = 0; wrap.style.top = 0; wrap.style.right = 0; wrap.style.bottom = 0;
            wrap.style.backgroundColor = theme.cardBackground;
            wrap.style.flexDirection = FlexDirection.Column;
            wrap.style.justifyContent = Justify.Center;
            wrap.style.alignItems = Align.Center;
            wrap.style.paddingTop = 24; wrap.style.paddingBottom = 24;
            wrap.style.paddingLeft = 24; wrap.style.paddingRight = 24;

            var title = new Label("Chat is unavailable.");
            title.style.color = theme.textPrimary;
            title.style.fontSize = theme.fontSizeTitle - 2;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 14;
            title.style.whiteSpace = WhiteSpace.Normal;
            wrap.Add(title);

            var closeBtn = new Button(Hide) { text = "Close" };
            StylePrimaryButton(closeBtn);
            wrap.Add(closeBtn);
            return wrap;
        }

        // ─── HTTP ────────────────────────────────────────────────────────

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
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();

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

        // ─── Formatting / utility ───────────────────────────────────────

        static bool TryParseIso(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(iso)) return false;
            return DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out utc);
        }

        static string FormatAbsoluteTime(string isoTime)
        {
            if (!TryParseIso(isoTime, out var t)) return isoTime ?? "";
            return t.ToLocalTime().ToString("MMM d, h:mm tt");
        }

        // ─── Data types ──────────────────────────────────────────────────

        internal class ChatMessage
        {
            public string Id;
            public string ThreadId;
            public string Sender;          // "sdk" | "qa"
            public string Body;
            public string CreatedAt;
            public string UserName;
            public string ReadBySdkAt;
            public string ReadByQaAt;
            public List<ChatAttachment> Attachments;

            public static ChatMessage FromJson(JToken t)
            {
                var msg = new ChatMessage
                {
                    Id = (string)t["Id"] ?? (string)t["id"] ?? "",
                    ThreadId = (string)t["ThreadId"] ?? (string)t["threadId"] ?? "",
                    Sender = (string)t["Sender"] ?? (string)t["sender"] ?? "sdk",
                    Body = (string)t["Body"] ?? (string)t["body"] ?? "",
                    CreatedAt = (string)t["CreatedAt"] ?? (string)t["createdAt"] ?? "",
                    UserName = (string)t["UserName"] ?? (string)t["userName"],
                    ReadBySdkAt = (string)t["ReadBySdkAt"] ?? (string)t["readBySdkAt"],
                    ReadByQaAt = (string)t["ReadByQaAt"] ?? (string)t["readByQaAt"],
                };
                var arr = (t["attachments"] ?? t["Attachments"]) as JArray;
                if (arr != null)
                {
                    msg.Attachments = new List<ChatAttachment>(arr.Count);
                    foreach (var a in arr)
                        msg.Attachments.Add(ChatAttachment.FromJson(a));
                }
                return msg;
            }
        }

        internal class ChatAttachment
        {
            public string Type; public string Url; public string Mime;
            public int Width; public int Height;
            public static ChatAttachment FromJson(JToken t)
            {
                return new ChatAttachment
                {
                    Type = (string)t["type"] ?? "image",
                    Url = (string)t["url"] ?? "",
                    Mime = (string)t["mime"] ?? "image/*",
                    Width = (int?)t["width"] ?? 0,
                    Height = (int?)t["height"] ?? 0,
                };
            }
        }

        class PendingAttachment
        {
            public string Url; public string Mime;
            public int Width; public int Height;
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
