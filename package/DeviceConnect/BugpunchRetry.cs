namespace ODDGames.Bugpunch
{
    public static class BugpunchRetry
    {
        /// <summary>
        /// Exponential backoff in milliseconds: <paramref name="initialMs"/>
        /// times 2^(attempt-1), capped at <paramref name="capMs"/>. Attempt is
        /// 1-based; values &lt; 1 are clamped to 1. Uses bitshift to avoid the
        /// <c>Math.Pow</c> float roundtrip and guards against overflow at high
        /// attempt counts. Java/Obj-C++ sides should pin their constants to
        /// the same shape — see docs/upload-manifest.md.
        /// </summary>
        public static int ExponentialBackoff(int attempt, int initialMs, int capMs)
        {
            if (attempt < 1) attempt = 1;
            int shift = attempt - 1;
            if (shift >= 30) return capMs;
            long delay = (long)initialMs << shift;
            return delay > capMs ? capMs : (int)delay;
        }
    }
}
