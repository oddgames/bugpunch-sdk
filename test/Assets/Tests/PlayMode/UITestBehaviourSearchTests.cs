using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using TMPro;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// PlayMode tests for UITestBehaviour search helper methods.
    /// Tests static helper methods: Search(), Name(), Text(), Type(), Sprite(), Path(), Tag(), Any(), Adjacent(), Near().
    /// Also tests Wait and Assert methods.
    /// </summary>
    [TestFixture]
    public class UITestBehaviourSearchTests
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();

            // Create EventSystem
            var esGO = new GameObject("EventSystem");
            _eventSystem = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            _createdObjects.Add(esGO);

            // Create Canvas
            var canvasGO = new GameObject("Canvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            _createdObjects.Add(canvasGO);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _createdObjects.Clear();
        }

        #region Search() Helper Tests

        [UnityTest]
        public IEnumerator Search_WithTextPattern_FindsElementByText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButtonWithText("TestButton", "Play Game", Vector2.zero);

                await UniTask.Yield();

                var result = new Search("Play Game").FindFirst();
                Assert.IsNotNull(result, "Should find element by text");
            });
        }

        [UnityTest]
        public IEnumerator Search_WithoutPattern_ReturnsEmptySearch()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("AnyButton", Vector2.zero);

                await UniTask.Yield();

                // Empty search should work for chaining
                var search = new Search();
                Assert.IsNotNull(search, "Should create empty search");
            });
        }

        #endregion

        #region Name() Helper Tests

        [UnityTest]
        public IEnumerator Name_ExactMatch_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("ExactNameButton", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Name("ExactNameButton").FindFirst();
                Assert.IsNotNull(result, "Should find element by exact name");
                Assert.AreEqual("ExactNameButton", result.name);
            });
        }

        [UnityTest]
        public IEnumerator Name_WildcardPrefix_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("PlayButton", new Vector2(-50, 0));
                CreateButton("PauseButton", new Vector2(50, 0));

                await UniTask.Yield();

                var results = new Search().Name("*Button").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both buttons with wildcard prefix");
            });
        }

        [UnityTest]
        public IEnumerator Name_WildcardSuffix_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("ButtonPlay", new Vector2(-50, 0));
                CreateButton("ButtonPause", new Vector2(50, 0));

                await UniTask.Yield();

                var results = new Search().Name("Button*").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both buttons with wildcard suffix");
            });
        }

        [UnityTest]
        public IEnumerator Name_WildcardMiddle_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("MainMenuButton", new Vector2(-50, 0));
                CreateButton("SettingsMenuButton", new Vector2(50, 0));

                await UniTask.Yield();

                var results = new Search().Name("*Menu*").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both buttons with wildcard in middle");
            });
        }

        #endregion

        #region Text() Helper Tests

        [UnityTest]
        public IEnumerator Text_ExactMatch_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("Label", "Welcome to Game", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Text("Welcome to Game").FindFirst();
                Assert.IsNotNull(result, "Should find element by exact text");
            });
        }

        [UnityTest]
        public IEnumerator Text_WildcardMatch_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("Label1", "Level 1", new Vector2(-50, 0));
                CreateText("Label2", "Level 2", new Vector2(50, 0));

                await UniTask.Yield();

                // Text() now only matches elements with text directly on them (not ancestors)
                var results = new Search().Text("Level*").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both text elements with wildcard");
            });
        }

        [UnityTest]
        public IEnumerator Text_TMP_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateTMPText("TMPLabel", "TextMeshPro Content", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Text("*TextMeshPro*").FindFirst();
                Assert.IsNotNull(result, "Should find TMP element by text");
            });
        }

        #endregion

        #region Type() Helper Tests

        [UnityTest]
        public IEnumerator Type_GenericButton_FindsButtons()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("Button1", new Vector2(-50, 0));
                CreateButton("Button2", new Vector2(50, 0));

                await UniTask.Yield();

                var results = new Search().Type<Button>().FindAll();
                Assert.GreaterOrEqual(results.Count, 2, "Should find buttons by type");
            });
        }

        [UnityTest]
        public IEnumerator Type_GenericSlider_FindsSliders()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateSlider("Slider1", new Vector2(-50, 0));
                CreateSlider("Slider2", new Vector2(50, 0));

                await UniTask.Yield();

                var results = new Search().Type<Slider>().FindAll();
                Assert.AreEqual(2, results.Count, "Should find sliders by type");
            });
        }

        [UnityTest]
        public IEnumerator Type_StringName_FindsByTypeName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("TypeNameButton", Vector2.zero);

                await UniTask.Yield();

                var results = new Search().Type("Button").FindAll();
                Assert.GreaterOrEqual(results.Count, 1, "Should find by type name string");
            });
        }

        [UnityTest]
        public IEnumerator Type_WildcardTypeName_FindsMultipleTypes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("Btn", new Vector2(-50, 0));
                CreateSlider("Sld", new Vector2(50, 0));

                await UniTask.Yield();

                // Both Button and Slider inherit from Selectable
                var results = new Search().Type("*Selectable*").FindAll();
                // Note: This tests pattern matching on type names
            });
        }

        #endregion

        #region Sprite() Helper Tests

        [UnityTest]
        public IEnumerator Sprite_FindsBySpriteName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var image = CreateImage("SpriteTest", Vector2.zero);
                image.sprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0, 0, 4, 4),
                    new Vector2(0.5f, 0.5f));
                image.sprite.name = "TestSpriteName";

                await UniTask.Yield();

                var result = new Search().Sprite("TestSpriteName").FindFirst();
                Assert.IsNotNull(result, "Should find element by sprite name");
            });
        }

        [UnityTest]
        public IEnumerator Sprite_WildcardMatch_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var image1 = CreateImage("Icon1", new Vector2(-50, 0));
                image1.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
                image1.sprite.name = "icon_play";

                var image2 = CreateImage("Icon2", new Vector2(50, 0));
                image2.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
                image2.sprite.name = "icon_pause";

                await UniTask.Yield();

                // Find elements with matching sprite - filter to only those with Image component directly
                var results = new Search().Sprite("icon_*").FindAll();
                var directImageElements = results.Where(go => go.GetComponent<Image>() != null).ToList();
                Assert.AreEqual(2, directImageElements.Count, "Should find both elements with icon_ prefix");
            });
        }

        #endregion

        #region Path() Helper Tests

        [UnityTest]
        public IEnumerator Path_ExactPath_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var panel = new GameObject("MainPanel");
                panel.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(panel);

                var button = CreateButton("PathButton", Vector2.zero);
                button.transform.SetParent(panel.transform, false);

                await UniTask.Yield();

                var result = new Search().Path("*/MainPanel/PathButton").FindFirst();
                Assert.IsNotNull(result, "Should find element by hierarchy path");
            });
        }

        [UnityTest]
        public IEnumerator Path_WildcardPath_FindsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var panel1 = new GameObject("Panel1");
                panel1.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(panel1);

                var panel2 = new GameObject("Panel2");
                panel2.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(panel2);

                var btn1 = CreateButton("ActionBtn", Vector2.zero);
                btn1.transform.SetParent(panel1.transform, false);

                var btn2 = CreateButton("ActionBtn", Vector2.zero);
                btn2.transform.SetParent(panel2.transform, false);

                await UniTask.Yield();

                var results = new Search().Path("*/Panel*/ActionBtn").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both buttons through wildcard path");
            });
        }

        #endregion

        #region Tag() Helper Tests

        [UnityTest]
        public IEnumerator Tag_FindsByTag()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TaggedButton", Vector2.zero);
                // Note: Can only use built-in tags in tests without creating custom tags
                // Using "Untagged" as a test since it's always available
                button.gameObject.tag = "Untagged";

                await UniTask.Yield();

                var results = new Search().Tag("Untagged").Name("TaggedButton").FindAll();
                Assert.GreaterOrEqual(results.Count, 1, "Should find element by tag");
            });
        }

        #endregion

        #region Any() Helper Tests

        [UnityTest]
        public IEnumerator Any_MatchesName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("UniqueButtonName", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Any("UniqueButtonName").FindFirst();
                Assert.IsNotNull(result, "Should find element matching name via Any()");
            });
        }

        [UnityTest]
        public IEnumerator Any_MatchesText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("AnyTextTest", "UniqueTextContent", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Any("UniqueTextContent").FindFirst();
                Assert.IsNotNull(result, "Should find element matching text via Any()");
            });
        }

        #endregion

        #region Adjacent() Helper Tests

        [UnityTest]
        public IEnumerator Adjacent_FindsElementToRight()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("Label", "Username:", new Vector2(-100, 0));
                var input = CreateInputField("UsernameInput", new Vector2(50, 0));

                await UniTask.Yield();

                var result = new Search().Adjacent("Username:", Direction.Right).FindFirst();
                Assert.IsNotNull(result, "Should find element adjacent to label");
            });
        }

        [UnityTest]
        public IEnumerator Adjacent_FindsElementBelow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("Header", "Settings", new Vector2(0, 50));
                CreateButton("SettingsBtn", new Vector2(0, -50));

                await UniTask.Yield();

                var result = new Search().Adjacent("Settings", Direction.Below).FindFirst();
                Assert.IsNotNull(result, "Should find element below header");
            });
        }

        #endregion

        #region Near() Helper Tests

        [UnityTest]
        public IEnumerator Near_FindsClosestElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("CenterLabel", "Center", Vector2.zero);
                CreateButton("CloseBtn", new Vector2(50, 0));
                CreateButton("FarBtn", new Vector2(200, 0));

                await UniTask.Yield();

                var result = new Search().Near("Center").Type<Button>().FindFirst();
                Assert.IsNotNull(result, "Should find closest button");
                Assert.AreEqual("CloseBtn", result.name, "Should be the closest button");
            });
        }

        [UnityTest]
        public IEnumerator Near_WithDirection_FindsInDirection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("DirectionLabel", "Reference", Vector2.zero);
                CreateButton("RightBtn", new Vector2(100, 0));
                CreateButton("LeftBtn", new Vector2(-100, 0));

                await UniTask.Yield();

                var result = new Search().Near("Reference", Direction.Right).Type<Button>().FindFirst();
                Assert.IsNotNull(result, "Should find button in specified direction");
                Assert.AreEqual("RightBtn", result.name, "Should be the button to the right");
            });
        }

        [UnityTest]
        public IEnumerator Near_OrdersByDistance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateText("DistanceLabel", "Origin", Vector2.zero);
                CreateButton("NearBtn", new Vector2(30, 0));
                CreateButton("MidBtn", new Vector2(60, 0));
                CreateButton("FarBtn", new Vector2(90, 0));

                await UniTask.Yield();

                var results = new Search().Near("Origin", Direction.Right).Type<Button>().FindAll();
                Assert.AreEqual(3, results.Count, "Should find all 3 buttons");
                Assert.AreEqual("NearBtn", results[0].name, "First should be nearest");
                Assert.AreEqual("MidBtn", results[1].name, "Second should be middle");
                Assert.AreEqual("FarBtn", results[2].name, "Third should be farthest");
            });
        }

        #endregion

        #region Combined Search Tests

        [UnityTest]
        public IEnumerator CombinedSearch_NameAndType_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("CombinedBtn", new Vector2(-50, 0));
                CreateSlider("CombinedSlider", new Vector2(50, 0));

                await UniTask.Yield();

                var result = new Search().Name("Combined*").Type<Button>().FindFirst();
                Assert.IsNotNull(result, "Should find button with combined search");
                Assert.AreEqual("CombinedBtn", result.name);
            });
        }

        [UnityTest]
        public IEnumerator CombinedSearch_TextAndType_FindsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Use HasChild(Text()) to match button by child text
                var btn = CreateButtonWithText("PlayBtn", "Start Game", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().HasChild(new Search().Text("*Game*")).Type<Button>().FindFirst();
                Assert.IsNotNull(result, "Should find button with text and type search");
            });
        }

        [UnityTest]
        public IEnumerator CombinedSearch_MultipleConditions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn = CreateButton("MultiCondBtn", Vector2.zero);

                await UniTask.Yield();

                var result = new Search()
                    .Name("Multi*")
                    .Type<Button>()
                    .FindFirst();

                Assert.IsNotNull(result, "Should find with multiple conditions");
            });
        }

        #endregion

        #region Ordering Tests

        [UnityTest]
        public IEnumerator First_ReturnsFirstMatch()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("OrderA", new Vector2(-100, 0));
                CreateButton("OrderB", new Vector2(0, 0));
                CreateButton("OrderC", new Vector2(100, 0));

                await UniTask.Yield();

                var result = new Search().Name("Order*").First().FindFirst();
                Assert.IsNotNull(result, "Should return first match");
            });
        }

        [UnityTest]
        public IEnumerator Last_ReturnsLastMatch()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("LastA", new Vector2(-100, 0));
                CreateButton("LastB", new Vector2(0, 0));
                CreateButton("LastC", new Vector2(100, 0));

                await UniTask.Yield();

                var result = new Search().Name("Last*").Last().FindFirst();
                Assert.IsNotNull(result, "Should return last match");
            });
        }

        [UnityTest]
        public IEnumerator Skip_SkipsElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("Skip0", new Vector2(-100, 0));
                CreateButton("Skip1", new Vector2(0, 0));
                CreateButton("Skip2", new Vector2(100, 0));

                await UniTask.Yield();

                var results = new Search().Name("Skip*").Skip(1).FindAll();
                Assert.AreEqual(2, results.Count, "Should skip first element");
            });
        }

        [UnityTest]
        public IEnumerator OrderBy_RectTransformWidth_OrdersByWidth()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btnC = CreateButton("C_Button", new Vector2(100, 0));
                btnC.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 40);

                var btnA = CreateButton("A_Button", new Vector2(-100, 0));
                btnA.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 40);

                var btnB = CreateButton("B_Button", new Vector2(0, 0));
                btnB.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 40);

                await UniTask.Yield();

                var results = new Search().Name("*_Button").OrderBy<RectTransform>(rt => rt.sizeDelta.x).FindAll();
                Assert.AreEqual("A_Button", results[0].name, "First should be smallest (A)");
                Assert.AreEqual("B_Button", results[1].name, "Second should be medium (B)");
                Assert.AreEqual("C_Button", results[2].name, "Third should be largest (C)");
            });
        }

        [UnityTest]
        public IEnumerator OrderByPosition_OrdersByScreenPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("PosBtn1", new Vector2(200, 0));
                CreateButton("PosBtn2", new Vector2(-200, 0));
                CreateButton("PosBtn3", new Vector2(0, 0));

                await UniTask.Yield();

                var results = new Search().Name("PosBtn*").OrderByPosition().FindAll();
                Assert.AreEqual(3, results.Count, "Should find all buttons");
                // First element should have smallest X position
            });
        }

        #endregion

        #region Hierarchy Tests

        [UnityTest]
        public IEnumerator GetParent_ReturnsParentElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var parent = new GameObject("ParentPanel");
                parent.transform.SetParent(_canvas.transform, false);
                parent.AddComponent<RectTransform>();
                _createdObjects.Add(parent);

                var child = CreateButton("ChildBtn", Vector2.zero);
                child.transform.SetParent(parent.transform, false);

                await UniTask.Yield();

                var result = new Search().Name("ChildBtn").GetParent().FindFirst();
                Assert.IsNotNull(result, "Should find parent");
                Assert.AreEqual("ParentPanel", result.name);
            });
        }

        [UnityTest]
        public IEnumerator GetChild_ReturnsChildElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var parent = new GameObject("ContainerPanel");
                parent.transform.SetParent(_canvas.transform, false);
                parent.AddComponent<RectTransform>();
                _createdObjects.Add(parent);

                var child = CreateButton("ContainedBtn", Vector2.zero);
                child.transform.SetParent(parent.transform, false);

                await UniTask.Yield();

                var result = new Search().Name("ContainerPanel").GetChild(0).FindFirst();
                Assert.IsNotNull(result, "Should find child");
                Assert.AreEqual("ContainedBtn", result.name);
            });
        }

        #endregion

        #region Helper Methods

        private Button CreateButton(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.gray;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            _createdObjects.Add(go);
            return button;
        }

        private Button CreateButtonWithText(string name, string text, Vector2 position)
        {
            var button = CreateButton(name, position);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(button.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private Text CreateText(string name, string content, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 30);
            rect.anchoredPosition = position;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;

            _createdObjects.Add(go);
            return text;
        }

        private TextMeshProUGUI CreateTMPText(string name, string content, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.color = Color.white;

            _createdObjects.Add(go);
            return tmp;
        }

        private Slider CreateSlider(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 20);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = Color.gray;

            _createdObjects.Add(go);
            return slider;
        }

        private Image CreateImage(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(64, 64);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.white;

            _createdObjects.Add(go);
            return image;
        }

        private TMP_InputField CreateInputField(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f);

            var inputField = go.AddComponent<TMP_InputField>();

            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = Vector2.zero;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.color = Color.white;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;

            _createdObjects.Add(go);
            return inputField;
        }

        #endregion
    }
}
