// Unity 6.3+ main-toolbar registration for the Bugpunch connection toggle.
//
// Lives as a source file inside the package (not in the precompiled
// ODDGames.Bugpunch.Editor.dll) so the hard IL ref to
// UnityEditor.Toolbars.MainToolbarButton / MainToolbarElement only resolves
// on Unity versions that actually expose those types. Baking the same
// types into the precompiled DLL tripped TypeLoadException on Unity 6000.0
// (see BugpunchEnableToolbarToggle.cs in csharp-src for the post-mortem),
// so the shipped DLL deliberately avoids the reference and we register the
// element here instead. Unity 6.3 hides reflection-injected toolbar
// children under "Unsupported User Elements", so the legacy IMGUI path in
// BugpunchLegacyToolbarToggle is invisible there — this file is what puts
// the button back.
#if UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using ODDGames.BugpunchSdk.Editor;

namespace ODDGames.BugpunchSdk.Editor.Package
{
    public static class BugpunchMainToolbarToggle
    {
        const string ElementPath = "Bugpunch/Connection";
        const string Tooltip =
            "Bugpunch — Editor SDK connection.\n\n" +
            "ON (orange dot): connects to the Bugpunch server, captures " +
            "logs, enables Remote IDE (hierarchy, inspector, console, " +
            "scripts).\n" +
            "OFF (gray dot): SDK disabled in the Editor — no remote " +
            "connection, no log push, no auto-init in Play mode.\n\n" +
            "Click to toggle. Mirrors the 'Bugpunch > Enable Connection' menu item.";

        [MainToolbarElement(
            ElementPath,
            defaultDockPosition = MainToolbarDockPosition.Middle,
            defaultDockIndex = 1)]
        static MainToolbarElement Create()
        {
            MainToolbarButton element = null;
            element = new MainToolbarButton(
                BuildContent(BugpunchEditorToggle.IsEnabled),
                () =>
                {
                    var next = !BugpunchEditorToggle.IsEnabled;
                    BugpunchEditorToggle.SetEnabled(next);
                    if (element != null)
                    {
                        element.content = BuildContent(next);
                        MainToolbar.Refresh(ElementPath);
                    }
                })
            { displayed = true };

            // Keep the button label in sync when the menu item flips state.
            BugpunchEditorToggle.Changed += () =>
            {
                if (element == null) return;
                element.content = BuildContent(BugpunchEditorToggle.IsEnabled);
                MainToolbar.Refresh(ElementPath);
            };
            return element;
        }

        static MainToolbarContent BuildContent(bool on)
        {
            // Rich-text bullet + "Bugpunch" label. Unity autosizes the
            // button to the text width, same way the built-in "Layout"
            // dropdown does. The colored disc is the on/off cue.
            var color = on ? "#FF6B35" : "#5A5A5A";
            return new MainToolbarContent($"<color={color}>●</color> Bugpunch", Tooltip);
        }
    }
}
#endif
