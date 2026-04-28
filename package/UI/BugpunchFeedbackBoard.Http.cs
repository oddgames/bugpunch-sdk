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
                BugpunchLog.Warn("FeedbackBoard", $"Parse failed: {ex.Message}");
                _listEmptyLabel.text = "Could not parse feedback list.";
                return;
            }

            RefreshList();
        }

        static async System.Threading.Tasks.Task CreateFeedback(string title, string description, bool bypassSimilarity, bool closeOnSuccess)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var url = baseUrl + "/api/issues/ingest";

            // Build payload manually — we need a JSON array for `attachments`
            // alongside the existing string fields, which the simple
            // Dictionary<string,string> BuildJson can't express.
            var sb = new StringBuilder("{");
            sb.Append("\"type\":\"feedback_item\",");
            sb.Append("\"title\":").Append(JsonConvert.ToString(title ?? "")).Append(',');
            sb.Append("\"description\":").Append(JsonConvert.ToString(description ?? ""));
            if (bypassSimilarity) sb.Append(",\"bypassSimilarity\":true");
            sb.Append(",\"attachments\":").Append(SerializeAttachmentsArray(_submitDraftAttachments));
            sb.Append('}');
            var body = sb.ToString();

            var (ok, status, _) = await HttpRequest("POST", url, apiKey, body);
            if (!ok)
            {
                BugpunchLog.Warn("FeedbackBoard", $"Create failed (HTTP {status}).");
                // Surface failure inline — lightweight toast on the card.
                ShowInlineError("Could not submit feedback. Please try again.");
                return;
            }

            _submitDraftAttachments.Clear();
            RenderAttachmentPreview(isComment: false);

            // Refresh and return to list.
            await FetchList();
            if (closeOnSuccess) SwitchTo(View.List);
        }

        /// <summary>
        /// Serialize a list of <see cref="PendingAttachment"/> into the JSON
        /// array shape the server expects on POST /api/issues/ingest and POST
        /// /api/feedback/:id/comments — matches the FeedbackAttachment type
        /// declared in <c>feedback.routes.ts</c>.
        /// </summary>
        static string SerializeAttachmentsArray(List<PendingAttachment> list)
        {
            if (list == null || list.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var a = list[i];
                sb.Append('{');
                sb.Append("\"type\":\"image\"");
                sb.Append(",\"url\":").Append(JsonConvert.ToString(a.url ?? ""));
                sb.Append(",\"mime\":").Append(JsonConvert.ToString(a.mime ?? "image/*"));
                if (a.width > 0) sb.Append(",\"width\":").Append(a.width);
                if (a.height > 0) sb.Append(",\"height\":").Append(a.height);
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
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
                var theme = BugpunchTheme.Current;
                arrow.style.color = hasMyVote ? theme.textPrimary : theme.textSecondary;
                count.style.color = hasMyVote ? theme.textPrimary : theme.textSecondary;
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("FeedbackBoard", $"Vote parse failed: {ex.Message}");
            }
        }

        static async System.Threading.Tasks.Task VoteFor(string itemId)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(itemId) + "/vote";
            var payload = BuildVotePayload();
            await HttpRequest("POST", url, apiKey, payload);
            await FetchList();
            SwitchTo(View.List);
        }

        /// <summary>
        /// Build the vote-request JSON, including the player's saved email if
        /// they've opted in. Empty email is omitted so the server doesn't get
        /// confused by "".
        /// </summary>
        static string BuildVotePayload()
        {
            var email = PlayerPrefs.GetString(PREF_EMAIL_VALUE, "");
            if (string.IsNullOrEmpty(email)) return "{}";
            var fields = new Dictionary<string, string> { ["email"] = email };
            return BuildJson(fields);
        }

        static async System.Threading.Tasks.Task FetchComments(string itemId)
        {
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;
            // Guard against race where the user nav'd back to list before
            // the response arrived.
            var snapshot = _detailItem;
            var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(itemId) + "/comments";
            var (ok, _, body) = await HttpRequest("GET", url, apiKey, null);
            if (!ok || _detailItem != snapshot) return;

            _commentsList.Clear();

            List<CommentItem> parsed = new();
            try
            {
                var obj = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var arr = obj["comments"] as JArray;
                if (arr != null)
                    foreach (var t in arr) parsed.Add(CommentItem.FromJson(t));
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("FeedbackBoard", $"Comments parse failed: {ex.Message}");
            }

            if (parsed.Count == 0)
            {
                var theme = BugpunchTheme.Current;
                _commentsEmptyLabel = new Label("No comments yet. Be the first to reply.");
                _commentsEmptyLabel.style.color = theme.textMuted;
                _commentsEmptyLabel.style.fontSize = theme.fontSizeCaption;
                _commentsEmptyLabel.style.marginTop = 4; _commentsEmptyLabel.style.marginBottom = 8;
                _commentsList.Add(_commentsEmptyLabel);
                return;
            }

            foreach (var c in parsed) _commentsList.Add(CreateCommentRow(c));
        }

        static async System.Threading.Tasks.Task PostComment()
        {
            if (_detailItem == null) return;
            if (!TryGetBaseUrl(out var baseUrl, out var apiKey)) return;

            var draft = _commentDraftField.value?.Trim() ?? "";
            // Allow image-only comments — the server accepts an empty body
            // when there's at least one attachment. Matches the dashboard.
            if (string.IsNullOrEmpty(draft) && _commentDraftAttachments.Count == 0) return;

            // Persist the email inline — if the player typed one here without
            // going through the upvote prompt, we still want to remember it.
            var email = (_commentEmailField.value ?? "").Trim();
            if (!string.IsNullOrEmpty(email))
            {
                PlayerPrefs.SetString(PREF_EMAIL_VALUE, email);
                PlayerPrefs.Save();
            }

            _commentPostButton.SetEnabled(false);
            _commentPostButton.text = "Posting…";

            try
            {
                var url = baseUrl + "/api/feedback/" + UnityWebRequest.EscapeURL(_detailItem.id) + "/comments";

                var sb = new StringBuilder("{");
                sb.Append("\"body\":").Append(JsonConvert.ToString(draft));
                sb.Append(",\"playerName\":\"\"");
                sb.Append(",\"attachments\":").Append(SerializeAttachmentsArray(_commentDraftAttachments));
                sb.Append('}');
                var payload = sb.ToString();

                var (ok, status, _) = await HttpRequest("POST", url, apiKey, payload);
                if (!ok)
                {
                    BugpunchLog.Warn("FeedbackBoard", $"Comment POST failed (HTTP {status}).");
                    ShowInlineError("Could not post comment. Please try again.");
                    return;
                }

                _commentDraftField.value = "";
                _commentDraftAttachments.Clear();
                RenderAttachmentPreview(isComment: true);
                await FetchComments(_detailItem.id);
            }
            finally
            {
                _commentPostButton.text = "Post";
                _commentPostButton.SetEnabled(
                    !string.IsNullOrWhiteSpace(_commentDraftField.value) || _commentDraftAttachments.Count > 0);
            }
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
                BugpunchLog.Warn("FeedbackBoard", "BugpunchClient not initialized — feedback unavailable.");
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
    }
}
