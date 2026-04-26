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
        static void AppendTypingIndicator()
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.justifyContent = Justify.FlexStart;
            wrapper.style.marginTop = 2; wrapper.style.marginBottom = 4;
            wrapper.style.alignItems = Align.FlexEnd;

            // Avatar slot (small dot) — keep layout consistent with QA bubble runs.
            var avatarSlot = new VisualElement();
            avatarSlot.style.width = 26; avatarSlot.style.height = 26;
            avatarSlot.style.marginRight = 6;
            wrapper.Add(avatarSlot);

            var theme = BugpunchTheme.Current;
            var bubble = new VisualElement();
            bubble.style.flexDirection = FlexDirection.Row;
            // QA typing bubble = QA bubble colour (cardBorder).
            bubble.style.backgroundColor = theme.cardBorder;
            bubble.style.borderTopLeftRadius = bubble.style.borderTopRightRadius =
                bubble.style.borderBottomLeftRadius = bubble.style.borderBottomRightRadius = theme.cardRadius;
            bubble.style.paddingTop = 8; bubble.style.paddingBottom = 8;
            bubble.style.paddingLeft = 12; bubble.style.paddingRight = 12;

            for (int i = 0; i < 3; i++)
            {
                var dot = new VisualElement();
                dot.style.width = 6; dot.style.height = 6;
                dot.style.backgroundColor = theme.textSecondary;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                    dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 3;
                dot.style.marginLeft = 2; dot.style.marginRight = 2;
                bubble.Add(dot);
            }
            // Animate opacity on the dots so it feels like typing. UI Toolkit
            // doesn't have keyframe animations, so rotate a single "bright dot"
            // index every 400ms via the scheduler.
            var children = new List<VisualElement>();
            foreach (var c in bubble.Children()) children.Add(c);
            int tick = 0;
            var bright = theme.textPrimary;
            var dim = theme.textMuted;
            bubble.schedule.Execute(() =>
            {
                tick++;
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    c.style.backgroundColor = (i == tick % children.Count) ? bright : dim;
                }
            }).Every(400);

            wrapper.Add(bubble);
            _messagesContainer.Add(wrapper);
            _qaTypingBubble = wrapper;
        }

        static bool ShouldCollapseHeader(ChatMessage curr, ChatMessage prev)
        {
            if (prev == null) return false;
            if (!WithinRun(prev, curr)) return false;
            return IsSdk(prev) == IsSdk(curr);
        }

        static bool WithinRun(ChatMessage a, ChatMessage b)
        {
            if (!TryParseIso(a.CreatedAt, out var ta) || !TryParseIso(b.CreatedAt, out var tb)) return false;
            return Math.Abs((tb - ta).TotalMinutes) <= 2.0;
        }

        static bool IsSdk(ChatMessage m) => m != null && m.Sender == "sdk";

        static VisualElement CreateMessageBubble(ChatMessage m, bool collapseHeader, bool showAvatar)
        {
            bool isPlayer = IsSdk(m);

            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.justifyContent = isPlayer ? Justify.FlexEnd : Justify.FlexStart;
            wrapper.style.alignItems = Align.FlexEnd;
            wrapper.style.marginBottom = collapseHeader ? 2 : 6;

            // Avatar — only rendered for QA first-in-run. SDK bubbles leave
            // the slot empty; no avatar for "you".
            if (!isPlayer)
            {
                var avatar = new VisualElement();
                avatar.style.width = 26; avatar.style.height = 26;
                avatar.style.marginRight = 6;
                if (showAvatar)
                {
                    var initials = new Label(InitialsOf(m.UserName));
                    initials.style.color = BugpunchTheme.Current.textPrimary;
                    initials.style.fontSize = 11;
                    initials.style.unityFontStyleAndWeight = FontStyle.Bold;
                    initials.style.unityTextAlign = TextAnchor.MiddleCenter;
                    initials.style.width = 26; initials.style.height = 26;
                    avatar.style.backgroundColor = ColorFromSeed(m.UserName ?? "QA");
                    avatar.style.borderTopLeftRadius = avatar.style.borderTopRightRadius =
                        avatar.style.borderBottomLeftRadius = avatar.style.borderBottomRightRadius = 13;
                    avatar.Add(initials);
                }
                wrapper.Add(avatar);
            }

            var bubbleCol = new VisualElement();
            bubbleCol.style.flexDirection = FlexDirection.Column;
            bubbleCol.style.alignItems = isPlayer ? Align.FlexEnd : Align.FlexStart;
            bubbleCol.style.maxWidth = Length.Percent(78);

            var theme = BugpunchTheme.Current;

            // Author line (QA run starts): shown only on first bubble of a run
            if (!isPlayer && !collapseHeader && !string.IsNullOrEmpty(m.UserName))
            {
                var name = new Label(m.UserName);
                name.style.color = theme.accentChat;
                name.style.fontSize = 11;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.marginBottom = 2;
                bubbleCol.Add(name);
            }

            var bubble = new VisualElement();
            // Player bubble = primary accent (matches dashboard brand colour);
            // QA bubble = neutral cardBorder so messages-from-them read as
            // "system / other" without another accent competing.
            bubble.style.backgroundColor = isPlayer ? theme.accentPrimary : theme.cardBorder;
            bubble.style.borderTopLeftRadius = bubble.style.borderTopRightRadius =
                bubble.style.borderBottomLeftRadius = bubble.style.borderBottomRightRadius = theme.cardRadius;
            bubble.style.paddingTop = 8; bubble.style.paddingBottom = 8;
            bubble.style.paddingLeft = 12; bubble.style.paddingRight = 12;
            bubble.style.flexShrink = 1;

            // Attachments (images) — render first so they sit above any body text.
            if (m.Attachments != null)
            {
                foreach (var att in m.Attachments)
                {
                    if (att == null || string.IsNullOrEmpty(att.Url)) continue;
                    if (att.Type == "image")
                        bubble.Add(CreateImageAttachment(att));
                }
            }

            if (!string.IsNullOrEmpty(m.Body))
                bubble.Add(CreateBodyWithLinks(m.Body));

            // Tap-to-toggle timestamp. UI Toolkit hover isn't reliable on touch
            // so we just flip a boolean on click — parity across editor/mobile.
            var id = string.IsNullOrEmpty(m.Id) ? ("idx:" + Guid.NewGuid()) : m.Id;
            bubble.RegisterCallback<PointerDownEvent>(e =>
            {
                // Ignore pointer-downs that land on the inline <Image> — user
                // is probably tapping to open the image.
                if (e.target is Image) return;
                if (_revealedTimestamps.Contains(id)) _revealedTimestamps.Remove(id);
                else _revealedTimestamps.Add(id);
                RenderMessages();
                e.StopPropagation();
            });

            bubbleCol.Add(bubble);

            if (_revealedTimestamps.Contains(id))
            {
                var meta = new Label(BuildMetaLine(m, isPlayer));
                var fg = BugpunchTheme.Current.textPrimary;
                meta.style.color = new Color(fg.r, fg.g, fg.b, 0.55f);
                meta.style.fontSize = 10;
                meta.style.marginTop = 2;
                meta.style.unityTextAlign = isPlayer ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                bubbleCol.Add(meta);
            }

            wrapper.Add(bubbleCol);
            return wrapper;
        }

        static string BuildMetaLine(ChatMessage m, bool isPlayer)
        {
            var time = FormatAbsoluteTime(m.CreatedAt);
            if (!isPlayer) return time;
            // SDK-authored messages show a read indicator on the right side.
            var readAt = m.ReadByQaAt;
            var seen = !string.IsNullOrEmpty(readAt) ? " · Seen" : " · Sent";
            return time + seen;
        }

        static VisualElement CreateBodyWithLinks(string body)
        {
            // Split body into runs around URL matches; each URL becomes a
            // Label with a click callback. This is inside a horizontally
            // wrapping flex container so long messages still wrap cleanly.
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;
            container.style.alignItems = Align.FlexEnd;

            var matches = UrlRegex.Matches(body ?? "");
            if (matches.Count == 0)
            {
                var theme = BugpunchTheme.Current;
                var plain = new Label(body ?? "");
                plain.style.color = theme.textPrimary;
                plain.style.fontSize = theme.fontSizeBody - 1;
                plain.style.whiteSpace = WhiteSpace.Normal;
                return plain;
            }

            int pos = 0;
            foreach (Match mt in matches)
            {
                if (mt.Index > pos)
                    container.Add(MakeRunLabel(body.Substring(pos, mt.Index - pos), false));
                container.Add(MakeRunLabel(mt.Value, true));
                pos = mt.Index + mt.Length;
            }
            if (pos < body.Length)
                container.Add(MakeRunLabel(body.Substring(pos), false));

            return container;
        }

        static Label MakeRunLabel(string text, bool isLink)
        {
            var theme = BugpunchTheme.Current;
            var lab = new Label(text);
            lab.style.color = isLink ? theme.accentChat : theme.textPrimary;
            lab.style.fontSize = theme.fontSizeBody - 1; // 13px body-in-bubble
            lab.style.whiteSpace = WhiteSpace.Normal;
            if (isLink)
            {
                lab.style.unityFontStyleAndWeight = FontStyle.Bold;
                lab.RegisterCallback<PointerDownEvent>(e =>
                {
                    Application.OpenURL(text);
                    e.StopPropagation();
                });
            }
            return lab;
        }

        static VisualElement CreateImageAttachment(ChatAttachment att)
        {
            var img = new Image();
            img.scaleMode = ScaleMode.ScaleToFit;
            img.style.marginBottom = 4;

            // Preserve aspect ratio using known dimensions; fall back to a
            // square placeholder sized to the 240px cap until we know the
            // real texture size.
            float cap = 240f;
            float w = att.Width > 0 ? att.Width : cap;
            float h = att.Height > 0 ? att.Height : cap;
            float aspect = (w > 0 && h > 0) ? (h / w) : 0.75f;
            img.style.width = cap;
            img.style.height = cap * aspect;
            img.style.borderTopLeftRadius = img.style.borderTopRightRadius =
                img.style.borderBottomLeftRadius = img.style.borderBottomRightRadius = 8;

            // Tap → open full-size in the system browser / Photos.
            img.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!string.IsNullOrEmpty(att.Url)) Application.OpenURL(att.Url);
                e.StopPropagation();
            });

            // Cached? Use immediately. Otherwise kick off a download; cache
            // on completion and set the image once the texture arrives.
            if (_imageCache.TryGetValue(att.Url, out var cached) && cached != null)
            {
                img.image = cached;
            }
            else
            {
                _ = LoadImageAsync(att.Url, tex =>
                {
                    if (tex != null)
                    {
                        _imageCache[att.Url] = tex;
                        img.image = tex;
                    }
                });
            }

            return img;
        }

        static async System.Threading.Tasks.Task LoadImageAsync(string url, Action<Texture2D> onDone)
        {
            if (string.IsNullOrEmpty(url)) { onDone?.Invoke(null); return; }
            using var req = UnityWebRequestTexture.GetTexture(url);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
            {
                BugpunchLog.Warn("ChatBoard", $"image download failed: {req.error}");
                onDone?.Invoke(null);
                return;
            }
            var tex = DownloadHandlerTexture.GetContent(req);
            onDone?.Invoke(tex);
        }

        static string InitialsOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "QA";
            var parts = name.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "QA";
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return ("" + parts[0][0] + parts[1][0]).ToUpperInvariant();
        }

        static Color ColorFromSeed(string seed)
        {
            if (string.IsNullOrEmpty(seed)) seed = "QA";
            unchecked
            {
                int h = 17;
                foreach (var c in seed) h = h * 31 + c;
                float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
                return Color.HSVToRGB(hue, 0.55f, 0.70f);
            }
        }
    }
}
