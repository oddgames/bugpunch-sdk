using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Bridge between the native debug tools panel (Java/ObjC) and C# game code.
///
/// On Start, scans all loaded assemblies for [DebugButton], [DebugToggle],
/// [DebugSlider] attributes. Serializes them to JSON and passes to the native
/// side. A single "Tools" button (created by the game or this script) calls
/// ShowPanel() to launch the native UI.
///
/// The native panel sends callbacks via UnitySendMessage to the GameObject
/// named "BugpunchToolsBridge" — this script must live on that GameObject.
///
/// Works on Android (native Activity) and iOS (native ViewController).
/// On Editor/Standalone, falls back to a simple IMGUI overlay.
/// </summary>
public class DebugToolsBridge : MonoBehaviour
{
    struct ToolDef
    {
        public string id;
        public string name;
        public string category;
        public string description;
        public string icon;
        public string controlType; // button, toggle, slider
        public string color;
        public bool toggleValue;
        public float sliderValue, sliderMin, sliderMax;
        // Runtime references for reflection callbacks
        public MethodInfo buttonMethod;
        public FieldInfo toggleField;
        public PropertyInfo toggleProp;
        public FieldInfo sliderField;
        public PropertyInfo sliderProp;
    }

    static List<ToolDef> _tools = new();
    static string _toolsJson;
    static bool _scanned;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void Bugpunch_ShowToolsPanel(string toolsJson);
#endif

    void Awake()
    {
        gameObject.name = "BugpunchToolsBridge";
        DontDestroyOnLoad(gameObject);
        if (!_scanned) ScanAttributes();
    }

