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
using UnityEngine;
using UnityEngine.UIElements;
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
                    // Turning the SDK ON requires a VALID Bugpunch session. Prompt a
                    // sign-in if there isn't one (missing or expired), and turn the
                    // toggle back OFF if the user cancels or it stays invalid — so
                    // the Editor never connects as an unauthenticated public device.
                    // No-op (stays on) when a valid session is already cached.
                    if (next)
                        BugpunchEditorAuth.EnsureValidSession(valid =>
                        {
                            if (!valid) BugpunchEditorToggle.SetEnabled(false);
                        });
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

            // Right-click context menu: who you are + Sign In / Sign Out.
            element.populateContextMenu = menu =>
            {
                if (BugpunchEditorAuth.HasValidSession)
                {
                    var who = string.IsNullOrEmpty(BugpunchEditorAuth.DisplayName)
                        ? BugpunchEditorAuth.Email : BugpunchEditorAuth.DisplayName;
                    if (!string.IsNullOrEmpty(who))
                        menu.AppendAction($"Signed in as {who}", _ => { }, DropdownMenuAction.Status.Disabled);
                    menu.AppendAction("Sign Out", _ => BugpunchEditorAuth.Logout());
                }
                else
                {
                    menu.AppendAction("Sign In…", _ => BugpunchEditorAuth.EnsureValidSession(__ => { }));
                }
            };
            return element;
        }

        static Texture2D _pillOn;
        static Texture2D _pillOff;

        static MainToolbarContent BuildContent(bool on)
        {
            // Unity 6.3's MainToolbarButton renders its icon in a small square slot,
            // so we use a SQUARE pill-only PNG (the orange toggle switch, no
            // wordmark) + "Bugpunch" as text — the wide combined pill+wordmark PNG
            // the legacy IMGUI path draws gets crushed here. Falls back to a
            // rich-text disc + label if the icon isn't imported yet.
            if (_pillOn == null)
                _pillOn = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/au.com.oddgames.bugpunch/Icons/bugpunch-pill-on.png");
            if (_pillOff == null)
                _pillOff = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/au.com.oddgames.bugpunch/Icons/bugpunch-pill-off.png");

            var icon = on ? _pillOn : _pillOff;
            // Brand wordmark colours: "Bug" white + "punch" orange when on; the
            // whole label dimmed when off. (Unity's toolbar text uses the editor
            // font — the exact brand typeface would have to be baked into an image.)
            var label = on
                ? "<b>Bug<color=#FF6B35>punch</color></b>"
                : "<b><color=#8A8A8A>Bugpunch</color></b>";
            if (icon != null)
                return new MainToolbarContent(label, icon, Tooltip);

            var disc = on ? "#FF6B35" : "#5A5A5A";
            return new MainToolbarContent($"<color={disc}>●</color> {label}", Tooltip);
        }
    }
}
#endif
