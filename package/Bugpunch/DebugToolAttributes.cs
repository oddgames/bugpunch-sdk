using System;

/// <summary>
/// Mark a static method as a debug tool button. The DebugTools panel discovers
/// these via reflection and shows them in the specified category.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DebugButtonAttribute : Attribute
{
    public string Category { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }

    /// <param name="category">Tool category (left sidebar).</param>
    /// <param name="name">Display name. Defaults to method name if null.</param>
    /// <param name="description">Short description shown below the name.</param>
    /// <param name="icon">Material Symbols icon name (e.g. "bug_report"). Defaults to "play_arrow".</param>
    public DebugButtonAttribute(string category, string name = null, string description = "", string icon = "play_arrow")
    {
        Category = category;
        Name = name;
        Description = description;
        Icon = icon;
    }
}

/// <summary>
/// Mark a static bool field or property as a debug toggle. The panel shows
/// a toggle switch that reads/writes the value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class DebugToggleAttribute : Attribute
{
    public string Category { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }

    public DebugToggleAttribute(string category, string name = null, string description = "", string icon = "toggle_on")
    {
        Category = category;
        Name = name;
        Description = description;
        Icon = icon;
    }
}

/// <summary>
/// Mark a static float field or property as a debug slider. The panel shows
/// a slider with the specified range.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class DebugSliderAttribute : Attribute
{
    public string Category { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public float Min { get; }
    public float Max { get; }

    public DebugSliderAttribute(string category, float min, float max, string name = null, string description = "", string icon = "tune")
    {
        Category = category;
        Min = min;
        Max = max;
        Name = name;
        Description = description;
        Icon = icon;
    }
}
