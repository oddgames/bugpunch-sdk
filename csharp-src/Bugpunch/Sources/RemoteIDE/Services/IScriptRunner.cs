namespace ODDGames.Bugpunch.RemoteIDE
{
    /// <summary>
    /// Interface for runtime C# script execution.
    /// Implementations must handle compilation and execution without JIT (IL2CPP compatible).
    ///
    /// Default implementation is <see cref="ScriptRunner"/>, backed by ODDGames.Scripting.
    /// </summary>
    public interface IScriptRunner
    {
        /// <summary>
        /// Compile and execute C# code. Returns a JSON result string.
        /// </summary>
        /// <param name="code">C# source code to execute</param>
        /// <returns>JSON: { "ok": true/false, "output": "...", "errors": [...] }</returns>
        string Execute(string code);

        /// <summary>
        /// Compile only (parse + bind + emit) without executing. Used by the editor to
        /// surface live compile diagnostics as Monaco squiggles.
        /// </summary>
        /// <param name="code">C# source code to analyze</param>
        /// <returns>
        /// JSON: { "ok": true, "diagnostics": [{ "line", "column", "length", "severity", "code", "message" }] }
        /// Severity is "error" | "warning" | "info". The array is empty when the source compiles cleanly.
        /// </returns>
        string Diagnose(string code);

        /// <summary>
        /// Whether this runner is available and ready.
        /// </summary>
        bool IsAvailable { get; }
    }
}
