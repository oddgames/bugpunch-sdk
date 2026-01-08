using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ODDGames.UITest;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// NUnit tests for Search.ByAdjacent() functionality.
    /// Tests the scoring algorithm for finding interactables adjacent to text labels.
    /// </summary>
    [TestFixture]
    public class SearchAdjacentTests
    {
        private GameObject _canvas;
        private GameObject _panel;

        [SetUp]
        public void SetUp()
        {
            // Create a canvas for UI elements
            _canvas = new GameObject("TestCanvas");
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.AddComponent<CanvasScaler>();
            _canvas.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("TestPanel");
            _panel.transform.SetParent(_canvas.transform, false);
            var panelRect = _panel.AddComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(800, 600);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvas);
        }

        #region Right Direction Tests

        [Test]
        public void ByAdjacent_Right_FindsInputToRightOfLabel()
        {
            // Arrange: Label on left, input on right
            var label = CreateLabel("Username:", new Vector2(-100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Username:");
            bool matches = search.Matches(input);

            // Assert
            Assert.IsTrue(matches, "Should find input to the right of label");
        }

        [Test]
        public void ByAdjacent_Right_IgnoresInputToLeft()
        {
            // Arrange: Label on right, input on left (wrong direction)
            var label = CreateLabel("Username:", new Vector2(100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(-100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Username:", UITestBehaviour.Adjacent.Right);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsFalse(matches, "Should not match input to the left when searching Right");
        }

        [Test]
        public void ByAdjacent_Right_PrefersCloserInput()
        {
            // Arrange: Label with two inputs to the right, closer one should match
            var label = CreateLabel("Field:", new Vector2(-150, 0));
            var closeInput = CreateInputField("CloseInput", new Vector2(0, 0));
            var farInput = CreateInputField("FarInput", new Vector2(200, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Field:");

            // Assert
            Assert.IsTrue(search.Matches(closeInput), "Closer input should match");
            Assert.IsFalse(search.Matches(farInput), "Farther input should not match");
        }

        [Test]
        public void ByAdjacent_Right_WithDistractorBelow()
        {
            // Arrange: Label with input to right and distractor below
            var label = CreateLabel("Name:", new Vector2(-100, 50));
            var rightInput = CreateInputField("RightInput", new Vector2(100, 50));
            var belowInput = CreateInputField("BelowInput", new Vector2(-100, -50));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Name:", UITestBehaviour.Adjacent.Right);

            // Assert
            Assert.IsTrue(search.Matches(rightInput), "Input to the right should match");
            Assert.IsFalse(search.Matches(belowInput), "Input below should not match for Right direction");
        }

        #endregion

        #region Left Direction Tests

        [Test]
        public void ByAdjacent_Left_FindsInputToLeftOfLabel()
        {
            // Arrange: Input on left, label on right
            var input = CreateInputField("VolumeSlider", new Vector2(-100, 0));
            var label = CreateLabel("Volume Level", new Vector2(100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Volume Level", UITestBehaviour.Adjacent.Left);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsTrue(matches, "Should find input to the left of label");
        }

        [Test]
        public void ByAdjacent_Left_IgnoresInputToRight()
        {
            // Arrange: Input on right of label
            var label = CreateLabel("Volume Level", new Vector2(-100, 0));
            var input = CreateInputField("VolumeSlider", new Vector2(100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Volume Level", UITestBehaviour.Adjacent.Left);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsFalse(matches, "Should not match input to the right when searching Left");
        }

        #endregion

        #region Below Direction Tests

        [Test]
        public void ByAdjacent_Below_FindsInputBelowLabel()
        {
            // Arrange: Label on top, input below
            var label = CreateLabel("Description", new Vector2(0, 50));
            var input = CreateInputField("DescriptionInput", new Vector2(0, -50));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Description", UITestBehaviour.Adjacent.Below);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsTrue(matches, "Should find input below label");
        }

        [Test]
        public void ByAdjacent_Below_IgnoresInputAbove()
        {
            // Arrange: Input above label
            var input = CreateInputField("DescriptionInput", new Vector2(0, 50));
            var label = CreateLabel("Description", new Vector2(0, -50));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Description", UITestBehaviour.Adjacent.Below);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsFalse(matches, "Should not match input above when searching Below");
        }

        [Test]
        public void ByAdjacent_Below_WithDistractorToRight()
        {
            // Arrange: Label with input below and distractor to right
            var label = CreateLabel("Bio:", new Vector2(-50, 100));
            var belowInput = CreateInputField("BelowInput", new Vector2(-50, 0));
            var rightInput = CreateInputField("RightInput", new Vector2(100, 100));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Bio:", UITestBehaviour.Adjacent.Below);

            // Assert
            Assert.IsTrue(search.Matches(belowInput), "Input below should match");
            Assert.IsFalse(search.Matches(rightInput), "Input to right should not match for Below direction");
        }

        #endregion

        #region Above Direction Tests

        [Test]
        public void ByAdjacent_Above_FindsInputAboveLabel()
        {
            // Arrange: Input on top, label below
            var input = CreateInputField("OptionToggle", new Vector2(0, 50));
            var label = CreateLabel("Clear Selection", new Vector2(0, -50));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Clear Selection", UITestBehaviour.Adjacent.Above);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsTrue(matches, "Should find input above label");
        }

        [Test]
        public void ByAdjacent_Above_IgnoresInputBelow()
        {
            // Arrange: Input below label
            var label = CreateLabel("Clear Selection", new Vector2(0, 50));
            var input = CreateInputField("OptionToggle", new Vector2(0, -50));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Clear Selection", UITestBehaviour.Adjacent.Above);
            bool matches = search.Matches(input);

            // Assert
            Assert.IsFalse(matches, "Should not match input below when searching Above");
        }

        #endregion

        #region Ambiguous Layout Tests

        [Test]
        public void ByAdjacent_AmbiguousLayout_RightPicksCorrectOne()
        {
            // Arrange: Label with inputs in multiple directions
            var label = CreateLabel("Ambiguous:", new Vector2(0, 0));
            var rightInput = CreateInputField("RightInput", new Vector2(150, 0));
            var belowInput = CreateInputField("BelowInput", new Vector2(0, -80));
            var aboveInput = CreateInputField("AboveInput", new Vector2(0, 80));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Ambiguous:", UITestBehaviour.Adjacent.Right);

            // Assert
            Assert.IsTrue(search.Matches(rightInput), "Right direction should match right input");
            Assert.IsFalse(search.Matches(belowInput), "Right direction should not match below input");
            Assert.IsFalse(search.Matches(aboveInput), "Right direction should not match above input");
        }

        [Test]
        public void ByAdjacent_AmbiguousLayout_BelowPicksCorrectOne()
        {
            // Arrange: Label with inputs in multiple directions
            var label = CreateLabel("Ambiguous:", new Vector2(0, 0));
            var rightInput = CreateInputField("RightInput", new Vector2(150, 0));
            var belowInput = CreateInputField("BelowInput", new Vector2(0, -80));
            var aboveInput = CreateInputField("AboveInput", new Vector2(0, 80));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Ambiguous:", UITestBehaviour.Adjacent.Below);

            // Assert
            Assert.IsFalse(search.Matches(rightInput), "Below direction should not match right input");
            Assert.IsTrue(search.Matches(belowInput), "Below direction should match below input");
            Assert.IsFalse(search.Matches(aboveInput), "Below direction should not match above input");
        }

        #endregion

        #region Edge Cases

        [Test]
        public void ByAdjacent_NoMatchingLabel_ReturnsFalse()
        {
            // Arrange: Input with no matching label
            var label = CreateLabel("Other Label:", new Vector2(-100, 0));
            var input = CreateInputField("TestInput", new Vector2(100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("NonExistent:");
            bool matches = search.Matches(input);

            // Assert
            Assert.IsFalse(matches, "Should not match when label text doesn't exist");
        }

        [Test]
        public void ByAdjacent_WildcardPattern_Matches()
        {
            // Arrange
            var label = CreateLabel("Username:", new Vector2(-100, 0));
            var input = CreateInputField("UsernameInput", new Vector2(100, 0));

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("User*");
            bool matches = search.Matches(input);

            // Assert
            Assert.IsTrue(matches, "Should match with wildcard pattern");
        }

        [Test]
        public void ByAdjacent_NonInteractable_ReturnsFalse()
        {
            // Arrange: A non-interactable GameObject
            var label = CreateLabel("Label:", new Vector2(-100, 0));
            var nonInteractable = new GameObject("NonInteractable");
            nonInteractable.transform.SetParent(_panel.transform, false);
            var rect = nonInteractable.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(100, 0);

            // Act
            var search = UITestBehaviour.Search.ByAdjacent("Label:");
            bool matches = search.Matches(nonInteractable);

            // Assert
            Assert.IsFalse(matches, "Should not match non-interactable GameObjects");
        }

        #endregion

        #region Helper Methods

        private GameObject CreateLabel(string text, Vector2 position)
        {
            var go = new GameObject($"Label_{text}");
            go.transform.SetParent(_panel.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;

            return go;
        }

        private GameObject CreateInputField(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_panel.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150, 30);
            rect.anchoredPosition = position;

            go.AddComponent<Image>();

            // Create text area and text component for TMP_InputField
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

        private GameObject CreateSlider(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_panel.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150, 20);
            rect.anchoredPosition = position;

            go.AddComponent<Slider>();

            return go;
        }

        private GameObject CreateToggle(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_panel.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100, 25);
            rect.anchoredPosition = position;

            go.AddComponent<Toggle>();

            return go;
        }

        #endregion
    }
}
