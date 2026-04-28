using System;
using System.Globalization;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Central theme — the single source of truth for every Bugpunch UI surface
    /// (UI Toolkit in C#, native cards on Android / iOS). Colour fields are real
    /// <see cref="Color"/>s so the Unity Inspector gives designers proper colour
    /// pickers (no hex typing). Serialised to the runtime config JSON by
    /// <see cref="BugpunchNative.BuildConfigJson"/> as <c>#RRGGBB</c> /
    /// <c>#RRGGBBAA</c> hex strings — native parses them back.
    ///
    /// Defaults match the shared JSON agreed between the C# and native teams —
    /// any change here must be mirrored on the native side (and vice-versa).
    /// </summary>
    [Serializable]
    public class BugpunchTheme
    {
        [Header("Surfaces")]
        [Tooltip("Background colour of the main card surface (modals, boards, picker).")]
        public Color cardBackground = new Color(0.129f, 0.129f, 0.129f, 1f);

        [Tooltip("Colour of the 1px border around cards + separators.")]
        public Color cardBorder = new Color(0.278f, 0.278f, 0.278f, 1f);

        [Tooltip("Colour of the darkening backdrop behind a modal card.")]
        public Color backdrop = new Color(0f, 0f, 0f, 0.6f);

        [Tooltip("Corner radius (pt / DIP) applied to cards and bubbles.")]
        public int cardRadius = 12;

        [Header("Text")]
        [Tooltip("Primary body / title colour.")]
        public Color textPrimary = Color.white;

        [Tooltip("Secondary subtitle / caption colour.")]
        public Color textSecondary = new Color(0.722f, 0.722f, 0.722f, 1f);

        [Tooltip("Muted / disabled / helper colour.")]
        public Color textMuted = new Color(0.549f, 0.549f, 0.549f, 1f);

        [Header("Accents")]
        [Tooltip("Primary accent (CTAs, vote pill, player chat bubble).")]
        public Color accentPrimary = new Color(0.251f, 0.490f, 0.298f, 1f);

        [Tooltip("Accent for recording / destructive actions.")]
        public Color accentRecord = new Color(0.824f, 0.180f, 0.180f, 1f);

        [Tooltip("Accent for the 'Ask for help' option / chat surfaces.")]
        public Color accentChat = new Color(0.200f, 0.380f, 0.600f, 1f);

        [Tooltip("Accent for the 'Send feedback' option / feedback surfaces.")]
        public Color accentFeedback = new Color(0.251f, 0.490f, 0.298f, 1f);

        [Tooltip("Accent for the 'Record a bug' option / bug report surfaces.")]
        public Color accentBug = new Color(0.580f, 0.220f, 0.220f, 1f);

        [Header("Type Scale (px)")]
        public int fontSizeTitle = 20;
        public int fontSizeBody = 14;
        public int fontSizeCaption = 12;

        [Header("Branding (optional)")]
        [Tooltip("Brand logo shown on the C# UIToolkit dialog header (e.g. crash overlay). Leave null to draw no logo. Native overlays render their own logo if a `bugpunch-logo.png` is dropped into the host app bundle.")]
        public Texture2D brandLogo;

        [Tooltip("Brand name shown next to the logo. Leave blank to omit. Has no effect unless a brandLogo is also set.")]
        public string brandName = "";

        [Header("Icons")]
        [Tooltip("Icon for the 'Record a bug' picker panel. Auto-populated with the packaged default when a BugpunchConfig is created. Replace with your own texture to brand the picker — the default is then unreferenced and won't ship in the build.")]
        public Texture2D iconBug;

        [Tooltip("Icon for the 'Ask for help' picker panel. Same defaulting + replacement behaviour as iconBug.")]
        public Texture2D iconAsk;

        [Tooltip("Icon for the 'Send feedback' picker panel. Same defaulting + replacement behaviour as iconBug.")]
        public Texture2D iconFeedback;

        /// <summary>
        /// Resolve a picker panel icon. Returns whatever asset the field
        /// references — packaged defaults are populated by
        /// <see cref="BugpunchConfig"/>'s editor Reset hook, custom icons
        /// the developer drops on the field replace them. Null is acceptable;
        /// callers fall back to a coloured accent dot.
        /// </summary>
        public Texture2D ResolveIcon(string slot)
        {
            switch (slot)
            {
                case "bug":      return iconBug;
                case "ask":      return iconAsk;
                case "feedback": return iconFeedback;
                default:         return null;
            }
        }

        /// <summary>
        /// Live resolver — every UI surface calls this once per element.
        /// Returns the active config's theme, or a fresh default if the SDK
        /// hasn't initialised yet (keeps Editor previews + early-start UI
        /// from exploding).
        /// </summary>
        public static BugpunchTheme Current
        {
            get
            {
                var client = BugpunchClient.Instance;
                var cfg = client?.Config;
                return cfg?.Theme ?? new BugpunchTheme();
            }
        }

        /// <summary>
        /// Serialise a <see cref="Color"/> to <c>#RRGGBB</c> (opaque) or
        /// <c>#RRGGBBAA</c> (when alpha &lt; 1). Used by
        /// <see cref="BugpunchNative.BuildConfigJson"/> to emit the theme
        /// subtree native expects.
        /// </summary>
        public static string ToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            if (a == 255)
                return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                           + g.ToString("X2", CultureInfo.InvariantCulture)
                           + b.ToString("X2", CultureInfo.InvariantCulture);
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                       + g.ToString("X2", CultureInfo.InvariantCulture)
                       + b.ToString("X2", CultureInfo.InvariantCulture)
                       + a.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
