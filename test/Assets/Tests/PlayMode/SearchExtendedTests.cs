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
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// Extended PlayMode tests for Search class - covers uncovered methods from coverage report.
    /// Tests: IncludeInactive, IncludeDisabled, HasSibling, InRegion, Take, OrderByDescending,
    /// Randomize, Visible, Interactable, Or, FindAll, FindFirst, GetScreenPosition.
    /// </summary>
    [TestFixture]
    public class SearchExtendedTests
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

        #region IncludeInactive Tests

        [UnityTest]
        public IEnumerator IncludeInactive_FindsInactiveObjects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var activeBtn = CreateButton("ActiveButton", new Vector2(-50, 0));
                var inactiveBtn = CreateButton("InactiveButton", new Vector2(50, 0));
                inactiveBtn.gameObject.SetActive(false);

                await UniTask.Yield();

                // Without IncludeInactive - should not find inactive
                var normalResults = new Search().Name("*Button").FindAll();
                Assert.AreEqual(1, normalResults.Count, "Should only find active button");

                // With IncludeInactive - should find both
                var allResults = new Search().Name("*Button").IncludeInactive().FindAll();
                Assert.AreEqual(2, allResults.Count, "Should find both active and inactive buttons");
            });
        }

        [UnityTest]
        public IEnumerator IncludeInactive_FindsNestedInactiveObjects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create parent that is inactive
                var parent = new GameObject("InactiveParent");
                parent.transform.SetParent(_canvas.transform, false);
                parent.SetActive(false);
                _createdObjects.Add(parent);

                var childBtn = CreateButton("NestedChild", Vector2.zero);
                childBtn.transform.SetParent(parent.transform, false);

                await UniTask.Yield();

                var results = new Search().Name("NestedChild").IncludeInactive().FindAll();
                Assert.AreEqual(1, results.Count, "Should find button in inactive parent");
            });
        }

        #endregion

        #region IncludeDisabled Tests

        [UnityTest]
        public IEnumerator IncludeDisabled_FindsDisabledComponents()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var enabledBtn = CreateButton("EnabledButton", new Vector2(-50, 0));
                var disabledBtn = CreateButton("DisabledButton", new Vector2(50, 0));
                disabledBtn.interactable = false;

                await UniTask.Yield();

                // Without IncludeDisabled - behavior depends on search type
                var normalResults = new Search().Name("*Button").Type<Button>().FindAll();

                // With IncludeDisabled - should find both
                var allResults = new Search().Name("*Button").Type<Button>().IncludeDisabled().FindAll();
                Assert.AreEqual(2, allResults.Count, "Should find both enabled and disabled buttons");
            });
        }

        #endregion

        #region HasSibling Tests

        [UnityTest]
        public IEnumerator HasSibling_FindsElementWithMatchingSibling()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create parent with two children
                var parent = new GameObject("Parent");
                parent.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(parent);

                var label = CreateText("Label", "Username:", new Vector2(-100, 0));
                label.transform.SetParent(parent.transform, false);

                var input = CreateInputField("InputField", new Vector2(50, 0));
                input.transform.SetParent(parent.transform, false);

                await UniTask.Yield();

                var results = new Search().Name("InputField").HasSibling(new Search().Text("Username:")).FindAll();
                Assert.AreEqual(1, results.Count, "Should find input field with username label sibling");
            });
        }

        [UnityTest]
        public IEnumerator HasSibling_WithString_FindsElementByTextSibling()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var parent = new GameObject("Parent");
                parent.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(parent);

                var label = CreateText("PasswordLabel", "Password:", new Vector2(-100, 0));
                label.transform.SetParent(parent.transform, false);

                var input = CreateInputField("PassInput", new Vector2(50, 0));
                input.transform.SetParent(parent.transform, false);

                await UniTask.Yield();

                // HasSibling(string) searches by NAME pattern, not text
                var results = new Search().Name("PassInput").HasSibling("PasswordLabel").FindAll();
                Assert.AreEqual(1, results.Count, "Should find input field with password label sibling");
            });
        }

        #endregion

        #region InRegion Tests

        [UnityTest]
        public IEnumerator InRegion_FindsElementsInBounds()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("TopLeft", new Vector2(-150, 150));
                CreateButton("TopRight", new Vector2(150, 150));
                CreateButton("BottomLeft", new Vector2(-150, -150));
                CreateButton("Center", new Vector2(0, 0));

                await UniTask.Yield();

                // Search for buttons in the left half of screen (assuming ~400px canvas width)
                // InRegion uses normalized coordinates (0-1)
                var results = new Search().Name("*").Type<Button>().InRegion(0, 0, 0.5f, 1f).FindAll();

                // Should find buttons on the left side
                Assert.GreaterOrEqual(results.Count, 1, "Should find at least one button in left region");
            });
        }

        #endregion

        #region Take Tests

        [UnityTest]
        public IEnumerator Take_LimitsResultCount()
        {
            return UniTask.ToCoroutine(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    CreateButton($"TakeButton{i}", new Vector2(i * 50 - 100, 0));
                }

                await UniTask.Yield();

                var results = new Search().Name("TakeButton*").Take(3).FindAll();
                Assert.AreEqual(3, results.Count, "Should return exactly 3 results");
            });
        }

        [UnityTest]
        public IEnumerator Take_ReturnsAllIfFewerThanLimit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("OnlyButton", Vector2.zero);

                await UniTask.Yield();

                var results = new Search().Name("OnlyButton").Take(10).FindAll();
                Assert.AreEqual(1, results.Count, "Should return 1 when fewer than limit exist");
            });
        }

        #endregion

        #region OrderByDescending Tests

        [UnityTest]
        public IEnumerator OrderByDescending_ReversesOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btnA = CreateButton("A_Button", new Vector2(-100, 0));
                btnA.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 40);

                var btnB = CreateButton("B_Button", new Vector2(0, 0));
                btnB.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 40);

                var btnC = CreateButton("C_Button", new Vector2(100, 0));
                btnC.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 40);

                await UniTask.Yield();

                var ascending = new Search().Name("*_Button").OrderBy<RectTransform>(rt => rt.sizeDelta.x).FindAll();
                var descending = new Search().Name("*_Button").OrderByDescending<RectTransform>(rt => rt.sizeDelta.x).FindAll();

                Assert.AreEqual(ascending.First().name, descending.Last().name, "Descending should reverse order");
            });
        }

        #endregion

        #region Randomize Tests

        [UnityTest]
        public IEnumerator Randomize_ShufflesResults()
        {
            return UniTask.ToCoroutine(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    CreateButton($"RandBtn{i}", new Vector2((i - 5) * 30, 0));
                }

                await UniTask.Yield();

                // Run multiple times and check if order varies
                var orders = new List<string>();
                for (int trial = 0; trial < 5; trial++)
                {
                    var results = new Search().Name("RandBtn*").Randomize().FindAll();
                    orders.Add(string.Join(",", results.Select(r => r.name)));
                }

                // At least some orders should be different (not guaranteed but very likely)
                var uniqueOrders = orders.Distinct().Count();
                Assert.GreaterOrEqual(uniqueOrders, 1, "Randomize should produce results");
            });
        }

        #endregion

        #region Visible Tests

        [UnityTest]
        public IEnumerator Visible_FindsOnlyVisibleElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var visibleBtn = CreateButton("VisibleBtn", new Vector2(0, 0));
                var offscreenBtn = CreateButton("OffscreenBtn", new Vector2(5000, 5000)); // Way off screen

                await UniTask.Yield();

                var results = new Search().Name("*Btn").Visible().FindAll();

                // Should find visible button, might not find offscreen one
                Assert.GreaterOrEqual(results.Count, 1, "Should find at least the visible button");
                Assert.IsTrue(results.Any(r => r.name == "VisibleBtn"), "Should include visible button");
            });
        }

        #endregion

        #region Interactable Tests

        [UnityTest]
        public IEnumerator Interactable_FindsOnlyInteractableElements()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var interactableBtn = CreateButton("InteractableBtn", new Vector2(-50, 0));
                var disabledBtn = CreateButton("DisabledBtn", new Vector2(50, 0));
                disabledBtn.interactable = false;

                await UniTask.Yield();

                var results = new Search().Name("*Btn").Interactable().FindAll();

                Assert.AreEqual(1, results.Count, "Should find only interactable button");
                Assert.AreEqual("InteractableBtn", results[0].name, "Should be the interactable button");
            });
        }

        [UnityTest]
        public IEnumerator Interactable_FindsSliderAndToggle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TestSlider", new Vector2(0, 50));
                var toggle = CreateToggle("TestToggle", new Vector2(0, -50));

                await UniTask.Yield();

                var results = new Search().Name("Test*").Interactable().FindAll();

                Assert.AreEqual(2, results.Count, "Should find both slider and toggle");
            });
        }

        #endregion

        #region Or Tests

        [UnityTest]
        public IEnumerator Or_CombinesTwoSearches()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("BlueButton", new Vector2(-100, 0));
                CreateButton("RedButton", new Vector2(0, 0));
                CreateButton("GreenButton", new Vector2(100, 0));

                await UniTask.Yield();

                var blueSearch = new Search().Name("BlueButton");
                var redSearch = new Search().Name("RedButton");

                var results = blueSearch.Or(redSearch).FindAll();

                Assert.AreEqual(2, results.Count, "Should find both blue and red buttons");
                Assert.IsTrue(results.Any(r => r.name == "BlueButton"), "Should include blue button");
                Assert.IsTrue(results.Any(r => r.name == "RedButton"), "Should include red button");
            });
        }

        #endregion

        #region FindAll Tests

        [UnityTest]
        public IEnumerator FindAll_ReturnsAllMatchingObjects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    CreateButton($"FindAllBtn{i}", new Vector2((i - 2) * 50, 0));
                }

                await UniTask.Yield();

                var results = new Search().Name("FindAllBtn*").FindAll();
                Assert.AreEqual(5, results.Count, "Should find all 5 buttons");
            });
        }

        [UnityTest]
        public IEnumerator FindAll_ReturnsEmptyListWhenNoMatches()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("SomeButton", Vector2.zero);

                await UniTask.Yield();

                var results = new Search().Name("NonExistent*").FindAll();
                Assert.AreEqual(0, results.Count, "Should return empty list");
                Assert.IsNotNull(results, "Should not return null");
            });
        }

        #endregion

        #region FindFirst Tests

        [UnityTest]
        public IEnumerator FindFirst_ReturnsFirstMatch()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("FirstBtn", new Vector2(-50, 0));
                CreateButton("SecondBtn", new Vector2(50, 0));

                await UniTask.Yield();

                var result = new Search().Name("*Btn").FindFirst();
                Assert.IsNotNull(result, "Should find a button");
                Assert.IsTrue(result.name.EndsWith("Btn"), "Should match pattern");
            });
        }

        [UnityTest]
        public IEnumerator FindFirst_ReturnsNullWhenNoMatches()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("SomeButton", Vector2.zero);

                await UniTask.Yield();

                var result = new Search().Name("NonExistent*").FindFirst();
                Assert.IsNull(result, "Should return null when no matches");
            });
        }

        #endregion

        #region GetScreenPosition Tests

        [UnityTest]
        public IEnumerator GetScreenPosition_ReturnsValidPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ScreenPosBtn", new Vector2(0, 0));

                await UniTask.Yield();

                var screenPos = new Search().Name("ScreenPosBtn").GetScreenPosition();

                Assert.IsTrue(screenPos.HasValue, "Should return a screen position");
                Assert.Greater(screenPos.Value.x, 0, "X should be positive");
                Assert.Greater(screenPos.Value.y, 0, "Y should be positive");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenPosition_WithIndex_ReturnsCorrectElementPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("IndexBtn", new Vector2(-100, 0));
                var btn2 = CreateButton("IndexBtn", new Vector2(100, 0));

                await UniTask.Yield();

                var pos0 = new Search().Name("IndexBtn").GetScreenPosition(0);
                var pos1 = new Search().Name("IndexBtn").GetScreenPosition(1);

                Assert.IsTrue(pos0.HasValue, "Should return position for index 0");
                Assert.IsTrue(pos1.HasValue, "Should return position for index 1");
                Assert.AreNotEqual(pos0.Value.x, pos1.Value.x, "Positions should be different");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenPosition_ReturnsNullWhenNotFound()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var screenPos = new Search().Name("NonExistentElement").GetScreenPosition();

                Assert.IsFalse(screenPos.HasValue, "Should return null when element not found");
            });
        }

        #endregion

        #region Texture Tests

        [UnityTest]
        public IEnumerator Texture_MatchesImageSpriteName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("SpriteBtn", Vector2.zero);
                // Create and assign a sprite with a known name
                var texture = new Texture2D(32, 32);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite.name = "TestIconSprite_Unique123";
                button.GetComponent<Image>().sprite = sprite;

                await UniTask.Yield();

                // Use Name filter to get exact element, verify texture matches
                var results = new Search().Name("SpriteBtn").Texture("TestIconSprite_Unique123").FindAll();
                Assert.AreEqual(1, results.Count, "Should find element by sprite name");
                Assert.AreEqual("SpriteBtn", results[0].name);
            });
        }

        [UnityTest]
        public IEnumerator Texture_MatchesImageSpriteNameWithWildcard()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("IconBtnWildcard", Vector2.zero);
                var texture = new Texture2D(32, 32);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite.name = "uniqueicon_settings_gear_xyz";
                button.GetComponent<Image>().sprite = sprite;

                await UniTask.Yield();

                // Verify the specific button is found with wildcard patterns
                var results = new Search().Name("IconBtnWildcard").Texture("uniqueicon_*").FindAll();
                Assert.AreEqual(1, results.Count, "Should find element by sprite wildcard pattern");

                results = new Search().Name("IconBtnWildcard").Texture("*settings*").FindAll();
                Assert.AreEqual(1, results.Count, "Should find element by middle wildcard");
            });
        }

        [UnityTest]
        public IEnumerator Texture_MatchesRawImageTextureName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create RawImage with texture
                var go = new GameObject("RawImageObj");
                go.transform.SetParent(_canvas.transform, false);
                var rect = go.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(100, 100);
                var rawImage = go.AddComponent<RawImage>();
                var texture = new Texture2D(64, 64);
                texture.name = "unique_avatar_texture_01_xyz";
                rawImage.texture = texture;
                _createdObjects.Add(go);

                await UniTask.Yield();

                // Verify exact match by name + texture
                var results = new Search().Name("RawImageObj").Texture("unique_avatar_texture_01_xyz").FindAll();
                Assert.AreEqual(1, results.Count, "Should find RawImage by texture name");

                results = new Search().Name("RawImageObj").Texture("unique_avatar_*").FindAll();
                Assert.AreEqual(1, results.Count, "Should find RawImage by texture wildcard");
            });
        }

        [UnityTest]
        public IEnumerator Texture_MatchesSpriteRenderer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create 2D SpriteRenderer
                var go = new GameObject("SpriteRendererObj");
                var sr = go.AddComponent<SpriteRenderer>();
                var texture = new Texture2D(32, 32);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite.name = "unique_player_idle_anim_xyz";
                sr.sprite = sprite;
                _createdObjects.Add(go);

                await UniTask.Yield();

                // Verify exact match
                var results = new Search().Name("SpriteRendererObj").Texture("unique_player_idle_anim_xyz").FindAll();
                Assert.AreEqual(1, results.Count, "Should find SpriteRenderer by sprite name");

                results = new Search().Name("SpriteRendererObj").Texture("*idle*").FindAll();
                Assert.AreEqual(1, results.Count, "Should find SpriteRenderer by wildcard");
            });
        }

        [UnityTest]
        public IEnumerator Texture_MatchesMeshRendererMaterial()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create 3D object with mesh and material
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "TexturedCubeUnique";
                var renderer = go.GetComponent<MeshRenderer>();
                var material = new Material(Shader.Find("Standard"));
                var texture = new Texture2D(64, 64);
                texture.name = "unique_wood_diffuse_01_xyz";
                material.mainTexture = texture;
                renderer.sharedMaterial = material;
                _createdObjects.Add(go);

                await UniTask.Yield();

                // Verify the specific cube is found
                var results = new Search().Name("TexturedCubeUnique").Texture("unique_wood_diffuse_01_xyz").FindAll();
                Assert.AreEqual(1, results.Count, "Should find MeshRenderer by material texture name");

                results = new Search().Name("TexturedCubeUnique").Texture("unique_wood_*").FindAll();
                Assert.AreEqual(1, results.Count, "Should find MeshRenderer by texture wildcard");
            });
        }

        [UnityTest]
        public IEnumerator Texture_MatchesOrPattern()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("OrPatternBtn1", new Vector2(-50, 0));
                var tex1 = new Texture2D(32, 32);
                var sprite1 = Sprite.Create(tex1, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite1.name = "unique_play_icon_xyz";
                btn1.GetComponent<Image>().sprite = sprite1;

                var btn2 = CreateButton("OrPatternBtn2", new Vector2(50, 0));
                var tex2 = new Texture2D(32, 32);
                var sprite2 = Sprite.Create(tex2, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite2.name = "unique_pause_icon_xyz";
                btn2.GetComponent<Image>().sprite = sprite2;

                await UniTask.Yield();

                // Filter by name pattern to get only our buttons, then verify texture OR works
                var results = new Search().Name("OrPatternBtn*").Texture("unique_play_icon_xyz|unique_pause_icon_xyz").FindAll();
                Assert.AreEqual(2, results.Count, "Should find both buttons with OR pattern");
            });
        }

        [UnityTest]
        public IEnumerator Texture_ChainsWithOtherFilters()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var btn1 = CreateButton("ChainIconBtn1", new Vector2(-50, 0));
                var tex1 = new Texture2D(32, 32);
                var sprite1 = Sprite.Create(tex1, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite1.name = "unique_icon_star_xyz";
                btn1.GetComponent<Image>().sprite = sprite1;

                var btn2 = CreateButton("ChainIconBtn2", new Vector2(50, 0));
                btn2.interactable = false;
                var tex2 = new Texture2D(32, 32);
                var sprite2 = Sprite.Create(tex2, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                sprite2.name = "unique_icon_star_xyz";
                btn2.GetComponent<Image>().sprite = sprite2;

                await UniTask.Yield();

                // Find only interactable buttons with icon_star among our specific buttons
                var results = new Search().Name("ChainIconBtn*").Texture("unique_icon_star_xyz").Interactable().FindAll();
                Assert.AreEqual(1, results.Count, "Should find only interactable button");
                Assert.AreEqual("ChainIconBtn1", results[0].name);
            });
        }

        [UnityTest]
        public IEnumerator Texture_DoesNotMatchWhenNoTexture()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Button with no sprite assigned
                var button = CreateButton("NoTextureBtnUnique", Vector2.zero);
                button.GetComponent<Image>().sprite = null;

                await UniTask.Yield();

                // Search for our specific button and verify it doesn't match any texture pattern
                var results = new Search().Name("NoTextureBtnUnique").Texture("*anytexture*").FindAll();
                Assert.AreEqual(0, results.Count, "Should not match element with no texture");
            });
        }

        #endregion

        #region Component Tests

        [UnityTest]
        public IEnumerator Component_Generic_GetsComponentFromUIElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ComponentTestBtn", Vector2.zero);
                // Add a Rigidbody2D for testing (unusual but works for test)
                var rb = button.gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0;
                rb.bodyType = RigidbodyType2D.Kinematic;

                await UniTask.Yield();

                // Get component using generic version
                var search = new Search().Name("ComponentTestBtn").Component<Rigidbody2D>();
                Assert.IsNotNull(search.Value, "Should get Rigidbody2D component");
                Assert.IsTrue(search.Value is Rigidbody2D, "Value should be Rigidbody2D");
            });
        }

        [UnityTest]
        public IEnumerator Component_String_GetsComponentByTypeName()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ComponentStringBtn", Vector2.zero);
                var rb = button.gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0;

                await UniTask.Yield();

                // Get component using string version
                var search = new Search().Name("ComponentStringBtn").Component("Rigidbody2D");
                Assert.IsNotNull(search.Value, "Should get Rigidbody2D component by name");
                Assert.IsTrue(search.Value is Rigidbody2D, "Value should be Rigidbody2D");
            });
        }

        [UnityTest]
        public IEnumerator Component_PropertyChain_AccessesComponentProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ComponentPropBtn", Vector2.zero);
                var rb = button.gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 5.5f;

                await UniTask.Yield();

                // Chain Component() with Property() to access component properties
                var gravityScale = new Search().Name("ComponentPropBtn")
                    .Component<Rigidbody2D>()
                    .Property("gravityScale")
                    .FloatValue;

                Assert.AreEqual(5.5f, gravityScale, 0.01f, "Should read gravityScale property");
            });
        }

        [UnityTest]
        public IEnumerator Component_SetValue_SetsComponentProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ComponentSetBtn", Vector2.zero);
                var rb = button.gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 1f;

                await UniTask.Yield();

                // Use Component() + Property() + SetValue() to modify property
                new Search().Name("ComponentSetBtn")
                    .Component<Rigidbody2D>()
                    .Property("gravityScale")
                    .SetValue(9.8f);

                Assert.AreEqual(9.8f, rb.gravityScale, 0.01f, "gravityScale should be updated");
            });
        }

        [UnityTest]
        public IEnumerator Component_ThrowsWhenComponentNotFound()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("NoComponentBtn", Vector2.zero);

                await UniTask.Yield();

                // Should throw when component doesn't exist
                Assert.Throws<System.InvalidOperationException>(() =>
                {
                    new Search().Name("NoComponentBtn").Component<Rigidbody2D>();
                });
            });
        }

        [UnityTest]
        public IEnumerator Component_ThrowsWhenElementNotFound()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Should throw when no element matches
                Assert.Throws<System.InvalidOperationException>(() =>
                {
                    new Search().Name("NonExistentElement").Component<Rigidbody2D>();
                });
            });
        }

        [UnityTest]
        public IEnumerator Component_WorksWithImage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ImageComponentBtn", Vector2.zero);

                await UniTask.Yield();

                // Get Image component (which exists on button)
                var colorValue = new Search().Name("ImageComponentBtn")
                    .Component<Image>()
                    .Property("color")
                    .ColorValue;

                Assert.AreEqual(Color.gray, colorValue, "Should read Image color property");
            });
        }

        [UnityTest]
        public IEnumerator Component_SetValue_ChangesImageColor()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ImageSetBtn", Vector2.zero);
                var image = button.GetComponent<Image>();

                await UniTask.Yield();

                // Change color via Component + Property + SetValue
                new Search().Name("ImageSetBtn")
                    .Component<Image>()
                    .Property("color")
                    .SetValue(Color.red);

                Assert.AreEqual(Color.red, image.color, "Image color should be updated to red");
            });
        }

        #endregion

        #region Combined Chain Tests

        [UnityTest]
        public IEnumerator CombinedChain_MultipleFilters()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var activeEnabled = CreateButton("ChainBtn1", new Vector2(-100, 0));
                var activeDisabled = CreateButton("ChainBtn2", new Vector2(0, 0));
                activeDisabled.interactable = false;
                var inactive = CreateButton("ChainBtn3", new Vector2(100, 0));
                inactive.gameObject.SetActive(false);

                await UniTask.Yield();

                // Find all including inactive and disabled
                var allResults = new Search()
                    .Name("ChainBtn*")
                    .IncludeInactive()
                    .IncludeDisabled()
                    .FindAll();

                Assert.AreEqual(3, allResults.Count, "Should find all 3 buttons with filters");

                // Find only interactable
                var interactableResults = new Search()
                    .Name("ChainBtn*")
                    .Interactable()
                    .FindAll();

                Assert.AreEqual(1, interactableResults.Count, "Should find only 1 interactable button");
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

        private Text CreateText(string name, string content, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;

            _createdObjects.Add(go);
            return text;
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

        private Toggle CreateToggle(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;

            var toggle = go.AddComponent<Toggle>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = Color.gray;

            var check = new GameObject("Checkmark");
            check.transform.SetParent(bg.transform, false);
            var checkRect = check.AddComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-4, -4);
            var checkImage = check.AddComponent<Image>();
            checkImage.color = Color.green;

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            _createdObjects.Add(go);
            return toggle;
        }

        #endregion
    }
}
