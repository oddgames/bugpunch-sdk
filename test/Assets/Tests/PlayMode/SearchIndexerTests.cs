using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ODDGames.Bugpunch;

namespace ODDGames.Bugpunch.Tests
{
    /// <summary>
    /// Tests for Search indexer support - Index() method and inline [index] syntax.
    /// </summary>
    [TestFixture]
    public class SearchIndexerTests
    {
        #region Test Data Classes

        // Static test class for indexer tests
        public static class TestData
        {
            public static string[] StringArray = new[] { "zero", "one", "two", "three" };
            public static List<int> IntList = new List<int> { 10, 20, 30, 40, 50 };
            public static Dictionary<string, string> StringDict = new Dictionary<string, string>
            {
                { "name", "TestPlayer" },
                { "level", "5" },
                { "score", "1000" }
            };
            public static List<PlayerData> Players = new List<PlayerData>
            {
                new PlayerData { Name = "Alice", Score = 100 },
                new PlayerData { Name = "Bob", Score = 200 },
                new PlayerData { Name = "Charlie", Score = 300 }
            };
            public static Dictionary<string, PlayerData> PlayersByName = new Dictionary<string, PlayerData>
            {
                { "alice", new PlayerData { Name = "Alice", Score = 100 } },
                { "bob", new PlayerData { Name = "Bob", Score = 200 } }
            };
            public static int[][] NestedArray = new[]
            {
                new[] { 1, 2, 3 },
                new[] { 4, 5, 6 },
                new[] { 7, 8, 9 }
            };
        }

        public class PlayerData
        {
            public string Name;
            public int Score;
        }

        #endregion

        #region Index() Method Tests

        [Test]
        public async Task Index_AccessesArrayElement()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")
                .Index(1)
                .GetValue<string>();

            Assert.AreEqual("one", value);
        }

        [Test]
        public async Task Index_AccessesListElement()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.IntList")
                .Index(2)
                .GetValue<int>();

