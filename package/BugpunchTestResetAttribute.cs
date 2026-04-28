using System;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Marks a static method to be called before each automated test run.
    /// Use this to reset game state, clear caches, reset scores, etc.
    ///
    /// Example:
    /// <code>
    /// [BugpunchTestReset]
    /// public static void ResetForTest()
    /// {
    ///     PlayerPrefs.DeleteAll();
    ///     ScoreManager.Reset();
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class BugpunchTestResetAttribute : Attribute { }
}
