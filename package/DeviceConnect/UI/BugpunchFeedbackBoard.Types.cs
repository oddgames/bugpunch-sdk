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
            public List<AttachmentInfo> attachments;

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
                    attachments = AttachmentInfo.ListFromJson(t["attachments"] as JArray),
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

        class CommentItem
        {
            public string id;
            public string authorKind;
            public string authorName;
            public bool authorIsStaff;
            public string body;
            public string createdAt;
            public bool deleted;
            public List<AttachmentInfo> attachments;

            public static CommentItem FromJson(JToken t)
            {
                return new CommentItem
                {
                    id = (string)t["id"] ?? "",
                    authorKind = (string)t["authorKind"] ?? "sdk",
                    authorName = (string)t["authorName"] ?? "",
                    authorIsStaff = (bool?)t["authorIsStaff"] ?? false,
                    body = (string)t["body"] ?? "",
                    createdAt = (string)t["createdAt"] ?? "",
                    deleted = (string)t["deletedAt"] != null,
                    attachments = AttachmentInfo.ListFromJson(t["attachments"] as JArray),
                };
            }
        }

        /// <summary>
        /// Shape returned by /api/feedback/attachments and stored on items
        /// and comments. Mirror of the server's FeedbackAttachment type —
        /// just enough fields for rendering an inline image.
        /// </summary>
        class AttachmentInfo
        {
            public string type;
            public string url;
            public string mime;
            public int width;
            public int height;

            public static AttachmentInfo FromJson(JToken t)
            {
                return new AttachmentInfo
                {
                    type = (string)t["type"] ?? "image",
                    url = (string)t["url"] ?? "",
                    mime = (string)t["mime"] ?? "image/*",
                    width = (int?)t["width"] ?? 0,
                    height = (int?)t["height"] ?? 0,
                };
            }

            public static List<AttachmentInfo> ListFromJson(JArray arr)
            {
                var list = new List<AttachmentInfo>();
                if (arr == null) return list;
                foreach (var t in arr)
                {
                    var a = FromJson(t);
                    if (!string.IsNullOrEmpty(a.url)) list.Add(a);
                }
                return list;
            }
        }

        class PendingAttachment
        {
            public string url;
            public string mime;
            public int width;
            public int height;
        }
    }
}