    /// <summary>Show the native debug tools panel.</summary>
    public static void ShowPanel()
    {
        if (!_scanned) ScanAttributes();

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var cls = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchToolsActivity");
            cls.CallStatic("launch", _toolsJson);
        }
        catch (Exception e) { Debug.LogWarning("[DebugTools] Android launch failed: " + e.Message); }
#elif UNITY_IOS && !UNITY_EDITOR
        Bugpunch_ShowToolsPanel(_toolsJson);
#else
        // Editor/standalone: toggle IMGUI overlay
        _showImgui = !_showImgui;
#endif
    }

    // ── Attribute scanning ──

    static void ScanAttributes()
    {
        _scanned = true;
        _tools.Clear();
        int id = 0;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Skip system assemblies
            var name = asm.GetName().Name;
            if (name.StartsWith("System") || name.StartsWith("Unity") ||
                name.StartsWith("mscorlib") || name.StartsWith("Mono") ||
                name.StartsWith("netstandard") || name.StartsWith("Newtonsoft"))
                continue;

            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                // Buttons — static methods
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = method.GetCustomAttribute<DebugButtonAttribute>();
                    if (attr == null) continue;
                    _tools.Add(new ToolDef
                    {
                        id = "btn_" + id++,
                        name = attr.Name ?? method.Name,
                        category = attr.Category,
                        description = attr.Description,
                        icon = attr.Icon,
                        controlType = "button",
                        color = "accent",
                        buttonMethod = method,
                    });
                }

                // Toggles — static bool fields/properties
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = field.GetCustomAttribute<DebugToggleAttribute>();
                    if (attr == null || field.FieldType != typeof(bool)) continue;
                    _tools.Add(new ToolDef
                    {
                        id = "tog_" + id++,
                        name = attr.Name ?? field.Name,
                        category = attr.Category,
                        description = attr.Description,
                        icon = attr.Icon,
                        controlType = "toggle",
                        color = "accent",
                        toggleValue = (bool)field.GetValue(null),
                        toggleField = field,
                    });
                }
                foreach (var prop in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = prop.GetCustomAttribute<DebugToggleAttribute>();
                    if (attr == null || prop.PropertyType != typeof(bool) || !prop.CanRead || !prop.CanWrite) continue;
                    _tools.Add(new ToolDef
                    {
                        id = "tog_" + id++,
                        name = attr.Name ?? prop.Name,
                        category = attr.Category,
                        description = attr.Description,
                        icon = attr.Icon,
                        controlType = "toggle",
                        color = "accent",
                        toggleValue = (bool)prop.GetValue(null),
                        toggleProp = prop,
                    });
                }

                // Sliders — static float fields/properties
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = field.GetCustomAttribute<DebugSliderAttribute>();
                    if (attr == null || field.FieldType != typeof(float)) continue;
                    _tools.Add(new ToolDef
                    {
                        id = "sld_" + id++,
                        name = attr.Name ?? field.Name,
                        category = attr.Category,
                        description = attr.Description,
                        icon = attr.Icon,
                        controlType = "slider",
                        color = "accent",
                        sliderValue = (float)field.GetValue(null),
                        sliderMin = attr.Min,
                        sliderMax = attr.Max,
                        sliderField = field,
                    });
                }
                foreach (var prop in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = prop.GetCustomAttribute<DebugSliderAttribute>();
                    if (attr == null || prop.PropertyType != typeof(float) || !prop.CanRead || !prop.CanWrite) continue;
                    _tools.Add(new ToolDef
                    {
                        id = "sld_" + id++,
                        name = attr.Name ?? prop.Name,
                        category = attr.Category,
                        description = attr.Description,
                        icon = attr.Icon,
                        controlType = "slider",
                        color = "accent",
                        sliderValue = (float)prop.GetValue(null),
                        sliderMin = attr.Min,
                        sliderMax = attr.Max,
                        sliderProp = prop,
                    });
                }
            }
        }

        // Sort by category then name
        _tools.Sort((a, b) =>
        {
            int c = string.Compare(a.category, b.category, StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.name, b.name, StringComparison.Ordinal);
        });

        _toolsJson = SerializeTools();
        Debug.Log($"[DebugTools] Scanned {_tools.Count} tools from attributes");
    }

    static string SerializeTools()
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < _tools.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var t = _tools[i];
            sb.Append('{');
            sb.Append($"\"id\":\"{Esc(t.id)}\",");
            sb.Append($"\"name\":\"{Esc(t.name)}\",");
            sb.Append($"\"category\":\"{Esc(t.category)}\",");
            sb.Append($"\"description\":\"{Esc(t.description)}\",");
            sb.Append($"\"icon\":\"{Esc(t.icon)}\",");
            sb.Append($"\"controlType\":\"{t.controlType}\",");
            sb.Append($"\"color\":\"{t.color}\",");
            sb.Append($"\"toggleValue\":{(t.toggleValue ? "true" : "false")},");
            sb.Append($"\"sliderValue\":{t.sliderValue},");
            sb.Append($"\"sliderMin\":{t.sliderMin},");
            sb.Append($"\"sliderMax\":{t.sliderMax}");
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

    // ── Callback from native (UnitySendMessage) ──

    // Called by the native panel: "toolId|action|value"
    void OnToolAction(string msg)
    {
        var parts = msg.Split('|');
        if (parts.Length < 3) return;
        string toolId = parts[0], action = parts[1], value = parts[2];

        var tool = _tools.FirstOrDefault(t => t.id == toolId);
        if (tool.id == null) { Debug.LogWarning("[DebugTools] unknown tool: " + toolId); return; }

        switch (action)
        {
            case "click":
                try { tool.buttonMethod?.Invoke(null, null); }
                catch (Exception e) { Debug.LogException(e); }
                break;
            case "toggle":
                bool bv = value == "true";
                if (tool.toggleField != null) tool.toggleField.SetValue(null, bv);
                else if (tool.toggleProp != null) tool.toggleProp.SetValue(null, bv);
                break;
            case "slider":
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fv))
                {
                    if (tool.sliderField != null) tool.sliderField.SetValue(null, fv);
                    else if (tool.sliderProp != null) tool.sliderProp.SetValue(null, fv);
                }
                break;
        }
    }

    // Called by the native widget's "Tools" button.
    void OnShowTools(string msg) => ShowPanel();

    // Called by the native widget's screenshot button: "path|timestampMs"
    static readonly List<(string path, long ts)> _manualScreenshots = new();

    void OnManualScreenshot(string msg)
    {
        var parts = msg.Split('|');
        if (parts.Length < 2) return;
        string path = parts[0];
        long.TryParse(parts[1], out long ts);
        _manualScreenshots.Add((path, ts));
        Debug.Log($"[DebugTools] Manual screenshot captured ({_manualScreenshots.Count} total): {path}");
    }

    /// <summary>Get and clear all manually captured screenshots (for attaching to a report).</summary>
    public static List<(string path, long timestampMs)> DrainManualScreenshots()
    {
        var copy = new List<(string, long)>(_manualScreenshots);
        _manualScreenshots.Clear();
        return copy;
    }

    // ── Editor/Standalone IMGUI fallback ──

    static bool _showImgui;
    static Vector2 _imguiScroll;
    static string _imguiSearch = "";

    void OnGUI()
    {
        if (!_showImgui) return;

        var area = new Rect(20, 20, Screen.width - 40, Screen.height - 40);
        GUI.Box(area, "");
        GUILayout.BeginArea(area);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Debug Tools", GUILayout.Width(120));
        _imguiSearch = GUILayout.TextField(_imguiSearch, GUILayout.Width(200));
        if (GUILayout.Button("X", GUILayout.Width(30))) _showImgui = false;
        GUILayout.EndHorizontal();

        _imguiScroll = GUILayout.BeginScrollView(_imguiScroll);
        foreach (var tool in _tools)
        {
            if (!string.IsNullOrEmpty(_imguiSearch) &&
                tool.name.IndexOf(_imguiSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

            GUILayout.BeginHorizontal("box");
            GUILayout.Label($"[{tool.category}] {tool.name}", GUILayout.Width(300));
            switch (tool.controlType)
            {
                case "button":
                    if (GUILayout.Button("Run", GUILayout.Width(60)))
                        try { tool.buttonMethod?.Invoke(null, null); } catch (Exception e) { Debug.LogException(e); }
                    break;
                case "toggle":
                    bool cur = tool.toggleField != null ? (bool)tool.toggleField.GetValue(null)
                        : tool.toggleProp != null ? (bool)tool.toggleProp.GetValue(null) : false;
                    bool next = GUILayout.Toggle(cur, "", GUILayout.Width(30));
                    if (next != cur)
                    {
                        if (tool.toggleField != null) tool.toggleField.SetValue(null, next);
                        else if (tool.toggleProp != null) tool.toggleProp.SetValue(null, next);
                    }
                    break;
                case "slider":
                    float sv = tool.sliderField != null ? (float)tool.sliderField.GetValue(null)
                        : tool.sliderProp != null ? (float)tool.sliderProp.GetValue(null) : 0;
                    float nv = GUILayout.HorizontalSlider(sv, tool.sliderMin, tool.sliderMax, GUILayout.Width(150));
                    GUILayout.Label(nv.ToString("F1"), GUILayout.Width(40));
                    if (Math.Abs(nv - sv) > 0.001f)
                    {
                        if (tool.sliderField != null) tool.sliderField.SetValue(null, nv);
                        else if (tool.sliderProp != null) tool.sliderProp.SetValue(null, nv);
                    }
                    break;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