            Assert.AreEqual(30, value);
        }

        [Test]
        public async Task Index_AccessesDictionaryByKey()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")
                .Index("name")
                .GetValue<string>();

            Assert.AreEqual("TestPlayer", value);
        }

        [Test]
        public async Task Index_ChainsWithProperty()
        {
            await Async.DelayFrames(1);

            var name = Search.Reflect("SearchIndexerTests+TestData.Players")
                .Index(0)
                .Property("Name")
                .GetValue<string>();

            Assert.AreEqual("Alice", name);

            var score = Search.Reflect("SearchIndexerTests+TestData.Players")
                .Index(1)
                .Property("Score")
                .GetValue<int>();

            Assert.AreEqual(200, score);
        }

        [Test]
        public async Task Index_DictionaryThenProperty()
        {
            await Async.DelayFrames(1);

            var name = Search.Reflect("SearchIndexerTests+TestData.PlayersByName")
                .Index("bob")
                .Property("Name")
                .GetValue<string>();

            Assert.AreEqual("Bob", name);
        }

        [Test]
        public async Task Index_ThrowsOnOutOfRange()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.IndexOutOfRangeException>(() =>
            {
                var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")
                    .Index(100)
                    .GetValue<string>();
            });
        }

        [Test]
        public async Task Index_ThrowsOnMissingKey()
        {
            await Async.DelayFrames(1);

            Assert.Throws<KeyNotFoundException>(() =>
            {
                var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")
                    .Index("nonexistent")
                    .GetValue<string>();
            });
        }

        #endregion

        #region Inline Indexer Syntax Tests

        [Test]
        public async Task InlineSyntax_ArrayIndex()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.StringArray[2]")
                .GetValue<string>();

            Assert.AreEqual("two", value);
        }

        [Test]
        public async Task InlineSyntax_ListIndex()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.IntList[4]")
                .GetValue<int>();

            Assert.AreEqual(50, value);
        }

        [Test]
        public async Task InlineSyntax_DictionaryKey()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.StringDict[\"level\"]")
                .GetValue<string>();

            Assert.AreEqual("5", value);
        }

        [Test]
        public async Task InlineSyntax_SingleQuotedKey()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData.StringDict['score']")
                .GetValue<string>();

            Assert.AreEqual("1000", value);
        }

        [Test]
        public async Task InlineSyntax_IndexThenProperty()
        {
            await Async.DelayFrames(1);

            var name = Search.Reflect("SearchIndexerTests+TestData.Players[2].Name")
                .GetValue<string>();

            Assert.AreEqual("Charlie", name);
        }

        [Test]
        public async Task InlineSyntax_DictKeyThenProperty()
        {
            await Async.DelayFrames(1);

            var score = Search.Reflect("SearchIndexerTests+TestData.PlayersByName[\"alice\"].Score")
                .GetValue<int>();

            Assert.AreEqual(100, score);
        }

        [Test]
        public async Task InlineSyntax_ChainedIndexers()
        {
            await Async.DelayFrames(1);

            // NestedArray[1][2] should be 6
            var value = Search.Reflect("SearchIndexerTests+TestData.NestedArray[1][2]")
                .GetValue<int>();

            Assert.AreEqual(6, value);
        }

        #endregion

        #region Property() with Indexer Syntax Tests

        [Test]
        public async Task Property_WithIndexerSyntax()
        {
            await Async.DelayFrames(1);

            // Use Property() to navigate to collection then use inline indexer
            var value = Search.Reflect("SearchIndexerTests+TestData")
                .Property("StringArray[0]")
                .GetValue<string>();

            Assert.AreEqual("zero", value);
        }

        [Test]
        public async Task Property_WithDictIndexerSyntax()
        {
            await Async.DelayFrames(1);

            var value = Search.Reflect("SearchIndexerTests+TestData")
                .Property("StringDict[\"name\"]")
                .GetValue<string>();

            Assert.AreEqual("TestPlayer", value);
        }

        #endregion

        #region C# Indexer Syntax Tests (this[])

        [Test]
        public async Task CSharpIndexer_IntegerAccess()
        {
            await Async.DelayFrames(1);

            // Use C# indexer syntax instead of .Index()
            var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")[1]
                .GetValue<string>();

            Assert.AreEqual("one", value);
        }

        [Test]
        public async Task CSharpIndexer_StringKeyAccess()
        {
            await Async.DelayFrames(1);

            // Use C# indexer syntax for dictionary
            var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")["name"]
                .GetValue<string>();

            Assert.AreEqual("TestPlayer", value);
        }

        [Test]
        public async Task CSharpIndexer_ChainedWithProperty()
        {
            await Async.DelayFrames(1);

            // Chain indexer with property access
            var name = Search.Reflect("SearchIndexerTests+TestData")
                .Property("Players")[1]
                .Property("Name")
                .GetValue<string>();

            Assert.AreEqual("Bob", name);
        }

        [Test]
        public async Task CSharpIndexer_NestedAccess()
        {
            await Async.DelayFrames(1);

            // Use indexer syntax for nested array access
            var value = Search.Reflect("SearchIndexerTests+TestData")
                .Property("NestedArray")[2][0]
                .GetValue<int>();

            Assert.AreEqual(7, value);
        }

        #endregion

        #region Nested Type Syntax Tests (dot instead of +)

        [Test]
        public async Task NestedType_DotSyntax_AccessesArray()
        {
            await Async.DelayFrames(1);

            // Use dot instead of + for nested type
            var value = Search.Reflect("SearchIndexerTests.TestData.StringArray[0]")
                .GetValue<string>();

            Assert.AreEqual("zero", value);
        }

        [Test]
        public async Task NestedType_DotSyntax_WithIndexMethod()
        {
            await Async.DelayFrames(1);

            // Use dot instead of + with Index() method
            var value = Search.Reflect("SearchIndexerTests.TestData.IntList")
                .Index(3)
                .GetValue<int>();

            Assert.AreEqual(40, value);
        }

        [Test]
        public async Task NestedType_DotSyntax_ChainedAccess()
        {
            await Async.DelayFrames(1);

            // Use dot syntax with chained property and indexer
            var name = Search.Reflect("SearchIndexerTests.TestData.Players[1].Name")
                .GetValue<string>();

            Assert.AreEqual("Bob", name);
        }

        #endregion

        #region SetValue Tests

        [Test]
        public async Task SetValue_SetsFieldViaProperty()
        {
            await Async.DelayFrames(1);

            // Save original value
            var originalScore = TestData.Players[0].Score;

            // Set value via Property chain
            Search.Reflect("SearchIndexerTests.TestData.Players[0]")
                .Property("Score")
                .SetValue(999);

            Assert.AreEqual(999, TestData.Players[0].Score);

            // Restore
            TestData.Players[0].Score = originalScore;
        }

        [Test]
        public async Task SetValue_SetsFieldViaIndexAndProperty()
        {
            await Async.DelayFrames(1);

            // Save original value
            var originalName = TestData.Players[1].Name;

            // Set value via Index() then Property()
            Search.Reflect("SearchIndexerTests.TestData.Players")
                .Index(1)
                .Property("Name")
                .SetValue("Modified");

            Assert.AreEqual("Modified", TestData.Players[1].Name);

            // Restore
            TestData.Players[1].Name = originalName;
        }

        [Test]
        public async Task SetValue_SetsFieldViaInlineIndexer()
        {
            await Async.DelayFrames(1);

            // Save original value
            var originalScore = TestData.Players[2].Score;

            // Set value via inline indexer in path
            Search.Reflect("SearchIndexerTests.TestData.Players[2]")
                .Property("Score")
                .SetValue(777);

            Assert.AreEqual(777, TestData.Players[2].Score);

            // Restore
            TestData.Players[2].Score = originalScore;
        }

        [Test]
        public async Task SetValue_ThrowsOnDirectStaticPath()
        {
            await Async.DelayFrames(1);

            // Attempting to set value directly on Static() result should fail
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                Search.Reflect("SearchIndexerTests.TestData.Players[0]")
                    .SetValue(null);
            });
        }

        [Test]
        public async Task SetValue_ChainedPropertyAccess()
        {
            await Async.DelayFrames(1);

            // Save original
            var originalName = TestData.PlayersByName["alice"].Name;

            // Set via dictionary index and property
            Search.Reflect("SearchIndexerTests.TestData.PlayersByName")
                .Index("alice")
                .Property("Name")
                .SetValue("Alicia");

            Assert.AreEqual("Alicia", TestData.PlayersByName["alice"].Name);

            // Restore
            TestData.PlayersByName["alice"].Name = originalName;
        }

        #endregion
    }
}
