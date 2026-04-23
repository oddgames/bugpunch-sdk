namespace ODDGames.Bugpunch.DeviceConnect.Database
{
    /// <summary>
    /// Plugin interface for on-device database parsing. Implementations detect
    /// whether a specific library (Siaqodb, Odin, etc.) is available in the
    /// current project and use the real serializer to parse files.
    ///
    /// Plugins are discovered by <see cref="DatabasePluginRegistry"/> via
    /// assembly scanning. To add a new format, implement this interface — no
    /// registration code required.
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
        /// Parse a file at the given path and return a JSON string with the
        /// standard database viewer format:
        /// <code>{"ok":true,"tables":[{"name":"...","columns":[...],"rows":[...]}]}</code>
        /// </summary>
        string Parse(string filePath);
    }
}
