using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ODDGames.Bugpunch;
using ODDGames.Bugpunch.DeviceConnect;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Fake casual-game menu system used to generate realistic storyboards for
/// testing the crash-reporting dashboard. Builds a deep UI hierarchy
/// (Canvas/Home/TopBar/CoinDisplay, Canvas/Shop/ItemList/Gem_Pack/BuyButton,
/// …) so tap breadcrumbs carry meaningful paths, and emits warnings/errors
/// on certain actions so the Logs rail has interesting entries. Scene
/// changes are pushed as virtual scenes via BugpunchNative.UpdateScene —
/// no actual Unity scene load needed.
///
/// Open via the "Open Demo Menus" debug button (see GameDebugTools.cs).
/// </summary>
public class StoryboardDemo : MonoBehaviour
{
    static StoryboardDemo s_instance;

    Canvas _canvas;
    RectTransform _panelHost;
    TMPro.TextMeshProUGUI _coinsLabel;
    readonly Stack<PanelKind> _stack = new();
    int _coins = 25;
    readonly HashSet<string> _equipped = new();

    enum PanelKind { Home, Play, Shop, Inventory, Settings, Profile, Debug, Audio, Video, Account }

    public static void Open()
    {
        if (s_instance != null) return;
        var go = new GameObject("StoryboardDemo");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<StoryboardDemo>();
    }

    public static void Close()
    {
        if (s_instance == null) return;
        Destroy(s_instance.gameObject);
        s_instance = null;
    }

    void Start()
    {
        BuildCanvas();
        Navigate(PanelKind.Home, pushStack: false);
    }

    // ── Canvas + chrome ──

