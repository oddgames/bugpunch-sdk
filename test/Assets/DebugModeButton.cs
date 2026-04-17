using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Creates a single "Enter Debug" button on screen — the consent flow that
/// starts video recording. Also ensures the DebugToolsBridge exists.
/// </summary>
public class DebugModeButton : MonoBehaviour
{
    void Start()
    {
        if (FindAnyObjectByType<DebugToolsBridge>() == null)
        {
            var bridge = new GameObject("BugpunchToolsBridge");
            bridge.AddComponent<DebugToolsBridge>();
        }

        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("DebugCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        // "Enter Debug" — top-right, always visible
        var btn = new GameObject("EnterDebugButton");
        btn.transform.SetParent(canvas.transform, false);
        var rect = btn.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-16, -16);
        rect.sizeDelta = new Vector2(140, 40);
        btn.AddComponent<Image>().color = new Color(0.18f, 0.55f, 0.83f, 0.9f);
        btn.AddComponent<Button>().onClick.AddListener(() => ODDGames.Bugpunch.Bugpunch.EnterDebugMode());

        var label = new GameObject("Label");
        label.transform.SetParent(btn.transform, false);
        var lr = label.AddComponent<RectTransform>();
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = Vector2.zero;
        lr.offsetMax = Vector2.zero;
        var tmp = label.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = "Enter Debug";
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
    }
}
