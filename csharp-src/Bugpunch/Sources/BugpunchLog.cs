using System;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Centralized logging for the Bugpunch SDK. Use this instead of
    /// <c>Debug.Log("[Bugpunch.X] ...")</c> directly so the prefix format
    /// stays consistent and we can flip log level / strip in release at one
    /// place rather than chasing 100+ ad-hoc call sites.
    ///
    /// <para>The <paramref name="source"/> argument identifies the subsystem
    /// (e.g. "BugpunchClient", "WebRTCStreamer") and gets formatted as
    /// <c>[Bugpunch.{source}]</c>. Pass null/empty to fall back to plain
    /// <c>[Bugpunch]</c>.</para>
    ///
    /// <para>Set <see cref="MinLevel"/> to filter at runtime. Messages below
    /// the threshold are dropped before any formatting cost.</para>
    /// </summary>
    public static class BugpunchLog
    {
        public enum Level { None = 0, Error = 1, Warn = 2, Info = 3, Debug = 4 }

        /// <summary>
        /// Lowest level that will reach the Unity console. Default
        /// <see cref="Level.Info"/>. Lower to <see cref="Level.Warn"/> or
        /// <see cref="Level.Error"/> in noisy production builds; raise to
        /// <see cref="Level.Debug"/> when chasing a bug.
        /// </summary>
        public static Level MinLevel = Level.Info;

        public static void Debug(string source, string message)
        {
            if (MinLevel < Level.Debug) return;
            UnityEngine.Debug.Log(Format(source, message));
        }

        public static void Info(string source, string message)
        {
            if (MinLevel < Level.Info) return;
            UnityEngine.Debug.Log(Format(source, message));
        }

        public static void Warn(string source, string message)
        {
            if (MinLevel < Level.Warn) return;
            UnityEngine.Debug.LogWarning(Format(source, message));
        }

        public static void Error(string source, string message)
        {
            if (MinLevel < Level.Error) return;
            UnityEngine.Debug.LogError(Format(source, message));
        }

        /// <summary>
        /// Log an exception with source context. Always emits at error level
        /// regardless of <see cref="MinLevel"/> being None — exceptions are
        /// crash-investigation evidence.
        /// </summary>
        public static void Exception(string source, Exception ex)
        {
            if (ex == null) return;
            UnityEngine.Debug.LogError(Format(source, ex.GetType().Name + ": " + ex.Message));
            UnityEngine.Debug.LogException(ex);
        }

        static string Format(string source, string message)
            => string.IsNullOrEmpty(source) ? "[Bugpunch] " + message : "[Bugpunch." + source + "] " + message;
    }
}
