using ODDGames.Bugpunch.RemoteIDE;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="BugpunchConfig"/>. Draws the standard
    /// field editor for every config option, plus a live theme preview that
    /// mirrors the real on-device RequestHelp picker — same layout, same
    /// icons, same rounded corners, same runtime font — so designers see
    /// colour / size / icon tweaks applied to a faithful mock without
    /// leaving the Inspector.
    /// </summary>
    [CustomEditor(typeof(BugpunchConfig))]
    public class BugpunchConfigEditor : UnityEditor.Editor
    {
        // Cache Unity's built-in runtime font so the preview text matches what
        // ships on-device instead of the Editor's chrome font. LegacyRuntime is
        // the default UGUI / fallback UI Toolkit font; if it's ever pruned
        // from the builtin set we fall back to Arial via OS-font creation.
        static Font s_previewFont;
        static Font PreviewFont
        {
            get
            {
                if (s_previewFont != null) return s_previewFont;
                s_previewFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_previewFont == null)
                    s_previewFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Helvetica Neue", "Helvetica", "Arial", "Liberation Sans" }, 14);
                return s_previewFont;
            }
        }

        // 12×12 transparency checkerboard tiled behind the modal so the
        // backdrop colour's alpha is actually visible — without something
        // colourful behind, a 60%-black overlay just looks solid black and
        // reads as a confusing "bar". Cached + reused.
        static Texture2D s_checker;
        static Texture2D Checkerboard
        {
            get
            {
                if (s_checker != null) return s_checker;
                const int size = 12;
                const int cell = 6;
                s_checker = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "BugpunchPreviewChecker",
                };
                var a = new Color(0.36f, 0.36f, 0.38f, 1f);
                var b = new Color(0.48f, 0.48f, 0.50f, 1f);
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                        s_checker.SetPixel(x, y, ((x / cell + y / cell) & 1) == 0 ? a : b);
                s_checker.Apply();
                return s_checker;
            }
        }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (BugpunchConfig)target;
            var theme = config?.Theme;
            if (theme == null) return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Theme Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Live preview of the RequestHelp picker. Icons, colours, and sizes update as you edit. " +
                "Leave the icon slots empty to use the packaged Resources art.",
                MessageType.None);
            DrawThemePreview(theme);
        }

        // ─── Preview ────────────────────────────────────────────────────

        static void DrawThemePreview(BugpunchTheme t)
        {
            // Compute the real content height up front so the preview fits
            // tight — no leftover backdrop "bars" above or below when the
            // card is shorter than a fixed reservation.
            const float pad = 6f;                // thin backdrop frame
            const float cardPadV = 18f;          // card vertical inner padding
            const float titleBlockH = 22f;       // title row
            const float subtitleBlockH = 22f;    // subtitle row
            const float headerGap = 14f;         // space before picker row
            const float pickerH = 200f;
            const float cancelGap = 14f;         // picker → cancel button
            const float cancelH = 22f;           // cancel text link
            const float readoutGap = 10f;        // cancel → design-time readout
            const float readoutH = 16f;          // debug info line

            float contentH = cardPadV + titleBlockH + 2 + subtitleBlockH + headerGap
                           + pickerH + cancelGap + cancelH + readoutGap + readoutH + cardPadV;
            float hostH = contentH + pad * 2;

            var host = GUILayoutUtility.GetRect(1, hostH, GUILayout.ExpandWidth(true));

            // Tile a checkerboard to stand in for "the game" behind the modal.
            // Without this, the alpha-blended backdrop just looks solid black
            // (since the Inspector background is already dark) and reads as
            // an unexplained bar. The checkerboard makes the backdrop's
            // tint-and-translucency obvious.
            var checker = Checkerboard;
            if (checker != null)
            {
                GUI.DrawTextureWithTexCoords(host, checker,
                    new Rect(0, 0, host.width / checker.width, host.height / checker.height));
            }
            // Backdrop overlay — dims the checkerboard. Same value native uses.
            EditorGUI.DrawRect(host, t.backdrop);

            // Outer card — rounded corners + 1px border.
            var card = new Rect(host.x + pad, host.y + pad,
                host.width - pad * 2, host.height - pad * 2);
            FillRoundedRect(card, t.cardRadius, t.cardBackground);
            StrokeRoundedRect(card, t.cardRadius, t.cardBorder, 1f);

            // Title + subtitle inside the card.
            var inner = new Rect(card.x + 20, card.y + cardPadV, card.width - 40, card.height - cardPadV * 2);
            var titleStyle = new GUIStyle(EditorStyles.label) {
                font = PreviewFont,
                fontSize = t.fontSizeTitle, fontStyle = FontStyle.Bold,
                normal = { textColor = t.textPrimary },
                wordWrap = true,
            };
            var subtitleStyle = new GUIStyle(EditorStyles.label) {
                font = PreviewFont,
                fontSize = t.fontSizeBody,
                normal = { textColor = t.textSecondary },
                wordWrap = true,
            };

            float y = inner.y;
            var titleSize = titleStyle.CalcSize(new GUIContent("What would you like to do?"));
            GUI.Label(new Rect(inner.x, y, inner.width, titleSize.y),
                "What would you like to do?", titleStyle);
            y += titleSize.y + 2;
            var subH = subtitleStyle.CalcHeight(new GUIContent(
                "Pick what fits — we'll only bother the dev team with what you send."), inner.width);
            GUI.Label(new Rect(inner.x, y, inner.width, subH),
                "Pick what fits — we'll only bother the dev team with what you send.", subtitleStyle);
            y += subH + 14;

            // Three picker cards in a row — icon on top, title, caption,
            // accent bottom bar. Matches BugpunchRequestHelpPicker exactly:
            // Ask for help → Record a bug → Request a feature.
            var gap = 10f;
            var cardH = 200f;
            var cardW = (inner.width - gap * 2) / 3f;
            DrawPickerCard(
                new Rect(inner.x,                        y, cardW, cardH),
                t.ResolveIcon("ask"),
                "Ask for help", "Short question to the dev team",
                t.accentChat, t);
            DrawPickerCard(
                new Rect(inner.x + (cardW + gap),        y, cardW, cardH),
                t.ResolveIcon("bug"),
                "Record a bug", "Capture a video + report a problem",
                t.accentBug, t);
            DrawPickerCard(
                new Rect(inner.x + (cardW + gap) * 2,    y, cardW, cardH),
                t.ResolveIcon("feedback"),
                "Request a feature", "Suggest / vote on improvements",
                t.accentFeedback, t);
            y += pickerH + cancelGap;

            // Cancel text link — matches BugpunchRequestHelpPicker: muted
            // colour, caption+1 size, centered below the cards.
            var cancelStyle = new GUIStyle(EditorStyles.label) {
                font = PreviewFont,
                fontSize = t.fontSizeCaption + 1,
                normal = { textColor = t.textMuted },
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(new Rect(inner.x, y, inner.width, cancelH), "Cancel", cancelStyle);
            y += cancelH + readoutGap;

            // Design-time readout — separates itself visually from the picker
            // with a smaller, dimmer row. Not part of the on-device UI.
            var readoutStyle = new GUIStyle(EditorStyles.miniLabel) {
                normal = { textColor = new Color(t.textMuted.r, t.textMuted.g, t.textMuted.b, t.textMuted.a * 0.7f) },
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(new Rect(inner.x, y, inner.width, readoutH),
                $"radius {t.cardRadius}  ·  title/body/caption {t.fontSizeTitle}/{t.fontSizeBody}/{t.fontSizeCaption}px",
                readoutStyle);
        }

        static void DrawPickerCard(Rect r, Texture2D icon, string title, string caption, Color accent, BugpunchTheme t)
        {
            FillRoundedRect(r, t.cardRadius, t.cardBackground);
            StrokeRoundedRect(r, t.cardRadius, t.cardBorder, 1f);
            // Accent bottom bar — inset by the radius so it doesn't poke past
            // the rounded corners.
            var rad = Mathf.Min(t.cardRadius, Mathf.Min(r.width, r.height) / 2f);
            EditorGUI.DrawRect(new Rect(r.x + rad, r.y + r.height - 2, r.width - rad * 2, 2), accent);

            // Icon — 96px square, centred at the top.
            var iconSize = Mathf.Min(96f, r.width - 24);
            var iconRect = new Rect(r.x + (r.width - iconSize) / 2f, r.y + 14, iconSize, iconSize);
            if (icon != null)
            {
                // Preserve alpha on the packaged PNGs (they have transparency).
                var prev = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = prev;
            }
            else
            {
                // Designer cleared the slot AND stripped Resources. Show the
                // accent dot so the card still reads correctly.
                var dotSize = 32f;
                var dot = new Rect(r.x + (r.width - dotSize) / 2f, r.y + 40, dotSize, dotSize);
                FillRoundedRect(dot, dotSize / 2f, accent);
            }

            float textY = iconRect.y + iconSize + 8;

            var titleStyle = new GUIStyle(EditorStyles.label) {
                font = PreviewFont,
                fontSize = Mathf.Max(t.fontSizeBody + 2, 14),
                fontStyle = FontStyle.Bold,
                normal = { textColor = t.textPrimary },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            var captionStyle = new GUIStyle(EditorStyles.label) {
                font = PreviewFont,
                fontSize = t.fontSizeCaption,
                normal = { textColor = t.textSecondary },
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
            };

            GUI.Label(new Rect(r.x + 8, textY, r.width - 16, 24), title, titleStyle);
            textY += 24;
            GUI.Label(new Rect(r.x + 8, textY, r.width - 16, 44), caption, captionStyle);
        }

        // ─── Rounded-rect drawing helpers (IMGUI has no built-in roundrect) ──

        /// <summary>
        /// Fill a rect with rounded corners. Approximates the arcs by sweeping
        /// 1px-tall rows through a circle equation — quick, allocation-free,
        /// and pixel-accurate enough for previewing design tokens.
        /// </summary>
        static void FillRoundedRect(Rect r, float radius, Color c)
        {
            float rad = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2f));
            if (rad <= 0.5f)
            {
                EditorGUI.DrawRect(r, c);
                return;
            }

            // Center body (full width), sides above + below rounded corners.
            EditorGUI.DrawRect(new Rect(r.x, r.y + rad, r.width, r.height - rad * 2), c);

            // Sweep rows into the 4 corner quadrants.
            int steps = Mathf.CeilToInt(rad);
            for (int i = 0; i < steps; i++)
            {
                // y distance from the centre of the corner circle (0..rad).
                float dy = rad - i - 0.5f;
                float dx = Mathf.Sqrt(Mathf.Max(0f, rad * rad - dy * dy));
                float runWidth = rad + dx;

                // Top row
                float topY = r.y + i;
                EditorGUI.DrawRect(new Rect(r.x + (rad - dx), topY, runWidth, 1), c);
                EditorGUI.DrawRect(new Rect(r.x + r.width - (rad - dx) - runWidth, topY, runWidth, 1), c);

                // Bottom row (mirror)
                float botY = r.y + r.height - 1 - i;
                EditorGUI.DrawRect(new Rect(r.x + (rad - dx), botY, runWidth, 1), c);
                EditorGUI.DrawRect(new Rect(r.x + r.width - (rad - dx) - runWidth, botY, runWidth, 1), c);
            }
        }

        /// <summary>
        /// 1px stroke around a rounded rect. Draws by filling at radius then
        /// overdrawing the interior in the fill colour — which isn't known
        /// here, so we layer two FillRoundedRect calls: outer (stroke) at full
        /// size, inner (the surrounding fill) insetted by strokeWidth. Callers
        /// must pass the exact interior fill colour.
        /// </summary>
        static void StrokeRoundedRect(Rect r, float radius, Color stroke, float strokeWidth)
        {
            // We don't know the card fill colour here, so cheat: draw 4 thin
            // edges + 4 corner quadrants in the stroke colour over the already-
            // filled rect. This is plenty crisp at 1px in the Inspector.
            float rad = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2f));
            if (rad <= 0.5f)
            {
                // Simple rect border.
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, strokeWidth), stroke);
                EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - strokeWidth, r.width, strokeWidth), stroke);
                EditorGUI.DrawRect(new Rect(r.x, r.y, strokeWidth, r.height), stroke);
                EditorGUI.DrawRect(new Rect(r.x + r.width - strokeWidth, r.y, strokeWidth, r.height), stroke);
                return;
            }

            // Straight edges, inset by the radius.
            EditorGUI.DrawRect(new Rect(r.x + rad, r.y, r.width - rad * 2, strokeWidth), stroke);
            EditorGUI.DrawRect(new Rect(r.x + rad, r.y + r.height - strokeWidth, r.width - rad * 2, strokeWidth), stroke);
            EditorGUI.DrawRect(new Rect(r.x, r.y + rad, strokeWidth, r.height - rad * 2), stroke);
            EditorGUI.DrawRect(new Rect(r.x + r.width - strokeWidth, r.y + rad, strokeWidth, r.height - rad * 2), stroke);

            // Corner arcs — 1-pixel wide sliver per row at each corner.
            int steps = Mathf.CeilToInt(rad);
            for (int i = 0; i < steps; i++)
            {
                float dy = rad - i - 0.5f;
                float dx = Mathf.Sqrt(Mathf.Max(0f, rad * rad - dy * dy));
                // Pixel on the circle for this row, relative to the corner's
                // interior centre.
                float edgePx = dx;

                // Top-left
                EditorGUI.DrawRect(new Rect(r.x + rad - edgePx, r.y + i, strokeWidth, 1), stroke);
                // Top-right
                EditorGUI.DrawRect(new Rect(r.x + r.width - rad + edgePx - strokeWidth, r.y + i, strokeWidth, 1), stroke);
                // Bottom-left
                EditorGUI.DrawRect(new Rect(r.x + rad - edgePx, r.y + r.height - 1 - i, strokeWidth, 1), stroke);
                // Bottom-right
                EditorGUI.DrawRect(new Rect(r.x + r.width - rad + edgePx - strokeWidth, r.y + r.height - 1 - i, strokeWidth, 1), stroke);
            }
        }
    }
}
