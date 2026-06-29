using System.Collections.Generic;
using System.Text;
using ODDGames.Scripting.Ide;
using UnityEngine;

namespace ODDGames.Scripting.Unity
{
    /// <summary>
    /// Shared conventions for Unity scripts so every host presents the same MonoBehaviour-style surface:
    /// the typed context fields injected into a class-less script body, and the standard MonoBehaviour
    /// callbacks a script can declare (for the IDE's catalog / "Events" list). A host appends its own
    /// game-specific events to <see cref="StandardCallbacks"/>.
    /// </summary>
    public static class UnityScriptConventions
    {
        /// <summary>
        /// Field declarations injected ahead of a class-less script body so it reads <c>gameObject</c> and
        /// <c>transform</c> exactly like a MonoBehaviour. Pass as the runtime's <c>injectedFields</c> and as
        /// the IDE's <c>ScriptPreamble</c> (so they autocomplete and aren't flagged "undefined").
        /// </summary>
        public const string InjectedFields = "UnityEngine.GameObject gameObject;UnityEngine.Transform transform;";

        /// <summary>The standard Unity MonoBehaviour callbacks, as IDE catalog descriptors.</summary>
        public static List<IdeEventDescriptor> StandardCallbacks() => new List<IdeEventDescriptor>
        {
            Cb("Start", "", "Runs once, before the first frame, when the object becomes active."),
            Cb("Update", "", "Runs every frame. Use Time.deltaTime for frame-rate-independent motion."),
            Cb("FixedUpdate", "", "Runs every physics step (fixed timestep)."),
            Cb("LateUpdate", "", "Runs every frame, after all Update calls."),
            Cb("OnCollisionEnter", "Collision collision", "This object started touching another collider/rigidbody."),
            Cb("OnCollisionExit", "Collision collision", "This object stopped touching another collider/rigidbody."),
            Cb("OnTriggerEnter", "Collider other", "A collider entered this object's trigger volume."),
            Cb("OnTriggerExit", "Collider other", "A collider left this object's trigger volume."),
            Cb("OnDestroy", "", "Runs when this object is destroyed."),
        };

        private static IdeEventDescriptor Cb(string name, string parameters, string description)
            => new IdeEventDescriptor { Name = name, Parameters = parameters, Description = description, Category = "MonoBehaviour" };

        /// <summary>
        /// A <see cref="ScriptCompileOptions"/> seeded with the implicit usings every Unity script wants
        /// (<c>UnityEngine</c>, <c>System.Collections.Generic</c>) plus any host extras. Use for both the
        /// runtime and the IDE so authored and running code resolve the same types.
        /// </summary>
        public static ScriptCompileOptions DefaultCompileOptions(params string[] extraUsings)
        {
            var opts = new ScriptCompileOptions();
            opts.DefaultUsings.Add("UnityEngine");
            opts.DefaultUsings.Add("System.Collections.Generic");
            if (extraUsings != null)
                foreach (var u in extraUsings)
                    if (!string.IsNullOrEmpty(u)) opts.DefaultUsings.Add(u);
            return opts;
        }

        /// <summary>
        /// A stable, filesystem-safe workspace-relative path for a scene object's editable script file —
        /// e.g. <c>Objects/Crate__1A2B.cs</c> — derived from the object's name + instance id.
        /// </summary>
        public static string WorkspacePath(GameObject go, string folder = "Objects", string extension = ".cs")
        {
            if (go == null) return folder + "/Object" + extension;
            return folder + "/" + Sanitize(go.name) + "__" + go.GetInstanceID().ToString("X") + extension;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Object";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
