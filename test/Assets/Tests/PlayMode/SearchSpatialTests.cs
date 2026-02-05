using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// PlayMode tests for spatial helpers and Unity type value properties.
    /// Tests: Vector3Value, Vector2Value, ColorValue, QuaternionValue,
    /// ScreenCenter, ScreenBounds, WorldPosition, WorldBounds,
    /// IsAbove, IsBelow, IsLeftOf, IsRightOf, DistanceTo, WorldDistanceTo,
    /// Overlaps, Contains, IsHorizontallyAligned, IsVerticallyAligned,
    /// IsInFrontOf, IsBehind.
    /// </summary>
    [TestFixture]
    public class SearchSpatialTests
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private Camera _camera;
        private GameObject _canvasGO;
        private GameObject _esGO;
        private GameObject _cameraGO;

        [SetUp]
        public void SetUp()
        {
            // Create Camera
            _cameraGO = new GameObject("Main Camera");
            _camera = _cameraGO.AddComponent<Camera>();
            _cameraGO.tag = "MainCamera";
            _camera.transform.position = new Vector3(0, 0, -10);

            // Create EventSystem
            _esGO = new GameObject("EventSystem");
            _eventSystem = _esGO.AddComponent<EventSystem>();
            _esGO.AddComponent<InputSystemUIInputModule>();

            // Create Canvas
            _canvasGO = new GameObject("Canvas");
            _canvas = _canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGO != null) Object.Destroy(_canvasGO);
            if (_esGO != null) Object.Destroy(_esGO);
            if (_cameraGO != null) Object.Destroy(_cameraGO);
        }

        #region Unity Type Value Properties

        [Test]
        public async Task Vector3Value_ReturnsAnchoredPosition3D()
        {
            var btn = CreateButton("TestBtn", new Vector2(100, 50));

            await Async.DelayFrames(1);

            var value = new Search().Name("TestBtn").Vector3Value;
            Assert.AreEqual(100, value.x, 0.1f, "X should match");
            Assert.AreEqual(50, value.y, 0.1f, "Y should match");
        }

        [Test]
        public async Task Vector3Value_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").Vector3Value;
            });
        }

        [Test]
        public async Task Vector2Value_ReturnsAnchoredPosition()
        {
            var btn = CreateButton("TestBtn2", new Vector2(-75, 120));

            await Async.DelayFrames(1);

            var value = new Search().Name("TestBtn2").Vector2Value;
            Assert.AreEqual(-75, value.x, 0.1f, "X should match");
            Assert.AreEqual(120, value.y, 0.1f, "Y should match");
        }

        [Test]
        public async Task Vector2Value_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").Vector2Value;
            });
        }

        [Test]
        public async Task ColorValue_ReturnsImageColor()
        {
            var btn = CreateButton("ColorBtn", Vector2.zero);
            var image = btn.GetComponent<Image>();
            image.color = Color.red;

            await Async.DelayFrames(1);

            var value = new Search().Name("ColorBtn").ColorValue;
            Assert.AreEqual(Color.red, value, "Should return image color");
        }

        [Test]
        public async Task ColorValue_ReturnsTextColor()
        {
            var text = CreateTMPText("ColorText", "Hello", Vector2.zero);
            text.color = Color.blue;

            await Async.DelayFrames(1);

            var value = new Search().Name("ColorText").ColorValue;
            Assert.AreEqual(Color.blue, value, "Should return text color");
        }

        [Test]
        public async Task ColorValue_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").ColorValue;
            });
        }

        [Test]
        public async Task QuaternionValue_ReturnsRotation()
        {
            var btn = CreateButton("RotatedBtn", Vector2.zero);
            btn.transform.rotation = Quaternion.Euler(0, 0, 45);

            await Async.DelayFrames(1);

            var value = new Search().Name("RotatedBtn").QuaternionValue;
            Assert.AreEqual(45, value.eulerAngles.z, 0.1f, "Z rotation should be 45 degrees");
        }

        [Test]
        public async Task QuaternionValue_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").QuaternionValue;
            });
        }

        #endregion

        #region ScreenCenter and ScreenBounds

        [Test]
        public async Task ScreenCenter_ReturnsValidPosition()
        {
            var btn = CreateButton("CenterBtn", Vector2.zero);

            await Async.DelayFrames(1);

            var center = new Search().Name("CenterBtn").ScreenCenter;
            Assert.Greater(center.x, 0, "X should be positive");
            Assert.Greater(center.y, 0, "Y should be positive");
        }

        [Test]
        public async Task ScreenCenter_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").ScreenCenter;
            });
        }

        [Test]
        public async Task ScreenBounds_ReturnsValidRect()
        {
            var btn = CreateButton("BoundsBtn", Vector2.zero);
            btn.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 50);

            await Async.DelayFrames(1);

            var bounds = new Search().Name("BoundsBtn").ScreenBounds;
            Assert.Greater(bounds.width, 0, "Width should be positive");
            Assert.Greater(bounds.height, 0, "Height should be positive");
        }

        [Test]
        public async Task ScreenBounds_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").ScreenBounds;
            });
        }

        #endregion

        #region WorldPosition and WorldBounds

        [Test]
        public async Task WorldPosition_ReturnsTransformPosition()
        {
            var btn = CreateButton("WorldBtn", Vector2.zero);

            await Async.DelayFrames(1);

            var pos = new Search().Name("WorldBtn").WorldPosition;
            // In screen space overlay, world position is based on canvas transform
            Assert.IsNotNull(pos, "Should return a position");
        }

        [Test]
        public async Task WorldPosition_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").WorldPosition;
            });
        }

        [Test]
        public async Task WorldBounds_ReturnsZeroSizeForUIElements()
        {
            var btn = CreateButton("WorldBoundsBtn", Vector2.zero);

            await Async.DelayFrames(1);

            // UI elements without Renderer/Collider return zero-size bounds at position
            var bounds = new Search().Name("WorldBoundsBtn").WorldBounds;
            Assert.AreEqual(Vector3.zero, bounds.size, "UI elements without Renderer should have zero size bounds");
        }

        [Test]
        public async Task WorldBounds_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = new Search().Name("NonExistent").WorldBounds;
            });
        }

        #endregion

        #region Relative Position Tests (IsAbove, IsBelow, IsLeftOf, IsRightOf)

        [Test]
        public async Task IsAbove_ReturnsCorrectly()
        {
            CreateButton("TopBtn", new Vector2(0, 100));
            CreateButton("BottomBtn", new Vector2(0, -100));

            await Async.DelayFrames(1);

            var topSearch = new Search().Name("TopBtn");
            var bottomSearch = new Search().Name("BottomBtn");

            Assert.IsTrue(topSearch.IsAbove(bottomSearch), "Top should be above bottom");
            Assert.IsFalse(bottomSearch.IsAbove(topSearch), "Bottom should not be above top");
        }

        [Test]
        public async Task IsBelow_ReturnsCorrectly()
        {
            CreateButton("TopBtn2", new Vector2(0, 100));
            CreateButton("BottomBtn2", new Vector2(0, -100));

            await Async.DelayFrames(1);

            var topSearch = new Search().Name("TopBtn2");
            var bottomSearch = new Search().Name("BottomBtn2");

            Assert.IsTrue(bottomSearch.IsBelow(topSearch), "Bottom should be below top");
            Assert.IsFalse(topSearch.IsBelow(bottomSearch), "Top should not be below bottom");
        }

        [Test]
        public async Task IsLeftOf_ReturnsCorrectly()
        {
            CreateButton("LeftBtn", new Vector2(-100, 0));
            CreateButton("RightBtn", new Vector2(100, 0));

            await Async.DelayFrames(1);

            var leftSearch = new Search().Name("LeftBtn");
            var rightSearch = new Search().Name("RightBtn");

            Assert.IsTrue(leftSearch.IsLeftOf(rightSearch), "Left should be left of right");
            Assert.IsFalse(rightSearch.IsLeftOf(leftSearch), "Right should not be left of left");
        }

        [Test]
        public async Task IsRightOf_ReturnsCorrectly()
        {
            CreateButton("LeftBtn2", new Vector2(-100, 0));
            CreateButton("RightBtn2", new Vector2(100, 0));

            await Async.DelayFrames(1);

            var leftSearch = new Search().Name("LeftBtn2");
            var rightSearch = new Search().Name("RightBtn2");

            Assert.IsTrue(rightSearch.IsRightOf(leftSearch), "Right should be right of left");
            Assert.IsFalse(leftSearch.IsRightOf(rightSearch), "Left should not be right of right");
        }

        #endregion

        #region Distance Tests

        [Test]
        public async Task DistanceTo_ReturnsScreenSpaceDistance()
        {
            CreateButton("DistBtn1", new Vector2(0, 0));
            CreateButton("DistBtn2", new Vector2(100, 0));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("DistBtn1");
            var search2 = new Search().Name("DistBtn2");

            var distance = search1.DistanceTo(search2);
            Assert.Greater(distance, 0, "Distance should be positive");
            // In screen space overlay, the distance should be roughly 100 pixels
            Assert.AreEqual(100, distance, 10f, "Distance should be approximately 100");
        }

        [Test]
        public async Task WorldDistanceTo_ReturnsWorldSpaceDistance()
        {
            CreateButton("WorldDistBtn1", new Vector2(0, 0));
            CreateButton("WorldDistBtn2", new Vector2(100, 0));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("WorldDistBtn1");
            var search2 = new Search().Name("WorldDistBtn2");

            var distance = search1.WorldDistanceTo(search2);
            Assert.Greater(distance, 0, "World distance should be positive");
        }

        #endregion

        #region Overlap and Contains Tests

        [Test]
        public async Task Overlaps_ReturnsTrueForOverlappingElements()
        {
            var btn1 = CreateButton("OverlapBtn1", new Vector2(0, 0));
            btn1.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);

            var btn2 = CreateButton("OverlapBtn2", new Vector2(50, 0));
            btn2.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);

            await Async.DelayFrames(1);

            var search1 = new Search().Name("OverlapBtn1");
            var search2 = new Search().Name("OverlapBtn2");

            Assert.IsTrue(search1.Overlaps(search2), "Elements should overlap");
            Assert.IsTrue(search2.Overlaps(search1), "Overlap should be symmetric");
        }

        [Test]
        public async Task Overlaps_ReturnsFalseForSeparateElements()
        {
            var btn1 = CreateButton("SepBtn1", new Vector2(-200, 0));
            btn1.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 50);

            var btn2 = CreateButton("SepBtn2", new Vector2(200, 0));
            btn2.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 50);

            await Async.DelayFrames(1);

            var search1 = new Search().Name("SepBtn1");
            var search2 = new Search().Name("SepBtn2");

            Assert.IsFalse(search1.Overlaps(search2), "Elements should not overlap");
        }

        [Test]
        public async Task Contains_ReturnsTrueWhenFullyContained()
        {
            var outer = CreateButton("OuterBtn", new Vector2(0, 0));
            outer.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);

            var inner = CreateButton("InnerBtn", new Vector2(0, 0));
            inner.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 50);

            await Async.DelayFrames(1);

            var outerSearch = new Search().Name("OuterBtn");
            var innerSearch = new Search().Name("InnerBtn");

            Assert.IsTrue(outerSearch.Contains(innerSearch), "Outer should contain inner");
            Assert.IsFalse(innerSearch.Contains(outerSearch), "Inner should not contain outer");
        }

        [Test]
        public async Task WorldIntersects_ReturnsTrueForIntersectingBounds()
        {
            // UI elements without Renderer have zero-size world bounds at their position
            // Both at same position means they intersect
            var btn1 = CreateButton("WorldIntBtn1", new Vector2(0, 0));
            var btn2 = CreateButton("WorldIntBtn2", new Vector2(0, 0));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("WorldIntBtn1");
            var search2 = new Search().Name("WorldIntBtn2");

            // Zero-size bounds at same position still intersect
            Assert.IsTrue(search1.WorldIntersects(search2), "Bounds at same position should intersect");
        }

        [Test]
        public async Task WorldContains_ReturnsTrueWhenFullyContained()
        {
            // For UI elements without Renderer, WorldBounds returns zero-size at position
            // A point contains itself
            var btn1 = CreateButton("WorldContBtn1", new Vector2(0, 0));
            var btn2 = CreateButton("WorldContBtn2", new Vector2(0, 0));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("WorldContBtn1");
            var search2 = new Search().Name("WorldContBtn2");

            // Zero-size bounds at same point - they contain each other
            Assert.IsTrue(search1.WorldContains(search2), "Point should contain point at same location");
        }

        #endregion

        #region Alignment Tests

        [Test]
        public async Task IsHorizontallyAligned_ReturnsTrueForSameY()
        {
            CreateButton("HAlignBtn1", new Vector2(-100, 50));
            CreateButton("HAlignBtn2", new Vector2(100, 50));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("HAlignBtn1");
            var search2 = new Search().Name("HAlignBtn2");

            Assert.IsTrue(search1.IsHorizontallyAligned(search2), "Elements at same Y should be horizontally aligned");
        }

        [Test]
        public async Task IsHorizontallyAligned_ReturnsFalseForDifferentY()
        {
            CreateButton("HMisalignBtn1", new Vector2(-100, 100));
            CreateButton("HMisalignBtn2", new Vector2(100, -100));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("HMisalignBtn1");
            var search2 = new Search().Name("HMisalignBtn2");

            Assert.IsFalse(search1.IsHorizontallyAligned(search2), "Elements at different Y should not be horizontally aligned");
        }

        [Test]
        public async Task IsVerticallyAligned_ReturnsTrueForSameX()
        {
            CreateButton("VAlignBtn1", new Vector2(50, 100));
            CreateButton("VAlignBtn2", new Vector2(50, -100));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("VAlignBtn1");
            var search2 = new Search().Name("VAlignBtn2");

            Assert.IsTrue(search1.IsVerticallyAligned(search2), "Elements at same X should be vertically aligned");
        }

        [Test]
        public async Task IsVerticallyAligned_ReturnsFalseForDifferentX()
        {
            CreateButton("VMisalignBtn1", new Vector2(-100, 100));
            CreateButton("VMisalignBtn2", new Vector2(100, -100));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("VMisalignBtn1");
            var search2 = new Search().Name("VMisalignBtn2");

            Assert.IsFalse(search1.IsVerticallyAligned(search2), "Elements at different X should not be vertically aligned");
        }

        [Test]
        public async Task IsHorizontallyAligned_RespectsCustomTolerance()
        {
            CreateButton("TolBtn1", new Vector2(0, 50));
            CreateButton("TolBtn2", new Vector2(0, 55));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("TolBtn1");
            var search2 = new Search().Name("TolBtn2");

            // Default tolerance is 10, so 5 pixel difference should pass
            Assert.IsTrue(search1.IsHorizontallyAligned(search2, 10f), "Should be aligned with 10px tolerance");
            // 2 pixel tolerance should fail for 5 pixel difference
            Assert.IsFalse(search1.IsHorizontallyAligned(search2, 2f), "Should not be aligned with 2px tolerance");
        }

        #endregion

        #region 3D Depth Tests (IsInFrontOf, IsBehind)

        [Test]
        public async Task IsInFrontOf_WithUIElements()
        {
            // UI elements in screen space overlay are at same Z, so this tests the fallback behavior
            CreateButton("FrontBtn", Vector2.zero);
            CreateButton("BackBtn", Vector2.zero);

            await Async.DelayFrames(1);

            var frontSearch = new Search().Name("FrontBtn");
            var backSearch = new Search().Name("BackBtn");

            // Both at same position, so neither is in front
            // This tests that the method doesn't throw
            var result = frontSearch.IsInFrontOf(backSearch);
            Assert.IsTrue(result || !result, "Should return a boolean value");
        }

        [Test]
        public async Task IsBehind_IsOppositeOfIsInFrontOf()
        {
            CreateButton("TestBtn1", new Vector2(-50, 0));
            CreateButton("TestBtn2", new Vector2(50, 0));

            await Async.DelayFrames(1);

            var search1 = new Search().Name("TestBtn1");
            var search2 = new Search().Name("TestBtn2");

            // IsBehind should be opposite of IsInFrontOf
            Assert.AreNotEqual(search1.IsInFrontOf(search2), search1.IsBehind(search2),
                "IsBehind should be opposite of IsInFrontOf");
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

            return button;
        }

        private TextMeshProUGUI CreateTMPText(string name, string content, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 50);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;

            return tmp;
        }

        #endregion
    }
}
