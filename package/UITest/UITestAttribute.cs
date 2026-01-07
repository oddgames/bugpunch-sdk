using System;

namespace ODDGames.UITest
{
    public enum TestSeverity
    {
        Blocker,
        Critical,
        Normal,
        Minor,
        Trivial
    }

    public enum TestDataMode
    {
        UseDefined,
        UseCurrent,
        Ask
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class UITestAttribute : Attribute
    {
        public int Scenario { get; set; }

        public string Feature { get; set; }

        public string Story { get; set; }

        public TestSeverity Severity { get; set; } = TestSeverity.Normal;

        public string[] Tags { get; set; }

        public string Description { get; set; }

        public string Owner { get; set; }

        public int TimeoutSeconds { get; set; } = 180;

        public TestDataMode DataMode { get; set; } = TestDataMode.Ask;
    }
}
