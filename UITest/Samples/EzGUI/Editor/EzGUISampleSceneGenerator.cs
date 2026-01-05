using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ODDGames.UITest.Samples.EzGUI.Editor
{
    /// <summary>
    /// Editor tool to generate sample test scenes for EzGUI (AnB Software) demonstration.
    /// Creates scenes with EzGUI components that work with the EzGUI sample tests.
    /// Only available when HAS_EZ_GUI is defined.
    /// </summary>
    public static class EzGUISampleSceneGenerator
    {
        [MenuItem("Window/UI Test Behaviours/Samples/EzGUI/Generate Button Sample Scene")]
        public static void GenerateButtonSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Create main panel with EzGUI buttons
            var panel = CreateEzGUIPanel("EzGUIButtonPanel");

            // Title
            CreateEzGUIText(panel.transform, "Title", "EzGUI Button Sample", new Vector3(0, 3, 0));

            // Test buttons
            CreateUIButton3D(panel.transform, "SimpleButton", "Simple Button", new Vector3(0, 2, 0));
            CreateUIButton3D(panel.transform, "SettingsButton", "Settings", new Vector3(0, 1, 0));
            CreateUIButton3D(panel.transform, "OptionsButton", "Options", new Vector3(0, 0, 0));
            CreateUIButton3D(panel.transform, "BackButton", "Back", new Vector3(0, -1, 0));
            CreateUIButton3D(panel.transform, "CloseButton", "Close", new Vector3(0, -2, 0));

            // Item buttons for index testing
            for (int i = 0; i < 3; i++)
            {
                CreateUIButton3D(panel.transform, "ItemButton", $"Item {i + 1}", new Vector3(2, 1 - i, 0));
            }

            // Add test behaviour
            var testRunner = new GameObject("EzGUIButtonTest");
            testRunner.AddComponent<EzGUIButtonTest>();

            MarkSceneDirty(scene, "EzGUIButtonSampleScene");
        }

        [MenuItem("Window/UI Test Behaviours/Samples/EzGUI/Generate Navigation Sample Scene")]
        public static void GenerateNavigationSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Main Menu
            var mainMenu = CreateEzGUIPanel("MainMenu");

            CreateEzGUIText(mainMenu.transform, "Title", "Main Menu", new Vector3(0, 3, 0));
            CreateUIButton3D(mainMenu.transform, "PlayButton", "Play", new Vector3(0, 1.5f, 0));
            CreateUIButton3D(mainMenu.transform, "GarageButton", "Garage", new Vector3(0, 0.5f, 0));
            CreateUIButton3D(mainMenu.transform, "ShopButton", "Shop", new Vector3(0, -0.5f, 0));
            CreateUIButton3D(mainMenu.transform, "SettingsButton", "Settings", new Vector3(0, -1.5f, 0));

            // Settings Panel (offset to the side, disabled)
            var settingsPanel = CreateEzGUIPanel("SettingsPanel", new Vector3(10, 0, 0));
            settingsPanel.SetActive(false);

            CreateEzGUIText(settingsPanel.transform, "Title", "Settings", new Vector3(0, 3, 0));
            CreateUIButton3D(settingsPanel.transform, "SoundButton", "Sound: On", new Vector3(0, 1, 0));
            CreateUIButton3D(settingsPanel.transform, "MusicButton", "Music: On", new Vector3(0, 0, 0));
            CreateUIButton3D(settingsPanel.transform, "BackButton", "Back", new Vector3(0, -2, 0));

            // Shop Panel (offset to the other side, disabled)
            var shopPanel = CreateEzGUIPanel("ShopPanel", new Vector3(-10, 0, 0));
            shopPanel.SetActive(false);

            CreateEzGUIText(shopPanel.transform, "Title", "Shop", new Vector3(0, 3, 0));
            CreateUIButton3D(shopPanel.transform, "VehicleTab", "Vehicles", new Vector3(-1.5f, 2, 0));
            CreateUIButton3D(shopPanel.transform, "UpgradeTab", "Upgrades", new Vector3(0, 2, 0));
            CreateUIButton3D(shopPanel.transform, "CoinTab", "Coins", new Vector3(1.5f, 2, 0));

            // Shop items
            for (int i = 0; i < 4; i++)
            {
                CreateUIButton3D(shopPanel.transform, "ItemButton", $"Item {i + 1}", new Vector3(-1 + i, 0, 0));
            }

            CreateUIButton3D(shopPanel.transform, "BackButton", "Back", new Vector3(0, -2, 0));

            // Garage Panel (offset, disabled)
            var garagePanel = CreateEzGUIPanel("GaragePanel", new Vector3(0, 10, 0));
            garagePanel.SetActive(false);

            CreateEzGUIText(garagePanel.transform, "Title", "Garage", new Vector3(0, 3, 0));
            CreateUIButton3D(garagePanel.transform, "VehicleSelect", "Select Vehicle", new Vector3(0, 1, 0));
            CreateUIButton3D(garagePanel.transform, "UpgradeButton", "Upgrades", new Vector3(0, 0, 0));
            CreateUIButton3D(garagePanel.transform, "BackButton", "Back", new Vector3(0, -2, 0));

            // Add test behaviour
            var testRunner = new GameObject("EzGUINavigationTest");
            testRunner.AddComponent<EzGUINavigationTest>();

            MarkSceneDirty(scene, "EzGUINavigationSampleScene");
        }

        [MenuItem("Window/UI Test Behaviours/Samples/EzGUI/Generate Purchase Flow Sample Scene")]
        public static void GeneratePurchaseFlowSampleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Main Menu / Lobby
            var lobby = CreateEzGUIPanel("LobbyScreen");

            CreateEzGUIText(lobby.transform, "Title", "Lobby", new Vector3(0, 3, 0));
            CreateUIButton3D(lobby.transform, "ShopButton", "Shop", new Vector3(0, 1, 0));
            CreateUIButton3D(lobby.transform, "StoreButton", "Store", new Vector3(0, 0, 0));

            // Shop Screen
            var shopScreen = CreateEzGUIPanel("ShopScreen", new Vector3(10, 0, 0));
            shopScreen.SetActive(false);

            CreateEzGUIText(shopScreen.transform, "Title", "Shop", new Vector3(0, 3, 0));

            // Category tabs
            CreateUIButton3D(shopScreen.transform, "VehicleTab", "Vehicles", new Vector3(-2, 2, 0));
            CreateUIButton3D(shopScreen.transform, "UpgradeTab", "Upgrades", new Vector3(0, 2, 0));
            CreateUIButton3D(shopScreen.transform, "CoinTab", "Coins", new Vector3(2, 2, 0));

            // Item buttons
            for (int i = 0; i < 6; i++)
            {
                int row = i / 3;
                int col = i % 3;
                CreateUIButton3D(shopScreen.transform, "ItemButton", $"Item {i + 1}", new Vector3(-1.5f + col * 1.5f, 0.5f - row * 1.5f, 0));
            }

            // Premium items
            CreateUIButton3D(shopScreen.transform, "PremiumItem", "Premium Bundle", new Vector3(0, -2, 0));
            CreateUIButton3D(shopScreen.transform, "VIPItem", "VIP Pack", new Vector3(2, -2, 0));

            CreateUIButton3D(shopScreen.transform, "BackButton", "Back", new Vector3(-2, -3, 0));

            // Confirmation Dialog
            var confirmDialog = CreateEzGUIPanel("ConfirmDialog", new Vector3(0, 0, -1));
            confirmDialog.SetActive(false);

            CreateEzGUIText(confirmDialog.transform, "Title", "Confirm Purchase?", new Vector3(0, 1, 0));
            CreateUIButton3D(confirmDialog.transform, "ConfirmButton", "Buy", new Vector3(-1, -1, 0));
            CreateUIButton3D(confirmDialog.transform, "CancelButton", "Cancel", new Vector3(1, -1, 0));

            // Insufficient Funds Dialog
            var insufficientDialog = CreateEzGUIPanel("InsufficientFundsDialog", new Vector3(0, 0, -2));
            insufficientDialog.SetActive(false);

            CreateEzGUIText(insufficientDialog.transform, "Title", "Not Enough Coins!", new Vector3(0, 1, 0));
            CreateUIButton3D(insufficientDialog.transform, "GetMoreButton", "Get More", new Vector3(-1, -1, 0));
            CreateUIButton3D(insufficientDialog.transform, "CancelButton", "Cancel", new Vector3(1, -1, 0));

            // Add test behaviour
            var testRunner = new GameObject("EzGUIPurchaseFlowTest");
            testRunner.AddComponent<EzGUIPurchaseFlowTest>();

            MarkSceneDirty(scene, "EzGUIPurchaseFlowSampleScene");
        }

        private static GameObject CreateEzGUIPanel(string name, Vector3? position = null)
        {
            var go = new GameObject(name);
            go.transform.position = position ?? Vector3.zero;
            return go;
        }

        private static UIButton3D CreateUIButton3D(Transform parent, string name, string label, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            // Add UIButton3D component
            var button = go.AddComponent<UIButton3D>();

            // Add a visual representation (simple cube for now)
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(1.5f, 0.4f, 0.1f);

            // Add text label
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, -0.1f);
            var textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.fontSize = 24;
            textMesh.characterSize = 0.1f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;

            // Add collider for raycasting
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.5f, 0.4f, 0.2f);

            return button;
        }

        private static TextMesh CreateEzGUIText(Transform parent, string name, string content, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var textMesh = go.AddComponent<TextMesh>();
            textMesh.text = content;
            textMesh.fontSize = 32;
            textMesh.characterSize = 0.15f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;

            return textMesh;
        }

        private static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene, string name)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[UITest] Generated {name}. Save it with Ctrl+S or File > Save Scene.");
        }
    }
}
