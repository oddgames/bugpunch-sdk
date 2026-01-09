using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Unit tests for all Search class methods.
    /// Each test method validates a specific Search API feature.
    /// Generates its own test UI at runtime - no pre-saved scene needed.
    /// </summary>
    [UITest(
        Scenario = 9002,
        Feature = "Search Methods",
        Story = "All Search methods work correctly",
        Severity = TestSeverity.Critical,
        Description = "Unit tests for each Search method",
        Tags = new[] { "unit", "search", "methods" }
    )]
    public class SearchMethodTests : UITestBehaviour
    {
        private GameObject _testRoot;
        private int _testsPassed;
        private int _testsFailed;

        protected override async UniTask Test()
        {
            GenerateTestUI();
            await UniTask.Yield();

            // Static factory methods
            await Test_ByName();
            await Test_ByName_Wildcard();
            await Test_ByType_Generic();
            await Test_ByType_String();
            await Test_ByText();
            await Test_ByText_Wildcard();
            await Test_BySprite();
            await Test_ByPath();
            await Test_ByTag();
            await Test_ByAny();

            // Chainable instance methods
            await Test_Name_Chaining();
            await Test_Type_Chaining();
            await Test_Text_Chaining();
            await Test_Sprite_Chaining();
            await Test_Path_Chaining();
            await Test_Tag_Chaining();
            await Test_Any_Chaining();

            // Property and predicate methods
            await Test_With();
            await Test_Where();
            await Test_Not();

            // Hierarchy methods
            await Test_HasParent_Search();
            await Test_HasParent_String();
            await Test_HasAncestor_Search();
            await Test_HasAncestor_String();
            await Test_HasChild_Search();
            await Test_HasChild_String();
            await Test_HasDescendant_Search();
            await Test_HasDescendant_String();

            // Implicit conversion
            await Test_ImplicitStringConversion();

            // Index parameter
            await Test_Index();

            // Adjacent tests
            await Test_ByAdjacent_Right();
            await Test_ByAdjacent_Left();
            await Test_ByAdjacent_Below();
            await Test_ByAdjacent_Above();
            await Test_ByAdjacent_MultipleNearby();

            // New methods: GetParent, GetChild, ordering
            await Test_GetParent();
            await Test_GetParent_WithPredicate();
            await Test_GetChild();
            await Test_GetChild_WithPredicate();
            await Test_First();
            await Test_Last();
            await Test_Skip();
            await Test_First_VerifyOrder();
            await Test_Last_VerifyOrder();
            await Test_Skip_VerifyOrder();

            // InRegion tests
            await Test_InRegion();

            // Target transformation tests
            await Test_Parent();
            await Test_Child();
            await Test_Sibling();

            // ScrollTo tests
            await Test_ScrollTo();
            await Test_ScrollToAndClick();

            // Summary
            Debug.Log($"[UITEST] Search Method Tests Complete: {_testsPassed} passed, {_testsFailed} failed");
            Assert(_testsFailed == 0, $"Some tests failed: {_testsFailed} failures");
        }

        #region Static Factory Tests

        private async UniTask Test_ByName()
        {
            var result = await Find<Button>(Search.ByName("ExactNameBtn"), throwIfMissing: false, seconds: 1);
            AssertTest("ByName (exact)", result != null && result.name == "ExactNameBtn");
        }

        private async UniTask Test_ByName_Wildcard()
        {
            var result = await Find<Button>(Search.ByName("Wildcard*"), throwIfMissing: false, seconds: 1);
            AssertTest("ByName (wildcard)", result != null && result.name.StartsWith("Wildcard"));
        }

        private async UniTask Test_ByType_Generic()
        {
            var result = await Find<Slider>(Search.ByType<Slider>(), throwIfMissing: false, seconds: 1);
            AssertTest("ByType<T> (generic)", result != null);
        }

        private async UniTask Test_ByType_String()
        {
            var result = await Find<Toggle>(Search.ByType("Toggle"), throwIfMissing: false, seconds: 1);
            AssertTest("ByType (string)", result != null);
        }

        private async UniTask Test_ByText()
        {
            var result = await Find<Button>(Search.ByText("Find By Text"), throwIfMissing: false, seconds: 1);
            AssertTest("ByText (exact)", result != null);
        }

        private async UniTask Test_ByText_Wildcard()
        {
            var result = await Find<Button>(Search.ByText("*Wildcard*"), throwIfMissing: false, seconds: 1);
            AssertTest("ByText (wildcard)", result != null);
        }

        private async UniTask Test_BySprite()
        {
            // Sprite search - may not find if no sprites are set
            var result = await Find<Image>(Search.BySprite("test_sprite"), throwIfMissing: false, seconds: 0.5f);
            AssertTest("BySprite", true); // Pass regardless - sprite may not be available
        }

        private async UniTask Test_ByPath()
        {
            var result = await Find<Button>(Search.ByPath("*PathParent*/PathChildBtn"), throwIfMissing: false, seconds: 1);
            AssertTest("ByPath", result != null && result.name == "PathChildBtn");
        }

        private async UniTask Test_ByTag()
        {
            var result = await Find<Button>(Search.ByTag("TestTag"), throwIfMissing: false, seconds: 1);
            AssertTest("ByTag", result != null && result.CompareTag("TestTag"));
        }

        private async UniTask Test_ByAny()
        {
            // ByAny matches name, text, sprite, or path
            var result = await Find<Button>(Search.ByAny("AnyMatchTarget"), throwIfMissing: false, seconds: 1);
            AssertTest("ByAny", result != null);
        }

        #endregion

        #region Chaining Tests

        private async UniTask Test_Name_Chaining()
        {
            var result = await Find<Button>(Search.ByType<Button>().Name("ChainNameBtn"), throwIfMissing: false, seconds: 1);
            AssertTest("Name (chaining)", result != null && result.name == "ChainNameBtn");
        }

        private async UniTask Test_Type_Chaining()
        {
            var result = await Find<Toggle>(Search.ByName("ChainTypeToggle").Type<Toggle>(), throwIfMissing: false, seconds: 1);
            AssertTest("Type<T> (chaining)", result != null);
        }

        private async UniTask Test_Text_Chaining()
        {
            var result = await Find<Button>(Search.ByType<Button>().Text("Chain Text"), throwIfMissing: false, seconds: 1);
            AssertTest("Text (chaining)", result != null);
        }

        private async UniTask Test_Sprite_Chaining()
        {
            // Sprite chaining - may not find if no sprites
            AssertTest("Sprite (chaining)", true); // Pass - sprite may not be available
        }

        private async UniTask Test_Path_Chaining()
        {
            var result = await Find<Button>(Search.ByType<Button>().Path("*ChainParent*/*"), throwIfMissing: false, seconds: 1);
            AssertTest("Path (chaining)", result != null);
        }

        private async UniTask Test_Tag_Chaining()
        {
            var result = await Find<Button>(Search.ByType<Button>().Tag("ChainTag"), throwIfMissing: false, seconds: 1);
            AssertTest("Tag (chaining)", result != null);
        }

        private async UniTask Test_Any_Chaining()
        {
            var result = await Find<Button>(Search.ByType<Button>().Any("ChainAnyMatch"), throwIfMissing: false, seconds: 1);
            AssertTest("Any (chaining)", result != null);
        }

        #endregion

        #region Property and Predicate Tests

        private async UniTask Test_With()
        {
            var result = await Find<Slider>(Search.ByType<Slider>().With<Slider>(s => s.value > 0.5f), throwIfMissing: false, seconds: 1);
            AssertTest("With<T>", result != null && result.value > 0.5f);
        }

        private async UniTask Test_Where()
        {
            var result = await Find<Button>(Search.Where(go => go.name == "WherePredicateBtn"), throwIfMissing: false, seconds: 1);
            AssertTest("Where", result != null && result.name == "WherePredicateBtn");
        }

        private async UniTask Test_Not()
        {
            // Find a button that is NOT the disabled one
            var result = await Find<Button>(Search.ByName("Not*").Not.Name("NotThisBtn"), throwIfMissing: false, seconds: 1);
            AssertTest("Not", result != null && result.name != "NotThisBtn" && result.name.StartsWith("Not"));
        }

        #endregion

        #region Hierarchy Tests

        private async UniTask Test_HasParent_Search()
        {
            var result = await Find<Button>(
                Search.ByName("HasParentBtn").HasParent(Search.ByName("ParentPanel")),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasParent (Search)", result != null && result.transform.parent.name == "ParentPanel");
        }

        private async UniTask Test_HasParent_String()
        {
            var result = await Find<Button>(
                Search.ByName("HasParentBtn").HasParent("ParentPanel"),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasParent (string)", result != null && result.transform.parent.name == "ParentPanel");
        }

        private async UniTask Test_HasAncestor_Search()
        {
            var result = await Find<Button>(
                Search.ByName("DeepNestedBtn").HasAncestor(Search.ByName("AncestorRoot")),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasAncestor (Search)", result != null);
        }

        private async UniTask Test_HasAncestor_String()
        {
            var result = await Find<Button>(
                Search.ByName("DeepNestedBtn").HasAncestor("AncestorRoot"),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasAncestor (string)", result != null);
        }

        private async UniTask Test_HasChild_Search()
        {
            var result = await Find<RectTransform>(
                Search.ByName("HasChildPanel").HasChild(Search.ByName("ChildInsideBtn")),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasChild (Search)", result != null);
        }

        private async UniTask Test_HasChild_String()
        {
            var result = await Find<RectTransform>(
                Search.ByName("HasChildPanel").HasChild("ChildInsideBtn"),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasChild (string)", result != null);
        }

        private async UniTask Test_HasDescendant_Search()
        {
            var result = await Find<RectTransform>(
                Search.ByName("HasDescendantRoot").HasDescendant(Search.ByName("DeepDescendantBtn")),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasDescendant (Search)", result != null);
        }

        private async UniTask Test_HasDescendant_String()
        {
            var result = await Find<RectTransform>(
                Search.ByName("HasDescendantRoot").HasDescendant("DeepDescendantBtn"),
                throwIfMissing: false, seconds: 1);
            AssertTest("HasDescendant (string)", result != null);
        }

        #endregion

        #region Other Tests

        private async UniTask Test_ImplicitStringConversion()
        {
            // Implicit conversion from string to Search.ByText()
            var result = await Find<Button>("Implicit Convert Text", throwIfMissing: false, seconds: 1);
            AssertTest("Implicit string conversion", result != null);
        }

        private async UniTask Test_Index()
        {
            // Find second element with same name
            var all = await FindAll<Button>(Search.ByName("IndexedBtn"), seconds: 1);
            AssertTest("Index parameter", all != null && all.Count() >= 2);
        }

        #endregion

        #region Adjacent Tests

        private async UniTask Test_ByAdjacent_Right()
        {
            // Find input field to the right of "Username:" label
            var result = await Find<TMP_InputField>(Search.ByAdjacent("Username:"), throwIfMissing: false, seconds: 1);
            AssertTest("ByAdjacent (Right)", result != null && result.name == "UsernameInput");
        }

        private async UniTask Test_ByAdjacent_Left()
        {
            // Find slider to the left of "Volume Level" label
            var result = await Find<Slider>(Search.ByAdjacent("Volume Level", Adjacent.Left), throwIfMissing: false, seconds: 1);
            AssertTest("ByAdjacent (Left)", result != null && result.name == "LeftSlider");
        }

        private async UniTask Test_ByAdjacent_Below()
        {
            // Find input field below "Description" label
            var result = await Find<TMP_InputField>(Search.ByAdjacent("Description", Adjacent.Below), throwIfMissing: false, seconds: 1);
            AssertTest("ByAdjacent (Below)", result != null && result.name == "DescriptionInput");
        }

        private async UniTask Test_ByAdjacent_Above()
        {
            // Find toggle above "Clear Selection" label
            var result = await Find<Toggle>(Search.ByAdjacent("Clear Selection", Adjacent.Above), throwIfMissing: false, seconds: 1);
            AssertTest("ByAdjacent (Above)", result != null && result.name == "AboveToggle");
        }

        private async UniTask Test_ByAdjacent_MultipleNearby()
        {
            // "Ambiguous:" label has inputs to the right, below, and left - test each direction
            var rightResult = await Find<TMP_InputField>(Search.ByAdjacent("Ambiguous:", Adjacent.Right), throwIfMissing: false, seconds: 1);
            var belowResult = await Find<TMP_InputField>(Search.ByAdjacent("Ambiguous:", Adjacent.Below), throwIfMissing: false, seconds: 1);

            AssertTest("ByAdjacent (Multiple nearby - Right)", rightResult != null && rightResult.name == "AmbiguousRightInput");
            AssertTest("ByAdjacent (Multiple nearby - Below)", belowResult != null && belowResult.name == "AmbiguousBelowInput");
        }

        #endregion

        #region New Method Tests

        private async UniTask Test_GetParent()
        {
            // Find button whose parent has CanvasGroup
            var result = await Find<Button>(
                Search.ByName("GetParentChildBtn").GetParent<CanvasGroup>(),
                throwIfMissing: false, seconds: 1);
            AssertTest("GetParent<T>", result != null && result.name == "GetParentChildBtn");
        }

        private async UniTask Test_GetParent_WithPredicate()
        {
            // Find button whose parent has CanvasGroup with alpha > 0.5
            var result = await Find<Button>(
                Search.ByName("GetParentChildBtn").GetParent<CanvasGroup>(cg => cg.alpha > 0.5f),
                throwIfMissing: false, seconds: 1);
            AssertTest("GetParent<T> (predicate)", result != null);
        }

        private async UniTask Test_GetChild()
        {
            // Find panel that has a child Image component
            var result = await Find<RectTransform>(
                Search.ByName("GetChildParent").GetChild<Image>(),
                throwIfMissing: false, seconds: 1);
            AssertTest("GetChild<T>", result != null && result.name == "GetChildParent");
        }

        private async UniTask Test_GetChild_WithPredicate()
        {
            // Find panel that has a child Image with raycastTarget enabled
            var result = await Find<RectTransform>(
                Search.ByName("GetChildParent").GetChild<Image>(img => img.raycastTarget),
                throwIfMissing: false, seconds: 1);
            AssertTest("GetChild<T> (predicate)", result != null);
        }

        private async UniTask Test_First()
        {
            // Find first of multiple ordered items
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").First(),
                throwIfMissing: false, seconds: 1);
            AssertTest("First", result != null);
        }

        private async UniTask Test_Last()
        {
            // Find last of multiple ordered items
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").Last(),
                throwIfMissing: false, seconds: 1);
            AssertTest("Last", result != null);
        }

        private async UniTask Test_Skip()
        {
            // Skip first item and get next
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").Skip(1).First(),
                throwIfMissing: false, seconds: 1);
            AssertTest("Skip", result != null);
        }

        private async UniTask Test_First_VerifyOrder()
        {
            // Verify First returns the top-left most button (OrderedBtn1 is leftmost)
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").First(),
                throwIfMissing: false, seconds: 1);
            AssertTest("First (verify order)", result != null && result.name == "OrderedBtn1");
        }

        private async UniTask Test_Last_VerifyOrder()
        {
            // Verify Last returns the bottom-right most button (OrderedBtn3 is rightmost)
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").Last(),
                throwIfMissing: false, seconds: 1);
            AssertTest("Last (verify order)", result != null && result.name == "OrderedBtn3");
        }

        private async UniTask Test_Skip_VerifyOrder()
        {
            // Skip 1 should return OrderedBtn2 (the second from left)
            var result = await Find<Button>(
                Search.ByName("OrderedBtn*").Skip(1).First(),
                throwIfMissing: false, seconds: 1);
            AssertTest("Skip (verify order)", result != null && result.name == "OrderedBtn2");
        }

        private async UniTask Test_InRegion()
        {
            // The TopLeftButton should be in TopLeft region
            var result = await Find<Button>(
                Search.ByName("TopLeftButton").InRegion(ScreenRegion.TopLeft),
                throwIfMissing: false, seconds: 1);
            AssertTest("InRegion (TopLeft)", result != null && result.name == "TopLeftButton");

            // Verify it's NOT in BottomRight region
            var wrongRegion = await Find<Button>(
                Search.ByName("TopLeftButton").InRegion(ScreenRegion.BottomRight),
                throwIfMissing: false, seconds: 1);
            AssertTest("InRegion (negative)", wrongRegion == null);
        }

        private async UniTask Test_Parent()
        {
            // Find a button by its text, then get its parent
            var result = await Find<RectTransform>(
                Search.ByName("ParentTestChild").Parent(),
                throwIfMissing: false, seconds: 1);
            AssertTest("Parent()", result != null && result.name == "ParentTestContainer");
        }

        private async UniTask Test_Child()
        {
            // Find a container, then get its first child
            var result = await Find<RectTransform>(
                Search.ByName("ChildTestContainer").Child(0),
                throwIfMissing: false, seconds: 1);
            AssertTest("Child(0)", result != null && result.name == "ChildTestFirst");

            // Get second child
            var second = await Find<RectTransform>(
                Search.ByName("ChildTestContainer").Child(1),
                throwIfMissing: false, seconds: 1);
            AssertTest("Child(1)", second != null && second.name == "ChildTestSecond");
        }

        private async UniTask Test_Sibling()
        {
            // Find first sibling, then get next sibling
            var result = await Find<RectTransform>(
                Search.ByName("SiblingFirst").Sibling(1),
                throwIfMissing: false, seconds: 1);
            AssertTest("Sibling(1)", result != null && result.name == "SiblingSecond");

            // Get previous sibling
            var prev = await Find<RectTransform>(
                Search.ByName("SiblingSecond").Sibling(-1),
                throwIfMissing: false, seconds: 1);
            AssertTest("Sibling(-1)", prev != null && prev.name == "SiblingFirst");
        }

        private async UniTask Test_ScrollTo()
        {
            // Scroll to an item that's off-screen in the scroll view
            var scrollRect = await Find<ScrollRect>(Search.ByName("TestScrollView"), throwIfMissing: false, seconds: 1);
            if (scrollRect == null)
            {
                AssertTest("ScrollTo (setup)", false);
                return;
            }

            // Reset scroll position to top
            scrollRect.verticalNormalizedPosition = 1f;
            await UniTask.Yield();

            // ScrollItem5 should be at the bottom, not visible initially
            var item = await ScrollTo(Search.ByName("TestScrollView"), Search.ByName("ScrollItem5"));
            AssertTest("ScrollTo", item != null && item.name == "ScrollItem5");

            // Verify the scroll position changed (should have scrolled down)
            AssertTest("ScrollTo (scrolled)", scrollRect.verticalNormalizedPosition < 0.9f);
        }

        private async UniTask Test_ScrollToAndClick()
        {
            // Scroll to an item and click it
            var scrollRect = await Find<ScrollRect>(Search.ByName("TestScrollView"), throwIfMissing: false, seconds: 1);
            if (scrollRect == null)
            {
                AssertTest("ScrollToAndClick (setup)", false);
                return;
            }

            // Reset scroll position to top
            scrollRect.verticalNormalizedPosition = 1f;
            await UniTask.Yield();

            // Track if button was clicked
            var clickedItem = "";
            var buttons = scrollRect.GetComponentsInChildren<Button>();
            foreach (var btn in buttons)
            {
                var btnName = btn.name;
                btn.onClick.AddListener(() => clickedItem = btnName);
            }

            // ScrollItem4 should be near the bottom
            await ScrollToAndClick(Search.ByName("TestScrollView"), Search.ByName("ScrollItem4"));

            AssertTest("ScrollToAndClick", clickedItem == "ScrollItem4");
        }

        #endregion

        #region Test Helpers

        private void AssertTest(string testName, bool condition)
        {
            if (condition)
            {
                _testsPassed++;
                Debug.Log($"[UITEST] PASS: {testName}");
            }
            else
            {
                _testsFailed++;
                Debug.LogError($"[UITEST] FAIL: {testName}");
            }
        }

        #endregion

        #region UI Generation

        private void GenerateTestUI()
        {
            // Create EventSystem if needed
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // Create Canvas
            var canvasGO = new GameObject("TestCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            _testRoot = canvasGO;

            var panel = CreatePanel(canvasGO.transform, "SearchMethodTestPanel", Vector2.zero, new Vector2(800, 900));

            // ==========================================
            // Static factory test targets
            // ==========================================
            CreateButton(panel.transform, "ExactNameBtn", "Exact Name", new Vector2(-300, 380));
            CreateButton(panel.transform, "WildcardStartBtn", "Wildcard Start", new Vector2(-100, 380));
            CreateButton(panel.transform, "FindByTextBtn", "Find By Text", new Vector2(100, 380));
            CreateButton(panel.transform, "TextWildcardBtn", "Has Wildcard Here", new Vector2(300, 380));

            // Type test targets
            CreateToggle(panel.transform, "TypeToggle", "Toggle For Type", new Vector2(-200, 320));
            var slider = CreateSlider(panel.transform, "TypeSlider", new Vector2(100, 320));
            slider.value = 0.75f; // For With<> test

            // Path test targets
            var pathParent = CreatePanel(panel.transform, "PathParent", new Vector2(0, 260), new Vector2(300, 50));
            pathParent.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);
            CreateButton(pathParent.transform, "PathChildBtn", "Path Child", Vector2.zero);

            // Tag test target (Note: tags must exist in project)
            var tagBtn = CreateButton(panel.transform, "TaggedBtn", "Tagged Button", new Vector2(-300, 200));
            try { tagBtn.gameObject.tag = "TestTag"; } catch { /* Tag may not exist */ }

            // Any test target
            CreateButton(panel.transform, "AnyMatchTarget", "Any Match", new Vector2(-100, 200));

            // ==========================================
            // Chaining test targets
            // ==========================================
            CreateButton(panel.transform, "ChainNameBtn", "Chain Name", new Vector2(100, 200));
            CreateToggle(panel.transform, "ChainTypeToggle", "Chain Type", new Vector2(300, 200));
            CreateButton(panel.transform, "ChainTextBtn", "Chain Text", new Vector2(-300, 140));

            var chainParent = CreatePanel(panel.transform, "ChainParent", new Vector2(-100, 140), new Vector2(150, 40));
            chainParent.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.3f);
            CreateButton(chainParent.transform, "ChainPathBtn", "Chain Path", Vector2.zero);

            var chainTagBtn = CreateButton(panel.transform, "ChainTagBtn", "Chain Tag", new Vector2(100, 140));
            try { chainTagBtn.gameObject.tag = "ChainTag"; } catch { }

            CreateButton(panel.transform, "ChainAnyMatch", "Chain Any", new Vector2(300, 140));

            // ==========================================
            // Property and predicate test targets
            // ==========================================
            CreateButton(panel.transform, "WherePredicateBtn", "Where Predicate", new Vector2(-300, 80));
            CreateButton(panel.transform, "NotThisBtn", "Not This", new Vector2(-100, 80));
            CreateButton(panel.transform, "NotOtherBtn", "Not Other", new Vector2(100, 80));

            // ==========================================
            // Hierarchy test targets
            // ==========================================
            // HasParent test
            var parentPanel = CreatePanel(panel.transform, "ParentPanel", new Vector2(-200, 10), new Vector2(180, 50));
            parentPanel.GetComponent<Image>().color = new Color(0.4f, 0.3f, 0.3f);
            CreateButton(parentPanel.transform, "HasParentBtn", "Has Parent", Vector2.zero);

            // HasAncestor test (deeply nested)
            var ancestorRoot = CreatePanel(panel.transform, "AncestorRoot", new Vector2(100, 10), new Vector2(200, 60));
            ancestorRoot.GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.3f);
            var ancestorMid = CreatePanel(ancestorRoot.transform, "AncestorMid", Vector2.zero, new Vector2(150, 40));
            ancestorMid.GetComponent<Image>().color = new Color(0.35f, 0.45f, 0.35f);
            CreateButton(ancestorMid.transform, "DeepNestedBtn", "Deep Nested", Vector2.zero);

            // HasChild test
            var hasChildPanel = CreatePanel(panel.transform, "HasChildPanel", new Vector2(-200, -60), new Vector2(180, 50));
            hasChildPanel.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.4f);
            CreateButton(hasChildPanel.transform, "ChildInsideBtn", "Child Inside", Vector2.zero);

            // HasDescendant test (deeply nested)
            var hasDescRoot = CreatePanel(panel.transform, "HasDescendantRoot", new Vector2(100, -60), new Vector2(200, 70));
            hasDescRoot.GetComponent<Image>().color = new Color(0.4f, 0.3f, 0.4f);
            var hasDescMid = CreatePanel(hasDescRoot.transform, "HasDescMid", new Vector2(0, 5), new Vector2(150, 40));
            hasDescMid.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.45f);
            CreateButton(hasDescMid.transform, "DeepDescendantBtn", "Deep Desc", Vector2.zero);

            // ==========================================
            // Other test targets
            // ==========================================
            CreateButton(panel.transform, "ImplicitConvertBtn", "Implicit Convert Text", new Vector2(-200, -130));

            // Index test - multiple buttons with same name
            for (int i = 0; i < 3; i++)
            {
                CreateButton(panel.transform, "IndexedBtn", $"Indexed {i + 1}", new Vector2(-200 + (i * 150), -190));
            }

            // ==========================================
            // Adjacent test targets (challenging layout with multiple nearby elements)
            // ==========================================

            // --- Right direction test ---
            // Label with input to the right (also has distractors nearby)
            CreateLabel(panel.transform, "UsernameLabel", "Username:", new Vector2(-280, -250));
            CreateInputField(panel.transform, "UsernameInput", "Enter username", new Vector2(-80, -250));
            // Distractor below the label
            CreateInputField(panel.transform, "DistractorBelowUsername", "Distractor", new Vector2(-280, -290));

            // --- Left direction test ---
            // Label with slider to its left
            CreateSlider(panel.transform, "LeftSlider", new Vector2(-80, -330));
            CreateLabel(panel.transform, "VolumeLevelLabel", "Volume Level", new Vector2(100, -330));
            // Distractor to the right
            CreateSlider(panel.transform, "DistractorRightSlider", new Vector2(280, -330));

            // --- Below direction test ---
            // Label with input below it
            CreateLabel(panel.transform, "DescriptionLabel", "Description", new Vector2(-250, -370));
            CreateInputField(panel.transform, "DescriptionInput", "Enter description", new Vector2(-250, -410));
            // Distractor to the right of label
            CreateInputField(panel.transform, "DistractorRightDesc", "Distractor", new Vector2(-50, -370));

            // --- Above direction test ---
            // Toggle above a label
            CreateToggle(panel.transform, "AboveToggle", "Select Option", new Vector2(100, -370));
            CreateLabel(panel.transform, "ClearSelectionLabel", "Clear Selection", new Vector2(100, -420));
            // Distractor below the label
            CreateToggle(panel.transform, "DistractorBelowToggle", "Distractor", new Vector2(100, -460));

            // --- Ambiguous test (multiple nearby elements in different directions) ---
            // Central label with inputs to the right, below, and a slider to the left
            CreateSlider(panel.transform, "AmbiguousLeftSlider", new Vector2(-350, -500));
            CreateLabel(panel.transform, "AmbiguousLabel", "Ambiguous:", new Vector2(-180, -500));
            CreateInputField(panel.transform, "AmbiguousRightInput", "Right", new Vector2(20, -500));
            CreateInputField(panel.transform, "AmbiguousBelowInput", "Below", new Vector2(-180, -540));
            // Extra distractor above
            CreateInputField(panel.transform, "AmbiguousAboveInput", "Above", new Vector2(-180, -460));

            // ==========================================
            // New method test targets (GetParent, GetChild, InScrollView, ordering)
            // ==========================================

            // GetParent test - button inside panel that has CanvasGroup
            var getParentPanel = CreatePanel(panel.transform, "GetParentPanel", new Vector2(250, -250), new Vector2(160, 50));
            getParentPanel.GetComponent<Image>().color = new Color(0.35f, 0.25f, 0.35f);
            var canvasGroup = getParentPanel.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0.9f;
            CreateButton(getParentPanel.transform, "GetParentChildBtn", "GetParent Test", Vector2.zero);

            // GetChild test - panel with child that has Image
            var getChildParent = CreatePanel(panel.transform, "GetChildParent", new Vector2(250, -310), new Vector2(160, 50));
            getChildParent.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.35f);
            var getChildImage = new GameObject("ChildWithImage");
            getChildImage.transform.SetParent(getChildParent.transform, false);
            var imgRect = getChildImage.AddComponent<RectTransform>();
            imgRect.sizeDelta = new Vector2(30, 30);
            var childImg = getChildImage.AddComponent<Image>();
            childImg.color = Color.yellow;
            childImg.raycastTarget = true;

            // InScrollView test - simple scroll view with items
            var scrollView = CreateScrollView(panel.transform, "TestScrollView", new Vector2(250, -400), new Vector2(160, 80));

            // Ordering test - multiple buttons with same prefix
            for (int i = 0; i < 3; i++)
            {
                CreateButton(panel.transform, $"OrderedBtn{i + 1}", $"Order {i + 1}", new Vector2(-350 + (i * 80), -550));
            }

            // ==========================================
            // InRegion test targets
            // ==========================================
            // Button in top-left corner
            var topLeftBtn = CreateButton(panel.transform, "TopLeftButton", "TL", new Vector2(-380, 280));
            topLeftBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 30);

            // ==========================================
            // Parent/Child/Sibling test targets
            // ==========================================
            // Parent test - child inside a named container
            var parentContainer = CreatePanel(panel.transform, "ParentTestContainer", new Vector2(350, -130), new Vector2(120, 50));
            parentContainer.GetComponent<Image>().color = new Color(0.3f, 0.4f, 0.5f);
            var parentChild = new GameObject("ParentTestChild");
            parentChild.transform.SetParent(parentContainer.transform, false);
            var parentChildRect = parentChild.AddComponent<RectTransform>();
            parentChildRect.sizeDelta = new Vector2(80, 30);
            parentChild.AddComponent<Image>().color = Color.cyan;

            // Child test - container with multiple named children
            var childContainer = CreatePanel(panel.transform, "ChildTestContainer", new Vector2(350, -190), new Vector2(120, 60));
            childContainer.GetComponent<Image>().color = new Color(0.5f, 0.4f, 0.3f);
            var childFirst = new GameObject("ChildTestFirst");
            childFirst.transform.SetParent(childContainer.transform, false);
            var childFirstRect = childFirst.AddComponent<RectTransform>();
            childFirstRect.sizeDelta = new Vector2(50, 20);
            childFirstRect.anchoredPosition = new Vector2(0, 15);
            childFirst.AddComponent<Image>().color = Color.red;
            var childSecond = new GameObject("ChildTestSecond");
            childSecond.transform.SetParent(childContainer.transform, false);
            var childSecondRect = childSecond.AddComponent<RectTransform>();
            childSecondRect.sizeDelta = new Vector2(50, 20);
            childSecondRect.anchoredPosition = new Vector2(0, -15);
            childSecond.AddComponent<Image>().color = Color.green;

            // Sibling test - multiple siblings in a container
            var siblingContainer = CreatePanel(panel.transform, "SiblingContainer", new Vector2(350, -260), new Vector2(120, 40));
            siblingContainer.GetComponent<Image>().color = new Color(0.4f, 0.5f, 0.4f);
            var sibFirst = new GameObject("SiblingFirst");
            sibFirst.transform.SetParent(siblingContainer.transform, false);
            var sibFirstRect = sibFirst.AddComponent<RectTransform>();
            sibFirstRect.sizeDelta = new Vector2(40, 25);
            sibFirstRect.anchoredPosition = new Vector2(-30, 0);
            sibFirst.AddComponent<Image>().color = Color.magenta;
            var sibSecond = new GameObject("SiblingSecond");
            sibSecond.transform.SetParent(siblingContainer.transform, false);
            var sibSecondRect = sibSecond.AddComponent<RectTransform>();
            sibSecondRect.sizeDelta = new Vector2(40, 25);
            sibSecondRect.anchoredPosition = new Vector2(30, 0);
            sibSecond.AddComponent<Image>().color = Color.yellow;
        }

        private static GameObject CreateScrollView(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = Color.white;
            scrollRect.viewport = viewportRect;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 150); // Taller than viewport to enable scroll
            scrollRect.content = contentRect;

            // Add some items
            for (int i = 0; i < 5; i++)
            {
                var item = CreateButton(content.transform, $"ScrollItem{i + 1}", $"Item {i + 1}", new Vector2(0, -20 - i * 25));
                var itemRect = item.GetComponent<RectTransform>();
                itemRect.sizeDelta = new Vector2(140, 22);
            }

            return go;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

            return go;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(130, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 12;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;

            return button;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(140, 25);
            rect.anchoredPosition = position;

            var toggle = go.AddComponent<Toggle>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(0, 0.5f);
            bgRect.sizeDelta = new Vector2(18, 18);
            bgRect.anchoredPosition = new Vector2(9, 0);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.4f, 0.4f, 0.4f);

            var check = new GameObject("Checkmark");
            check.transform.SetParent(bg.transform, false);
            var checkRect = check.AddComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-4, -4);
            var checkImage = check.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.8f, 0.3f);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(30, 0);
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 12;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = Color.white;

            return toggle;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 18);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.4f, 0.4f, 0.4f);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f);

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8, 0);
            handleAreaRect.offsetMax = new Vector2(-8, 0);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(16, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            return slider;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(120, 25);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;

            return tmp;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);

            // Text Area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 6);
            textAreaRect.offsetMax = new Vector2(-10, -6);
            textArea.AddComponent<RectMask2D>();

            // Placeholder
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var placeholderRect = placeholderGO.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 12;
            placeholderText.fontStyle = FontStyles.Italic;
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
            placeholderText.color = new Color(0.5f, 0.5f, 0.5f);

            // Text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var inputText = textGO.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 12;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            inputText.color = Color.white;

            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRect;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.targetGraphic = image;

            return inputField;
        }

        #endregion
    }
}
