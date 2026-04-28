using UnityEngine;
using UnityEngine.UIElements;

namespace ODDGames.Bugpunch.UI
{
    public static partial class BugpunchChatBoard
    {
        // ─── Styling (delegate to BugpunchUIToolkit) ─────────────────────

        static void StyleInput(TextField field, string placeholder, bool multiline = false)
            => BugpunchUIToolkit.StyleInput(field, placeholder, multiline, multilineMinHeight: 44f);

        static void StylePrimaryButton(Button btn)
            => BugpunchUIToolkit.StylePrimaryButton(btn, horizontalPadding: 16f, marginLeft: 6f);

        static void StyleIconButton(Button btn)
            => BugpunchUIToolkit.StyleIconButton(btn);

        // ─── Infra ───────────────────────────────────────────────────────

        static VisualElement CreateBackdrop(System.Action onClickOutside)
            => BugpunchUIToolkit.CreateBackdrop(onClickOutside);

        static VisualElement CreateCard()
            // Chat card is slightly wider than feedback (560 vs 520) and
            // uses Position.Relative so the emoji popover anchors to it.
            => BugpunchUIToolkit.CreateCard(maxWidth: 560f, minWidth: 360f, widthPercent: 92f, relativePosition: true);

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
    }
}
