using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Whitelist of member names that the Unity editor's default Inspector shows
    /// per component type. Used by <see cref="InspectorService"/> to filter the
    /// reflection-based view down to the editor-shaped view when debug mode is off.
    ///
    /// Two layers:
    ///   1. Hardcoded map of common Unity built-ins (Transform, MeshRenderer, …)
    ///      — their CustomEditors draw a curated set of C# properties that
    ///      cannot be derived from a SerializedObject iterator at runtime.
    ///   2. Optional JSON override at Resources/BugpunchInspectorSchema — produced
    ///      by the editor-side exporter, merged on top of the built-ins. This is
    ///      where user scripts with CustomEditors land.
    ///
    /// For any type not covered by either layer, the fallback rule is
    /// Unity-serialization shape: public instance non-static fields + private
    /// fields with [SerializeField], no properties. Matches what Unity's default
    /// inspector shows for user MonoBehaviours without a CustomEditor.
    /// </summary>
    public static class InspectorSchema
    {
        // Type full-name → set of member names to show.
        static readonly Dictionary<string, HashSet<string>> s_builtins = new Dictionary<string, HashSet<string>>
        {
            ["UnityEngine.Transform"] = new HashSet<string>
            { "localPosition", "localEulerAngles", "localScale" },

            ["UnityEngine.RectTransform"] = new HashSet<string>
            { "anchoredPosition", "sizeDelta", "anchorMin", "anchorMax", "pivot",
              "localPosition", "localEulerAngles", "localScale" },

            ["UnityEngine.MeshFilter"] = new HashSet<string>
            { "sharedMesh" },

            ["UnityEngine.MeshRenderer"] = new HashSet<string>
            { "enabled", "sharedMaterials", "sharedMaterial", "shadowCastingMode",
              "receiveShadows", "lightProbeUsage", "reflectionProbeUsage",
              "lightProbeAnchor", "probeAnchor", "motionVectorGenerationMode",
              "allowOcclusionWhenDynamic", "staticShadowCaster",
              "sortingLayerName", "sortingOrder", "renderingLayerMask" },

            ["UnityEngine.SkinnedMeshRenderer"] = new HashSet<string>
            { "enabled", "sharedMesh", "sharedMaterials", "sharedMaterial",
              "bones", "rootBone", "quality", "updateWhenOffscreen",
              "localBounds", "shadowCastingMode", "receiveShadows",
              "lightProbeUsage", "reflectionProbeUsage", "motionVectorGenerationMode",
              "skinnedMotionVectors", "sortingLayerName", "sortingOrder" },

            ["UnityEngine.BoxCollider"] = new HashSet<string>
            { "enabled", "isTrigger", "sharedMaterial", "center", "size" },
            ["UnityEngine.SphereCollider"] = new HashSet<string>
            { "enabled", "isTrigger", "sharedMaterial", "center", "radius" },
            ["UnityEngine.CapsuleCollider"] = new HashSet<string>
            { "enabled", "isTrigger", "sharedMaterial", "center", "radius", "height", "direction" },
            ["UnityEngine.MeshCollider"] = new HashSet<string>
            { "enabled", "isTrigger", "sharedMaterial", "sharedMesh", "convex", "cookingOptions" },

            ["UnityEngine.Rigidbody"] = new HashSet<string>
            { "mass", "linearDamping", "angularDamping", "useGravity", "isKinematic",
              "interpolation", "collisionDetectionMode", "constraints" },

            ["UnityEngine.Rigidbody2D"] = new HashSet<string>
            { "bodyType", "mass", "linearDamping", "angularDamping", "gravityScale",
              "collisionDetectionMode", "sleepMode", "interpolation", "constraints" },

            ["UnityEngine.Camera"] = new HashSet<string>
            { "enabled", "clearFlags", "backgroundColor", "cullingMask",
              "orthographic", "orthographicSize", "fieldOfView", "nearClipPlane",
              "farClipPlane", "rect", "depth", "renderingPath", "targetTexture",
              "useOcclusionCulling", "allowHDR", "allowMSAA", "allowDynamicResolution",
              "targetDisplay" },

            ["UnityEngine.Light"] = new HashSet<string>
            { "enabled", "type", "color", "intensity", "bounceIntensity", "range",
              "spotAngle", "innerSpotAngle", "shadows", "shadowStrength",
              "shadowBias", "shadowNormalBias", "shadowNearPlane", "cullingMask",
              "renderingLayerMask", "cookie", "cookieSize", "flare", "lightmapBakeType" },

            ["UnityEngine.AudioSource"] = new HashSet<string>
            { "enabled", "clip", "outputAudioMixerGroup", "mute", "bypassEffects",
              "bypassListenerEffects", "bypassReverbZones", "playOnAwake", "loop",
              "priority", "volume", "pitch", "panStereo", "spatialBlend", "reverbZoneMix",
              "dopplerLevel", "spread", "rolloffMode", "minDistance", "maxDistance" },

            ["UnityEngine.Canvas"] = new HashSet<string>
            { "enabled", "renderMode", "pixelPerfect", "sortingOrder", "targetDisplay",
              "additionalShaderChannels", "sortingLayerName", "worldCamera",
              "planeDistance", "overridePixelPerfect", "overrideSorting" },

            ["UnityEngine.CanvasGroup"] = new HashSet<string>
            { "alpha", "interactable", "blocksRaycasts", "ignoreParentGroups" },

            ["UnityEngine.UI.Image"] = new HashSet<string>
            { "enabled", "sprite", "color", "material", "raycastTarget", "raycastPadding",
              "maskable", "type", "preserveAspect", "fillCenter", "fillMethod",
              "fillAmount", "fillClockwise", "fillOrigin" },

            ["UnityEngine.UI.RawImage"] = new HashSet<string>
            { "enabled", "texture", "color", "material", "raycastTarget", "raycastPadding",
              "maskable", "uvRect" },

            ["UnityEngine.UI.Text"] = new HashSet<string>
            { "enabled", "text", "font", "fontStyle", "fontSize", "lineSpacing",
              "supportRichText", "alignment", "alignByGeometry", "horizontalOverflow",
              "verticalOverflow", "resizeTextForBestFit", "color", "material",
              "raycastTarget", "raycastPadding", "maskable" },

            ["UnityEngine.UI.Button"] = new HashSet<string>
            { "enabled", "interactable", "transition", "targetGraphic", "colors",
              "spriteState", "animationTriggers", "navigation" },

            ["UnityEngine.UI.Slider"] = new HashSet<string>
            { "enabled", "interactable", "fillRect", "handleRect", "direction",
              "minValue", "maxValue", "wholeNumbers", "value" },

            ["UnityEngine.UI.ScrollRect"] = new HashSet<string>
            { "enabled", "content", "horizontal", "vertical", "movementType",
              "elasticity", "inertia", "decelerationRate", "scrollSensitivity",
              "viewport", "horizontalScrollbar", "verticalScrollbar" },

            ["UnityEngine.AudioListener"] = new HashSet<string>{ "enabled" },
            ["UnityEngine.Animator"] = new HashSet<string>
            { "enabled", "runtimeAnimatorController", "avatar", "applyRootMotion",
              "updateMode", "cullingMode", "speed" },

            ["UnityEngine.ParticleSystem"] = new HashSet<string>
            { "main", "emission", "shape", "velocityOverLifetime",
              "colorOverLifetime", "sizeOverLifetime", "rotationOverLifetime",
              "textureSheetAnimation", "renderer" },
        };

        // Populated from Resources/BugpunchInspectorSchema at first use.
        static Dictionary<string, HashSet<string>> s_userOverrides;
        static bool s_userOverridesLoaded;

        /// <summary>
        /// Should this member be shown for the given component type in normal
        /// (editor-like) mode? Returns true for members in the whitelist, or
        /// for Unity-serialized fields when no whitelist entry exists.
        /// </summary>
        public static bool ShouldShow(Type componentType, MemberInfo member, bool isSerializedField)
        {
            EnsureUserOverridesLoaded();

            var fullName = componentType.FullName;
            if (s_userOverrides != null && s_userOverrides.TryGetValue(fullName, out var userSet))
                return userSet.Contains(member.Name);
            if (s_builtins.TryGetValue(fullName, out var builtinSet))
                return builtinSet.Contains(member.Name);

            // Fallback: Unity-serialization rule. Show public non-static fields
            // and [SerializeField] privates. Also show `enabled` on Behaviours so
            // tester can flip a component off. Hide everything else (noisy
            // derived properties like isVisible / worldToLocalMatrix / bounds).
            if (isSerializedField) return true;
            if (member.Name == "enabled" && member is PropertyInfo && typeof(Behaviour).IsAssignableFrom(componentType))
                return true;
            return false;
        }

        /// <summary>True if the field qualifies as Unity-serialized (public or [SerializeField]).</summary>
        public static bool IsUnitySerialized(FieldInfo f)
        {
            if (f.IsStatic) return false;
            if (f.IsLiteral) return false;            // const
            if (f.IsInitOnly) return false;           // readonly
            if (f.GetCustomAttribute<NonSerializedAttribute>() != null) return false;
            if (f.IsPublic) return true;
            return f.GetCustomAttribute<SerializeField>() != null;
        }

        static void EnsureUserOverridesLoaded()
        {
            if (s_userOverridesLoaded) return;
            s_userOverridesLoaded = true;
            try
            {
                var asset = Resources.Load<TextAsset>("BugpunchInspectorSchema");
                if (asset == null) return;
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string[]>>(asset.text);
                if (parsed == null) return;
                s_userOverrides = new Dictionary<string, HashSet<string>>(parsed.Count);
                foreach (var kv in parsed)
                    s_userOverrides[kv.Key] = new HashSet<string>(kv.Value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.InspectorSchema] Failed to load BugpunchInspectorSchema: {ex.Message}");
            }
        }
    }
}
