using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Tests
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

        [UnityTest]
        public IEnumerator Index_AccessesArrayElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")
                    .Index(1)
                    .StringValue;

                Assert.AreEqual("one", value);
            });
        }

        [UnityTest]
        public IEnumerator Index_AccessesListElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.IntList")
                    .Index(2)
                    .IntValue;

                Assert.AreEqual(30, value);
            });
        }

        [UnityTest]
        public IEnumerator Index_AccessesDictionaryByKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")
                    .Index("name")
                    .StringValue;

                Assert.AreEqual("TestPlayer", value);
            });
        }

        [UnityTest]
        public IEnumerator Index_ChainsWithProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var name = Search.Reflect("SearchIndexerTests+TestData.Players")
                    .Index(0)
                    .Property("Name")
                    .StringValue;

                Assert.AreEqual("Alice", name);

                var score = Search.Reflect("SearchIndexerTests+TestData.Players")
                    .Index(1)
                    .Property("Score")
                    .IntValue;

                Assert.AreEqual(200, score);
            });
        }

        [UnityTest]
        public IEnumerator Index_DictionaryThenProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var name = Search.Reflect("SearchIndexerTests+TestData.PlayersByName")
                    .Index("bob")
                    .Property("Name")
                    .StringValue;

                Assert.AreEqual("Bob", name);
            });
        }

        [UnityTest]
        public IEnumerator Index_ThrowsOnOutOfRange()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.Throws<System.IndexOutOfRangeException>(() =>
                {
                    var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")
                        .Index(100)
                        .StringValue;
                });
            });
        }

        [UnityTest]
        public IEnumerator Index_ThrowsOnMissingKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.Throws<KeyNotFoundException>(() =>
                {
                    var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")
                        .Index("nonexistent")
                        .StringValue;
                });
            });
        }

        #endregion

        #region Inline Indexer Syntax Tests

        [UnityTest]
        public IEnumerator InlineSyntax_ArrayIndex()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.StringArray[2]")
                    .StringValue;

                Assert.AreEqual("two", value);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_ListIndex()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.IntList[4]")
                    .IntValue;

                Assert.AreEqual(50, value);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_DictionaryKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.StringDict[\"level\"]")
                    .StringValue;

                Assert.AreEqual("5", value);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_SingleQuotedKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData.StringDict['score']")
                    .StringValue;

                Assert.AreEqual("1000", value);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_IndexThenProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var name = Search.Reflect("SearchIndexerTests+TestData.Players[2].Name")
                    .StringValue;

                Assert.AreEqual("Charlie", name);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_DictKeyThenProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var score = Search.Reflect("SearchIndexerTests+TestData.PlayersByName[\"alice\"].Score")
                    .IntValue;

                Assert.AreEqual(100, score);
            });
        }

        [UnityTest]
        public IEnumerator InlineSyntax_ChainedIndexers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // NestedArray[1][2] should be 6
                var value = Search.Reflect("SearchIndexerTests+TestData.NestedArray[1][2]")
                    .IntValue;

                Assert.AreEqual(6, value);
            });
        }

        #endregion

        #region Property() with Indexer Syntax Tests

        [UnityTest]
        public IEnumerator Property_WithIndexerSyntax()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use Property() to navigate to collection then use inline indexer
                var value = Search.Reflect("SearchIndexerTests+TestData")
                    .Property("StringArray[0]")
                    .StringValue;

                Assert.AreEqual("zero", value);
            });
        }

        [UnityTest]
        public IEnumerator Property_WithDictIndexerSyntax()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var value = Search.Reflect("SearchIndexerTests+TestData")
                    .Property("StringDict[\"name\"]")
                    .StringValue;

                Assert.AreEqual("TestPlayer", value);
            });
        }

        #endregion

        #region C# Indexer Syntax Tests (this[])

        [UnityTest]
        public IEnumerator CSharpIndexer_IntegerAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use C# indexer syntax instead of .Index()
                var value = Search.Reflect("SearchIndexerTests+TestData.StringArray")[1]
                    .StringValue;

                Assert.AreEqual("one", value);
            });
        }

        [UnityTest]
        public IEnumerator CSharpIndexer_StringKeyAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use C# indexer syntax for dictionary
                var value = Search.Reflect("SearchIndexerTests+TestData.StringDict")["name"]
                    .StringValue;

                Assert.AreEqual("TestPlayer", value);
            });
        }

        [UnityTest]
        public IEnumerator CSharpIndexer_ChainedWithProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Chain indexer with property access
                var name = Search.Reflect("SearchIndexerTests+TestData")
                    .Property("Players")[1]
                    .Property("Name")
                    .StringValue;

                Assert.AreEqual("Bob", name);
            });
        }

        [UnityTest]
        public IEnumerator CSharpIndexer_NestedAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use indexer syntax for nested array access
                var value = Search.Reflect("SearchIndexerTests+TestData")
                    .Property("NestedArray")[2][0]
                    .IntValue;

                Assert.AreEqual(7, value);
            });
        }

        #endregion

        #region Nested Type Syntax Tests (dot instead of +)

        [UnityTest]
        public IEnumerator NestedType_DotSyntax_AccessesArray()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use dot instead of + for nested type
                var value = Search.Reflect("SearchIndexerTests.TestData.StringArray[0]")
                    .StringValue;

                Assert.AreEqual("zero", value);
            });
        }

        [UnityTest]
        public IEnumerator NestedType_DotSyntax_WithIndexMethod()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use dot instead of + with Index() method
                var value = Search.Reflect("SearchIndexerTests.TestData.IntList")
                    .Index(3)
                    .IntValue;

                Assert.AreEqual(40, value);
            });
        }

        [UnityTest]
        public IEnumerator NestedType_DotSyntax_ChainedAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use dot syntax with chained property and indexer
                var name = Search.Reflect("SearchIndexerTests.TestData.Players[1].Name")
                    .StringValue;

                Assert.AreEqual("Bob", name);
            });
        }

        #endregion

        #region SetValue Tests

        [UnityTest]
        public IEnumerator SetValue_SetsFieldViaProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Save original value
                var originalScore = TestData.Players[0].Score;

                // Set value via Property chain
                Search.Reflect("SearchIndexerTests.TestData.Players[0]")
                    .Property("Score")
                    .SetValue(999);

                Assert.AreEqual(999, TestData.Players[0].Score);

                // Restore
                TestData.Players[0].Score = originalScore;
            });
        }

        [UnityTest]
        public IEnumerator SetValue_SetsFieldViaIndexAndProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

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
            });
        }

        [UnityTest]
        public IEnumerator SetValue_SetsFieldViaInlineIndexer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Save original value
                var originalScore = TestData.Players[2].Score;

                // Set value via inline indexer in path
                Search.Reflect("SearchIndexerTests.TestData.Players[2]")
                    .Property("Score")
                    .SetValue(777);

                Assert.AreEqual(777, TestData.Players[2].Score);

                // Restore
                TestData.Players[2].Score = originalScore;
            });
        }

        [UnityTest]
        public IEnumerator SetValue_ThrowsOnDirectStaticPath()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Attempting to set value directly on Static() result should fail
                Assert.Throws<System.InvalidOperationException>(() =>
                {
                    Search.Reflect("SearchIndexerTests.TestData.Players[0]")
                        .SetValue(null);
                });
            });
        }

        [UnityTest]
        public IEnumerator SetValue_ChainedPropertyAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

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
            });
        }

        #endregion
    }
}
