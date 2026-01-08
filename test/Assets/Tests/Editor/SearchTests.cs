using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// Unity Test Framework tests for the Search API.
    /// Tests all static factory methods, chaining methods, and hierarchy queries.
    /// </summary>
    [TestFixture]
    public class SearchTests
    {
        private GameObject _testRoot;
        private List<GameObject> _testObjects;

        [SetUp]
        public void SetUp()
        {
            _testObjects = new List<GameObject>();
            _testRoot = new GameObject("TestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _testObjects)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            if (_testRoot != null)
                Object.DestroyImmediate(_testRoot);
        }

        private GameObject CreateTestObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? _testRoot.transform);
            _testObjects.Add(go);
            return go;
        }

        private GameObject CreateButtonWithText(string name, string text, Transform parent = null)
        {
            var go = CreateTestObject(name, parent);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>();
            go.AddComponent<Button>();

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            _testObjects.Add(textGO);

            return go;
        }

        #region Static Factory Tests - ByName

        [Test]
        public void ByName_ExactMatch_ReturnsTrue()
        {
            var go = CreateTestObject("ExactButton");
            var search = UITestBehaviour.Search.ByName("ExactButton");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByName_ExactMatch_WrongName_ReturnsFalse()
        {
            var go = CreateTestObject("OtherButton");
            var search = UITestBehaviour.Search.ByName("ExactButton");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void ByName_WildcardPrefix_ReturnsTrue()
        {
            var go = CreateTestObject("PrefixButton");
            var search = UITestBehaviour.Search.ByName("Prefix*");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByName_WildcardSuffix_ReturnsTrue()
        {
            var go = CreateTestObject("ButtonSuffix");
            var search = UITestBehaviour.Search.ByName("*Suffix");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByName_WildcardContains_ReturnsTrue()
        {
            var go = CreateTestObject("SomeMiddleText");
            var search = UITestBehaviour.Search.ByName("*Middle*");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByName_GlobPattern_ReturnsTrue()
        {
            var go = CreateTestObject("btn_play_icon");
            var search = UITestBehaviour.Search.ByName("btn_*_icon");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByName_GlobPattern_WrongPattern_ReturnsFalse()
        {
            var go = CreateTestObject("btn_play_button");
            var search = UITestBehaviour.Search.ByName("btn_*_icon");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByType

        [Test]
        public void ByType_Generic_WithComponent_ReturnsTrue()
        {
            var go = CreateTestObject("ButtonObj");
            go.AddComponent<Button>();
            var search = UITestBehaviour.Search.ByType<Button>();
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByType_Generic_WithoutComponent_ReturnsFalse()
        {
            var go = CreateTestObject("EmptyObj");
            var search = UITestBehaviour.Search.ByType<Button>();
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void ByType_String_WithComponent_ReturnsTrue()
        {
            var go = CreateTestObject("ToggleObj");
            go.AddComponent<Toggle>();
            var search = UITestBehaviour.Search.ByType("Toggle");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByType_String_WithoutComponent_ReturnsFalse()
        {
            var go = CreateTestObject("EmptyObj");
            var search = UITestBehaviour.Search.ByType("Toggle");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByText

        [Test]
        public void ByText_ExactMatch_ReturnsTrue()
        {
            var go = CreateButtonWithText("Btn", "Click Me");
            var search = UITestBehaviour.Search.ByText("Click Me");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByText_WildcardPrefix_ReturnsTrue()
        {
            var go = CreateButtonWithText("Btn", "Click Me");
            var search = UITestBehaviour.Search.ByText("Click*");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByText_WildcardSuffix_ReturnsTrue()
        {
            var go = CreateButtonWithText("Btn", "Click Me");
            var search = UITestBehaviour.Search.ByText("*Me");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByText_NoTextComponent_ReturnsFalse()
        {
            var go = CreateTestObject("NoText");
            var search = UITestBehaviour.Search.ByText("Click Me");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByPath

        [Test]
        public void ByPath_MatchesHierarchy_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            var search = UITestBehaviour.Search.ByPath("*Parent*/*Child*");
            Assert.IsTrue(search.Matches(child));
        }

        [Test]
        public void ByPath_WrongHierarchy_ReturnsFalse()
        {
            var parent = CreateTestObject("OtherPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            var search = UITestBehaviour.Search.ByPath("*Parent*/*Child*");
            Assert.IsFalse(search.Matches(child));
        }

        #endregion

        #region Static Factory Tests - ByTag

        [Test]
        public void ByTag_MatchingTag_ReturnsTrue()
        {
            var go = CreateTestObject("TaggedObj");
            // "Untagged" is a default Unity tag that always exists
            go.tag = "Untagged";
            var search = UITestBehaviour.Search.ByTag("Untagged");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByTag_DifferentTag_ReturnsFalse()
        {
            var go = CreateTestObject("TaggedObj");
            go.tag = "Untagged";
            var search = UITestBehaviour.Search.ByTag("MainCamera");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Static Factory Tests - BySprite

        [Test]
        public void BySprite_MatchingSpriteName_ReturnsTrue()
        {
            var go = CreateTestObject("SpriteObj");
            var image = go.AddComponent<Image>();
            // Create a temporary sprite for testing
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "btn_play_icon";
            image.sprite = sprite;

            var search = UITestBehaviour.Search.BySprite("btn_*_icon");
            Assert.IsTrue(search.Matches(go));

            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void BySprite_NoSprite_ReturnsFalse()
        {
            var go = CreateTestObject("NoSpriteObj");
            go.AddComponent<Image>(); // Image without sprite
            var search = UITestBehaviour.Search.BySprite("any_sprite");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void BySprite_WrongSpriteName_ReturnsFalse()
        {
            var go = CreateTestObject("SpriteObj");
            var image = go.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "other_sprite";
            image.sprite = sprite;

            var search = UITestBehaviour.Search.BySprite("btn_*_icon");
            Assert.IsFalse(search.Matches(go));

            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        #endregion

        #region Static Factory Tests - ByAny

        [Test]
        public void ByAny_MatchesName_ReturnsTrue()
        {
            var go = CreateTestObject("TargetName");
            var search = UITestBehaviour.Search.ByAny("TargetName");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByAny_MatchesText_ReturnsTrue()
        {
            var go = CreateButtonWithText("SomeButton", "TargetText");
            var search = UITestBehaviour.Search.ByAny("TargetText");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void ByAny_NoMatch_ReturnsFalse()
        {
            var go = CreateButtonWithText("OtherButton", "OtherText");
            var search = UITestBehaviour.Search.ByAny("Target");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Chaining Tests

        [Test]
        public void Chain_TypeAndName_BothMatch_ReturnsTrue()
        {
            var go = CreateTestObject("SpecificButton");
            go.AddComponent<Button>();
            var search = UITestBehaviour.Search.ByType<Button>().Name("Specific*");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Chain_TypeAndName_TypeMismatch_ReturnsFalse()
        {
            var go = CreateTestObject("SpecificButton");
            go.AddComponent<Toggle>(); // Wrong type
            var search = UITestBehaviour.Search.ByType<Button>().Name("Specific*");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void Chain_TypeAndName_NameMismatch_ReturnsFalse()
        {
            var go = CreateTestObject("OtherButton");
            go.AddComponent<Button>();
            var search = UITestBehaviour.Search.ByType<Button>().Name("Specific*");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void Chain_NameAndText_BothMatch_ReturnsTrue()
        {
            var go = CreateButtonWithText("SubmitBtn", "Submit");
            var search = UITestBehaviour.Search.ByName("*Btn").Text("Submit");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Chain_TypeTextName_AllMatch_ReturnsTrue()
        {
            var go = CreateButtonWithText("ConfirmButton", "OK");
            go.AddComponent<Button>();
            var search = UITestBehaviour.Search.ByType<Button>().Text("OK").Name("*Confirm*");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Chain_TypeAndSprite_BothMatch_ReturnsTrue()
        {
            var go = CreateTestObject("ImageObj");
            var image = go.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "icon_settings";
            image.sprite = sprite;

            var search = UITestBehaviour.Search.ByType<Image>().Sprite("icon_*");
            Assert.IsTrue(search.Matches(go));

            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        #endregion

        #region With Predicate Tests

        [Test]
        public void With_PredicateTrue_ReturnsTrue()
        {
            var go = CreateTestObject("InteractableBtn");
            var button = go.AddComponent<Button>();
            button.interactable = true;
            var search = UITestBehaviour.Search.ByType<Button>().With<Button>(b => b.interactable);
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void With_PredicateFalse_ReturnsFalse()
        {
            var go = CreateTestObject("DisabledBtn");
            var button = go.AddComponent<Button>();
            button.interactable = false;
            var search = UITestBehaviour.Search.ByType<Button>().With<Button>(b => b.interactable);
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void With_Toggle_IsOnTrue_ReturnsTrue()
        {
            var go = CreateTestObject("OnToggle");
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = true;
            var search = UITestBehaviour.Search.ByType<Toggle>().With<Toggle>(t => t.isOn);
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void With_Toggle_IsOnFalse_ReturnsFalse()
        {
            var go = CreateTestObject("OffToggle");
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = false;
            var search = UITestBehaviour.Search.ByType<Toggle>().With<Toggle>(t => t.isOn);
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void With_Slider_ValueCheck_ReturnsTrue()
        {
            var go = CreateTestObject("SliderObj");
            var slider = go.AddComponent<Slider>();
            slider.value = 0.75f;
            var search = UITestBehaviour.Search.ByType<Slider>().With<Slider>(s => s.value > 0.5f);
            Assert.IsTrue(search.Matches(go));
        }

        #endregion

        #region Where Predicate Tests

        [Test]
        public void Where_CustomPredicate_ReturnsTrue()
        {
            var go = CreateTestObject("CustomTarget");
            var search = UITestBehaviour.Search.Where(g => g.name == "CustomTarget");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Where_CustomPredicate_ReturnsFalse()
        {
            var go = CreateTestObject("OtherObject");
            var search = UITestBehaviour.Search.Where(g => g.name == "CustomTarget");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void Where_ComplexPredicate_ReturnsTrue()
        {
            var go = CreateTestObject("ActiveEnabled");
            go.SetActive(true);
            var search = UITestBehaviour.Search.Where(g => g.activeInHierarchy && g.name.Contains("Active"));
            Assert.IsTrue(search.Matches(go));
        }

        #endregion

        #region Not (Negation) Tests

        [Test]
        public void Not_Name_ExcludesMatch()
        {
            var go1 = CreateTestObject("NotThisOne");
            var go2 = CreateTestObject("NotOther");

            var search = UITestBehaviour.Search.ByName("Not*").Not.Name("NotThisOne");

            Assert.IsFalse(search.Matches(go1), "Should NOT match NotThisOne");
            Assert.IsTrue(search.Matches(go2), "Should match NotOther");
        }

        [Test]
        public void Not_Type_ExcludesComponent()
        {
            var goWithButton = CreateTestObject("WithButton");
            goWithButton.AddComponent<Button>();

            var goWithToggle = CreateTestObject("WithToggle");
            goWithToggle.AddComponent<Toggle>();

            var search = UITestBehaviour.Search.ByName("With*").Not.Type<Button>();

            Assert.IsFalse(search.Matches(goWithButton), "Should NOT match object with Button");
            Assert.IsTrue(search.Matches(goWithToggle), "Should match object with Toggle");
        }

        #endregion

        #region Hierarchy Tests - HasParent

        [Test]
        public void HasParent_Search_DirectParentMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("ChildButton").HasParent(UITestBehaviour.Search.ByName("ParentPanel"));
            Assert.IsTrue(search.Matches(child));
        }

        [Test]
        public void HasParent_Search_WrongParent_ReturnsFalse()
        {
            var parent = CreateTestObject("OtherPanel");
            var child = CreateTestObject("ChildButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("ChildButton").HasParent(UITestBehaviour.Search.ByName("ParentPanel"));
            Assert.IsFalse(search.Matches(child));
        }

        [Test]
        public void HasParent_String_DirectParentMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("ChildButton").HasParent("ParentPanel");
            Assert.IsTrue(search.Matches(child));
        }

        [Test]
        public void HasParent_String_Wildcard_ReturnsTrue()
        {
            var parent = CreateTestObject("MyParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("ChildButton").HasParent("*Parent*");
            Assert.IsTrue(search.Matches(child));
        }

        #endregion

        #region Hierarchy Tests - HasAncestor

        [Test]
        public void HasAncestor_Search_GrandparentMatches_ReturnsTrue()
        {
            var grandparent = CreateTestObject("RootPanel");
            var parent = CreateTestObject("MiddlePanel", grandparent.transform);
            var child = CreateTestObject("DeepButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("DeepButton").HasAncestor(UITestBehaviour.Search.ByName("RootPanel"));
            Assert.IsTrue(search.Matches(child));
        }

        [Test]
        public void HasAncestor_Search_NoMatchingAncestor_ReturnsFalse()
        {
            var grandparent = CreateTestObject("OtherRoot");
            var parent = CreateTestObject("MiddlePanel", grandparent.transform);
            var child = CreateTestObject("DeepButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("DeepButton").HasAncestor(UITestBehaviour.Search.ByName("RootPanel"));
            Assert.IsFalse(search.Matches(child));
        }

        [Test]
        public void HasAncestor_String_DeepHierarchy_ReturnsTrue()
        {
            var root = CreateTestObject("AncestorRoot");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("Level3", level2.transform);

            var search = UITestBehaviour.Search.ByName("Level3").HasAncestor("AncestorRoot");
            Assert.IsTrue(search.Matches(level3));
        }

        #endregion

        #region Hierarchy Tests - HasChild

        [Test]
        public void HasChild_Search_DirectChildMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);

            var search = UITestBehaviour.Search.ByName("ParentPanel").HasChild(UITestBehaviour.Search.ByName("ChildButton"));
            Assert.IsTrue(search.Matches(parent));
        }

        [Test]
        public void HasChild_Search_NoMatchingChild_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("OtherChild", parent.transform);

            var search = UITestBehaviour.Search.ByName("ParentPanel").HasChild(UITestBehaviour.Search.ByName("ChildButton"));
            Assert.IsFalse(search.Matches(parent));
        }

        [Test]
        public void HasChild_String_Wildcard_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("SpecificChildBtn", parent.transform);

            var search = UITestBehaviour.Search.ByName("ParentPanel").HasChild("*Child*");
            Assert.IsTrue(search.Matches(parent));
        }

        #endregion

        #region Hierarchy Tests - HasDescendant

        [Test]
        public void HasDescendant_Search_GrandchildMatches_ReturnsTrue()
        {
            var root = CreateTestObject("RootPanel");
            var middle = CreateTestObject("MiddlePanel", root.transform);
            var deep = CreateTestObject("DeepButton", middle.transform);

            var search = UITestBehaviour.Search.ByName("RootPanel").HasDescendant(UITestBehaviour.Search.ByName("DeepButton"));
            Assert.IsTrue(search.Matches(root));
        }

        [Test]
        public void HasDescendant_Search_NoMatchingDescendant_ReturnsFalse()
        {
            var root = CreateTestObject("RootPanel");
            var middle = CreateTestObject("MiddlePanel", root.transform);
            var deep = CreateTestObject("OtherButton", middle.transform);

            var search = UITestBehaviour.Search.ByName("RootPanel").HasDescendant(UITestBehaviour.Search.ByName("DeepButton"));
            Assert.IsFalse(search.Matches(root));
        }

        [Test]
        public void HasDescendant_String_DeepHierarchy_ReturnsTrue()
        {
            var root = CreateTestObject("DescendantRoot");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("TargetDescendant", level2.transform);

            var search = UITestBehaviour.Search.ByName("DescendantRoot").HasDescendant("*Target*");
            Assert.IsTrue(search.Matches(root));
        }

        #endregion

        #region Implicit Conversion Tests

        [Test]
        public void ImplicitConversion_String_ToSearch_Works()
        {
            var go = CreateButtonWithText("Btn", "Target Text");
            UITestBehaviour.Search search = "Target Text";
            Assert.IsTrue(search.Matches(go));
        }

        #endregion

        #region Edge Cases

        [Test]
        public void Search_NullGameObject_ReturnsFalse()
        {
            var search = UITestBehaviour.Search.ByName("Any");
            Assert.IsFalse(search.Matches(null));
        }

        [Test]
        public void Search_InactiveGameObject_StillMatches()
        {
            var go = CreateTestObject("InactiveObject");
            go.SetActive(false);
            var search = UITestBehaviour.Search.ByName("InactiveObject");
            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Search_EmptyName_ReturnsFalse()
        {
            // Empty pattern returns false by design - searching for "" is not meaningful
            var go = CreateTestObject("");
            var search = UITestBehaviour.Search.ByName("");
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void Chain_MultipleConditions_AllMustMatch()
        {
            var go = CreateButtonWithText("SubmitButton", "Submit");
            go.AddComponent<Button>();

            // All conditions match
            var searchAllMatch = UITestBehaviour.Search
                .ByType<Button>()
                .Name("*Button")
                .Text("Submit");
            Assert.IsTrue(searchAllMatch.Matches(go));

            // One condition fails
            var searchOneFails = UITestBehaviour.Search
                .ByType<Button>()
                .Name("*Button")
                .Text("Cancel");
            Assert.IsFalse(searchOneFails.Matches(go));
        }

        #endregion

        #region Advanced Edge Cases

        [Test]
        public void ByName_SpecialCharacters_MatchesExactly()
        {
            var go = CreateTestObject("Button (1)");
            Assert.IsTrue(UITestBehaviour.Search.ByName("Button (1)").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("Button (*)").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("*(*)*").Matches(go));
        }

        [Test]
        public void ByName_UnicodeCharacters_Matches()
        {
            var go = CreateTestObject("Кнопка_按钮_ボタン");
            Assert.IsTrue(UITestBehaviour.Search.ByName("Кнопка_按钮_ボタン").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("*按钮*").Matches(go));
        }

        [Test]
        public void ByName_CaseInsensitive_Matches()
        {
            var go = CreateTestObject("MyButton");
            Assert.IsTrue(UITestBehaviour.Search.ByName("mybutton").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("MYBUTTON").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("MyBuTtOn").Matches(go));
        }

        [Test]
        public void ByName_MultipleWildcards_Matches()
        {
            var go = CreateTestObject("Prefix_Middle_Suffix");
            Assert.IsTrue(UITestBehaviour.Search.ByName("*_*_*").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("Prefix*Suffix").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("*Middle*").Matches(go));
            Assert.IsFalse(UITestBehaviour.Search.ByName("*Other*").Matches(go));
        }

        [Test]
        public void ByName_ConsecutiveWildcards_Matches()
        {
            var go = CreateTestObject("TestButton");
            Assert.IsTrue(UITestBehaviour.Search.ByName("**Button").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("Test**").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("***").Matches(go));
        }

        [Test]
        public void ByName_OnlyWildcard_MatchesAny()
        {
            var go = CreateTestObject("AnyName");
            Assert.IsTrue(UITestBehaviour.Search.ByName("*").Matches(go));
        }

        [Test]
        public void ByText_NestedTextComponents_FindsInChildren()
        {
            var parent = CreateTestObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);
            var tmp = child.AddComponent<TextMeshProUGUI>();
            tmp.text = "Nested Text";
            _testObjects.Add(child);

            // Should find text in children
            Assert.IsTrue(UITestBehaviour.Search.ByText("Nested Text").Matches(parent));
        }

        [Test]
        public void ByText_MultipleTextComponents_MatchesAny()
        {
            var go = CreateTestObject("MultiText");

            var child1 = new GameObject("Text1");
            child1.transform.SetParent(go.transform);
            var tmp1 = child1.AddComponent<TextMeshProUGUI>();
            tmp1.text = "First";
            _testObjects.Add(child1);

            var child2 = new GameObject("Text2");
            child2.transform.SetParent(go.transform);
            var tmp2 = child2.AddComponent<TextMeshProUGUI>();
            tmp2.text = "Second";
            _testObjects.Add(child2);

            // Should find any text component in children
            Assert.IsTrue(UITestBehaviour.Search.ByText("First").Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByText("Second").Matches(go));
            Assert.IsFalse(UITestBehaviour.Search.ByText("Third").Matches(go));
        }

        [Test]
        public void Chain_FiveConditions_AllMustMatch()
        {
            var go = CreateTestObject("SubmitButton");
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.interactable = true;
            go.tag = "Untagged";

            var textChild = new GameObject("Text");
            textChild.transform.SetParent(go.transform);
            var tmp = textChild.AddComponent<TextMeshProUGUI>();
            tmp.text = "Submit";
            _testObjects.Add(textChild);

            var search = UITestBehaviour.Search
                .ByType<Button>()
                .Name("*Button")
                .Text("Submit")
                .Tag("Untagged")
                .With<Button>(b => b.interactable);

            Assert.IsTrue(search.Matches(go));
        }

        [Test]
        public void Not_ChainedMultipleTimes_AllNegated()
        {
            var go1 = CreateTestObject("KeepThis");
            go1.AddComponent<Button>();

            var go2 = CreateTestObject("ExcludeA");
            go2.AddComponent<Button>();

            var go3 = CreateTestObject("ExcludeB");
            go3.AddComponent<Button>();

            // Match buttons NOT named ExcludeA and NOT named ExcludeB
            var search = UITestBehaviour.Search
                .ByType<Button>()
                .Not.Name("ExcludeA")
                .Not.Name("ExcludeB");

            Assert.IsTrue(search.Matches(go1));
            Assert.IsFalse(search.Matches(go2));
            Assert.IsFalse(search.Matches(go3));
        }

        [Test]
        public void HasParent_GrandparentDoesNotMatch()
        {
            var grandparent = CreateTestObject("GrandPanel");
            var parent = CreateTestObject("ParentPanel", grandparent.transform);
            var child = CreateTestObject("ChildButton", parent.transform);

            // HasParent should only match immediate parent, not grandparent
            var searchParent = UITestBehaviour.Search.ByName("ChildButton").HasParent("ParentPanel");
            var searchGrandparent = UITestBehaviour.Search.ByName("ChildButton").HasParent("GrandPanel");

            Assert.IsTrue(searchParent.Matches(child));
            Assert.IsFalse(searchGrandparent.Matches(child), "HasParent should not match grandparent");
        }

        [Test]
        public void HasAncestor_MatchesAtAnyDepth()
        {
            var root = CreateTestObject("RootPanel");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("Level3", level2.transform);
            var level4 = CreateTestObject("DeepButton", level3.transform);

            // HasAncestor should match at any depth
            Assert.IsTrue(UITestBehaviour.Search.ByName("DeepButton").HasAncestor("RootPanel").Matches(level4));
            Assert.IsTrue(UITestBehaviour.Search.ByName("DeepButton").HasAncestor("Level1").Matches(level4));
            Assert.IsTrue(UITestBehaviour.Search.ByName("DeepButton").HasAncestor("Level2").Matches(level4));
            Assert.IsTrue(UITestBehaviour.Search.ByName("DeepButton").HasAncestor("Level3").Matches(level4));
        }

        [Test]
        public void HasChild_OnlyMatchesDirectChildren()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildPanel", parent.transform);
            var grandchild = CreateTestObject("GrandchildButton", child.transform);

            // HasChild should only match direct children
            var searchChild = UITestBehaviour.Search.ByName("ParentPanel").HasChild("ChildPanel");
            var searchGrandchild = UITestBehaviour.Search.ByName("ParentPanel").HasChild("GrandchildButton");

            Assert.IsTrue(searchChild.Matches(parent));
            Assert.IsFalse(searchGrandchild.Matches(parent), "HasChild should not match grandchildren");
        }

        [Test]
        public void HasDescendant_MatchesAtAnyDepth()
        {
            var root = CreateTestObject("RootPanel");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var deepButton = CreateTestObject("DeepButton", level2.transform);

            Assert.IsTrue(UITestBehaviour.Search.ByName("RootPanel").HasDescendant("DeepButton").Matches(root));
            Assert.IsTrue(UITestBehaviour.Search.ByName("RootPanel").HasDescendant("Level2").Matches(root));
            Assert.IsTrue(UITestBehaviour.Search.ByName("Level1").HasDescendant("DeepButton").Matches(level1));
        }

        [Test]
        public void Hierarchy_CombinedParentAndChild()
        {
            var root = CreateTestObject("Root");
            var panel = CreateTestObject("TargetPanel", root.transform);
            var button = CreateTestObject("TargetButton", panel.transform);

            // Find panel that has Root as parent AND has TargetButton as child
            var search = UITestBehaviour.Search
                .ByName("TargetPanel")
                .HasParent("Root")
                .HasChild("TargetButton");

            Assert.IsTrue(search.Matches(panel));
        }

        [Test]
        public void With_MultiplePredicatesOnSameComponent()
        {
            var go = CreateTestObject("SliderObj");
            var slider = go.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.value = 75;
            slider.interactable = true;

            var search = UITestBehaviour.Search
                .ByType<Slider>()
                .With<Slider>(s => s.value > 50)
                .With<Slider>(s => s.value < 80)
                .With<Slider>(s => s.interactable);

            Assert.IsTrue(search.Matches(go));

            slider.value = 90; // Now fails value < 80
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void With_PredicateOnDifferentComponents()
        {
            var go = CreateTestObject("ComplexUI");
            var btn = go.AddComponent<Button>();
            btn.interactable = true;
            var image = go.AddComponent<Image>();
            image.raycastTarget = true;

            var search = UITestBehaviour.Search
                .ByName("ComplexUI")
                .With<Button>(b => b.interactable)
                .With<Image>(i => i.raycastTarget);

            Assert.IsTrue(search.Matches(go));

            image.raycastTarget = false;
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void Where_ComplexPredicate_WithClosure()
        {
            var go1 = CreateTestObject("Target_1");
            var go2 = CreateTestObject("Target_2");
            var go3 = CreateTestObject("Other_3");

            var validNames = new[] { "Target_1", "Target_2" };
            var search = UITestBehaviour.Search.Where(go => validNames.Contains(go.name));

            Assert.IsTrue(search.Matches(go1));
            Assert.IsTrue(search.Matches(go2));
            Assert.IsFalse(search.Matches(go3));
        }

        [Test]
        public void ByPath_DeepHierarchy_Matches()
        {
            var root = CreateTestObject("Canvas");
            var panel1 = CreateTestObject("MainPanel", root.transform);
            var panel2 = CreateTestObject("SubPanel", panel1.transform);
            var button = CreateTestObject("ActionButton", panel2.transform);

            Assert.IsTrue(UITestBehaviour.Search.ByPath("*Canvas*/*MainPanel*/*SubPanel*/*ActionButton*").Matches(button));
            Assert.IsTrue(UITestBehaviour.Search.ByPath("*/*/*/*ActionButton*").Matches(button));
            Assert.IsFalse(UITestBehaviour.Search.ByPath("*WrongPanel*/*ActionButton*").Matches(button));
        }

        [Test]
        public void ByAny_MatchesNameOrTextOrSprite()
        {
            // Test name match
            var goName = CreateTestObject("UniqueTarget");
            Assert.IsTrue(UITestBehaviour.Search.ByAny("UniqueTarget").Matches(goName));

            // Test text match
            var goText = CreateButtonWithText("SomeButton", "UniqueTarget");
            Assert.IsTrue(UITestBehaviour.Search.ByAny("UniqueTarget").Matches(goText));

            // Test sprite match
            var goSprite = CreateTestObject("SpriteObj");
            var image = goSprite.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "UniqueTarget";
            image.sprite = sprite;
            Assert.IsTrue(UITestBehaviour.Search.ByAny("UniqueTarget").Matches(goSprite));

            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DestroyedGameObject_ReturnsFalse()
        {
            var go = CreateTestObject("WillBeDestroyed");
            var search = UITestBehaviour.Search.ByName("WillBeDestroyed");

            Assert.IsTrue(search.Matches(go));

            Object.DestroyImmediate(go);
            _testObjects.Remove(go);

            // Should handle destroyed object gracefully
            Assert.IsFalse(search.Matches(go));
        }

        [Test]
        public void DisabledComponents_StillMatch()
        {
            var go = CreateTestObject("DisabledButton");
            var btn = go.AddComponent<Button>();
            btn.enabled = false;

            // Type search should still find disabled components
            Assert.IsTrue(UITestBehaviour.Search.ByType<Button>().Name("DisabledButton").Matches(go));
        }

        [Test]
        public void ByType_InheritedTypes_DoesNotMatch()
        {
            var go = CreateTestObject("GraphicObj");
            go.AddComponent<Image>(); // Image inherits from Graphic

            // ByType<Graphic> should match Image since Image is a Graphic
            Assert.IsTrue(UITestBehaviour.Search.ByType<Graphic>().Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByType<Image>().Matches(go));

            // But searching for RawImage should not match Image
            Assert.IsFalse(UITestBehaviour.Search.ByType<RawImage>().Matches(go));
        }

        [Test]
        public void Chain_OrderIndependence()
        {
            var go = CreateButtonWithText("TestButton", "Click");
            go.AddComponent<Button>();

            // Different ordering should produce same result
            var search1 = UITestBehaviour.Search.ByName("TestButton").Text("Click").Type<Button>();
            var search2 = UITestBehaviour.Search.ByType<Button>().Name("TestButton").Text("Click");
            var search3 = UITestBehaviour.Search.ByText("Click").Type<Button>().Name("TestButton");

            Assert.IsTrue(search1.Matches(go));
            Assert.IsTrue(search2.Matches(go));
            Assert.IsTrue(search3.Matches(go));
        }

        [Test]
        public void LongName_Matches()
        {
            var longName = new string('A', 500) + "_Button_" + new string('B', 500);
            var go = CreateTestObject(longName);

            Assert.IsTrue(UITestBehaviour.Search.ByName(longName).Matches(go));
            Assert.IsTrue(UITestBehaviour.Search.ByName("*_Button_*").Matches(go));
        }

        #endregion
    }
}
