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
        // ─── Image attachment rendering ──────────────────────────────────

        /// <summary>
        /// Render an inline image under a body / comment. Width capped at
        /// 240 px; tapping opens the full-size image via
        /// <see cref="Application.OpenURL"/> so the OS handles the zoom UI.
        /// </summary>
        static VisualElement CreateImageAttachment(AttachmentInfo att)
        {
            var img = new Image();
            img.scaleMode = ScaleMode.ScaleToFit;
            img.style.marginTop = 4;
            img.style.marginBottom = 4;

            float cap = 240f;
            float w = att.width > 0 ? att.width : cap;
            float h = att.height > 0 ? att.height : cap;
            float aspect = (w > 0 && h > 0) ? (h / w) : 0.75f;
            img.style.width = cap;
            img.style.height = cap * aspect;
            img.style.borderTopLeftRadius = img.style.borderTopRightRadius =
                img.style.borderBottomLeftRadius = img.style.borderBottomRightRadius = 8;

            img.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!string.IsNullOrEmpty(att.url)) Application.OpenURL(att.url);
                e.StopPropagation();
            });

            if (_imageCache.TryGetValue(att.url, out var cached) && cached != null)
            {
                img.image = cached;
            }
            else
            {
                _ = LoadImageAsync(att.url, tex =>
                {
                    if (tex != null)
                    {
                        _imageCache[att.url] = tex;
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
            // The /api/files route is auth-gated; the API key is our one
            // cross-surface credential so reuse it here too.
            if (TryGetBaseUrl(out _, out var apiKey) && !string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
            {
                BugpunchLog.Warn("FeedbackBoard", $"image download failed: {req.error}");
                onDone?.Invoke(null);
                return;
            }
            var tex = DownloadHandlerTexture.GetContent(req);
            onDone?.Invoke(tex);
        }

        // ─── Attachment picker + upload ──────────────────────────────────

        static void OnAttachTapped(bool isComment)
        {
            // Only iOS and Android have native image pickers hooked up (see
            // BugpunchImagePicker.java / BugpunchImagePicker.mm). On other
            // platforms we tell the player inline so they aren't left
            // wondering why the button did nothing.
#if UNITY_ANDROID && !UNITY_EDITOR
            BugpunchNative.PickImage(path => OnNativeImagePicked(path, isComment));
#elif UNITY_IOS && !UNITY_EDITOR
            BugpunchNative.PickImage(path => OnNativeImagePicked(path, isComment));
#else
            ShowInlineError("Image attachments are only supported on iOS and Android.");
#endif
        }

        static void OnNativeImagePicked(string localPath, bool isComment)
        {
            if (string.IsNullOrEmpty(localPath)) return; // user cancelled
            _ = UploadDraftAttachment(localPath, isComment);
        }

        static async System.Threading.Tasks.Task UploadDraftAttachment(string localPath, bool isComment)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(localPath); }
            catch (Exception ex)
            {
                BugpunchLog.Warn("FeedbackBoard", $"Read picked file failed: {ex.Message}");
                return;
            }
            if (bytes.Length > 5 * 1024 * 1024)
            {
                ShowInlineError("Image is over 5MB — pick a smaller one.");
                return;
            }

            var mime = GuessMime(localPath);
            var filename = Path.GetFileName(localPath);
            if (string.IsNullOrEmpty(filename)) filename = "upload";

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", bytes, filename, mime),
            };

            using var req = UnityWebRequest.Post(baseUrl + "/api/feedback/attachments", form);
            req.SetRequestHeader("Authorization", "Bearer " + (apiKey ?? ""));
            req.SetRequestHeader("X-Device-Id", DeviceIdentity.GetDeviceId() ?? "");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 30;

            var btn = isComment ? _commentAttachButton : _submitAttachButton;
            if (btn != null) { btn.SetEnabled(false); btn.text = "…"; }

            var op = req.SendWebRequest();
            while (!op.isDone) await System.Threading.Tasks.Task.Yield();

            if (btn != null) { btn.SetEnabled(true); btn.text = "📎"; }

            bool ok = req.result == UnityWebRequest.Result.Success
                      && req.responseCode >= 200 && req.responseCode < 300;
            if (!ok)
            {
                ShowInlineError($"Upload failed ({req.responseCode}).");
                return;
            }

            PendingAttachment att;
            try
            {
                var obj = JObject.Parse(req.downloadHandler?.text ?? "{}");
                att = new PendingAttachment
                {
                    url = (string)obj["url"],
                    mime = (string)obj["mime"],
                    width = (int?)obj["width"] ?? 0,
                    height = (int?)obj["height"] ?? 0,
                };
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("FeedbackBoard", $"upload parse failed: {ex.Message}");
                return;
            }
            if (string.IsNullOrEmpty(att.url)) return;

            var list = isComment ? _commentDraftAttachments : _submitDraftAttachments;
            list.Add(att);
            RenderAttachmentPreview(isComment);
            if (isComment)
            {
                _commentPostButton?.SetEnabled(
                    !string.IsNullOrWhiteSpace(_commentDraftField?.value) || _commentDraftAttachments.Count > 0);
            }
        }

        static void RenderAttachmentPreview(bool isComment)
        {
            var holder = isComment ? _commentAttachmentPreview : _submitAttachmentPreview;
            var list = isComment ? _commentDraftAttachments : _submitDraftAttachments;
            if (holder == null) return;

            holder.Clear();
            if (list.Count == 0) { holder.style.display = DisplayStyle.None; return; }
            holder.style.display = DisplayStyle.Flex;

            foreach (var a in list)
            {
                var cell = new VisualElement();
                cell.style.position = Position.Relative;
                cell.style.marginRight = 6;
                cell.style.marginBottom = 6;

                var thumb = new Image();
                thumb.scaleMode = ScaleMode.ScaleToFit;
                thumb.style.width = 48; thumb.style.height = 48;
                thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                    thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 4;

                if (_imageCache.TryGetValue(a.url, out var cached) && cached != null)
                    thumb.image = cached;
                else
                    _ = LoadImageAsync(a.url, tex => { if (tex != null) { _imageCache[a.url] = tex; thumb.image = tex; } });

                cell.Add(thumb);

                var removeBtn = new Button(() =>
                {
                    list.Remove(a);
                    RenderAttachmentPreview(isComment);
                    if (isComment)
                    {
                        _commentPostButton?.SetEnabled(
                            !string.IsNullOrWhiteSpace(_commentDraftField?.value) || _commentDraftAttachments.Count > 0);
                    }
                })
                { text = "×" };
                removeBtn.style.position = Position.Absolute;
                removeBtn.style.top = -4; removeBtn.style.right = -4;
                removeBtn.style.width = 16; removeBtn.style.height = 16;
                removeBtn.style.paddingTop = 0; removeBtn.style.paddingBottom = 0;
                removeBtn.style.paddingLeft = 0; removeBtn.style.paddingRight = 0;
                removeBtn.style.fontSize = 11;
                removeBtn.style.backgroundColor = BugpunchTheme.Current.cardBackground;
                removeBtn.style.color = BugpunchTheme.Current.textPrimary;
                removeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                removeBtn.style.borderTopLeftRadius = removeBtn.style.borderTopRightRadius =
                    removeBtn.style.borderBottomLeftRadius = removeBtn.style.borderBottomRightRadius = 8;
                removeBtn.style.borderTopWidth = removeBtn.style.borderBottomWidth =
                    removeBtn.style.borderLeftWidth = removeBtn.style.borderRightWidth = 0;
                removeBtn.style.marginLeft = 0; removeBtn.style.marginRight = 0;
                cell.Add(removeBtn);

                holder.Add(cell);
            }
        }

        static string GuessMime(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
            switch (ext)
            {
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".webp": return "image/webp";
                default:      return "image/jpeg";
            }
        }
    }
}
