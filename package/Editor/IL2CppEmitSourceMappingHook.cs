using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Adds the `--emit-source-mapping` flag to IL2CPP's command line so
    /// every generated .cpp file under Library/Bee/.../il2cppOutput/cpp/
    /// gets `//&lt;source_info:Foo.cs:17&gt;` comments interleaved with the
    /// transpiled C++. The post-build symbol pipeline parses those into a
    /// {method_name → cs_file:cs_line} JSON which the server uses to
    /// enrich nearest-symbol resolutions for libil2cpp.so frames.
    ///
    /// Restored to the original value after the build so users don't see
    /// the flag mysteriously appearing in their Player Settings.
    ///
    /// First build after enabling: IL2CPP regenerates everything (cache
    /// invalidates because cpp output changes). Subsequent builds are
    /// normal speed.
    /// </summary>
    public static class IL2CppEmitSourceMappingHook
    {
        // Saved per-named-build-target so we restore exactly what the user had.
        // Keyed by NamedBuildTarget.TargetName since that's what Get/Set use.
        static readonly Dictionary<string, string> s_originalArgs = new();
        const string FLAG = "--emit-source-mapping";

        public class Pre : IPreprocessBuildWithReport
        {
            // Run before IL2CPP so the flag reaches the il2cpp.exe invocation.
            public int callbackOrder => -10000;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (!IsIl2cppPlatform(report.summary.platform)) return;
                var nbt = NamedBuildTargetForPlatform(report.summary.platform);
                if (nbt == null) return;
                var current = PlayerSettings.GetAdditionalIl2CppArgs() ?? "";
                s_originalArgs[nbt] = current;
                if (!current.Contains(FLAG))
                {
                    var next = string.IsNullOrWhiteSpace(current) ? FLAG : (current.TrimEnd() + " " + FLAG);
                    PlayerSettings.SetAdditionalIl2CppArgs(next);
                    Debug.Log("[Bugpunch.IL2CppEmitSourceMappingHook] Added --emit-source-mapping to IL2CPP args for this build (will restore after).");
                }
            }
        }

        public class Post : IPostprocessBuildWithReport
        {
            // Run last so we restore even if other post hooks throw — by
            // putting this at the very end, partial failures still leave
            // PlayerSettings untouched.
            public int callbackOrder => 10000;

            public void OnPostprocessBuild(BuildReport report)
            {
                if (!IsIl2cppPlatform(report.summary.platform)) return;
                var nbt = NamedBuildTargetForPlatform(report.summary.platform);
                if (nbt == null) return;
                if (s_originalArgs.TryGetValue(nbt, out var original))
                {
                    PlayerSettings.SetAdditionalIl2CppArgs(original);
                    s_originalArgs.Remove(nbt);
                }
            }
        }

        static bool IsIl2cppPlatform(BuildTarget t) =>
            t == BuildTarget.Android || t == BuildTarget.iOS ||
            t == BuildTarget.StandaloneWindows || t == BuildTarget.StandaloneWindows64 ||
            t == BuildTarget.StandaloneOSX || t == BuildTarget.StandaloneLinux64;

        // PlayerSettings.GetAdditionalIl2CppArgs() ignores the named build
        // target and reads/writes the active player's IL2CPP args, but we
        // still key our save-state by target name so swapping platforms
        // mid-session doesn't lose the original.
        static string NamedBuildTargetForPlatform(BuildTarget t) => t switch
        {
            BuildTarget.Android => "Android",
            BuildTarget.iOS => "iOS",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "Standalone",
            BuildTarget.StandaloneOSX => "Standalone",
            BuildTarget.StandaloneLinux64 => "Standalone",
            _ => null,
        };
    }
}
