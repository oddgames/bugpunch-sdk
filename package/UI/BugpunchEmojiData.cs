using System.Collections.Generic;

namespace ODDGames.Bugpunch.UI
{
    /// <summary>
    /// Hard-coded emoji list for the chat composer's emoji picker. This is a
    /// game chat tool, not a full messaging app — ~120 hand-picked emojis
    /// across 8 categories is plenty. Loading a real Unicode emoji dataset
    /// (~3800+ codepoints) at runtime would bloat the package and add a
    /// dependency just so someone can send a sparkles emoji.
    /// </summary>
    internal static class BugpunchEmojiData
    {
        internal struct Category
        {
            public string Name;
            public string[] Emojis;
        }

        /// <summary>
        /// Ordered list of emoji categories. Each shows as a tab in the
        /// picker. Emojis are plain UTF-16 strings — UI Toolkit's font
        /// renderer handles them via the system emoji font (Android:
        /// NotoColorEmoji, iOS: Apple Color Emoji). On Windows Editor the
        /// default TextMeshPro font may render as missing glyphs, which is
        /// acceptable for a fallback picker.
        /// </summary>
        internal static readonly Category[] Categories =
        {
            new Category
            {
                Name = "Smileys",
                Emojis = new[]
                {
                    "😀","😃","😄","😁","😆","😅","🤣","😂","🙂","🙃",
                    "😉","😊","😇","😍","🤩","😘","🤔","🤨","😐","🙄",
                    "😴","😷","🤒","🤕","😵","🥳"
                }
            },
            new Category
            {
                Name = "People",
                Emojis = new[]
                {
                    "👍","👎","👏","🙌","👋","🤝","💪","🙏","🫡","🫶",
                    "👀","🧠","💯","🔥","✨","🎉"
                }
            },
            new Category
            {
                Name = "Food",
                Emojis = new[]
                {
                    "🍎","🍌","🍇","🍓","🍕","🍔","🍟","🌮","🍣","🍩",
                    "🍰","🍪","☕","🍺","🍷","🥤"
                }
            },
            new Category
            {
                Name = "Travel",
                Emojis = new[]
                {
                    "🚗","🚕","🚌","🚲","🏍️","✈️","🚀","🚢","🗺️","🏖️",
                    "🏔️","🗽","🎡","🏠"
                }
            },
            new Category
            {
                Name = "Activities",
                Emojis = new[]
                {
                    "⚽","🏀","🏈","🎾","🏐","🎮","🎲","🎯","🎤","🎧",
                    "🎬","📚","🎨","🎸"
                }
            },
            new Category
            {
                Name = "Objects",
                Emojis = new[]
                {
                    "💻","📱","⌨️","🖱️","🖥️","🎥","📷","📺","🔋","💡",
                    "🔑","🔒","📦","🎁","✏️","📝"
                }
            },
            new Category
            {
                Name = "Symbols",
                Emojis = new[]
                {
                    "❤️","🧡","💛","💚","💙","💜","🖤","🤍","💔","❓",
                    "❗","✅","❌","⚠️","🚫","💤"
                }
            },
            new Category
            {
                Name = "Flags",
                Emojis = new[]
                {
                    "🏳️","🏴","🏁","🚩","🏳️‍🌈","🇺🇸","🇬🇧","🇦🇺","🇯🇵","🇨🇦",
                    "🇩🇪","🇫🇷","🇧🇷","🇰🇷"
                }
            },
        };

        /// <summary>Total emoji count across all categories.</summary>
        internal static int TotalCount
        {
            get
            {
                int n = 0;
                foreach (var c in Categories) n += c.Emojis.Length;
                return n;
            }
        }
    }
}
