using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ODDGames.UIAutomation.Editor
{
    /// <summary>
    /// Live test recorder - Shift+Right-click to see Search options for UI element under cursor.
    /// </summary>
    public class UITestLiveRecorder : MonoBehaviour
    {
        private static UITestLiveRecorder _instance;
        private static bool _isEnabled = false;

        private bool _showOptionsPanel = false;
        private GameObject _targetObject;
        private Vector2 _panelPosition;
        private List<RecordedAction> _recordedActions = new List<RecordedAction>();
        private bool _showGeneratedCode = false;
        private Vector2 _scrollPosition;
        private List<SearchOption> _hoverOptions = new List<SearchOption>();

        // Styling
        private GUIStyle _headerStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _optionStyle;
        private GUIStyle _smallButtonStyle;
        private GUIStyle _listItemStyle;
        private GUIStyle _recordingStyle;
        private Texture2D _panelBackground;
        private Texture2D _highlightTex;
        private bool _stylesInitialized;

        private class RecordedAction
        {
            public string Code;
            public string Description;
        }

        private class SearchOption
        {
            public string Label;      // e.g., "ByName"
            public string Code;       // e.g., await Click(Search.ByName("ButtonName"));
            public string SearchPart; // e.g., Search.ByName("ButtonName")
            public Rect ButtonRect;
        }

#if UNITY_EDITOR
        [MenuItem("Window/Analysis/UI Automation/Live Recorder #r")] // Shift+R
        public static void ToggleRecorder()
        {
            _isEnabled = !_isEnabled;

            if (_isEnabled)
            {
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                if (Application.isPlaying)
                {
                    CreateInstance();
                }
                Debug.Log("[UITest] Live Recorder ENABLED. Enter Play Mode, Shift+Right-click UI to see options.");
            }
            else
            {
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                if (_instance != null)
                {
                    Destroy(_instance.gameObject);
                    _instance = null;
                }
                Debug.Log("[UITest] Live Recorder disabled.");
            }
        }

        [MenuItem("Window/Analysis/UI Automation/Live Recorder", true)]
        public static bool ToggleRecorderValidate()
        {
            Menu.SetChecked("Window/Analysis/UI Automation/Live Recorder", _isEnabled);
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && _isEnabled)
            {
                CreateInstance();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && _instance != null)
            {
                if (_instance._recordedActions.Count > 0)
                {
                    var code = _instance.GenerateTestCode();
                    Debug.Log($"[UITest] Generated Test Code:\n\n{code}");
                    GUIUtility.systemCopyBuffer = code;
                    Debug.Log("[UITest] Code copied to clipboard!");
                }
            }
        }
#endif

        private static void CreateInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("UITestLiveRecorder");
            _instance = go.AddComponent<UITestLiveRecorder>();
            DontDestroyOnLoad(go);
            Debug.Log("[UITest] Live Recorder active. Shift+Right-click on UI to see code options. Shift+Tab to view recorded code.");
        }

        private void OnDestroy()
        {
            if (_panelBackground != null) Destroy(_panelBackground);
            if (_highlightTex != null) Destroy(_highlightTex);
            _instance = null;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            // Shift+Right-click to show options panel
            if (mouse.rightButton.wasPressedThisFrame && keyboard.shiftKey.isPressed)
            {
                var clickedObj = GetObjectUnderMouse();
                if (clickedObj != null)
                {
                    _targetObject = clickedObj;
                    _panelPosition = mouse.position.ReadValue();
                    GenerateSearchOptions(_targetObject);
                    _showOptionsPanel = true;
                }
            }

            // Close panel on Escape or any click outside
            if (_showOptionsPanel)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    _showOptionsPanel = false;
                }
            }

            // Toggle code view with Shift+Tab
            if (keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed)
            {
                _showGeneratedCode = !_showGeneratedCode;
                _showOptionsPanel = false;
            }

            // Close code view on Escape
            if (_showGeneratedCode && keyboard.escapeKey.wasPressedThisFrame)
            {
                _showGeneratedCode = false;
            }
        }

        private GameObject GetObjectUnderMouse()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return null;

            var mouse = Mouse.current;
            if (mouse == null) return null;

            var pointerData = new PointerEventData(eventSystem) { position = mouse.position.ReadValue() };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            return results.Count > 0 ? results[0].gameObject : null;
        }


        private void GenerateSearchOptions(GameObject go)
        {
            _hoverOptions.Clear();
            var name = go.name;

            // Get text content
            var tmpText = go.GetComponent<TMP_Text>();
            var legacyText = go.GetComponent<Text>();
            var childTmp = go.GetComponentInChildren<TMP_Text>();
            var childText = go.GetComponentInChildren<Text>();
            string textContent = tmpText?.text ?? legacyText?.text ?? childTmp?.text ?? childText?.text;

            // Get components for context
            var image = go.GetComponent<Image>();
            var button = go.GetComponent<Button>();
            var toggle = go.GetComponent<Toggle>();
            var slider = go.GetComponent<Slider>();
            var inputField = go.GetComponent<TMP_InputField>() ?? (Component)go.GetComponent<InputField>();
            var dropdown = go.GetComponent<TMP_Dropdown>() ?? (Component)go.GetComponent<Dropdown>();

            // 1. ByText (if has text - most readable)
            if (!string.IsNullOrEmpty(textContent) && textContent.Length < 40)
            {
                var escaped = Escape(textContent.Trim());
                _hoverOptions.Add(new SearchOption
                {
                    Label = $"1. ByText",
                    SearchPart = $"\"{escaped}\"",
                    Code = $"await Click(\"{escaped}\");"
                });
            }

            // 2. ByName
            _hoverOptions.Add(new SearchOption
            {
                Label = $"2. ByName",
                SearchPart = $"Search.ByName(\"{name}\")",
                Code = $"await Click(Search.ByName(\"{name}\"));"
            });

            // 3. ByTexture (if has sprite/texture)
            if (image != null && image.sprite != null)
            {
                var spriteName = image.sprite.name;
                _hoverOptions.Add(new SearchOption
                {
                    Label = $"3. ByTexture",
                    SearchPart = $"Search.ByTexture(\"{spriteName}\")",
                    Code = $"await Click(Search.ByTexture(\"{spriteName}\"));"
                });
            }

            // 4. ByType (if interactive component)
            if (button != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = "4. ByType<Button>",
                    SearchPart = $"Search.ByType<Button>().Name(\"{name}\")",
                    Code = $"await Click(Search.ByType<Button>().Name(\"{name}\"));"
                });
            }
            else if (toggle != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = "4. ByType<Toggle>",
                    SearchPart = $"Search.ByType<Toggle>().Name(\"{name}\")",
                    Code = $"await Click(Search.ByType<Toggle>().Name(\"{name}\"));"
                });
            }
            else if (slider != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = "4. ClickSlider",
                    SearchPart = $"\"{name}\"",
                    Code = $"await ClickSlider(\"{name}\", 0.5f);"
                });
            }
            else if (inputField != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = "4. TextInput",
                    SearchPart = $"Search.ByName(\"{name}\")",
                    Code = $"await TextInput(Search.ByName(\"{name}\"), \"TODO\");"
                });
            }
            else if (dropdown != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = "4. ClickDropdown",
                    SearchPart = $"\"{name}\"",
                    Code = $"await ClickDropdown(\"{name}\", 0);"
                });
            }

            // 5. Adjacent (look for nearby labels)
            var nearbyLabel = FindNearbyLabel(go);
            if (nearbyLabel != null)
            {
                _hoverOptions.Add(new SearchOption
                {
                    Label = $"5. Adjacent",
                    SearchPart = $"Search.Adjacent(\"{Escape(nearbyLabel.Text)}\", Direction.{nearbyLabel.Direction})",
                    Code = $"await Click(Search.Adjacent(\"{Escape(nearbyLabel.Text)}\", Direction.{nearbyLabel.Direction}));"
                });
            }

            // 6. Wait option
            _hoverOptions.Add(new SearchOption
            {
                Label = $"{_hoverOptions.Count + 1}. Wait",
                SearchPart = $"Search.ByName(\"{name}\")",
                Code = $"await Wait(Search.ByName(\"{name}\"), seconds: 10);"
            });

            // Renumber labels
            for (int i = 0; i < _hoverOptions.Count; i++)
            {
                var opt = _hoverOptions[i];
                opt.Label = $"{i + 1}. {opt.Label.Substring(opt.Label.IndexOf('.') + 2)}";
            }
        }

        private class NearbyLabel { public string Text; public string Direction; }

        private NearbyLabel FindNearbyLabel(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return null;

            var center = GetScreenCenter(rect);
            var allTexts = FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)
                .Cast<Component>()
                .Concat(FindObjectsByType<Text>(FindObjectsSortMode.None));

            NearbyLabel best = null;
            float bestDist = 200f;

            foreach (var textComp in allTexts)
            {
                if (textComp.gameObject == go) continue;
                if (textComp.transform.IsChildOf(go.transform)) continue;

                var textRect = textComp.GetComponent<RectTransform>();
                if (textRect == null) continue;

                var text = textComp is TMP_Text tmp ? tmp.text : ((Text)textComp).text;
                if (string.IsNullOrWhiteSpace(text) || text.Length > 25 || text.Length < 2) continue;

                var textCenter = GetScreenCenter(textRect);
                var delta = center - textCenter;
                var distance = delta.magnitude;

                if (distance < bestDist)
                {
                    bestDist = distance;
                    string direction;
                    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                        direction = delta.x > 0 ? "Right" : "Left";
                    else
                        direction = delta.y > 0 ? "Below" : "Above";

                    best = new NearbyLabel { Text = text.Trim(), Direction = direction };
                }
            }

            return best;
        }

        private Vector2 GetScreenCenter(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return Vector2.zero;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return (corners[0] + corners[2]) / 2f;
            }
            else
            {
                var cam = canvas.worldCamera ?? Camera.main;
                if (cam == null) return Vector2.zero;
                return cam.WorldToScreenPoint((corners[0] + corners[2]) / 2f);
            }
        }

        private void RecordOption(SearchOption option)
        {
            _recordedActions.Add(new RecordedAction
            {
                Code = option.Code,
                Description = option.Label
            });
            Debug.Log($"[UITest] Recorded: {option.Code}");
        }

        private string GenerateTestCode()
        {
            var sb = new StringBuilder();
            sb.AppendLine("protected override async UniTask Test()");
            sb.AppendLine("{");

            foreach (var action in _recordedActions)
            {
                sb.AppendLine($"    {action.Code}");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _panelBackground = new Texture2D(1, 1);
            _panelBackground.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.12f, 0.95f));
            _panelBackground.Apply();

            _highlightTex = new Texture2D(1, 1);
            _highlightTex.SetPixel(0, 0, new Color(0f, 0.8f, 1f, 0.3f));
            _highlightTex.Apply();

            _headerStyle = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                padding = new RectOffset(6, 6, 4, 4)
            };

            _codeStyle = new GUIStyle
            {
                fontSize = 10,
                normal = { textColor = new Color(0.5f, 1f, 0.5f) },
                padding = new RectOffset(12, 6, 0, 4),
                wordWrap = true
            };

            _optionStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4)
            };

            _smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                padding = new RectOffset(6, 6, 3, 3)
            };

            _listItemStyle = new GUIStyle
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                padding = new RectOffset(6, 6, 3, 3)
            };

            _recordingStyle = new GUIStyle
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                padding = new RectOffset(8, 8, 4, 4)
            };
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            InitStyles();

            // Recording indicator
            DrawRecordingIndicator();

            // Options panel (from Shift+Right-click)
            if (_showOptionsPanel && _targetObject != null)
            {
                DrawElementHighlight(_targetObject);
                DrawOptionsPanel();
            }

            // Generated code view
            if (_showGeneratedCode)
            {
                DrawGeneratedCode();
            }
        }

        private void DrawRecordingIndicator()
        {
            if (_recordedActions.Count == 0) return;

            var text = $"● REC ({_recordedActions.Count})";
            var rect = new Rect(Screen.width - 80, 10, 70, 22);
            GUI.DrawTexture(rect, _panelBackground);
            GUI.Label(rect, text, _recordingStyle);
        }

        private void DrawElementHighlight(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            Vector2 min, max;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                min = corners[0];
                max = corners[2];
            }
            else
            {
                var cam = canvas.worldCamera ?? Camera.main;
                if (cam == null) return;
                min = cam.WorldToScreenPoint(corners[0]);
                max = cam.WorldToScreenPoint(corners[2]);
            }

            min.y = Screen.height - min.y;
            max.y = Screen.height - max.y;

            var highlightRect = new Rect(min.x - 2, max.y - 2, max.x - min.x + 4, min.y - max.y + 4);
            GUI.DrawTexture(highlightRect, _highlightTex);
        }

        private void DrawOptionsPanel()
        {
            if (_hoverOptions.Count == 0) return;

            // Position panel at click location
            float panelWidth = 400;
            float panelHeight = 30 + _hoverOptions.Count * 45;
            float x = _panelPosition.x + 15;
            float y = Screen.height - _panelPosition.y;

            // Keep on screen
            if (x + panelWidth > Screen.width - 10) x = _panelPosition.x - panelWidth - 15;
            if (y + panelHeight > Screen.height - 10) y = Screen.height - panelHeight - 10;
            if (y < 10) y = 10;

            var panelRect = new Rect(x, y, panelWidth, panelHeight);
            GUI.DrawTexture(panelRect, _panelBackground);

            GUILayout.BeginArea(panelRect);

            GUILayout.BeginHorizontal();
            GUILayout.Label(_targetObject.name, _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", _smallButtonStyle, GUILayout.Width(22)))
            {
                _showOptionsPanel = false;
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < _hoverOptions.Count; i++)
            {
                var opt = _hoverOptions[i];

                GUILayout.BeginVertical();

                if (GUILayout.Button(opt.Label, _optionStyle))
                {
                    RecordOption(opt);
                    _showOptionsPanel = false;
                }

                GUILayout.Label(opt.Code, _codeStyle);

                GUILayout.EndVertical();
            }

            GUILayout.EndArea();

            // Close panel if clicked outside
            if (Event.current.type == EventType.MouseDown)
            {
                if (!panelRect.Contains(Event.current.mousePosition))
                {
                    _showOptionsPanel = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawGeneratedCode()
        {
            float w = 450, h = 350;
            float x = (Screen.width - w) / 2;
            float y = (Screen.height - h) / 2;

            var rect = new Rect(x, y, w, h);
            GUI.DrawTexture(rect, _panelBackground);

            GUILayout.BeginArea(rect);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Generated Code", _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", _smallButtonStyle, GUILayout.Width(50)))
            {
                GUIUtility.systemCopyBuffer = GenerateTestCode();
                Debug.Log("[UITest] Copied!");
            }
            if (GUILayout.Button("X", _smallButtonStyle, GUILayout.Width(22)))
            {
                _showGeneratedCode = false;
            }
            GUILayout.EndHorizontal();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            if (_recordedActions.Count == 0)
            {
                GUILayout.Label("No actions recorded.\nShift+Right-click on UI elements to record.", _listItemStyle);
            }
            else
            {
                var code = GenerateTestCode();
                GUILayout.TextArea(code, _codeStyle, GUILayout.ExpandHeight(true));
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (_recordedActions.Count > 0 && GUILayout.Button("Clear", _smallButtonStyle, GUILayout.Width(50)))
            {
                _recordedActions.Clear();
            }
            if (_recordedActions.Count > 0 && GUILayout.Button("Undo", _smallButtonStyle, GUILayout.Width(50)))
            {
                _recordedActions.RemoveAt(_recordedActions.Count - 1);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
