using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using TMPro;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// PlayMode tests for Search API.
    /// All tests run in PlayMode to ensure proper runtime behavior.
    /// </summary>
    [TestFixture]
    public class SearchPlayModeTests
    {
        private GameObject _canvas;
        private Canvas _canvasComponent;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();

            // Destroy any lingering EventSystems from previous failed tests
            var existingEventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            foreach (var es in existingEventSystems)
            {
                Object.DestroyImmediate(es.gameObject);
            }

            // Reset Input System state to clear any leftover mouse button/touch states
            // This is critical for run-all mode where previous tests may leave mouse buttons pressed
            var mouse = Mouse.current;
            if (mouse != null)
            {
                // Reset mouse state by queueing a clean state event
                using (StateEvent.From(mouse, out var statePtr))
                {
                    mouse.position.WriteValueIntoEvent(Vector2.zero, statePtr);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, statePtr);
                    mouse.leftButton.WriteValueIntoEvent(0f, statePtr);
                    mouse.rightButton.WriteValueIntoEvent(0f, statePtr);
                    mouse.middleButton.WriteValueIntoEvent(0f, statePtr);
                    InputSystem.QueueEvent(statePtr);
                }
                InputSystem.Update();
            }

            // Create EventSystem
            var esGO = new GameObject("EventSystem");
            _eventSystem = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            _createdObjects.Add(esGO);

            // Create Canvas
            _canvas = new GameObject("TestCanvas");
            _canvasComponent = _canvas.AddComponent<Canvas>();
            _canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _canvas.AddComponent<GraphicRaycaster>();
            _createdObjects.Add(_canvas);
        }

        [TearDown]
        public void TearDown()
        {
            // Reset Input System state before destroying objects
            // This ensures the next test starts with a clean input state
            var mouse = Mouse.current;
            if (mouse != null)
            {
                using (StateEvent.From(mouse, out var statePtr))
                {
                    mouse.position.WriteValueIntoEvent(Vector2.zero, statePtr);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, statePtr);
                    mouse.leftButton.WriteValueIntoEvent(0f, statePtr);
                    mouse.rightButton.WriteValueIntoEvent(0f, statePtr);
                    mouse.middleButton.WriteValueIntoEvent(0f, statePtr);
                    InputSystem.QueueEvent(statePtr);
                }
                InputSystem.Update();
            }

            // Destroy in reverse order (children before parents for safety)
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                var obj = _createdObjects[i];
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            _createdObjects.Clear();

            // Clear references to destroyed objects
            _canvas = null;
            _canvasComponent = null;
            _eventSystem = null;

            // Force cleanup any orphaned test objects by name pattern
            var orphans = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in orphans)
            {
                if (go != null && (go.name.StartsWith("Test") || go.name.StartsWith("Diag") ||
                    go.name == "EventSystem" || go.name == "TestCanvas" ||
                    go.name.Contains("Helper") || go.name.Contains("Debugger")))
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        #region Static Factory Tests - ByName

        [UnityTest]
        public IEnumerator ByName_ExactMatch_ReturnsTrue()
        {
            var go = CreateTestObject("ExactButton");
            yield return null;
            var search = new Search().Name("ExactButton");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_ExactMatch_WrongName_ReturnsFalse()
        {
            var go = CreateTestObject("OtherButton");
            yield return null;
            var search = new Search().Name("ExactButton");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_WildcardPrefix_ReturnsTrue()
        {
            var go = CreateTestObject("PrefixButton");
            yield return null;
            var search = new Search().Name("Prefix*");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_WildcardSuffix_ReturnsTrue()
        {
            var go = CreateTestObject("ButtonSuffix");
            yield return null;
            var search = new Search().Name("*Suffix");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_WildcardContains_ReturnsTrue()
        {
            var go = CreateTestObject("SomeMiddleText");
            yield return null;
            var search = new Search().Name("*Middle*");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_GlobPattern_ReturnsTrue()
        {
            var go = CreateTestObject("btn_play_icon");
            yield return null;
            var search = new Search().Name("btn_*_icon");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_GlobPattern_WrongPattern_ReturnsFalse()
        {
            var go = CreateTestObject("btn_play_button");
            yield return null;
            var search = new Search().Name("btn_*_icon");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_SpecialCharacters_MatchesExactly()
        {
            var go = CreateTestObject("Button (1)");
            yield return null;
            Assert.IsTrue(new Search().Name("Button (1)").Matches(go));
            Assert.IsTrue(new Search().Name("Button (*)").Matches(go));
            Assert.IsTrue(new Search().Name("*(*)*").Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_UnicodeCharacters_Matches()
        {
            var go = CreateTestObject("Кнопка_按钮_ボタン");
            yield return null;
            Assert.IsTrue(new Search().Name("Кнопка_按钮_ボタン").Matches(go));
            Assert.IsTrue(new Search().Name("*按钮*").Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_CaseInsensitive_Matches()
        {
            var go = CreateTestObject("MyButton");
            yield return null;
            Assert.IsTrue(new Search().Name("mybutton").Matches(go));
            Assert.IsTrue(new Search().Name("MYBUTTON").Matches(go));
            Assert.IsTrue(new Search().Name("MyBuTtOn").Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_MultipleWildcards_Matches()
        {
            var go = CreateTestObject("Prefix_Middle_Suffix");
            yield return null;
            Assert.IsTrue(new Search().Name("*_*_*").Matches(go));
            Assert.IsTrue(new Search().Name("Prefix*Suffix").Matches(go));
            Assert.IsTrue(new Search().Name("*Middle*").Matches(go));
            Assert.IsFalse(new Search().Name("*Other*").Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_ConsecutiveWildcards_Matches()
        {
            var go = CreateTestObject("TestButton");
            yield return null;
            Assert.IsTrue(new Search().Name("**Button").Matches(go));
            Assert.IsTrue(new Search().Name("Test**").Matches(go));
            Assert.IsTrue(new Search().Name("***").Matches(go));
        }

        [UnityTest]
        public IEnumerator ByName_OnlyWildcard_MatchesAny()
        {
            var go = CreateTestObject("AnyName");
            yield return null;
            Assert.IsTrue(new Search().Name("*").Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByType

        [UnityTest]
        public IEnumerator ByType_Generic_WithComponent_ReturnsTrue()
        {
            var go = CreateTestObject("ButtonObj");
            go.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>();
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByType_Generic_WithoutComponent_ReturnsFalse()
        {
            var go = CreateTestObject("EmptyObj");
            yield return null;
            var search = new Search().Type<Button>();
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByType_String_WithComponent_ReturnsTrue()
        {
            var go = CreateTestObject("ToggleObj");
            go.AddComponent<Toggle>();
            yield return null;
            var search = new Search().Type("Toggle");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByType_String_WithoutComponent_ReturnsFalse()
        {
            var go = CreateTestObject("EmptyObj");
            yield return null;
            var search = new Search().Type("Toggle");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByType_InheritedTypes_Matches()
        {
            var go = CreateTestObject("GraphicObj");
            go.AddComponent<Image>(); // Image inherits from Graphic
            yield return null;
            Assert.IsTrue(new Search().Type<Graphic>().Matches(go));
            Assert.IsTrue(new Search().Type<Image>().Matches(go));
            Assert.IsFalse(new Search().Type<RawImage>().Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByText

        [UnityTest]
        public IEnumerator ByText_ExactMatch_ReturnsTrue()
        {
            // Text() matches only the actual text element, not the parent button
            var go = CreateButtonWithText("Btn", "Click Me");
            var textElement = go.transform.GetChild(0).gameObject;
            yield return null;
            var search = new Search().Text("Click Me");
            Assert.IsTrue(search.Matches(textElement), "Text() should match the text element");
            Assert.IsFalse(search.Matches(go), "Text() should NOT match the parent button");
        }

        [UnityTest]
        public IEnumerator ByText_WildcardPrefix_ReturnsTrue()
        {
            var go = CreateButtonWithText("Btn", "Click Me");
            var textElement = go.transform.GetChild(0).gameObject;
            yield return null;
            var search = new Search().Text("Click*");
            Assert.IsTrue(search.Matches(textElement));
        }

        [UnityTest]
        public IEnumerator ByText_WildcardSuffix_ReturnsTrue()
        {
            var go = CreateButtonWithText("Btn", "Click Me");
            var textElement = go.transform.GetChild(0).gameObject;
            yield return null;
            var search = new Search().Text("*Me");
            Assert.IsTrue(search.Matches(textElement));
        }

        [UnityTest]
        public IEnumerator ByText_NoTextComponent_ReturnsFalse()
        {
            var go = CreateTestObject("NoText");
            yield return null;
            var search = new Search().Text("Click Me");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByText_NestedTextComponents_UseHasChild()
        {
            // Text() only matches the actual text element
            // Use HasChild(Text()) to match parent that contains text
            var parent = CreateTestObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);
            var tmp = child.AddComponent<TextMeshProUGUI>();
            tmp.text = "Nested Text";
            _createdObjects.Add(child);
            yield return null;
            // Text() matches the child, not the parent
            Assert.IsTrue(new Search().Text("Nested Text").Matches(child));
            Assert.IsFalse(new Search().Text("Nested Text").Matches(parent));
            // Use HasChild(Text()) to match parent
            Assert.IsTrue(new Search().HasChild(new Search().Text("Nested Text")).Matches(parent));
        }

        [UnityTest]
        public IEnumerator ByText_MultipleTextComponents_MatchesDirectOnly()
        {
            // Text() only matches the actual text elements, not the parent
            var go = CreateTestObject("MultiText");
            var child1 = new GameObject("Text1");
            child1.transform.SetParent(go.transform);
            var tmp1 = child1.AddComponent<TextMeshProUGUI>();
            tmp1.text = "First";
            _createdObjects.Add(child1);
            var child2 = new GameObject("Text2");
            child2.transform.SetParent(go.transform);
            var tmp2 = child2.AddComponent<TextMeshProUGUI>();
            tmp2.text = "Second";
            _createdObjects.Add(child2);
            yield return null;
            // Text() matches the children directly
            Assert.IsTrue(new Search().Text("First").Matches(child1));
            Assert.IsTrue(new Search().Text("Second").Matches(child2));
            // Text() does NOT match the parent
            Assert.IsFalse(new Search().Text("First").Matches(go));
            // Use HasChild to match parent with text children
            Assert.IsTrue(new Search().HasChild(new Search().Text("First")).Matches(go));
            Assert.IsTrue(new Search().HasChild(new Search().Text("Second")).Matches(go));
            Assert.IsFalse(new Search().HasChild(new Search().Text("Third")).Matches(go));
        }

        #endregion

        #region Static Factory Tests - ByPath

        [UnityTest]
        public IEnumerator ByPath_MatchesHierarchy_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Path("*Parent*/*Child*");
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator ByPath_WrongHierarchy_ReturnsFalse()
        {
            var parent = CreateTestObject("OtherPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Path("*Parent*/*Child*");
            Assert.IsFalse(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator ByPath_DeepHierarchy_Matches()
        {
            var root = CreateTestObject("Canvas");
            var panel1 = CreateTestObject("MainPanel", root.transform);
            var panel2 = CreateTestObject("SubPanel", panel1.transform);
            var button = CreateTestObject("ActionButton", panel2.transform);
            yield return null;
            Assert.IsTrue(new Search().Path("*Canvas*/*MainPanel*/*SubPanel*/*ActionButton*").Matches(button));
            Assert.IsTrue(new Search().Path("*/*/*/*ActionButton*").Matches(button));
            Assert.IsFalse(new Search().Path("*WrongPanel*/*ActionButton*").Matches(button));
        }

        #endregion

        #region Static Factory Tests - ByTag

        [UnityTest]
        public IEnumerator ByTag_MatchingTag_ReturnsTrue()
        {
            var go = CreateTestObject("TaggedObj");
            go.tag = "Untagged";
            yield return null;
            var search = new Search().Tag("Untagged");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByTag_DifferentTag_ReturnsFalse()
        {
            var go = CreateTestObject("TaggedObj");
            go.tag = "Untagged";
            yield return null;
            var search = new Search().Tag("MainCamera");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Static Factory Tests - BySprite

        [UnityTest]
        public IEnumerator BySprite_MatchingSpriteName_ReturnsTrue()
        {
            var go = CreateTestObject("SpriteObj");
            var image = go.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "btn_play_icon";
            image.sprite = sprite;
            yield return null;
            var search = new Search().Sprite("btn_*_icon");
            Assert.IsTrue(search.Matches(go));
            Object.Destroy(sprite);
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator BySprite_NoSprite_ReturnsFalse()
        {
            var go = CreateTestObject("NoSpriteObj");
            go.AddComponent<Image>();
            yield return null;
            var search = new Search().Sprite("any_sprite");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator BySprite_WrongSpriteName_ReturnsFalse()
        {
            var go = CreateTestObject("SpriteObj");
            var image = go.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "other_sprite";
            image.sprite = sprite;
            yield return null;
            var search = new Search().Sprite("btn_*_icon");
            Assert.IsFalse(search.Matches(go));
            Object.Destroy(sprite);
            Object.Destroy(texture);
        }

        #endregion

        #region Static Factory Tests - ByAny

        [UnityTest]
        public IEnumerator ByAny_MatchesName_ReturnsTrue()
        {
            var go = CreateTestObject("TargetName");
            yield return null;
            var search = new Search().Any("TargetName");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator ByAny_MatchesText_ReturnsTrue()
        {
            // Any() only matches text on the element itself (like Text()), not in children
            var go = CreateButtonWithText("SomeButton", "TargetText");
            var textElement = go.transform.GetChild(0).gameObject;
            yield return null;
            var search = new Search().Any("TargetText");
            Assert.IsTrue(search.Matches(textElement), "Any() should match the text element");
        }

        [UnityTest]
        public IEnumerator ByAny_NoMatch_ReturnsFalse()
        {
            var go = CreateButtonWithText("OtherButton", "OtherText");
            yield return null;
            var search = new Search().Any("Target");
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Chaining Tests

        [UnityTest]
        public IEnumerator Chain_TypeAndName_BothMatch_ReturnsTrue()
        {
            var go = CreateTestObject("SpecificButton");
            go.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().Name("Specific*");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_TypeAndName_TypeMismatch_ReturnsFalse()
        {
            var go = CreateTestObject("SpecificButton");
            go.AddComponent<Toggle>();
            yield return null;
            var search = new Search().Type<Button>().Name("Specific*");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_TypeAndName_NameMismatch_ReturnsFalse()
        {
            var go = CreateTestObject("OtherButton");
            go.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().Name("Specific*");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_NameAndText_BothMatch_ReturnsTrue()
        {
            // Use HasChild(Text()) to match parent by child's text
            var go = CreateButtonWithText("SubmitBtn", "Submit");
            yield return null;
            var search = new Search().Name("*Btn").HasChild(new Search().Text("Submit"));
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_TypeTextName_AllMatch_ReturnsTrue()
        {
            // Use HasChild(Text()) to match parent by child's text
            var go = CreateButtonWithText("ConfirmButton", "OK");
            go.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().HasChild(new Search().Text("OK")).Name("*Confirm*");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_TypeAndSprite_BothMatch_ReturnsTrue()
        {
            var go = CreateTestObject("ImageObj");
            var image = go.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = "icon_settings";
            image.sprite = sprite;
            yield return null;
            var search = new Search().Type<Image>().Sprite("icon_*");
            Assert.IsTrue(search.Matches(go));
            Object.Destroy(sprite);
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator Chain_FiveConditions_AllMustMatch()
        {
            // Use HasChild(Text()) to match parent by child's text
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
            _createdObjects.Add(textChild);
            yield return null;
            var search = new Search()
                .Type<Button>()
                .Name("*Button")
                .HasChild(new Search().Text("Submit"))
                .Tag("Untagged")
                .With<Button>(b => b.interactable);
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_OrderIndependence()
        {
            // Use HasChild(Text()) to match parent by child's text
            var go = CreateButtonWithText("TestButton", "Click");
            go.AddComponent<Button>();
            yield return null;
            var search1 = new Search().Name("TestButton").HasChild(new Search().Text("Click")).Type<Button>();
            var search2 = new Search().Type<Button>().Name("TestButton").HasChild(new Search().Text("Click"));
            var search3 = new Search().HasChild(new Search().Text("Click")).Type<Button>().Name("TestButton");
            Assert.IsTrue(search1.Matches(go));
            Assert.IsTrue(search2.Matches(go));
            Assert.IsTrue(search3.Matches(go));
        }

        #endregion

        #region With Predicate Tests

        [UnityTest]
        public IEnumerator With_PredicateTrue_ReturnsTrue()
        {
            var go = CreateTestObject("InteractableBtn");
            var button = go.AddComponent<Button>();
            button.interactable = true;
            yield return null;
            var search = new Search().Type<Button>().With<Button>(b => b.interactable);
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_PredicateFalse_ReturnsFalse()
        {
            var go = CreateTestObject("DisabledBtn");
            var button = go.AddComponent<Button>();
            button.interactable = false;
            yield return null;
            var search = new Search().Type<Button>().With<Button>(b => b.interactable);
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_Toggle_IsOnTrue_ReturnsTrue()
        {
            var go = CreateTestObject("OnToggle");
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = true;
            yield return null;
            var search = new Search().Type<Toggle>().With<Toggle>(t => t.isOn);
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_Toggle_IsOnFalse_ReturnsFalse()
        {
            var go = CreateTestObject("OffToggle");
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = false;
            yield return null;
            var search = new Search().Type<Toggle>().With<Toggle>(t => t.isOn);
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_Slider_ValueCheck_ReturnsTrue()
        {
            var go = CreateTestObject("SliderObj");
            var slider = go.AddComponent<Slider>();
            slider.value = 0.75f;
            yield return null;
            var search = new Search().Type<Slider>().With<Slider>(s => s.value > 0.5f);
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_MultiplePredicatesOnSameComponent()
        {
            var go = CreateTestObject("SliderObj");
            var slider = go.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.value = 75;
            slider.interactable = true;
            yield return null;
            var search = new Search()
                .Type<Slider>()
                .With<Slider>(s => s.value > 50)
                .With<Slider>(s => s.value < 80)
                .With<Slider>(s => s.interactable);
            Assert.IsTrue(search.Matches(go));
            slider.value = 90;
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator With_PredicateOnDifferentComponents()
        {
            var go = CreateTestObject("ComplexUI");
            var btn = go.AddComponent<Button>();
            btn.interactable = true;
            var image = go.AddComponent<Image>();
            image.raycastTarget = true;
            yield return null;
            var search = new Search()
                .Name("ComplexUI")
                .With<Button>(b => b.interactable)
                .With<Image>(i => i.raycastTarget);
            Assert.IsTrue(search.Matches(go));
            image.raycastTarget = false;
            Assert.IsFalse(search.Matches(go));
        }

        #endregion

        #region Where Predicate Tests

        [UnityTest]
        public IEnumerator Where_CustomPredicate_ReturnsTrue()
        {
            var go = CreateTestObject("CustomTarget");
            yield return null;
            var search = new Search().Where(g => g.name == "CustomTarget");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Where_CustomPredicate_ReturnsFalse()
        {
            var go = CreateTestObject("OtherObject");
            yield return null;
            var search = new Search().Where(g => g.name == "CustomTarget");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Where_ComplexPredicate_ReturnsTrue()
        {
            var go = CreateTestObject("ActiveEnabled");
            go.SetActive(true);
            yield return null;
            var search = new Search().Where(g => g.activeInHierarchy && g.name.Contains("Active"));
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Where_ComplexPredicate_WithClosure()
        {
            var go1 = CreateTestObject("Target_1");
            var go2 = CreateTestObject("Target_2");
            var go3 = CreateTestObject("Other_3");
            var validNames = new[] { "Target_1", "Target_2" };
            yield return null;
            var search = new Search().Where(go => validNames.Contains(go.name));
            Assert.IsTrue(search.Matches(go1));
            Assert.IsTrue(search.Matches(go2));
            Assert.IsFalse(search.Matches(go3));
        }

        #endregion

        #region Not (Negation) Tests

        [UnityTest]
        public IEnumerator Not_Name_ExcludesMatch()
        {
            var go1 = CreateTestObject("NotThisOne");
            var go2 = CreateTestObject("NotOther");
            yield return null;
            var search = new Search().Name("Not*").Not.Name("NotThisOne");
            Assert.IsFalse(search.Matches(go1), "Should NOT match NotThisOne");
            Assert.IsTrue(search.Matches(go2), "Should match NotOther");
        }

        [UnityTest]
        public IEnumerator Not_Type_ExcludesComponent()
        {
            var goWithButton = CreateTestObject("WithButton");
            goWithButton.AddComponent<Button>();
            var goWithToggle = CreateTestObject("WithToggle");
            goWithToggle.AddComponent<Toggle>();
            yield return null;
            var search = new Search().Name("With*").Not.Type<Button>();
            Assert.IsFalse(search.Matches(goWithButton), "Should NOT match object with Button");
            Assert.IsTrue(search.Matches(goWithToggle), "Should match object with Toggle");
        }

        [UnityTest]
        public IEnumerator Not_ChainedMultipleTimes_AllNegated()
        {
            var go1 = CreateTestObject("KeepThis");
            go1.AddComponent<Button>();
            var go2 = CreateTestObject("ExcludeA");
            go2.AddComponent<Button>();
            var go3 = CreateTestObject("ExcludeB");
            go3.AddComponent<Button>();
            yield return null;
            var search = new Search()
                .Type<Button>()
                .Not.Name("ExcludeA")
                .Not.Name("ExcludeB");
            Assert.IsTrue(search.Matches(go1));
            Assert.IsFalse(search.Matches(go2));
            Assert.IsFalse(search.Matches(go3));
        }

        #endregion

        #region Hierarchy Tests - HasParent

        [UnityTest]
        public IEnumerator HasParent_Search_DirectParentMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Name("ChildButton").HasParent(new Search().Name("ParentPanel"));
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasParent_Search_WrongParent_ReturnsFalse()
        {
            var parent = CreateTestObject("OtherPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Name("ChildButton").HasParent(new Search().Name("ParentPanel"));
            Assert.IsFalse(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasParent_String_DirectParentMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Name("ChildButton").HasParent("ParentPanel");
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasParent_String_Wildcard_ReturnsTrue()
        {
            var parent = CreateTestObject("MyParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Name("ChildButton").HasParent("*Parent*");
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasParent_GrandparentDoesNotMatch()
        {
            var grandparent = CreateTestObject("GrandPanel");
            var parent = CreateTestObject("ParentPanel", grandparent.transform);
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var searchParent = new Search().Name("ChildButton").HasParent("ParentPanel");
            var searchGrandparent = new Search().Name("ChildButton").HasParent("GrandPanel");
            Assert.IsTrue(searchParent.Matches(child));
            Assert.IsFalse(searchGrandparent.Matches(child), "HasParent should not match grandparent");
        }

        #endregion

        #region Hierarchy Tests - HasAncestor

        [UnityTest]
        public IEnumerator HasAncestor_Search_GrandparentMatches_ReturnsTrue()
        {
            var grandparent = CreateTestObject("RootPanel");
            var parent = CreateTestObject("MiddlePanel", grandparent.transform);
            var child = CreateTestObject("DeepButton", parent.transform);
            yield return null;
            var search = new Search().Name("DeepButton").HasAncestor(new Search().Name("RootPanel"));
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasAncestor_Search_NoMatchingAncestor_ReturnsFalse()
        {
            var grandparent = CreateTestObject("OtherRoot");
            var parent = CreateTestObject("MiddlePanel", grandparent.transform);
            var child = CreateTestObject("DeepButton", parent.transform);
            yield return null;
            var search = new Search().Name("DeepButton").HasAncestor(new Search().Name("RootPanel"));
            Assert.IsFalse(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator HasAncestor_String_DeepHierarchy_ReturnsTrue()
        {
            var root = CreateTestObject("AncestorRoot");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("Level3", level2.transform);
            yield return null;
            var search = new Search().Name("Level3").HasAncestor("AncestorRoot");
            Assert.IsTrue(search.Matches(level3));
        }

        [UnityTest]
        public IEnumerator HasAncestor_MatchesAtAnyDepth()
        {
            var root = CreateTestObject("RootPanel");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("Level3", level2.transform);
            var level4 = CreateTestObject("DeepButton", level3.transform);
            yield return null;
            Assert.IsTrue(new Search().Name("DeepButton").HasAncestor("RootPanel").Matches(level4));
            Assert.IsTrue(new Search().Name("DeepButton").HasAncestor("Level1").Matches(level4));
            Assert.IsTrue(new Search().Name("DeepButton").HasAncestor("Level2").Matches(level4));
            Assert.IsTrue(new Search().Name("DeepButton").HasAncestor("Level3").Matches(level4));
        }

        #endregion

        #region Hierarchy Tests - HasChild

        [UnityTest]
        public IEnumerator HasChild_Search_DirectChildMatches_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            yield return null;
            var search = new Search().Name("ParentPanel").HasChild(new Search().Name("ChildButton"));
            Assert.IsTrue(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator HasChild_Search_NoMatchingChild_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("OtherChild", parent.transform);
            yield return null;
            var search = new Search().Name("ParentPanel").HasChild(new Search().Name("ChildButton"));
            Assert.IsFalse(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator HasChild_String_Wildcard_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("SpecificChildBtn", parent.transform);
            yield return null;
            var search = new Search().Name("ParentPanel").HasChild("*Child*");
            Assert.IsTrue(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator HasChild_OnlyMatchesDirectChildren()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildPanel", parent.transform);
            var grandchild = CreateTestObject("GrandchildButton", child.transform);
            yield return null;
            var searchChild = new Search().Name("ParentPanel").HasChild("ChildPanel");
            var searchGrandchild = new Search().Name("ParentPanel").HasChild("GrandchildButton");
            Assert.IsTrue(searchChild.Matches(parent));
            Assert.IsFalse(searchGrandchild.Matches(parent), "HasChild should not match grandchildren");
        }

        #endregion

        #region Hierarchy Tests - HasDescendant

        [UnityTest]
        public IEnumerator HasDescendant_Search_GrandchildMatches_ReturnsTrue()
        {
            var root = CreateTestObject("RootPanel");
            var middle = CreateTestObject("MiddlePanel", root.transform);
            var deep = CreateTestObject("DeepButton", middle.transform);
            yield return null;
            var search = new Search().Name("RootPanel").HasDescendant(new Search().Name("DeepButton"));
            Assert.IsTrue(search.Matches(root));
        }

        [UnityTest]
        public IEnumerator HasDescendant_Search_NoMatchingDescendant_ReturnsFalse()
        {
            var root = CreateTestObject("RootPanel");
            var middle = CreateTestObject("MiddlePanel", root.transform);
            var deep = CreateTestObject("OtherButton", middle.transform);
            yield return null;
            var search = new Search().Name("RootPanel").HasDescendant(new Search().Name("DeepButton"));
            Assert.IsFalse(search.Matches(root));
        }

        [UnityTest]
        public IEnumerator HasDescendant_String_DeepHierarchy_ReturnsTrue()
        {
            var root = CreateTestObject("DescendantRoot");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var level3 = CreateTestObject("TargetDescendant", level2.transform);
            yield return null;
            var search = new Search().Name("DescendantRoot").HasDescendant("*Target*");
            Assert.IsTrue(search.Matches(root));
        }

        [UnityTest]
        public IEnumerator HasDescendant_MatchesAtAnyDepth()
        {
            var root = CreateTestObject("RootPanel");
            var level1 = CreateTestObject("Level1", root.transform);
            var level2 = CreateTestObject("Level2", level1.transform);
            var deepButton = CreateTestObject("DeepButton", level2.transform);
            yield return null;
            Assert.IsTrue(new Search().Name("RootPanel").HasDescendant("DeepButton").Matches(root));
            Assert.IsTrue(new Search().Name("RootPanel").HasDescendant("Level2").Matches(root));
            Assert.IsTrue(new Search().Name("Level1").HasDescendant("DeepButton").Matches(level1));
        }

        [UnityTest]
        public IEnumerator Hierarchy_CombinedParentAndChild()
        {
            var root = CreateTestObject("Root");
            var panel = CreateTestObject("TargetPanel", root.transform);
            var button = CreateTestObject("TargetButton", panel.transform);
            yield return null;
            var search = new Search()
                .Name("TargetPanel")
                .HasParent("Root")
                .HasChild("TargetButton");
            Assert.IsTrue(search.Matches(panel));
        }

        #endregion

        #region GetParent Tests

        [UnityTest]
        public IEnumerator GetParent_ComponentInParent_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            parent.AddComponent<CanvasGroup>();
            var child = CreateTestObject("ChildButton", parent.transform);
            child.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().GetParent<CanvasGroup>();
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator GetParent_NoComponentInParent_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentPanel");
            var child = CreateTestObject("ChildButton", parent.transform);
            child.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().GetParent<CanvasGroup>();
            Assert.IsFalse(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator GetParent_WithPredicate_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentPanel");
            var cg = parent.AddComponent<CanvasGroup>();
            cg.alpha = 0.8f;
            var child = CreateTestObject("ChildButton", parent.transform);
            child.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().GetParent<CanvasGroup>(g => g.alpha > 0.5f);
            Assert.IsTrue(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator GetParent_WithPredicate_PredicateFails_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentPanel");
            var cg = parent.AddComponent<CanvasGroup>();
            cg.alpha = 0.3f;
            var child = CreateTestObject("ChildButton", parent.transform);
            child.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().GetParent<CanvasGroup>(g => g.alpha > 0.5f);
            Assert.IsFalse(search.Matches(child));
        }

        [UnityTest]
        public IEnumerator GetParent_ExcludesSelf()
        {
            var go = CreateTestObject("SelfTest");
            go.AddComponent<Button>();
            go.AddComponent<CanvasGroup>();
            yield return null;
            var search = new Search().Type<Button>().GetParent<CanvasGroup>();
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Not_GetParent_Works()
        {
            var parent1 = CreateTestObject("WithCanvasGroup");
            parent1.AddComponent<CanvasGroup>();
            var child1 = CreateTestObject("Child1", parent1.transform);
            child1.AddComponent<Button>();
            var parent2 = CreateTestObject("WithoutCanvasGroup");
            var child2 = CreateTestObject("Child2", parent2.transform);
            child2.AddComponent<Button>();
            yield return null;
            var search = new Search().Type<Button>().Not.GetParent<CanvasGroup>();
            Assert.IsFalse(search.Matches(child1));
            Assert.IsTrue(search.Matches(child2));
        }

        #endregion

        #region GetChild Tests

        [UnityTest]
        public IEnumerator GetChild_ComponentInChildren_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentSlot");
            var child = CreateTestObject("ChildImage", parent.transform);
            child.AddComponent<Image>();
            yield return null;
            var search = new Search().Name("ParentSlot").GetChild<Image>();
            Assert.IsTrue(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator GetChild_NoComponentInChildren_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentSlot");
            var child = CreateTestObject("ChildEmpty", parent.transform);
            yield return null;
            var search = new Search().Name("ParentSlot").GetChild<Image>();
            Assert.IsFalse(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator GetChild_WithPredicate_ReturnsTrue()
        {
            var parent = CreateTestObject("ParentSlot");
            var child = CreateTestObject("ChildImage", parent.transform);
            var image = child.AddComponent<Image>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            image.sprite = sprite;
            yield return null;
            var search = new Search().Name("ParentSlot").GetChild<Image>(img => img.sprite != null);
            Assert.IsTrue(search.Matches(parent));
            Object.Destroy(sprite);
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator GetChild_WithPredicate_PredicateFails_ReturnsFalse()
        {
            var parent = CreateTestObject("ParentSlot");
            var child = CreateTestObject("ChildImage", parent.transform);
            var image = child.AddComponent<Image>();
            image.sprite = null;
            yield return null;
            var search = new Search().Name("ParentSlot").GetChild<Image>(img => img.sprite != null);
            Assert.IsFalse(search.Matches(parent));
        }

        [UnityTest]
        public IEnumerator GetChild_ExcludesSelf()
        {
            var go = CreateTestObject("SelfTest");
            go.AddComponent<Image>();
            yield return null;
            var search = new Search().Name("SelfTest").GetChild<Image>();
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Not_GetChild_Works()
        {
            var parent1 = CreateTestObject("ParentWithImage");
            var child1 = CreateTestObject("ImageChild", parent1.transform);
            child1.AddComponent<Image>();
            var parent2 = CreateTestObject("ParentWithoutImage");
            var child2 = CreateTestObject("EmptyChild", parent2.transform);
            yield return null;
            var search = new Search().Name("Parent*").Not.GetChild<Image>();
            Assert.IsFalse(search.Matches(parent1));
            Assert.IsTrue(search.Matches(parent2));
        }

        #endregion

        #region Implicit Conversion Tests

        [UnityTest]
        public IEnumerator ImplicitConversion_String_ToSearch_Works()
        {
            // Implicit conversion creates Text() search, which only matches text elements directly
            var go = CreateButtonWithText("Btn", "Target Text");
            var textElement = go.transform.GetChild(0).gameObject;
            yield return null;
            Search search = "Target Text";
            Assert.IsTrue(search.Matches(textElement), "Implicit search should match text element");
        }

        #endregion

        #region Edge Cases

        [UnityTest]
        public IEnumerator Search_NullGameObject_ReturnsFalse()
        {
            yield return null;
            var search = new Search().Name("Any");
            Assert.IsFalse(search.Matches(null));
        }

        [UnityTest]
        public IEnumerator Search_InactiveGameObject_StillMatches()
        {
            var go = CreateTestObject("InactiveObject");
            go.SetActive(false);
            yield return null;
            var search = new Search().Name("InactiveObject");
            Assert.IsTrue(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Search_EmptyName_ReturnsFalse()
        {
            var go = CreateTestObject("");
            yield return null;
            var search = new Search().Name("");
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator Chain_MultipleConditions_AllMustMatch()
        {
            // Use HasChild(Text()) to match parent by child's text
            var go = CreateButtonWithText("SubmitButton", "Submit");
            go.AddComponent<Button>();
            yield return null;
            var searchAllMatch = new Search()
                .Type<Button>()
                .Name("*Button")
                .HasChild(new Search().Text("Submit"));
            Assert.IsTrue(searchAllMatch.Matches(go));
            var searchOneFails = new Search()
                .Type<Button>()
                .Name("*Button")
                .HasChild(new Search().Text("Cancel"));
            Assert.IsFalse(searchOneFails.Matches(go));
        }

        [UnityTest]
        public IEnumerator LongName_Matches()
        {
            var longName = new string('A', 500) + "_Button_" + new string('B', 500);
            var go = CreateTestObject(longName);
            yield return null;
            Assert.IsTrue(new Search().Name(longName).Matches(go));
            Assert.IsTrue(new Search().Name("*_Button_*").Matches(go));
        }

        [UnityTest]
        public IEnumerator DestroyedGameObject_ReturnsFalse()
        {
            var go = CreateTestObject("WillBeDestroyed");
            var search = new Search().Name("WillBeDestroyed");
            Assert.IsTrue(search.Matches(go));
            Object.DestroyImmediate(go);
            _createdObjects.Remove(go);
            yield return null;
            Assert.IsFalse(search.Matches(go));
        }

        [UnityTest]
        public IEnumerator DisabledComponents_StillMatch()
        {
            var go = CreateTestObject("DisabledButton");
            var btn = go.AddComponent<Button>();
            btn.enabled = false;
            yield return null;
            Assert.IsTrue(new Search().Type<Button>().Name("DisabledButton").Matches(go));
        }

        #endregion

        #region Ordering Tests (Skip, First, Last)

        [UnityTest]
        public IEnumerator Skip_ReturnsCorrectSkipCount()
        {
            yield return null;
            var search = new Search().Name("Item*").Skip(2);
            Assert.IsTrue(search.HasPostProcessing);
        }

        [UnityTest]
        public IEnumerator First_SetsPostProcessing()
        {
            yield return null;
            var search = new Search().Name("Item*").First();
            Assert.IsTrue(search.HasPostProcessing);
        }

        [UnityTest]
        public IEnumerator Last_SetsPostProcessing()
        {
            yield return null;
            var search = new Search().Name("Item*").Last();
            Assert.IsTrue(search.HasPostProcessing);
        }

        [UnityTest]
        public IEnumerator OrderByPosition_SetsPostProcessing()
        {
            yield return null;
            var search = new Search().Name("Item*").OrderByPosition();
            Assert.IsTrue(search.HasPostProcessing);
        }

        [UnityTest]
        public IEnumerator ApplyPostProcessing_Skip_Works()
        {
            var go1 = CreateTestObject("Item1");
            var go2 = CreateTestObject("Item2");
            var go3 = CreateTestObject("Item3");
            yield return null;
            var search = new Search().Name("Item*").Skip(1);
            var input = new[] { go1, go2, go3 };
            var result = search.ApplyPostProcessing(input).ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(go2, result[0]);
            Assert.AreEqual(go3, result[1]);
        }

        [UnityTest]
        public IEnumerator ApplyPostProcessing_SkipAndFirst_Works()
        {
            var go1 = CreateTestObject("Item1");
            var go2 = CreateTestObject("Item2");
            var go3 = CreateTestObject("Item3");
            yield return null;
            var search = new Search().Name("Item*").Skip(1).First();
            var input = new[] { go1, go2, go3 };
            var result = search.ApplyPostProcessing(input).ToList();
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(go2, result[0]);
        }

        [UnityTest]
        public IEnumerator First_WithoutSkip_GetsFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrderedBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("OrderedBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("OrderedBtn3", "3", _canvas.transform, new Vector2(200, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("OrderedBtn*").First());
                Assert.IsNotNull(result, "Should find button");
                Assert.AreEqual("OrderedBtn1", result.name, "First() should return OrderedBtn1 (leftmost)");
            });
        }

        [UnityTest]
        public IEnumerator Skip_SkipsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrderedBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("OrderedBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("OrderedBtn3", "3", _canvas.transform, new Vector2(200, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("OrderedBtn*").Skip(1).First());
                Assert.IsNotNull(result, "Should find button after skip");
                Assert.AreEqual("OrderedBtn2", result.name, "Skip(1).First() should return second button");
            });
        }

        [UnityTest]
        public IEnumerator Skip2_First_GetsThird()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrderedBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("OrderedBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("OrderedBtn3", "3", _canvas.transform, new Vector2(200, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("OrderedBtn*").Skip(2).First());
                Assert.IsNotNull(result, "Should find button");
                Assert.AreEqual("OrderedBtn3", result.name, "Skip(2).First() should return OrderedBtn3");
            });
        }

        [UnityTest]
        public IEnumerator Skip_ThenLast_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrderedBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("OrderedBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("OrderedBtn3", "3", _canvas.transform, new Vector2(200, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("OrderedBtn*").Skip(1).Last());
                Assert.IsNotNull(result, "Should find button");
                Assert.AreEqual("OrderedBtn3", result.name, "Skip(1).Last() should return third button");
            });
        }

        [UnityTest]
        public IEnumerator VerticalOrdering_First_GetsTop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var top = CreateButton("TopBtn", "Top", _canvas.transform, new Vector2(0, 200));
                var mid = CreateButton("MidBtn", "Mid", _canvas.transform, new Vector2(0, 0));
                var bot = CreateButton("BotBtn", "Bot", _canvas.transform, new Vector2(0, -200));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("*Btn").First());
                Assert.IsNotNull(result, "Should find button");
                Assert.AreEqual("TopBtn", result.name, "First() should return TopBtn (top-most)");
            });
        }

        [UnityTest]
        public IEnumerator VerticalOrdering_Last_GetsBottom()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var top = CreateButton("TopBtn", "Top", _canvas.transform, new Vector2(0, 200));
                var mid = CreateButton("MidBtn", "Mid", _canvas.transform, new Vector2(0, 0));
                var bot = CreateButton("BotBtn", "Bot", _canvas.transform, new Vector2(0, -200));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(
                    new Search().Name("*Btn").Last());
                Assert.IsNotNull(result, "Should find button");
                Assert.AreEqual("BotBtn", result.name, "Last() should return BotBtn (bottom-most)");
            });
        }

        [UnityTest]
        public IEnumerator FindAll_ReturnsAllInOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrderedBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("OrderedBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("OrderedBtn3", "3", _canvas.transform, new Vector2(200, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();
                var results = await test.TestFindAll<Button>(
                    new Search().Name("OrderedBtn*").OrderByPosition());
                Assert.AreEqual(3, results.Count, "Should find all 3 buttons");
                Assert.AreEqual("OrderedBtn1", results[0].name, "First should be OrderedBtn1 (leftmost)");
                Assert.AreEqual("OrderedBtn2", results[1].name, "Second should be OrderedBtn2 (middle)");
                Assert.AreEqual("OrderedBtn3", results[2].name, "Third should be OrderedBtn3 (rightmost)");
            });
        }

        [UnityTest]
        public IEnumerator OrderBy_SortsCorrectly()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider1 = CreateSlider("Slider1", 0.1f, _canvas.transform, new Vector2(-100, 0));
                var slider2 = CreateSlider("Slider2", 0.9f, _canvas.transform, new Vector2(0, 0));
                var slider3 = CreateSlider("Slider3", 0.5f, _canvas.transform, new Vector2(100, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var lowest = await test.TestFind<Slider>(
                    new Search().Name("Slider*").OrderBy<Slider>(s => s.value).First());
                Assert.IsNotNull(lowest, "Should find slider");
                Assert.AreEqual("Slider1", lowest.name, "First by value should be Slider1 (0.1)");
                var highest = await test.TestFind<Slider>(
                    new Search().Name("Slider*").OrderBy<Slider>(s => s.value).Last());
                Assert.IsNotNull(highest, "Should find slider");
                Assert.AreEqual("Slider2", highest.name, "Last by value should be Slider2 (0.9)");
            });
        }

        [UnityTest]
        public IEnumerator Diagnostic_LogsScreenPositions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create buttons at known positions
                var btn1 = CreateButton("DiagBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("DiagBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("DiagBtn3", "3", _canvas.transform, new Vector2(200, 0));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                // Log canvas info
                Debug.Log($"[DIAGNOSTIC] Canvas: renderMode={_canvasComponent.renderMode}, scaleFactor={_canvasComponent.scaleFactor}");
                Debug.Log($"[DIAGNOSTIC] Screen: {Screen.width}x{Screen.height}");

                // Log each button's position info
                foreach (var btn in new[] { btn1, btn2, btn3 })
                {
                    var rect = btn.GetComponent<RectTransform>();
                    Vector3[] corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) / 2f;

                    Debug.Log($"[DIAGNOSTIC] {btn.name}: anchoredPos={rect.anchoredPosition}, worldCenter={center}, corners[0]={corners[0]}, corners[2]={corners[2]}");
                }

                // Test ordering with Debug.Log already in ApplyPostProcessing
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();

                Debug.Log("[DIAGNOSTIC] === Testing OrderByPosition ===");
                var results = await test.TestFindAll<Button>(
                    new Search().Name("DiagBtn*").OrderByPosition());

                Debug.Log($"[DIAGNOSTIC] Results count: {results.Count}");
                for (int i = 0; i < results.Count; i++)
                {
                    Debug.Log($"[DIAGNOSTIC] Result[{i}]: {results[i].name}");
                }

                // Verify ordering - btn1 should be first (leftmost), btn3 should be last (rightmost)
                Assert.AreEqual(3, results.Count, "Should find all 3 buttons");
                Assert.AreEqual("DiagBtn1", results[0].name, "First should be DiagBtn1 (leftmost at x=-200)");
                Assert.AreEqual("DiagBtn2", results[1].name, "Second should be DiagBtn2 (center at x=0)");
                Assert.AreEqual("DiagBtn3", results[2].name, "Third should be DiagBtn3 (rightmost at x=200)");
            });
        }

        [UnityTest]
        public IEnumerator Diagnostic_SkipFirst_Works()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("SkipDiagBtn1", "1", _canvas.transform, new Vector2(-200, 0));
                var btn2 = CreateButton("SkipDiagBtn2", "2", _canvas.transform, new Vector2(0, 0));
                var btn3 = CreateButton("SkipDiagBtn3", "3", _canvas.transform, new Vector2(200, 0));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                Debug.Log("[DIAGNOSTIC] === Testing Skip(1).First() ===");

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // This should order by position, skip 1, take 1 - returning btn2
                var result = await test.TestFind<Button>(
                    new Search().Name("SkipDiagBtn*").Skip(1).First());

                Debug.Log($"[DIAGNOSTIC] Skip(1).First() result: {(result != null ? result.name : "NULL")}");

                Assert.IsNotNull(result, "Should find button after Skip(1).First()");
                Assert.AreEqual("SkipDiagBtn2", result.name, "Skip(1).First() should return second button");
            });
        }

        #endregion

        #region Target Transformation Tests (Parent, Child, Sibling)

        [UnityTest]
        public IEnumerator Parent_ReturnsParentElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var container = CreatePanel("ParentContainer", _canvas.transform, Vector2.zero, new Vector2(200, 100));
                var child = CreateButton("ChildButton", "Child", container.transform, Vector2.zero);
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<RectTransform>(
                    new Search().Name("ChildButton").GetParent());
                Assert.IsNotNull(result, "Should find parent");
                Assert.AreEqual("ParentContainer", result.name, "Parent() should return the parent container");
            });
        }

        [UnityTest]
        public IEnumerator Child_ReturnsChildAtIndex()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var container = CreatePanel("Container", _canvas.transform, Vector2.zero, new Vector2(200, 100));
                var child0 = CreateButton("Child0", "First", container.transform, new Vector2(-50, 0));
                var child1 = CreateButton("Child1", "Second", container.transform, new Vector2(50, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result0 = await test.TestFind<Button>(
                    new Search().Name("Container").GetChild(0));
                Assert.IsNotNull(result0, "Should find child at index 0");
                Assert.AreEqual("Child0", result0.name);
                var result1 = await test.TestFind<Button>(
                    new Search().Name("Container").GetChild(1));
                Assert.IsNotNull(result1, "Should find child at index 1");
                Assert.AreEqual("Child1", result1.name);
            });
        }

        [UnityTest]
        public IEnumerator Sibling_ReturnsSiblingByOffset()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var container = CreatePanel("Container", _canvas.transform, Vector2.zero, new Vector2(400, 50));
                var first = CreateButton("First", "1", container.transform, new Vector2(-150, 0));
                var second = CreateButton("Second", "2", container.transform, new Vector2(-50, 0));
                var third = CreateButton("Third", "3", container.transform, new Vector2(50, 0));
                var fourth = CreateButton("Fourth", "4", container.transform, new Vector2(150, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var nextSibling = await test.TestFind<Button>(
                    new Search().Name("Second").GetSibling(1));
                Assert.IsNotNull(nextSibling, "Should find next sibling");
                Assert.AreEqual("Third", nextSibling.name);
                var prevSibling = await test.TestFind<Button>(
                    new Search().Name("Third").GetSibling(-1));
                Assert.IsNotNull(prevSibling, "Should find previous sibling");
                Assert.AreEqual("Second", prevSibling.name);
            });
        }

        [UnityTest]
        public IEnumerator ChainedTransformations_Work()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var root = CreatePanel("Root", _canvas.transform, Vector2.zero, new Vector2(300, 200));
                var level1 = CreatePanel("Level1", root.transform, Vector2.zero, new Vector2(250, 150));
                var level2 = CreatePanel("Level2", level1.transform, Vector2.zero, new Vector2(200, 100));
                var deepBtn = CreateButton("DeepButton", "Deep", level2.transform, Vector2.zero);
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                // Diagnostic: Log hierarchy
                Debug.Log($"[DIAGNOSTIC] Hierarchy: Root.childCount={root.transform.childCount}");
                Debug.Log($"[DIAGNOSTIC] Level1: childCount={level1.transform.childCount}");
                Debug.Log($"[DIAGNOSTIC] Level2: childCount={level2.transform.childCount}");
                Debug.Log($"[DIAGNOSTIC] DeepButton parent: {deepBtn.transform.parent.name}");

                // Verify Level2 can be found first
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                Debug.Log("[DIAGNOSTIC] === Testing Find Level2 (no transformation) ===");
                var level2Result = await test.TestFind<RectTransform>(
                    new Search().Name("Level2"));
                Assert.IsNotNull(level2Result, "Should find Level2");
                Assert.AreEqual("Level2", level2Result.name);

                Debug.Log("[DIAGNOSTIC] === Testing Level2.GetChild(0) ===");
                var result = await test.TestFind<RectTransform>(
                    new Search().Name("Level2").GetChild(0));
                Assert.IsNotNull(result, "Should find child");
                Assert.AreEqual("DeepButton", result.name);
            });
        }

        [UnityTest]
        public IEnumerator Diagnostic_ChildTransformation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var container = CreatePanel("DiagContainer", _canvas.transform, Vector2.zero, new Vector2(200, 100));
                var child = CreateButton("DiagChild", "Child", container.transform, Vector2.zero);
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                Debug.Log($"[DIAGNOSTIC] Container.childCount = {container.transform.childCount}");
                Debug.Log($"[DIAGNOSTIC] Child0 = {container.transform.GetChild(0).name}");

                // Manually verify the transformation works
                var search = new Search().Name("DiagContainer").GetChild(0);
                Debug.Log($"[DIAGNOSTIC] search.HasPostProcessing = {search.HasPostProcessing}");

                // Apply post-processing manually to see what happens
                var testInput = new[] { container };
                var postProcessed = search.ApplyPostProcessing(testInput).ToList();
                Debug.Log($"[DIAGNOSTIC] PostProcessed count = {postProcessed.Count}");
                foreach (var go in postProcessed)
                {
                    Debug.Log($"[DIAGNOSTIC] PostProcessed item: {(go != null ? go.name : "NULL")}");
                }

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                var result = await test.TestFind<Button>(
                    new Search().Name("DiagContainer").GetChild(0));
                Assert.IsNotNull(result, "Should find child button via Child(0)");
                Assert.AreEqual("DiagChild", result.name);
            });
        }

        #endregion

        #region Adjacent Tests

        [UnityTest]
        public IEnumerator Adjacent_Right_FindsInputToRightOfLabel()
        {
            var label = CreateLabel("Username:", new Vector2(-100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(100, 0));
            yield return null;
            var search = new Search().Adjacent("Username:");
            bool matches = search.Matches(input);
            Assert.IsTrue(matches, "Should find input to the right of label");
        }

        [UnityTest]
        public IEnumerator Adjacent_Right_IgnoresInputToLeft()
        {
            var label = CreateLabel("Username:", new Vector2(100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(-100, 0));
            yield return null;
            var search = new Search().Adjacent("Username:", Direction.Right);
            bool matches = search.Matches(input);
            Assert.IsFalse(matches, "Should not match input to the left when searching Right");
        }

        [UnityTest]
        public IEnumerator Adjacent_Right_PrefersCloserInput()
        {
            var label = CreateLabel("Field:", new Vector2(-150, 0));
            var closeInput = CreateInputField("CloseInput", new Vector2(0, 0));
            var farInput = CreateInputField("FarInput", new Vector2(200, 0));
            yield return null;
            var search = new Search().Adjacent("Field:");
            Assert.IsTrue(search.Matches(closeInput), "Closer input should match");
            Assert.IsFalse(search.Matches(farInput), "Farther input should not match");
        }

        [UnityTest]
        public IEnumerator Adjacent_Left_FindsInputToLeftOfLabel()
        {
            var input = CreateInputField("VolumeSlider", new Vector2(-100, 0));
            var label = CreateLabel("Volume Level", new Vector2(100, 0));
            yield return null;
            var search = new Search().Adjacent("Volume Level", Direction.Left);
            bool matches = search.Matches(input);
            Assert.IsTrue(matches, "Should find input to the left of label");
        }

        [UnityTest]
        public IEnumerator Adjacent_Below_FindsInputBelowLabel()
        {
            var label = CreateLabel("Description", new Vector2(0, 50));
            var input = CreateInputField("DescriptionInput", new Vector2(0, -50));
            yield return null;
            var search = new Search().Adjacent("Description", Direction.Below);
            bool matches = search.Matches(input);
            Assert.IsTrue(matches, "Should find input below label");
        }

        [UnityTest]
        public IEnumerator Adjacent_Above_FindsInputAboveLabel()
        {
            var input = CreateInputField("OptionToggle", new Vector2(0, 50));
            var label = CreateLabel("Clear Selection", new Vector2(0, -50));
            yield return null;
            var search = new Search().Adjacent("Clear Selection", Direction.Above);
            bool matches = search.Matches(input);
            Assert.IsTrue(matches, "Should find input above label");
        }

        [UnityTest]
        public IEnumerator Adjacent_NoMatchingLabel_ReturnsFalse()
        {
            var label = CreateLabel("Other Label:", new Vector2(-100, 0));
            var input = CreateInputField("TestInput", new Vector2(100, 0));
            yield return null;
            var search = new Search().Adjacent("NonExistent:");
            bool matches = search.Matches(input);
            Assert.IsFalse(matches, "Should not match when label text doesn't exist");
        }

        [UnityTest]
        public IEnumerator Adjacent_WildcardPattern_Matches()
        {
            var label = CreateLabel("Username:", new Vector2(-100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(100, 0));
            yield return null;
            var search = new Search().Adjacent("User*");
            bool matches = search.Matches(input);
            Assert.IsTrue(matches, "Should match with wildcard pattern");
        }

        #endregion

        #region Near Tests

        [UnityTest]
        public IEnumerator Near_MatchesAllInDirection_OrdersByDistance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Label in center, buttons at different distances
                var label = CreateLabel("Center Flag", new Vector2(0, 0));
                var closeButton = CreateButton("TextureButton", new Vector2(50, -30)); // Closest
                var farButton = CreateButton("OtherButton", new Vector2(200, 0)); // Further away
                await UniTask.Yield();

                // Near() without direction matches ALL elements
                var search = new Search().Near("Center Flag");
                Assert.IsTrue(search.Matches(closeButton), "Close button should match");
                Assert.IsTrue(search.Matches(farButton), "Far button should also match (no direction filter)");

                // Find() returns closest due to ordering
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(new Search().Near("Center Flag"));
                Assert.AreEqual("TextureButton", result.name, "Find should return closest button");
            });
        }

        [UnityTest]
        public IEnumerator Near_WithDirectionFilter_OnlyMatchesInDirection()
        {
            var label = CreateLabel("Center Flag", new Vector2(0, 0));
            var belowButton = CreateButton("BelowButton", new Vector2(0, -80)); // Below
            var aboveButton = CreateButton("AboveButton", new Vector2(0, 80)); // Above
            yield return null;

            // Direction filtering works - elements outside direction don't match
            var searchBelow = new Search().Near("Center Flag", Direction.Below);
            Assert.IsTrue(searchBelow.Matches(belowButton), "Button below should match when filtering Below");
            Assert.IsFalse(searchBelow.Matches(aboveButton), "Button above should NOT match when filtering Below");

            var searchAbove = new Search().Near("Center Flag", Direction.Above);
            Assert.IsTrue(searchAbove.Matches(aboveButton), "Button above should match when filtering Above");
            Assert.IsFalse(searchAbove.Matches(belowButton), "Button below should NOT match when filtering Above");
        }

        [UnityTest]
        public IEnumerator Near_WithDirectionFilter_AllInDirectionMatch()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var label = CreateLabel("Center Flag", new Vector2(0, 50));
                var closeBelow = CreateButton("CloseBelow", new Vector2(0, -30)); // Closer below
                var farBelow = CreateButton("FarBelow", new Vector2(0, -150)); // Further below
                await UniTask.Yield();

                // Both elements below match - Near() matches all in direction
                var search = new Search().Near("Center Flag", Direction.Below);
                Assert.IsTrue(search.Matches(closeBelow), "Closer button below should match");
                Assert.IsTrue(search.Matches(farBelow), "Farther button below should also match");

                // Find() returns closest
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(new Search().Near("Center Flag", Direction.Below));
                Assert.AreEqual("CloseBelow", result.name, "Find should return closest button");
            });
        }

        [UnityTest]
        public IEnumerator Near_DiagonalElement_AllMatch_OrderedByDistance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Label and buttons - Near() without direction matches all
                var label = CreateLabel("Texture", new Vector2(0, 50));
                var diagonalClose = CreateButton("DiagonalButton", new Vector2(30, -20)); // Diagonal but closest
                var straightFar = CreateButton("StraightButton", new Vector2(0, -150)); // Straight down but far
                await UniTask.Yield();

                var search = new Search().Near("Texture");
                Assert.IsTrue(search.Matches(diagonalClose), "Diagonal button should match");
                Assert.IsTrue(search.Matches(straightFar), "Straight button should also match");

                // Find() returns closest
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();
                var result = await test.TestFind<Button>(new Search().Near("Texture"));
                Assert.AreEqual("DiagonalButton", result.name, "Find should return closest (diagonal) button");
            });
        }

        [UnityTest]
        public IEnumerator Near_ChainedWithName_FiltersResults()
        {
            var label = CreateLabel("Center Flag", new Vector2(0, 50));
            var textureButton = CreateButton("TextureButton", new Vector2(0, -30));
            var maskButton = CreateButton("MaskButton", new Vector2(0, -80));
            yield return null;

            // Name filter excludes MaskButton, Near matches both (in direction)
            var search = new Search().Near("Center Flag", Direction.Below).Name("*Texture*");
            Assert.IsTrue(search.Matches(textureButton), "TextureButton should match Near+Name filter");
            Assert.IsFalse(search.Matches(maskButton), "MaskButton should not match Name filter (different name)");
        }

        [UnityTest]
        public IEnumerator Near_NonExistentLabel_ReturnsFalse()
        {
            var button = CreateButton("SomeButton", new Vector2(0, 0));
            yield return null;

            var search = new Search().Near("NonExistentLabel");
            Assert.IsFalse(search.Matches(button), "Should not match when label doesn't exist");
        }

        [UnityTest]
        public IEnumerator Near_WildcardPattern_Matches()
        {
            var label = CreateLabel("Center Flag", new Vector2(0, 50));
            var button = CreateButton("NearbyButton", new Vector2(0, -30));
            yield return null;

            var search = new Search().Near("Center*");
            Assert.IsTrue(search.Matches(button), "Should match with wildcard pattern");
        }

        [UnityTest]
        public IEnumerator Near_LeftDirection_FindsClosestToLeft()
        {
            var label = CreateLabel("Right Label", new Vector2(100, 0));
            var leftButton = CreateButton("LeftButton", new Vector2(-50, 0)); // To the left
            var rightButton = CreateButton("RightButton", new Vector2(200, 0)); // To the right
            yield return null;

            var search = new Search().Near("Right Label", Direction.Left);
            Assert.IsTrue(search.Matches(leftButton), "Button to the left should match");
            Assert.IsFalse(search.Matches(rightButton), "Button to the right should not match");
        }

        [UnityTest]
        public IEnumerator Near_RightDirection_FindsClosestToRight()
        {
            var label = CreateLabel("Left Label", new Vector2(-100, 0));
            var leftButton = CreateButton("LeftButton", new Vector2(-200, 0)); // To the left
            var rightButton = CreateButton("RightButton", new Vector2(50, 0)); // To the right
            yield return null;

            var search = new Search().Near("Left Label", Direction.Right);
            Assert.IsFalse(search.Matches(leftButton), "Button to the left should not match");
            Assert.IsTrue(search.Matches(rightButton), "Button to the right should match");
        }

        [UnityTest]
        public IEnumerator Near_VsAdjacent_NearIsMoreLenient()
        {
            // ByAdjacent requires strict row/column alignment
            // Near uses pure distance, so diagonal elements can match
            var label = CreateLabel("Test Label", new Vector2(0, 0));
            // Diagonal position - not strictly "right" or "below" but close
            var diagonalButton = CreateButton("DiagonalButton", new Vector2(80, -60));
            yield return null;

            // Adjacent.Right requires same row (within tolerance) - should fail for diagonal
            var adjacentSearch = new Search().Adjacent("Test Label", Direction.Right);

            // Near without direction should find it as closest
            var nearSearch = new Search().Near("Test Label");
            Assert.IsTrue(nearSearch.Matches(diagonalButton), "Near should find diagonal element as closest");
        }

        /// <summary>
        /// Tests Near() with duplicate elements - simulates Garage scenario where
        /// "Center Flag" and "Right Flag" sections both have "Texture" and "Mask" controls.
        /// Near() matches all elements in the specified direction, ordered by distance.
        /// The closest element is returned first by Find().
        /// </summary>
        [UnityTest]
        public IEnumerator Near_DuplicateElements_CenterFlag_NearFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create two sections with identical child structure
                var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
                CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
                var centerMaskBtn = CreateButton("CenterFlagMaskBtn", "Mask", centerSection.transform, new Vector2(0, -20));

                var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
                CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
                var rightMaskBtn = CreateButton("RightFlagMaskBtn", "Mask", rightSection.transform, new Vector2(0, -20));

                await UniTask.Yield();

                // Near matches all elements below, ordered by distance
                // Use HasChild(Text()) to match buttons by their child text
                var search = new Search().Near("Center Flag", Direction.Below).HasChild(new Search().Text("Mask"));

                // Both buttons are below "Center Flag" so both match
                Assert.IsTrue(search.Matches(centerMaskBtn.gameObject), "centerMaskBtn should match (closest)");
                Assert.IsTrue(search.Matches(rightMaskBtn.gameObject), "rightMaskBtn should match (farther but still below)");

                // Find returns closest first - centerMaskBtn is closest to "Center Flag"
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();
                var results = await test.TestFindAll<Button>(search);
                Assert.AreEqual(2, results.Count, "Should find 2 Mask buttons");
                Assert.AreEqual("CenterFlagMaskBtn", results[0].name, "First result should be centerMaskBtn (closest)");
                Assert.AreEqual("RightFlagMaskBtn", results[1].name, "Second result should be rightMaskBtn (farther)");
            });
        }

        [UnityTest]
        public IEnumerator Near_DuplicateElements_CenterFlag_TextFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create two sections with identical child structure
                var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
                CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
                var centerMaskBtn = CreateButton("CenterFlagMaskBtn", "Mask", centerSection.transform, new Vector2(0, -20));

                var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
                CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
                var rightMaskBtn = CreateButton("RightFlagMaskBtn", "Mask", rightSection.transform, new Vector2(0, -20));

                await UniTask.Yield();

                // HasChild(Text()) first, then Near - same result since Near sets ordering
                var search = new Search().HasChild(new Search().Text("Mask")).Near("Center Flag", Direction.Below);

                // Both buttons are below "Center Flag" so both match
                Assert.IsTrue(search.Matches(centerMaskBtn.gameObject), "centerMaskBtn should match (closest)");
                Assert.IsTrue(search.Matches(rightMaskBtn.gameObject), "rightMaskBtn should match (farther but still below)");

                // Find returns closest first
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();
                var results = await test.TestFindAll<Button>(search);
                Assert.AreEqual(2, results.Count, "Should find 2 Mask buttons");
                Assert.AreEqual("CenterFlagMaskBtn", results[0].name, "First result should be centerMaskBtn (closest)");
            });
        }

        [UnityTest]
        public IEnumerator Near_DuplicateElements_RightFlag_NearFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create two sections with identical child structure
                var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
                CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
                var centerMaskBtn = CreateButton("CenterFlagMaskBtn", "Mask", centerSection.transform, new Vector2(0, -20));

                var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
                CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
                var rightMaskBtn = CreateButton("RightFlagMaskBtn", "Mask", rightSection.transform, new Vector2(0, -20));

                await UniTask.Yield();

                // Near matches all elements below "Right Flag", ordered by distance
                var search = new Search().Near("Right Flag", Direction.Below).HasChild(new Search().Text("Mask"));

                // Both buttons are below "Right Flag" so both match
                Assert.IsTrue(search.Matches(rightMaskBtn.gameObject), "rightMaskBtn should match (closest)");
                Assert.IsTrue(search.Matches(centerMaskBtn.gameObject), "centerMaskBtn should match (farther but still below)");

                // Find returns closest first - rightMaskBtn is closest to "Right Flag"
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();
                var results = await test.TestFindAll<Button>(search);
                Assert.AreEqual(2, results.Count, "Should find 2 Mask buttons");
                Assert.AreEqual("RightFlagMaskBtn", results[0].name, "First result should be rightMaskBtn (closest)");
                Assert.AreEqual("CenterFlagMaskBtn", results[1].name, "Second result should be centerMaskBtn (farther)");
            });
        }

        [UnityTest]
        public IEnumerator Near_DuplicateElements_RightFlag_TextFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create two sections with identical child structure
                var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
                CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
                var centerMaskBtn = CreateButton("CenterFlagMaskBtn", "Mask", centerSection.transform, new Vector2(0, -20));

                var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
                CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
                var rightMaskBtn = CreateButton("RightFlagMaskBtn", "Mask", rightSection.transform, new Vector2(0, -20));

                await UniTask.Yield();

                // HasChild(Text()) first, then Near - same result since Near sets ordering
                var search = new Search().HasChild(new Search().Text("Mask")).Near("Right Flag", Direction.Below);

                // Both buttons are below "Right Flag" so both match
                Assert.IsTrue(search.Matches(rightMaskBtn.gameObject), "rightMaskBtn should match (closest)");
                Assert.IsTrue(search.Matches(centerMaskBtn.gameObject), "centerMaskBtn should match (farther but still below)");

                // Find returns closest first
                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindAllHelper>();
                var results = await test.TestFindAll<Button>(search);
                Assert.AreEqual(2, results.Count, "Should find 2 Mask buttons");
                Assert.AreEqual("RightFlagMaskBtn", results[0].name, "First result should be rightMaskBtn (closest)");
            });
        }

        [UnityTest]
        public IEnumerator Adjacent_DuplicateElements_CenterFlag_AdjacentFirst()
        {
            // Create two sections with identical child structure
            var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
            CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
            var centerTextureBtn = CreateButton("CenterFlagTextureBtn", "Texture", centerSection.transform, new Vector2(0, -20));

            var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
            CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
            var rightTextureBtn = CreateButton("RightFlagTextureBtn", "Texture", rightSection.transform, new Vector2(0, -20));

            yield return null;

            // Test: Adjacent first, then HasChild(Text()) - should find Center Flag's Texture button
            // HasChild(Text()) matches elements that have a child with the text
            var search = new Search().Adjacent("Center Flag", Direction.Below).HasChild(new Search().Text("Texture"));
            Assert.IsTrue(search.Matches(centerTextureBtn.gameObject), "Adjacent.HasChild(Text) should find CenterFlagTextureBtn");
            Assert.IsFalse(search.Matches(rightTextureBtn.gameObject), "Adjacent.HasChild(Text) should NOT match RightFlagTextureBtn");
        }

        [UnityTest]
        public IEnumerator Adjacent_DuplicateElements_CenterFlag_TextFirst()
        {
            // Create two sections with identical child structure
            var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(-150, 0), new Vector2(200, 150));
            CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 50));
            var centerTextureBtn = CreateButton("CenterFlagTextureBtn", "Texture", centerSection.transform, new Vector2(0, -20));

            var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(150, 0), new Vector2(200, 150));
            CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 50));
            var rightTextureBtn = CreateButton("RightFlagTextureBtn", "Texture", rightSection.transform, new Vector2(0, -20));

            yield return null;

            // Test: HasChild(Text()) first, then Adjacent - should find Center Flag's Texture button
            // HasChild(Text()) matches elements that have a child with the text
            var search = new Search().HasChild(new Search().Text("Texture")).Adjacent("Center Flag", Direction.Below);
            Assert.IsTrue(search.Matches(centerTextureBtn.gameObject), "HasChild(Text).Adjacent should find CenterFlagTextureBtn");
            Assert.IsFalse(search.Matches(rightTextureBtn.gameObject), "HasChild(Text).Adjacent should NOT match RightFlagTextureBtn");
        }

        [UnityTest]
        public IEnumerator Adjacent_WithName_FindsNearestMatchingName()
        {
            // Create label with two buttons to the right at different distances
            var label = CreateLabel("Settings:", new Vector2(-150, 0));
            var closeButton = CreateButton("CloseButton", "Edit", _canvas.transform, new Vector2(0, 0));
            var farButton = CreateButton("FarButton", "Save", _canvas.transform, new Vector2(150, 0));
            yield return null;

            // Adjacent finds the single nearest element - Name doesn't change that
            var search = new Search().Adjacent("Settings:", Direction.Right).Name("*Button*");
            Assert.IsTrue(search.Matches(closeButton.gameObject), "Adjacent.Name should find CloseButton (nearest)");
            Assert.IsFalse(search.Matches(farButton.gameObject), "Adjacent.Name should NOT match FarButton (not nearest)");
        }

        [UnityTest]
        public IEnumerator Adjacent_WithType_FindsNearestMatchingType()
        {
            // Create label with two buttons to the right at different distances
            var label = CreateLabel("Account:", new Vector2(-150, 0));
            var closeButton = CreateButton("CloseButton", "Save", _canvas.transform, new Vector2(0, 0));
            var farButton = CreateButton("FarButton", "Delete", _canvas.transform, new Vector2(150, 0));
            yield return null;

            // Adjacent finds the single nearest element - Type doesn't change that
            var search = new Search().Adjacent("Account:", Direction.Right).Type<Button>();
            Assert.IsTrue(search.Matches(closeButton.gameObject), "Adjacent.Type<Button> should find CloseButton (nearest)");
            Assert.IsFalse(search.Matches(farButton.gameObject), "Adjacent.Type<Button> should NOT match FarButton (not nearest)");
        }

        /// <summary>
        /// Tests Near() with vertically stacked sections like the Garage UI.
        /// Center Flag section is above Right Flag section, each with identical child labels.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_VerticalSections_HeaderAboveContent()
        {
            // Center Flag section at TOP (y=150)
            var centerSection = CreatePanel("CenterFlagSection", _canvas.transform, new Vector2(0, 150), new Vector2(250, 200));
            var centerLabel = CreateLabelInParent("Center Flag", centerSection.transform, new Vector2(0, 80));  // Header at top of section
            var centerTexture = CreateButton("CenterTexture", "Texture", centerSection.transform, new Vector2(0, 40));
            var centerMask = CreateButton("CenterMask", "Mask", centerSection.transform, new Vector2(0, 0));
            var centerNone = CreateButton("CenterNone", "None", centerSection.transform, new Vector2(0, -40));

            // Right Flag section BELOW (y=-100)
            var rightSection = CreatePanel("RightFlagSection", _canvas.transform, new Vector2(0, -100), new Vector2(250, 200));
            var rightLabel = CreateLabelInParent("Right Flag", rightSection.transform, new Vector2(0, 80));  // Header at top of section
            var rightTexture = CreateButton("RightTexture", "Texture", rightSection.transform, new Vector2(0, 40));
            var rightMask = CreateButton("RightMask", "Mask", rightSection.transform, new Vector2(0, 0));
            var rightNone = CreateButton("RightNone", "None", rightSection.transform, new Vector2(0, -40));

            yield return null;
            Canvas.ForceUpdateCanvases();

            // Debug: Log world positions
            Vector3[] corners = new Vector3[4];

            rightLabel.GetComponent<RectTransform>().GetWorldCorners(corners);
            var rightLabelY = (corners[0].y + corners[2].y) / 2;
            Debug.Log($"[Near Test] Right Flag label world Y: {rightLabelY}");

            rightTexture.GetComponent<RectTransform>().GetWorldCorners(corners);
            var rightTextureY = (corners[0].y + corners[2].y) / 2;
            Debug.Log($"[Near Test] rightTexture world Y: {rightTextureY}, distance to label: {Mathf.Abs(rightTextureY - rightLabelY)}");

            rightMask.GetComponent<RectTransform>().GetWorldCorners(corners);
            var rightMaskY = (corners[0].y + corners[2].y) / 2;
            Debug.Log($"[Near Test] rightMask world Y: {rightMaskY}, distance to label: {Mathf.Abs(rightMaskY - rightLabelY)}");

            // Test: Near("Right Flag", Below) alone - which elements match?
            var searchNearRightFlag = new Search().Near("Right Flag", Direction.Below);
            Debug.Log($"[Near Test] rightTexture matches Near('Right Flag', Below): {searchNearRightFlag.Matches(rightTexture.gameObject)}");
            Debug.Log($"[Near Test] rightMask matches Near('Right Flag', Below): {searchNearRightFlag.Matches(rightMask.gameObject)}");
            Debug.Log($"[Near Test] rightNone matches Near('Right Flag', Below): {searchNearRightFlag.Matches(rightNone.gameObject)}");

            // Near() matches ALL elements in the direction, but orders by distance
            // Use HasChild(Text()) to match buttons by their child text
            // Both Textures are below "Center Flag" (since rightTexture is also below Center Flag at y=230)
            var searchCenterTexture = new Search().HasChild(new Search().Text("Texture")).Near("Center Flag", Direction.Below);
            Assert.IsTrue(searchCenterTexture.Matches(centerTexture.gameObject), "Center Texture should match (below Center Flag)");
            // rightTexture is also below Center Flag, so it matches too
            Assert.IsTrue(searchCenterTexture.Matches(rightTexture.gameObject), "Right Texture also matches (below Center Flag)");

            // Near("Right Flag", Below) - both Masks are below Right Flag
            var searchRightMask = new Search().HasChild(new Search().Text("Mask")).Near("Right Flag", Direction.Below);
            Assert.IsTrue(searchRightMask.Matches(rightMask.gameObject), "Right Mask should match");
            // centerMask is ABOVE Right Flag (y=150 vs y=-20), so it should NOT match
            Assert.IsFalse(searchRightMask.Matches(centerMask.gameObject), "Center Mask should NOT match (above Right Flag)");

            // Near first, then HasChild(Text()) - Right Flag's None is below Right Flag
            var searchNearFirst = new Search().Near("Right Flag", Direction.Below).HasChild(new Search().Text("None"));
            Assert.IsTrue(searchNearFirst.Matches(rightNone.gameObject), "Right None should match (below Right Flag)");
            // centerNone is ABOVE Right Flag, so it should NOT match
            Assert.IsFalse(searchNearFirst.Matches(centerNone.gameObject), "Center None should NOT match (above Right Flag)");
        }

        /// <summary>
        /// Tests Near() with three vertically stacked sections - Near() matches all in direction,
        /// but Find() returns closest first due to ordering.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_ThreeVerticalSections_OrdersByDistance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Top section (y=300) - label at top, button well below with realistic 100px spacing
                var topSection = CreatePanel("TopSection", _canvas.transform, new Vector2(0, 300), new Vector2(200, 200));
                CreateLabelInParent("Top Header", topSection.transform, new Vector2(0, 70));
                var topBtn = CreateButton("TopBtn", "Action", topSection.transform, new Vector2(0, -70));

                // Middle section (y=0)
                var midSection = CreatePanel("MiddleSection", _canvas.transform, new Vector2(0, 0), new Vector2(200, 200));
                CreateLabelInParent("Middle Header", midSection.transform, new Vector2(0, 70));
                var midBtn = CreateButton("MiddleBtn", "Action", midSection.transform, new Vector2(0, -70));

                // Bottom section (y=-300)
                var bottomSection = CreatePanel("BottomSection", _canvas.transform, new Vector2(0, -300), new Vector2(200, 200));
                CreateLabelInParent("Bottom Header", bottomSection.transform, new Vector2(0, 70));
                var bottomBtn = CreateButton("BottomBtn", "Action", bottomSection.transform, new Vector2(0, -70));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // Find() with Near() returns closest element first
                // Use HasChild(Text()) to match buttons by their child text
                var topResult = await test.TestFind<Button>(new Search().HasChild(new Search().Text("Action")).Near("Top Header", Direction.Below));
                Assert.AreEqual("TopBtn", topResult.name, "Should find Top section's Action (closest to Top Header)");

                var midResult = await test.TestFind<Button>(new Search().HasChild(new Search().Text("Action")).Near("Middle Header", Direction.Below));
                Assert.AreEqual("MiddleBtn", midResult.name, "Should find Middle section's Action (closest to Middle Header)");

                var bottomResult = await test.TestFind<Button>(new Search().HasChild(new Search().Text("Action")).Near("Bottom Header", Direction.Below));
                Assert.AreEqual("BottomBtn", bottomResult.name, "Should find Bottom section's Action (closest to Bottom Header)");

                // Verify direction filtering works - topBtn is ABOVE Middle Header, so shouldn't match
                var searchMid = new Search().HasChild(new Search().Text("Action")).Near("Middle Header", Direction.Below);
                Assert.IsFalse(searchMid.Matches(topBtn.gameObject), "Top button is above Middle Header");
            });
        }

        /// <summary>
        /// Tests Near() with form-style layout - label on left, control on right in each row.
        /// Near() matches all elements in direction, Find() returns closest.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_FormLayout_LabelLeftControlRight()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Row 1: Username label -> input field
                CreateLabelInParent("Username", _canvas.transform, new Vector2(-100, 50));
                var usernameInput = CreateButton("UsernameInput", "input1", _canvas.transform, new Vector2(50, 50));

                // Row 2: Password label -> input field
                CreateLabelInParent("Password", _canvas.transform, new Vector2(-100, -50));
                var passwordInput = CreateButton("PasswordInput", "input2", _canvas.transform, new Vector2(50, -50));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // Find() returns closest input to the right of each label
                var usernameResult = await test.TestFind<Button>(new Search().Near("Username", Direction.Right));
                Assert.AreEqual("UsernameInput", usernameResult.name, "Should find Username's input (closest to Username)");

                var passwordResult = await test.TestFind<Button>(new Search().Near("Password", Direction.Right));
                Assert.AreEqual("PasswordInput", passwordResult.name, "Should find Password's input (closest to Password)");

                // Verify direction filtering - passwordInput is NOT to the right of Username (it's below)
                // Both inputs are at x=50, both labels at x=-100
                // So both inputs ARE to the right of BOTH labels
                var searchUsername = new Search().Near("Username", Direction.Right);
                Assert.IsTrue(searchUsername.Matches(usernameInput.gameObject), "Username input is right of Username label");
                Assert.IsTrue(searchUsername.Matches(passwordInput.gameObject), "Password input is also right of Username label (same x)");
            });
        }

        /// <summary>
        /// Tests Near() with realistic vertical spacing - labels and buttons clearly separated.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_RealisticVerticalSpacing_FindsCorrectButton()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create sections with realistic 150px spacing between rows
                // Label at top of each section, button well below it

                // Top section
                CreateLabelInParent("Section A", _canvas.transform, new Vector2(0, 300));
                var btnA = CreateButton("BtnA", "Action A", _canvas.transform, new Vector2(0, 200));

                // Middle section
                CreateLabelInParent("Section B", _canvas.transform, new Vector2(0, 50));
                var btnB = CreateButton("BtnB", "Action B", _canvas.transform, new Vector2(0, -50));

                // Bottom section
                CreateLabelInParent("Section C", _canvas.transform, new Vector2(0, -200));
                var btnC = CreateButton("BtnC", "Action C", _canvas.transform, new Vector2(0, -300));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // Each button should be found when searching below its label
                var resultA = await test.TestFind<Button>(new Search().Near("Section A", Direction.Below));
                Assert.AreEqual("BtnA", resultA.name, "Should find BtnA below Section A");

                var resultB = await test.TestFind<Button>(new Search().Near("Section B", Direction.Below));
                Assert.AreEqual("BtnB", resultB.name, "Should find BtnB below Section B");

                var resultC = await test.TestFind<Button>(new Search().Near("Section C", Direction.Below));
                Assert.AreEqual("BtnC", resultC.name, "Should find BtnC below Section C");
            });
        }

        /// <summary>
        /// Tests Near() with realistic horizontal form layout - label left, control right with gap.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_RealisticHorizontalForm_FindsCorrectInput()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Form with 200px horizontal gap between labels and inputs

                // Row 1
                CreateLabelInParent("Email:", _canvas.transform, new Vector2(-200, 100));
                var emailInput = CreateButton("EmailInput", "[email]", _canvas.transform, new Vector2(100, 100));

                // Row 2
                CreateLabelInParent("Phone:", _canvas.transform, new Vector2(-200, 0));
                var phoneInput = CreateButton("PhoneInput", "[phone]", _canvas.transform, new Vector2(100, 0));

                // Row 3
                CreateLabelInParent("Address:", _canvas.transform, new Vector2(-200, -100));
                var addressInput = CreateButton("AddressInput", "[address]", _canvas.transform, new Vector2(100, -100));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // Each input should be found when searching right of its label
                var resultEmail = await test.TestFind<Button>(new Search().Near("Email:", Direction.Right));
                Assert.AreEqual("EmailInput", resultEmail.name, "Should find EmailInput right of Email:");

                var resultPhone = await test.TestFind<Button>(new Search().Near("Phone:", Direction.Right));
                Assert.AreEqual("PhoneInput", resultPhone.name, "Should find PhoneInput right of Phone:");

                var resultAddress = await test.TestFind<Button>(new Search().Near("Address:", Direction.Right));
                Assert.AreEqual("AddressInput", resultAddress.name, "Should find AddressInput right of Address:");
            });
        }

        /// <summary>
        /// Tests Near() with grid layout - multiple items in each direction.
        /// </summary>
        [UnityTest]
        public IEnumerator Near_GridLayout_FindsClosestInDirection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Center label with buttons in all 4 directions at varying distances
                CreateLabelInParent("Center", _canvas.transform, new Vector2(0, 0));

                // Right side - closest and further
                var rightClose = CreateButton("RightClose", "R1", _canvas.transform, new Vector2(150, 0));
                var rightFar = CreateButton("RightFar", "R2", _canvas.transform, new Vector2(300, 0));

                // Left side
                var leftClose = CreateButton("LeftClose", "L1", _canvas.transform, new Vector2(-150, 0));
                var leftFar = CreateButton("LeftFar", "L2", _canvas.transform, new Vector2(-300, 0));

                // Below
                var belowClose = CreateButton("BelowClose", "B1", _canvas.transform, new Vector2(0, -150));
                var belowFar = CreateButton("BelowFar", "B2", _canvas.transform, new Vector2(0, -300));

                // Above
                var aboveClose = CreateButton("AboveClose", "A1", _canvas.transform, new Vector2(0, 150));
                var aboveFar = CreateButton("AboveFar", "A2", _canvas.transform, new Vector2(0, 300));

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestFindHelper>();

                // Should find closest in each direction
                var resultRight = await test.TestFind<Button>(new Search().Near("Center", Direction.Right));
                Assert.AreEqual("RightClose", resultRight.name, "Should find closest button to the right");

                var resultLeft = await test.TestFind<Button>(new Search().Near("Center", Direction.Left));
                Assert.AreEqual("LeftClose", resultLeft.name, "Should find closest button to the left");

                var resultBelow = await test.TestFind<Button>(new Search().Near("Center", Direction.Below));
                Assert.AreEqual("BelowClose", resultBelow.name, "Should find closest button below");

                var resultAbove = await test.TestFind<Button>(new Search().Near("Center", Direction.Above));
                Assert.AreEqual("AboveClose", resultAbove.name, "Should find closest button above");
            });
        }

        #endregion

        #region ScrollTo Tests

        [UnityTest]
        public IEnumerator Diagnostic_ScrollTo_Visibility()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollView = CreateScrollView("DiagScrollView", new Vector2(0, 0), new Vector2(300, 200));
                var scrollRect = scrollView.GetComponentInChildren<ScrollRect>();
                var viewport = scrollRect.viewport;
                var content = scrollRect.content;

                var visibleBtn = CreateScrollButton("DiagVisibleItem", "Visible", content.transform);
                for (int i = 0; i < 10; i++)
                {
                    CreateScrollButton($"DiagHiddenItem{i}", $"Hidden {i}", content.transform);
                }

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 1f; // Start at top
                await UniTask.Yield();

                // Log viewport info
                Vector3[] viewportCorners = new Vector3[4];
                viewport.GetWorldCorners(viewportCorners);
                Debug.Log($"[DIAGNOSTIC] Viewport corners: [{viewportCorners[0]}, {viewportCorners[1]}, {viewportCorners[2]}, {viewportCorners[3]}]");
                Debug.Log($"[DIAGNOSTIC] Viewport min: {viewportCorners[0]}, max: {viewportCorners[2]}");
                Debug.Log($"[DIAGNOSTIC] Content height: {content.rect.height}");

                // Log first few item positions
                var firstBtn = visibleBtn.GetComponent<RectTransform>();
                Vector3[] corners = new Vector3[4];
                firstBtn.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;
                Debug.Log($"[DIAGNOSTIC] {visibleBtn.name}: worldCenter={center}, corners[0]={corners[0]}, corners[2]={corners[2]}");

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestScrollHelper>();

                Debug.Log("[DIAGNOSTIC] === Testing ScrollTo with visible item ===");
                var result = await test.TestScrollTo(
                    new Search().Name("DiagScrollView"),
                    new Search().Name("DiagVisibleItem"));

                Debug.Log($"[DIAGNOSTIC] ScrollTo result: {(result != null ? result.name : "NULL")}");
                Assert.IsNotNull(result, "Should find visible item");
                Assert.AreEqual("DiagVisibleItem", result.name);
            });
        }

        [UnityTest]
        public IEnumerator ScrollTo_FindsVisibleItem_ReturnsImmediately()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollView = CreateScrollView("TestScrollView", new Vector2(0, 0), new Vector2(300, 200));
                var scrollRect = scrollView.GetComponentInChildren<ScrollRect>();
                var content = scrollRect.content;

                // First item is visible, rest are below
                CreateScrollButton("VisibleItem", "Visible", content.transform);
                for (int i = 0; i < 10; i++)
                {
                    CreateScrollButton($"HiddenItem{i}", $"Hidden {i}", content.transform);
                }

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 1f; // Start at top
                await UniTask.Yield();

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestScrollHelper>();
                var result = await test.TestScrollTo(
                    new Search().Name("TestScrollView"),
                    new Search().Name("VisibleItem"));
                Assert.IsNotNull(result, "Should find visible item");
                Assert.AreEqual("VisibleItem", result.name);
            });
        }

        [UnityTest]
        public IEnumerator ScrollTo_ScrollsToHiddenItem()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollView = CreateScrollView("TestScrollView", new Vector2(0, 0), new Vector2(300, 200));
                var scrollRect = scrollView.GetComponentInChildren<ScrollRect>();
                var content = scrollRect.content;

                // Create many items so we have content taller than the viewport
                // Use CreateScrollItem (not Button) so drag events bubble to ScrollRect
                for (int i = 0; i < 15; i++)
                {
                    CreateScrollItem($"Item{i}", $"Item {i}", content.transform);
                }
                CreateScrollItem("TargetItem", "Target", content.transform);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                await UniTask.Yield();

                // Ensure we're at the top
                scrollRect.verticalNormalizedPosition = 1f;
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                Debug.Log($"[TEST] Content height: {content.rect.height}, Viewport height: {scrollRect.viewport.rect.height}");
                Debug.Log($"[TEST] Initial scroll position: {scrollRect.verticalNormalizedPosition}");

                var testGO = new GameObject("TestBehaviour");
                _createdObjects.Add(testGO);
                var test = testGO.AddComponent<TestScrollHelper>();
                var result = await test.TestScrollTo(
                    new Search().Name("TestScrollView"),
                    new Search().Name("TargetItem"),
                    maxScrollAttempts: 20);

                Debug.Log($"[TEST] Final scroll position: {scrollRect.verticalNormalizedPosition}");

                Assert.IsNotNull(result, "Should find target after scrolling");
                Assert.AreEqual("TargetItem", result.name);
                Assert.Less(scrollRect.verticalNormalizedPosition, 0.5f, "Should have scrolled down");
            });
        }

        #endregion

        #region InRegion Tests

        [UnityTest]
        public IEnumerator InRegion_TopLeft_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var topLeftBtn = CreateButton("TopLeftBtn", "TL", _canvas.transform, new Vector2(-400, 250));
                var bottomRightBtn = CreateButton("BottomRightBtn", "BR", _canvas.transform, new Vector2(400, -250));
                var centerBtn = CreateButton("CenterBtn", "C", _canvas.transform, new Vector2(0, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var search = new Search().Type<Button>().InRegion(ScreenRegion.TopLeft);
                Assert.IsTrue(search.Matches(topLeftBtn.gameObject), "TopLeft button should match TopLeft region");
                Assert.IsFalse(search.Matches(bottomRightBtn.gameObject), "BottomRight button should not match TopLeft region");
                Assert.IsFalse(search.Matches(centerBtn.gameObject), "Center button should not match TopLeft region");
            });
        }

        [UnityTest]
        public IEnumerator InRegion_Center_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var topLeftBtn = CreateButton("TopLeftBtn", "TL", _canvas.transform, new Vector2(-400, 250));
                var bottomRightBtn = CreateButton("BottomRightBtn", "BR", _canvas.transform, new Vector2(400, -250));
                var centerBtn = CreateButton("CenterBtn", "C", _canvas.transform, new Vector2(0, 0));
                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                var search = new Search().Type<Button>().InRegion(ScreenRegion.Center);
                Assert.IsFalse(search.Matches(topLeftBtn.gameObject), "TopLeft button should not match Center region");
                Assert.IsFalse(search.Matches(bottomRightBtn.gameObject), "BottomRight button should not match Center region");
                Assert.IsTrue(search.Matches(centerBtn.gameObject), "Center button should match Center region");
            });
        }

        #endregion

        #region Helper Methods

        private GameObject CreateTestObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? _canvas.transform, false);
            _createdObjects.Add(go);
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
            _createdObjects.Add(textGO);
            return go;
        }

        private Button CreateScrollButton(string name, string text, Transform parent)
        {
            // Button for use in ScrollView with VerticalLayoutGroup - no explicit positioning needed
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 40); // Width controlled by layout, height is 40
            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 40;
            layoutElement.flexibleWidth = 1;
            go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
            var button = go.AddComponent<Button>();
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            _createdObjects.Add(textGO);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            return button;
        }

        /// <summary>
        /// Creates a scroll item WITHOUT a Button component (just Image + Text).
        /// This allows drag events to properly bubble to the ScrollRect.
        /// </summary>
        private GameObject CreateScrollItem(string name, string text, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 40);
            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 40;
            layoutElement.flexibleWidth = 1;
            var image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.25f);
            // NO Button component - drag events will bubble to ScrollRect

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            _createdObjects.Add(textGO);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            return go;
        }

        private Button CreateButton(string name, string text, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 40);
            rect.anchoredPosition = position;
            go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            _createdObjects.Add(textGO);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private Slider CreateSlider(string name, float value, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(160, 20);
            rect.anchoredPosition = position;
            var slider = go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            return slider;
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            return go;
        }

        private GameObject CreateScrollView(string name, Vector2 position, Vector2 size)
        {
            // Match the working sample structure:
            // - Mask on ScrollView (not viewport) with showMaskGraphic = true
            // - No Image on Content (children have Images for raycast)
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);
            go.AddComponent<Mask>().showMaskGraphic = true; // Mask on ScrollView, not viewport
            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped; // Prevent elastic bouncing
            scrollRect.inertia = false; // Disable inertia for predictable test behavior

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            // No Mask on viewport - it's on the parent ScrollView
            scrollRect.viewport = viewportRect;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, size.y * 2);
            // NO Image on content - child items provide raycast targets
            // This ensures drag events bubble up to ScrollRect properly

            // Add VerticalLayoutGroup for proper item placement
            var layoutGroup = content.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 5;
            layoutGroup.padding = new RectOffset(5, 5, 5, 5);
            // Add ContentSizeFitter to auto-resize based on children
            var sizeFitter = content.AddComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentRect;
            return go;
        }

        private GameObject CreateLabel(string text, Vector2 position)
        {
            var go = new GameObject($"Label_{text}");
            go.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            return go;
        }

        private GameObject CreateLabelInParent(string text, Transform parent, Vector2 position)
        {
            var go = new GameObject($"Label_{text}");
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            return go;
        }

        private GameObject CreateButton(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 40);
            rect.anchoredPosition = position;
            go.AddComponent<Image>();
            go.AddComponent<Button>();
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            _createdObjects.Add(textGO);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = name;
            return go;
        }

        private GameObject CreateInputField(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(go);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150, 30);
            rect.anchoredPosition = position;
            go.AddComponent<Image>();
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = Vector2.zero;
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var inputText = textGO.AddComponent<TextMeshProUGUI>();
            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRect;
            inputField.textComponent = inputText;
            return go;
        }

        #endregion

        #region FindItems Tests

        [UnityTest]
        public IEnumerator FindItems_ScrollRect_ReturnsItemsInOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollViewGO = CreateScrollView("InventoryList", Vector2.zero, new Vector2(300, 200));
                var scrollRect = scrollViewGO.GetComponent<ScrollRect>();
                var content = scrollRect.content;

                CreateScrollItem("Item1", "First Item", content);
                CreateScrollItem("Item2", "Second Item", content);
                CreateScrollItem("Item3", "Third Item", content);
                CreateScrollItem("Item4", "Fourth Item", content);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

                var helper = CreateFindItemsHelper();
                var container = await helper.TestFindItems("InventoryList");

                Assert.NotNull(container);
                Assert.NotNull(container.ScrollRect);
                var items = container.Items.ToList();
                Assert.AreEqual(4, items.Count, "Should find 4 items");
                Assert.AreEqual("Item1", items[0].gameObject.name, "First item should be Item1");
                Assert.AreEqual("Item4", items[3].gameObject.name, "Last item should be Item4");
            });
        }

        [UnityTest]
        public IEnumerator FindItems_WithFilter_ReturnsFilteredItems()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollViewGO = CreateScrollView("FilterList", Vector2.zero, new Vector2(300, 200));
                var scrollRect = scrollViewGO.GetComponent<ScrollRect>();
                var content = scrollRect.content;

                CreateScrollItem("RareItem1", "Rare Sword", content);
                CreateScrollItem("CommonItem1", "Common Shield", content);
                CreateScrollItem("RareItem2", "Rare Bow", content);
                CreateScrollItem("CommonItem2", "Common Helmet", content);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

                var helper = CreateFindItemsHelper();
                var container = await helper.TestFindItems("FilterList", new Search().Name("Rare*"));

                var items = container.Items.ToList();
                Assert.AreEqual(2, items.Count, "Should find 2 rare items");
                Assert.IsTrue(items.All(i => i.gameObject.name.StartsWith("Rare")), "All items should start with Rare");
            });
        }

        [UnityTest]
        public IEnumerator FindItems_VerticalLayoutGroup_ReturnsItems()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var layoutGO = new GameObject("TabBar");
                layoutGO.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(layoutGO);
                var rect = layoutGO.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200, 300);
                var vlg = layoutGO.AddComponent<VerticalLayoutGroup>();
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                CreateButton("Tab1", "Home", layoutGO.transform, Vector2.zero);
                CreateButton("Tab2", "Settings", layoutGO.transform, Vector2.zero);
                CreateButton("Tab3", "Profile", layoutGO.transform, Vector2.zero);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

                var helper = CreateFindItemsHelper();
                var container = await helper.TestFindItems("TabBar");

                var items = container.Items.ToList();
                Assert.AreEqual(3, items.Count, "Should find 3 tabs");
            });
        }

        [UnityTest]
        public IEnumerator FindItems_GridLayoutGroup_ReturnsItems()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var layoutGO = new GameObject("InventoryGrid");
                layoutGO.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(layoutGO);
                var rect = layoutGO.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(300, 300);
                var glg = layoutGO.AddComponent<GridLayoutGroup>();
                glg.cellSize = new Vector2(80, 80);
                glg.spacing = new Vector2(10, 10);
                glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = 3;

                for (int i = 1; i <= 6; i++)
                {
                    var itemGO = new GameObject($"Slot{i}");
                    itemGO.transform.SetParent(layoutGO.transform, false);
                    _createdObjects.Add(itemGO);
                    itemGO.AddComponent<RectTransform>();
                    itemGO.AddComponent<Image>();
                }

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

                var helper = CreateFindItemsHelper();
                var container = await helper.TestFindItems("InventoryGrid");

                var items = container.Items.ToList();
                Assert.AreEqual(6, items.Count, "Should find 6 slots");
            });
        }

        [UnityTest]
        public IEnumerator FindItems_CanIterateWithForeach()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollViewGO = CreateScrollView("TestList", Vector2.zero, new Vector2(300, 200));
                var scrollRect = scrollViewGO.GetComponent<ScrollRect>();
                var content = scrollRect.content;

                CreateScrollItem("ListItem1", "Item 1", content);
                CreateScrollItem("ListItem2", "Item 2", content);
                CreateScrollItem("ListItem3", "Item 3", content);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

                var helper = CreateFindItemsHelper();
                var container = await helper.TestFindItems("TestList");

                int count = 0;
                foreach (var (scrollRectComponent, item) in container)
                {
                    Assert.NotNull(scrollRectComponent, "ScrollRect should not be null");
                    Assert.NotNull(item, "Item should not be null");
                    count++;
                }
                Assert.AreEqual(3, count, "Should iterate 3 times");
            });
        }

        private TestFindItemsHelper CreateFindItemsHelper()
        {
            var helperGO = new GameObject("FindItemsHelper");
            helperGO.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(helperGO);
            return helperGO.AddComponent<TestFindItemsHelper>();
        }

        #endregion

        #region Component Overload Tests

        [UnityTest]
        public IEnumerator Click_Component_ClicksAtComponentPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TestBtn", "Click Me", _canvas.transform, Vector2.zero);
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var helper = CreateComponentHelper();
                await helper.TestClickComponent(button);

                Assert.AreEqual(1, clickCount, "Button should have been clicked once");
            });
        }

        [UnityTest]
        public IEnumerator Click_Component_WorksWithFindAllIteration()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create multiple buttons
                CreateButton("Btn1", "Button 1", _canvas.transform, new Vector2(-100, 0));
                CreateButton("Btn2", "Button 2", _canvas.transform, new Vector2(0, 0));
                CreateButton("Btn3", "Button 3", _canvas.transform, new Vector2(100, 0));

                int totalClicks = 0;
                foreach (var btn in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    if (btn.name.StartsWith("Btn"))
                        btn.onClick.AddListener(() => totalClicks++);
                }

                await UniTask.Yield();
                Canvas.ForceUpdateCanvases();

                var helper = CreateComponentHelper();
                var buttons = await helper.TestFindAllButtons(new Search().Name("Btn*"));

                foreach (var button in buttons)
                {
                    await helper.TestClickComponent(button);
                }

                Assert.AreEqual(3, totalClicks, "All 3 buttons should have been clicked");
            });
        }

        private TestComponentHelper CreateComponentHelper()
        {
            var helperGO = new GameObject("ComponentHelper");
            helperGO.transform.SetParent(_canvas.transform, false);
            _createdObjects.Add(helperGO);
            return helperGO.AddComponent<TestComponentHelper>();
        }

        #endregion
    }

    #region Test Helper Components

    [UITest(Scenario = 9999, Feature = "Test Helper", Story = "Scroll Helper")]
    public class TestScrollHelper : UITestBehaviour
    {
        protected override UniTask Test() => UniTask.CompletedTask;
        private void Awake() { enabled = false; }
        public async UniTask<GameObject> TestScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20)
        {
            return await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing: false, searchTime: 2);
        }
        public async UniTask TestScrollToAndClick(Search scrollViewSearch, Search targetSearch)
        {
            await ScrollToAndClick(scrollViewSearch, targetSearch, throwIfMissing: true, searchTime: 2);
        }
    }

    [UITest(Scenario = 9998, Feature = "Test Helper", Story = "Find Helper")]
    public class TestFindHelper : UITestBehaviour
    {
        private void Awake() { enabled = false; }
        protected override UniTask Test() => UniTask.CompletedTask;
        public async UniTask<T> TestFind<T>(Search search, bool throwIfMissing = true) where T : Component
        {
            return await Find<T>(search, throwIfMissing, seconds: 2);
        }
    }

    [UITest(Scenario = 9997, Feature = "Test Helper", Story = "FindAll Helper")]
    public class TestFindAllHelper : UITestBehaviour
    {
        private void Awake() { enabled = false; }
        protected override UniTask Test() => UniTask.CompletedTask;
        public async UniTask<List<T>> TestFindAll<T>(Search search) where T : Component
        {
            var results = await FindAll<T>(search, seconds: 2);
            return results.ToList();
        }
    }

    [UITest(Scenario = 9996, Feature = "Test Helper", Story = "FindItems Helper")]
    public class TestFindItemsHelper : UITestBehaviour
    {
        private void Awake() { enabled = false; }
        protected override UniTask Test() => UniTask.CompletedTask;
        public async UniTask<ItemContainer> TestFindItems(string containerName, Search itemSearch = null)
        {
            return await FindItems(containerName, itemSearch);
        }
        public async UniTask<ItemContainer> TestFindItems(Search containerSearch, Search itemSearch = null)
        {
            return await FindItems(containerSearch, itemSearch);
        }
    }

    [UITest(Scenario = 9995, Feature = "Test Helper", Story = "Component Helper")]
    public class TestComponentHelper : UITestBehaviour
    {
        private void Awake() { enabled = false; }
        protected override UniTask Test() => UniTask.CompletedTask;

        public async UniTask TestClickComponent(Component component)
        {
            await Click(component);
        }

        public async UniTask<List<Button>> TestFindAllButtons(Search search)
        {
            var results = await FindAll<Button>(search, seconds: 2);
            return results.ToList();
        }
    }

    #endregion

    /// <summary>
    /// Debug component to log mouse/pointer state each frame.
    /// Attach to scene to compare real vs synthetic drag input.
    /// </summary>
    public class DragInputDebugger : MonoBehaviour
    {
        private bool _logging = false;
        private int _frameCount = 0;
        private UnityEngine.InputSystem.Mouse _mouse;
        private EventSystem _eventSystem;

        public void StartLogging()
        {
            _logging = true;
            _frameCount = 0;
            _mouse = UnityEngine.InputSystem.Mouse.current;
            _eventSystem = EventSystem.current;
            Debug.Log("[DragDebug] === LOGGING STARTED ===");
        }

        public void StopLogging()
        {
            _logging = false;
            Debug.Log("[DragDebug] === LOGGING STOPPED ===");
        }

        private void OnDestroy()
        {
            // Clean up references when destroyed
            _logging = false;
            _mouse = null;
            _eventSystem = null;
        }

        private void Update()
        {
            if (!_logging || _mouse == null) return;

            // Safety check for destroyed EventSystem
            if (_eventSystem == null || !_eventSystem.gameObject.activeInHierarchy)
            {
                _logging = false;
                return;
            }

            _frameCount++;

            var pos = _mouse.position.ReadValue();
            var delta = _mouse.delta.ReadValue();
            var leftBtn = _mouse.leftButton.isPressed;
            var wasPressed = _mouse.leftButton.wasPressedThisFrame;
            var wasReleased = _mouse.leftButton.wasReleasedThisFrame;

            // Get current pointer data from InputSystemUIInputModule
            string pointerInfo = "N/A";
            if (_eventSystem != null && _eventSystem.currentInputModule is UnityEngine.InputSystem.UI.InputSystemUIInputModule)
            {
                var pointerData = new PointerEventData(_eventSystem) { position = pos };
                var raycastResults = new List<RaycastResult>();
                _eventSystem.RaycastAll(pointerData, raycastResults);
                pointerInfo = raycastResults.Count > 0 ? raycastResults[0].gameObject.name : "none";
            }

            // Only log when there's activity
            if (leftBtn || delta.sqrMagnitude > 0 || wasPressed || wasReleased)
            {
                Debug.Log($"[DragDebug] F{_frameCount}: pos=({pos.x:F1},{pos.y:F1}) delta=({delta.x:F2},{delta.y:F2}) btn={leftBtn} pressed={wasPressed} released={wasReleased} hit={pointerInfo}");
            }
        }
    }
}
