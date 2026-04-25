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
    public static partial class BugpunchChatBoard
    {
        // ─── Composer ────────────────────────────────────────────────────

        static void BuildComposer(VisualElement container)
        {
            _attachmentPreview = new VisualElement();
            _attachmentPreview.style.display = DisplayStyle.None;
            _attachmentPreview.style.flexDirection = FlexDirection.Row;
            _attachmentPreview.style.alignItems = Align.Center;
            _attachmentPreview.style.marginBottom = 6;
            _attachmentPreview.style.paddingTop = 6; _attachmentPreview.style.paddingBottom = 6;
            _attachmentPreview.style.paddingLeft = 8; _attachmentPreview.style.paddingRight = 8;
            _attachmentPreview.style.backgroundColor = BugpunchTheme.Current.cardBorder;
            _attachmentPreview.style.borderTopLeftRadius = _attachmentPreview.style.borderTopRightRadius =
                _attachmentPreview.style.borderBottomLeftRadius = _attachmentPreview.style.borderBottomRightRadius = 6;
            container.Add(_attachmentPreview);

            var compose = new VisualElement();
            compose.style.flexDirection = FlexDirection.Row;
            compose.style.alignItems = Align.FlexEnd;

            _composeField = new TextField();
            _composeField.multiline = true;
            _composeField.maxLength = 4000;
            StyleInput(_composeField, "Write a message…", multiline: true);
            _composeField.style.flexGrow = 1;
            _composeField.style.flexShrink = 1;
            _composeField.style.marginBottom = 0;
            _composeField.RegisterValueChangedCallback(e =>
            {
                _lastComposerChangeTime = Time.realtimeSinceStartup;
                UpdateSendEnabled();
            });

            // UI Toolkit TextField focus events — attach to the inner input element.
            var inputEl = _composeField.Q<VisualElement>(className: "unity-text-field__input")
                          ?? _composeField.Q<VisualElement>(className: "unity-base-field__input");
            if (inputEl != null)
            {
                inputEl.RegisterCallback<FocusInEvent>(_ => _composerFocused = true);
                inputEl.RegisterCallback<FocusOutEvent>(_ => _composerFocused = false);
            }
            compose.Add(_composeField);

            // Emoji + attach buttons live on a small stacked column to the
            // right of the composer, keeping the row short on narrow cards.
            _emojiBtn = new Button(ToggleEmojiPopover) { text = "☺" };
            StyleIconButton(_emojiBtn);
            compose.Add(_emojiBtn);

            _attachBtn = new Button(OnAttachTapped) { text = "📎" };
            StyleIconButton(_attachBtn);
            compose.Add(_attachBtn);

            _sendBtn = new Button(() => _ = SendMessage()) { text = "Send" };
            StylePrimaryButton(_sendBtn);
            _sendBtn.SetEnabled(false);
            compose.Add(_sendBtn);

            container.Add(compose);
        }

        static void UpdateSendEnabled()
        {
            bool hasText = !string.IsNullOrWhiteSpace(_composeField?.value);
            bool hasAttach = _pendingAttachment != null;
            _sendBtn?.SetEnabled(hasText || hasAttach);
        }

        // ─── Emoji popover ───────────────────────────────────────────────

        static void ToggleEmojiPopover()
        {
            if (_emojiPopover == null) return;
            if (_emojiPopover.style.display == DisplayStyle.Flex)
            {
                _emojiPopover.style.display = DisplayStyle.None;
                return;
            }
            BuildEmojiPopover(_emojiPopover);
            _emojiPopover.style.display = DisplayStyle.Flex;
        }

        static void BuildEmojiPopover(VisualElement container)
        {
            var theme = BugpunchTheme.Current;
            container.Clear();
            container.style.position = Position.Absolute;
            container.style.right = 10; container.style.bottom = 60;
            container.style.width = 280; container.style.height = 220;
            container.style.backgroundColor = theme.cardBackground;
            container.style.borderTopLeftRadius = container.style.borderTopRightRadius =
                container.style.borderBottomLeftRadius = container.style.borderBottomRightRadius = 8;
            container.style.borderTopColor = container.style.borderBottomColor =
                container.style.borderLeftColor = container.style.borderRightColor = theme.cardBorder;
            container.style.borderTopWidth = container.style.borderBottomWidth =
                container.style.borderLeftWidth = container.style.borderRightWidth = 1;
            container.style.paddingTop = 6; container.style.paddingBottom = 6;
            container.style.paddingLeft = 6; container.style.paddingRight = 6;

            // Outside-tap-to-close: swallow taps on the popover itself, and
            // register a one-shot on the root that closes.
            container.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginBottom = 6;
            container.Add(tabs);

            var grid = new ScrollView(ScrollViewMode.Vertical);
            grid.style.flexGrow = 1;
            grid.style.flexShrink = 1;
            container.Add(grid);

            int selected = 0;
            void RenderGrid(int cat)
            {
                grid.Clear();
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                grid.Add(row);
                var list = BugpunchEmojiData.Categories[cat].Emojis;
                foreach (var e in list)
                {
                    var b = new Button(() => InsertEmoji(e)) { text = e };
                    b.style.fontSize = 18;
                    b.style.width = 32; b.style.height = 32;
                    b.style.paddingTop = 0; b.style.paddingBottom = 0;
                    b.style.paddingLeft = 0; b.style.paddingRight = 0;
                    b.style.marginLeft = 0; b.style.marginRight = 0;
                    b.style.marginTop = 0; b.style.marginBottom = 0;
                    b.style.backgroundColor = Color.clear;
                    b.style.borderTopWidth = b.style.borderBottomWidth =
                        b.style.borderLeftWidth = b.style.borderRightWidth = 0;
                    row.Add(b);
                }
            }

            for (int i = 0; i < BugpunchEmojiData.Categories.Length; i++)
            {
                int idx = i;
                var t = new Button(() => { selected = idx; RenderGrid(idx); }) { text = BugpunchEmojiData.Categories[i].Name };
                t.style.fontSize = 10;
                t.style.paddingTop = 3; t.style.paddingBottom = 3;
                t.style.paddingLeft = 6; t.style.paddingRight = 6;
                t.style.marginLeft = 0; t.style.marginRight = 2; t.style.marginBottom = 2;
                t.style.backgroundColor = theme.cardBorder;
                t.style.color = theme.textPrimary;
                t.style.borderTopWidth = t.style.borderBottomWidth =
                    t.style.borderLeftWidth = t.style.borderRightWidth = 0;
                t.style.borderTopLeftRadius = t.style.borderTopRightRadius =
                    t.style.borderBottomLeftRadius = t.style.borderBottomRightRadius = 4;
                tabs.Add(t);
            }
            RenderGrid(selected);

            // Close on backdrop click (anywhere outside the popover).
            _root.RegisterCallback<PointerDownEvent>(OnRootPointerDownCloseEmoji);
        }

        static void OnRootPointerDownCloseEmoji(PointerDownEvent e)
        {
            if (_emojiPopover == null) return;
            if (_emojiPopover.style.display != DisplayStyle.Flex) return;
            // If the event's target is inside the popover it already stopped
            // propagation; anything reaching the root = click-outside.
            _emojiPopover.style.display = DisplayStyle.None;
            _root.UnregisterCallback<PointerDownEvent>(OnRootPointerDownCloseEmoji);
        }

        static void InsertEmoji(string emoji)
        {
            if (_composeField == null || string.IsNullOrEmpty(emoji)) return;
            // UI Toolkit doesn't expose a caret API — append at end is the
            // simplest cross-version approach and matches most game chats.
            _composeField.value = (_composeField.value ?? "") + emoji;
            _composeField.Focus();
        }

        // ─── Attachments ─────────────────────────────────────────────────

        static void OnAttachTapped()
        {
            // Route to native image picker on mobile; editor/standalone gets
            // a toast — the task brief explicitly allows this as the simplest
            // fallback for v1.
#if UNITY_ANDROID && !UNITY_EDITOR
            BugpunchNative.PickImage(OnNativeImagePicked);
#elif UNITY_IOS && !UNITY_EDITOR
            BugpunchNative.PickImage(OnNativeImagePicked);
#else
            ShowToast("Image attachments are only supported on iOS and Android.");
#endif
        }

        // Called by BugpunchNative.DispatchImagePicked — which itself is
        // invoked from the ReportOverlayCallback MonoBehaviour after the
        // native picker sends its UnitySendMessage("OnImagePicked", path).
        internal static void OnNativeImagePicked(string localPath)
        {
            if (string.IsNullOrEmpty(localPath))
            {
                // user cancelled — no-op
                return;
            }
            _ = UploadAttachment(localPath);
        }

        static async System.Threading.Tasks.Task UploadAttachment(string localPath)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(localPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] Read picked file failed: {ex.Message}");
                return;
            }

            // 5MB client-side guard — server rejects over this too, but
            // showing the toast before the upload saves bandwidth.
            if (bytes.Length > 5 * 1024 * 1024)
            {
                ShowToast("Image is over 5MB — pick a smaller one.");
                return;
            }

            // Multipart: single "file" part. Mime derived from extension.
            var mime = GuessMime(localPath);
            var filename = Path.GetFileName(localPath);
            if (string.IsNullOrEmpty(filename)) filename = "upload";

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", bytes, filename, mime)
            };

            using var req = UnityWebRequest.Post(baseUrl + "/api/v1/chat/attachments", form);
            req.SetRequestHeader("Authorization", "Bearer " + (apiKey ?? ""));
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 30;

            // Show uploading state
            _attachBtn.SetEnabled(false);
            _attachBtn.text = "…";

            var op = req.SendWebRequest();
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();

            _attachBtn.SetEnabled(true);
            _attachBtn.text = "📎";

            bool ok = req.result == UnityWebRequest.Result.Success
                      && req.responseCode >= 200 && req.responseCode < 300;
            if (!ok)
            {
                ShowToast($"Upload failed ({req.responseCode}).");
                return;
            }

            try
            {
                var obj = JObject.Parse(req.downloadHandler?.text ?? "{}");
                _pendingAttachment = new PendingAttachment
                {
                    Url = (string)obj["url"],
                    Mime = (string)obj["mime"],
                    Width = (int?)obj["width"] ?? 0,
                    Height = (int?)obj["height"] ?? 0,
                };
                RenderAttachmentPreview();
                UpdateSendEnabled();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.ChatBoard] upload parse failed: {ex.Message}");
            }
        }

        static void RenderAttachmentPreview()
        {
            if (_attachmentPreview == null) return;
            _attachmentPreview.Clear();
            if (_pendingAttachment == null || string.IsNullOrEmpty(_pendingAttachment.Url))
            {
                _attachmentPreview.style.display = DisplayStyle.None;
                return;
            }

            _attachmentPreview.style.display = DisplayStyle.Flex;

            var thumb = new Image();
            thumb.scaleMode = ScaleMode.ScaleToFit;
            thumb.style.width = 48; thumb.style.height = 48;
            thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 4;
            thumb.style.marginRight = 10;

            var url = _pendingAttachment.Url;
            if (_imageCache.TryGetValue(url, out var cached) && cached != null)
                thumb.image = cached;
            else
                _ = LoadImageAsync(url, tex => { if (tex != null) { _imageCache[url] = tex; thumb.image = tex; } });

            _attachmentPreview.Add(thumb);

            var themeA = BugpunchTheme.Current;
            var label = new Label("Image attached");
            label.style.color = themeA.textSecondary;
            label.style.fontSize = themeA.fontSizeCaption;
            label.style.flexGrow = 1;
            _attachmentPreview.Add(label);

            var x = new Button(ClearPendingAttachment) { text = "×" };
            x.style.backgroundColor = Color.clear;
            x.style.color = themeA.textSecondary;
            x.style.fontSize = 18;
            x.style.unityFontStyleAndWeight = FontStyle.Bold;
            x.style.paddingTop = 0; x.style.paddingBottom = 0;
            x.style.paddingLeft = 6; x.style.paddingRight = 6;
            x.style.borderTopWidth = x.style.borderBottomWidth =
                x.style.borderLeftWidth = x.style.borderRightWidth = 0;
            _attachmentPreview.Add(x);
        }

        static void ClearPendingAttachment()
        {
            _pendingAttachment = null;
            RenderAttachmentPreview();
            UpdateSendEnabled();
        }

        static string GuessMime(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg",
            };
        }

        // ─── Toast ───────────────────────────────────────────────────────

        static void ShowToast(string text)
        {
            if (_root == null) return;
            var theme = BugpunchTheme.Current;
            var toast = new Label(text);
            toast.style.position = Position.Absolute;
            toast.style.bottom = 20; toast.style.left = 20; toast.style.right = 20;
            toast.style.backgroundColor = theme.cardBorder;
            toast.style.color = theme.textPrimary;
            toast.style.fontSize = theme.fontSizeBody - 1;
            toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            toast.style.paddingTop = 10; toast.style.paddingBottom = 10;
            toast.style.paddingLeft = 14; toast.style.paddingRight = 14;
            toast.style.borderTopLeftRadius = toast.style.borderTopRightRadius =
                toast.style.borderBottomLeftRadius = toast.style.borderBottomRightRadius = 8;
            toast.pickingMode = PickingMode.Ignore;
            _root.Add(toast);
            _root.schedule.Execute(() => { if (toast.parent != null) toast.RemoveFromHierarchy(); }).StartingIn(2500);
        }
    }
}
