using System;
using UnityEngine.Scripting;

/// <summary>
/// Mark a field or property on a MonoBehaviour as a "declared" watch. Declared
/// watches show up automatically in the Bugpunch Remote IDE Watch panel —
/// grouped, range-aware, and editable on the fly without the user having to
/// search and pin them.
/// </summary>
/// <example>
/// <code>
/// [Watch] public float health;
/// [Watch("Suspension", 0f, 100f, WatchOwner.Parent)] public float springStrength;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
[Preserve]
public class WatchAttribute : Attribute
{
    /// <summary>Display group name. When null, the panel groups by the resolved owner GameObject's name.</summary>
    public string Group { get; }
    /// <summary>Slider minimum. When <see cref="Min"/> equals <see cref="Max"/>, no slider is shown.</summary>
    public float Min { get; }
    /// <summary>Slider maximum. When <see cref="Min"/> equals <see cref="Max"/>, no slider is shown.</summary>
    public float Max { get; }
    /// <summary>Which GameObject in the hierarchy this watch is grouped under in the IDE.</summary>
    public WatchOwner Owner { get; }

    public WatchAttribute(string group = null, float min = 0f, float max = 0f, WatchOwner owner = WatchOwner.Self)
    {
        Group = group;
        Min = min;
        Max = max;
        Owner = owner;
    }
}

/// <summary>
/// Determines which GameObject a [Watch]-marked member is grouped under in the
/// Remote IDE. Useful when several components on different children all
/// belong logically to a parent (e.g. four wheels under a truck).
/// </summary>
public enum WatchOwner
{
    /// <summary>Group under the component's own GameObject. Default.</summary>
    Self,
    /// <summary>Group under the component's transform.parent (falls back to Self at the root).</summary>
    Parent,
    /// <summary>Group under transform.root — the topmost GameObject in the hierarchy.</summary>
    Root,
}