    void BuildCanvas()
    {
        var canvasGo = new GameObject("StoryboardCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;   // above CityPlayer (100), below debug overlay (9999)
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        // Dim backdrop so the CityPlayer / CityTest scene doesn't leak through.
        var bg = new GameObject("Backdrop");
        bg.transform.SetParent(canvasGo.transform, false);
        Stretch(bg.AddComponent<RectTransform>());
        bg.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

        // Top bar — title + coin count. Coin label is stored so Shop can update it.
        var topBar = NewPanel(canvasGo.transform, "TopBar", new Color(0.12f, 0.14f, 0.19f, 1f));
        var topRect = topBar.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0, 1);
        topRect.anchorMax = new Vector2(1, 1);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(0, 120);
        NewLabel(topBar.transform, "Title", "STORYBOARD DEMO", 42, TMPro.TextAlignmentOptions.Left,
            new Vector2(32, 0), new Vector2(500, 120), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        _coinsLabel = NewLabel(topBar.transform, "CoinDisplay", "", 36, TMPro.TextAlignmentOptions.Right,
            new Vector2(-32, 0), new Vector2(400, 120), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        UpdateCoins();

        // Panel host — where screens swap in.
        var host = new GameObject("PanelHost");
        host.transform.SetParent(canvasGo.transform, false);
        _panelHost = host.AddComponent<RectTransform>();
        _panelHost.anchorMin = new Vector2(0, 0);
        _panelHost.anchorMax = new Vector2(1, 1);
        _panelHost.offsetMin = new Vector2(0, 120);
        _panelHost.offsetMax = new Vector2(0, -120);
    }

    // ── Navigation ──

    void Navigate(PanelKind kind, bool pushStack = true)
    {
        if (_stack.Count == 0) _stack.Push(kind);
        else if (pushStack && _stack.Peek() != kind) _stack.Push(kind);

        // Tell the SDK about the "scene" change so the storyboard rail picks
        // it up. Virtual scenes are fine — native stores whatever string we push.
        BugpunchNative.UpdateScene(kind.ToString());

        for (int i = _panelHost.childCount - 1; i >= 0; i--)
            Destroy(_panelHost.GetChild(i).gameObject);

        switch (kind)
        {
            case PanelKind.Home:      BuildHome();      break;
            case PanelKind.Play:      BuildPlay();      break;
            case PanelKind.Shop:      BuildShop();      break;
            case PanelKind.Inventory: BuildInventory(); break;
            case PanelKind.Settings:  BuildSettings();  break;
            case PanelKind.Profile:   BuildProfile();   break;
            case PanelKind.Debug:     BuildDebug();     break;
            case PanelKind.Audio:     BuildAudio();     break;
            case PanelKind.Video:     BuildVideo();     break;
            case PanelKind.Account:   BuildAccount();   break;
        }
        Debug.Log($"[StoryboardDemo] Navigated to {kind}");
    }

    void GoBack()
    {
        if (_stack.Count <= 1) return;
        _stack.Pop();
        Navigate(_stack.Peek(), pushStack: false);
    }

    // ── Screens ──

    void BuildHome()
    {
        var root = NewScreen("Home");
        NewLabel(root.transform, "Title", "Welcome back, tester!", 56, TMPro.TextAlignmentOptions.Center,
            new Vector2(0, 500), new Vector2(900, 80), centerPivot: true);

        var grid = NewList(root.transform, "MainMenu", topY: 380, rowHeight: 120, gap: 24);
        NewButton(grid, "PlayButton",      "Play",      icon: "▶", onClick: () => Navigate(PanelKind.Play));
        NewButton(grid, "ShopButton",      "Shop",      icon: "🛒", onClick: () => Navigate(PanelKind.Shop));
        NewButton(grid, "InventoryButton", "Inventory", icon: "🎒", onClick: () => Navigate(PanelKind.Inventory));
        NewButton(grid, "SettingsButton",  "Settings",  icon: "⚙", onClick: () => Navigate(PanelKind.Settings));
        NewButton(grid, "ProfileButton",   "Profile",   icon: "👤", onClick: () => Navigate(PanelKind.Profile));

        var footer = NewRow(root.transform, "Footer", y: -720);
        NewButton(footer, "DebugButton", "Dev Menu", tinted: new Color(0.35f, 0.2f, 0.2f),
            onClick: () => Navigate(PanelKind.Debug));
        NewButton(footer, "CloseDemoButton", "Close Demo", tinted: new Color(0.25f, 0.25f, 0.28f),
            onClick: Close);
    }

    void BuildPlay()
    {
        var root = NewScreen("Play");
        AddBackBar(root, "Level Select");

        var list = NewList(root.transform, "LevelList", topY: 300, rowHeight: 120, gap: 16);
        for (int i = 1; i <= 5; i++)
        {
            int level = i;
            NewButton(list, $"Level_{level:00}", $"Level {level}", icon: level <= 3 ? "★" : "🔒",
                onClick: () => {
                    if (level > 3) {
                        Debug.LogWarning($"[StoryboardDemo] Level {level} is locked — clear Level 3 first");
                        FlashToast($"Level {level} locked");
                    } else {
                        Debug.Log($"[StoryboardDemo] Starting level {level}");
                        FlashToast($"Loading Level {level}…");
                    }
                });
        }
    }

    void BuildShop()
    {
        var root = NewScreen("Shop");
        AddBackBar(root, "Shop");

        var items = new (string id, string label, int price)[] {
            ("Starter_Pack",  "Starter Pack",  50),
            ("Gem_Pack",      "Gem Pack",      120),
            ("Coin_Doubler",  "Coin Doubler",  200),
            ("Season_Pass",   "Season Pass",   500),
            ("Mystery_Box",   "Mystery Box",   15),
        };

        var list = NewList(root.transform, "ItemList", topY: 300, rowHeight: 140, gap: 12);
        foreach (var item in items)
        {
            var row = NewRowPanel(list, item.id, 140);
            NewLabel(row.transform, "Name",  item.label, 32, TMPro.TextAlignmentOptions.Left,
                new Vector2(32, 0), new Vector2(500, 140), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            NewLabel(row.transform, "Price", $"{item.price} 🪙", 28, TMPro.TextAlignmentOptions.Right,
                new Vector2(-220, 0), new Vector2(200, 140), new Vector2(1, 0.5f), new Vector2(1, 0.5f));

            int price = item.price;
            string id = item.id;
            string label = item.label;
            NewChildButton(row.transform, "BuyButton", "Buy", new Vector2(-32, 0),
                new Vector2(160, 80), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                onClick: () => OnBuy(id, label, price));
        }
    }

    void OnBuy(string id, string label, int price)
    {
        if (_coins < price)
        {
            Debug.LogWarning($"[StoryboardDemo] Buy {id} failed — insufficient coins ({_coins} < {price})");
            FlashToast("Not enough coins");
            return;
        }
        _coins -= price;
        UpdateCoins();
        Debug.Log($"[StoryboardDemo] Purchased {id} for {price} coins");
        FlashToast($"Bought {label}");
    }

    void BuildInventory()
    {
        var root = NewScreen("Inventory");
        AddBackBar(root, "Inventory");

        var tabs = NewRow(root.transform, "Tabs", y: 380);
        NewButton(tabs, "WeaponsTab",      "Weapons",     onClick: () => FlashToast("Weapons tab"));
        NewButton(tabs, "ConsumablesTab",  "Consumables", onClick: () => FlashToast("Consumables tab"));
        NewButton(tabs, "CosmeticsTab",    "Cosmetics",   onClick: () => FlashToast("Cosmetics tab"));

        string[] weapons = { "Iron_Sword", "Oak_Bow", "Flame_Staff", "Frost_Dagger" };
        var list = NewList(root.transform, "ItemGrid", topY: 220, rowHeight: 120, gap: 10);
        foreach (var w in weapons)
        {
            string id = w;
            string label = w.Replace('_', ' ');
            var row = NewRowPanel(list, id, 120);
            NewLabel(row.transform, "Name", label, 30, TMPro.TextAlignmentOptions.Left,
                new Vector2(32, 0), new Vector2(500, 120), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            bool isEquipped = _equipped.Contains(id);
            NewChildButton(row.transform, "EquipButton", isEquipped ? "Equipped" : "Equip",
                new Vector2(-32, 0), new Vector2(200, 80), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                onClick: () => {
                    if (_equipped.Contains(id)) _equipped.Remove(id); else _equipped.Add(id);
                    Debug.Log($"[StoryboardDemo] Toggle equip: {id}");
                    Navigate(PanelKind.Inventory, pushStack: false);
                });
        }
    }

    void BuildSettings()
    {
        var root = NewScreen("Settings");
        AddBackBar(root, "Settings");

        var list = NewList(root.transform, "Categories", topY: 350, rowHeight: 110, gap: 18);
        NewButton(list, "AudioButton",   "Audio",    icon: "🔊", onClick: () => Navigate(PanelKind.Audio));
        NewButton(list, "VideoButton",   "Video",    icon: "🖥",  onClick: () => Navigate(PanelKind.Video));
        NewButton(list, "AccountButton", "Account",  icon: "🪪",  onClick: () => Navigate(PanelKind.Account));
    }

    void BuildAudio()
    {
        var root = NewScreen("Audio");
        AddBackBar(root, "Audio Settings");

        var list = NewList(root.transform, "AudioOptions", topY: 300, rowHeight: 110, gap: 20);
        AddToggleRow(list, "MusicToggle", "Music",
            () => { Debug.Log("[StoryboardDemo] Music toggled"); });
        AddToggleRow(list, "SFXToggle", "Sound effects",
            () => { Debug.Log("[StoryboardDemo] SFX toggled"); });
        AddToggleRow(list, "VoiceToggle", "Voice chat",
            () => { Debug.LogWarning("[StoryboardDemo] Voice chat feature not implemented"); });
    }

    void BuildVideo()
    {
        var root = NewScreen("Video");
        AddBackBar(root, "Video Settings");

        var list = NewList(root.transform, "VideoOptions", topY: 300, rowHeight: 110, gap: 20);
        AddToggleRow(list, "VSyncToggle", "VSync",
            () => { Debug.Log("[StoryboardDemo] VSync toggled"); });
        AddToggleRow(list, "FullscreenToggle", "Fullscreen",
            () => { Debug.Log("[StoryboardDemo] Fullscreen toggled"); });
        AddToggleRow(list, "HDRToggle", "HDR",
            () => { Debug.LogError("[StoryboardDemo] HDR requires hardware support — not available on this device"); });
    }

    void BuildAccount()
    {
        var root = NewScreen("Account");
        AddBackBar(root, "Account");

        NewLabel(root.transform, "UsernameLabel", "Logged in as: tester@oddgames.com", 28,
            TMPro.TextAlignmentOptions.Left, new Vector2(64, 400), new Vector2(900, 60),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), centerPivot: true);

        var list = NewList(root.transform, "AccountActions", topY: 300, rowHeight: 110, gap: 18);
        NewButton(list, "SignOutButton", "Sign out", tinted: new Color(0.35f, 0.25f, 0.25f),
            onClick: () => {
                Debug.LogWarning("[StoryboardDemo] Sign-out requested");
                FlashToast("Signed out (simulated)");
            });
        NewButton(list, "DeleteAccountButton", "Delete account", tinted: new Color(0.5f, 0.2f, 0.2f),
            onClick: () => {
                Debug.LogError("[StoryboardDemo] Delete account failed — server endpoint not implemented");
                FlashToast("Delete failed");
            });
    }

    void BuildProfile()
    {
        var root = NewScreen("Profile");
        AddBackBar(root, "Profile");

        NewLabel(root.transform, "PlayerName", "tester", 48, TMPro.TextAlignmentOptions.Center,
            new Vector2(0, 460), new Vector2(900, 80), centerPivot: true);
        NewLabel(root.transform, "PlayerLevel", "Level 12 · 4,320 XP", 28, TMPro.TextAlignmentOptions.Center,
            new Vector2(0, 390), new Vector2(900, 60), centerPivot: true);

        var list = NewList(root.transform, "ProfileActions", topY: 260, rowHeight: 110, gap: 18);
        NewButton(list, "AchievementsButton", "Achievements",
            onClick: () => FlashToast("Achievements not wired"));
        NewButton(list, "ResetProgressButton", "Reset progress",
            tinted: new Color(0.45f, 0.25f, 0.2f),
            onClick: () => {
                Debug.LogWarning("[StoryboardDemo] Reset progress — this is destructive");
                FlashToast("Progress reset (simulated)");
            });
    }

    void BuildDebug()
    {
        var root = NewScreen("Debug");
        AddBackBar(root, "Dev Menu — Crash Triggers");

        NewLabel(root.transform, "Hint",
            "Tap a trigger below. The storyboard rail should show the\n" +
            "menu path you walked here plus any logs + the crash itself.",
            22, TMPro.TextAlignmentOptions.Center,
            new Vector2(0, 460), new Vector2(900, 80), centerPivot: true);

        var list = NewList(root.transform, "CrashTriggers", topY: 350, rowHeight: 110, gap: 16);
        NewButton(list, "ReportButton", "Send Bug Report",
            onClick: () => {
                Bugpunch.Report("Storyboard demo report", "Filed from the demo's Dev Menu");
                FlashToast("Report sent");
            });
        NewButton(list, "FeedbackButton", "Send Feedback",
            onClick: () => {
                Bugpunch.Feedback("Storyboard demo feedback");
                FlashToast("Feedback sent");
            });
        NewButton(list, "ExceptionButton", "Throw C# Exception",
            tinted: new Color(0.4f, 0.2f, 0.2f),
            onClick: () => {
                var go = new GameObject("Thrower");
                go.AddComponent<DeferredThrower>();
            });
        NewButton(list, "NullDerefButton", "Native Crash — Null Deref",
            tinted: new Color(0.5f, 0.15f, 0.15f),
            onClick: () => {
                var go = new GameObject("NullDeref");
                go.AddComponent<DeferredNullDeref>();
            });
        NewButton(list, "StackOverflowButton", "Native Crash — Stack Overflow",
            tinted: new Color(0.5f, 0.15f, 0.15f),
            onClick: () => {
                var go = new GameObject("StackOverflow");
                go.AddComponent<DeferredStackOverflow>();
            });
        NewButton(list, "AbortButton", "Native Crash — Abort",
            tinted: new Color(0.5f, 0.15f, 0.15f),
            onClick: () => {
                var go = new GameObject("Abort");
                go.AddComponent<DeferredAbort>();
            });
    }

    // ── UI primitives ──

    GameObject NewScreen(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_panelHost, false);
        Stretch(go.AddComponent<RectTransform>());
        return go;
    }

    GameObject NewPanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>().color = color;
        return go;
    }

    GameObject NewRowPanel(Transform parent, string name, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(960, height);
        go.AddComponent<Image>().color = new Color(0.17f, 0.19f, 0.25f, 1f);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return go;
    }

    RectTransform NewList(Transform parent, string name, float topY, float rowHeight, float gap)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1);
        rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -topY + 960);
        rt.sizeDelta = new Vector2(980, 1400);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.childControlHeight = false;
        v.childControlWidth = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.spacing = gap;
        return rt;
    }

    RectTransform NewRow(Transform parent, string name, float y)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(980, 110);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlHeight = false;
        h.childControlWidth = false;
        h.spacing = 20;
        return rt;
    }

    void AddBackBar(GameObject screen, string title)
    {
        var bar = NewRow(screen.transform, "BackBar", y: 820);
        NewButton(bar, "BackButton", "‹ Back", tinted: new Color(0.2f, 0.22f, 0.28f),
            onClick: GoBack);
        NewLabel(bar, "Title", title, 40, TMPro.TextAlignmentOptions.Left,
            Vector2.zero, new Vector2(640, 80));
    }

    void NewButton(RectTransform parent, string name, string label, string icon = null,
                   Color? tinted = null, Action onClick = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(860, 100);
        var img = go.AddComponent<Image>();
        img.color = tinted ?? new Color(0.22f, 0.3f, 0.45f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        go.AddComponent<LayoutElement>().preferredHeight = 100;

        var text = icon != null ? $"{icon}  {label}" : label;
        NewLabel(go.transform, "Label", text, 34, TMPro.TextAlignmentOptions.Center,
            Vector2.zero, new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 1), stretch: true);
    }

    void NewChildButton(Transform parent, string name, string label,
                        Vector2 anchoredPos, Vector2 size, Vector2 anchorMin, Vector2 anchorMax,
                        Action onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(anchorMin.x, anchorMin.y);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.55f, 0.4f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        NewLabel(go.transform, "Label", label, 30, TMPro.TextAlignmentOptions.Center,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, stretch: true);
    }

    void AddToggleRow(RectTransform parent, string name, string label, Action onChange)
    {
        var row = NewRowPanel(parent, name, 100);
        NewLabel(row.transform, "Label", label, 30, TMPro.TextAlignmentOptions.Left,
            new Vector2(32, 0), new Vector2(600, 100), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        bool on = true;
        var toggleGo = new GameObject("ToggleButton");
        toggleGo.transform.SetParent(row.transform, false);
        var rt = toggleGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-32, 0);
        rt.sizeDelta = new Vector2(120, 60);
        var img = toggleGo.AddComponent<Image>();
        img.color = new Color(0.25f, 0.5f, 0.35f, 1f);
        var labelTmp = NewLabel(toggleGo.transform, "Label", "ON", 26,
            TMPro.TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, stretch: true);
        var btn = toggleGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => {
            on = !on;
            img.color = on ? new Color(0.25f, 0.5f, 0.35f, 1f) : new Color(0.4f, 0.25f, 0.25f, 1f);
            labelTmp.text = on ? "ON" : "OFF";
            onChange?.Invoke();
        });
    }

    TMPro.TextMeshProUGUI NewLabel(Transform parent, string name, string text, int size,
                                   TMPro.TextAlignmentOptions align,
                                   Vector2 anchoredPos, Vector2 sizeDelta,
                                   Vector2 anchorMin = default, Vector2 anchorMax = default,
                                   bool centerPivot = false, bool stretch = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = anchorMin == default ? new Vector2(0.5f, 0.5f) : anchorMin;
            rt.anchorMax = anchorMax == default ? new Vector2(0.5f, 0.5f) : anchorMax;
            rt.pivot = centerPivot ? new Vector2(0.5f, 0.5f) : new Vector2(anchorMin.x, anchorMin.y);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
        }
        var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = align;
        tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ── UX helpers ──

    void UpdateCoins() { if (_coinsLabel != null) _coinsLabel.text = $"{_coins} 🪙"; }

    void FlashToast(string message)
    {
        var go = new GameObject("Toast");
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 200);
        rt.sizeDelta = new Vector2(720, 90);
        go.AddComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        NewLabel(go.transform, "Label", message, 28, TMPro.TextAlignmentOptions.Center,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, stretch: true);
        Destroy(go, 1.8f);
    }

    // ── Deferred crash helpers — isolated from Update frame of the menu ──

    class DeferredThrower : MonoBehaviour
    {
        void Update()
        {
            Destroy(gameObject);
            throw new InvalidOperationException(
                "Bugpunch test: synthetic exception fired from the storyboard demo");
        }
    }

    class DeferredNullDeref : MonoBehaviour
    {
        void Update() { Marshal.WriteInt32(IntPtr.Zero, 0); }
    }

    class DeferredStackOverflow : MonoBehaviour
    {
        void Update() { Recurse(0); }
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Recurse(int depth)
        {
            var pad = new byte[256]; pad[0] = (byte)(depth & 0xFF);
            return Recurse(depth + 1) + pad[0];
        }
    }

    class DeferredAbort : MonoBehaviour
    {
        [DllImport("c")] static extern void abort();
        void Update() { abort(); }
    }
}
