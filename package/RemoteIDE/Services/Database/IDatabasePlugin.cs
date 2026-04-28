namespace ODDGames.Bugpunch.RemoteIDE.Database
{
    /// <summary>
    /// Plugin contract for on-device database parsing. Implementations detect
    /// whether a specific library (Siaqodb, Odin, etc.) is available in the
    /// current project and use the real serializer to parse files.
    ///
    /// Plugins are discovered by <see cref="DatabasePluginRegistry"/> via
    /// assembly scanning — no registration code required.
    ///
    /// Most implementations should derive from <see cref="DatabasePluginBase"/>
    /// rather than implementing this interface directly. The base class
    /// provides reflection helpers and table builders so a typical plugin
    /// is &lt;30 lines.
    /// </summary>
    public interface IDatabasePlugin
    {
        /// <summary>Provider ID matching the server-side provider (e.g. "sqo", "odin").</summary>
        string ProviderId { get; }

        /// <summary>Human-readable name shown in the dashboard.</summary>
        string DisplayName { get; }

        /// <summary>File extensions this plugin handles (e.g. ".sqo").</summary>
        string[] Extensions { get; }

        /// <summary>
        /// Whether the required library is installed in the current project.
        /// Checked once on startup and cached by the registry.
        /// </summary>
        bool IsAvailable();

        /// <summary>
        /// Parse a file at the given path and return the typed result. The
        /// registry takes care of JSON serialization for the wire format.
        /// </summary>
        ParseResult Parse(string filePath);
    }
}
