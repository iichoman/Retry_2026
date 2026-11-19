using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class ExitUIBuilder
{
    private const string RootName = "Exit UI";
    private const string HoldPanelName = "Exit Hold Panel";
    private const string ResultPanelName = "Dungeon Result Panel";

    [MenuItem("Tools/Retry/Create Exit UI")]
    public static void CreateExitUI()
    {
        Canvas canvas = FindOrCreateCanvas();
        RemoveExisting(canvas.transform, RootName);

        Font font = FindDefaultFont();
        GameObject root = CreateUIObject(RootName, canvas.transform);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        GameObject holdPanel = CreateHoldPanel(root.transform, font);
        GameObject resultPanel = CreateResultPanel(root.transform, font, out DungeonResultUI resultUI);

        ExitPortalHoldUI holdUI = root.AddComponent<ExitPortalHoldUI>();
        AssignObject(holdUI, "root", holdPanel);
        AssignObject(holdUI, "messageText", holdPanel.transform.Find("Message").GetComponent<Text>());
        AssignObject(holdUI, "progressSlider", holdPanel.transform.Find("Progress").GetComponent<Slider>());

        WireDungeonExitManager(resultPanel, resultUI);
        EnsureEventSystem();

        Selection.activeGameObject = root;
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(root.scene);
    }

    private static GameObject CreateHoldPanel(Transform parent, Font font)
    {
        GameObject panel = CreatePanel(HoldPanelName, parent, new Color(0f, 0f, 0f, 0.55f));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 96f);
        rect.sizeDelta = new Vector2(420f, 96f);

        Text message = CreateText("Message", panel.transform, font, "탈출까지 7초", 26, TextAnchor.MiddleCenter);
        RectTransform messageRect = message.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0f, 0.35f);
        messageRect.anchorMax = new Vector2(1f, 1f);
        messageRect.offsetMin = new Vector2(18f, 0f);
        messageRect.offsetMax = new Vector2(-18f, -8f);

        Slider progress = CreateSlider("Progress", panel.transform);
        RectTransform progressRect = progress.GetComponent<RectTransform>();
        progressRect.anchorMin = new Vector2(0f, 0f);
        progressRect.anchorMax = new Vector2(1f, 0f);
        progressRect.pivot = new Vector2(0.5f, 0f);
        progressRect.anchoredPosition = new Vector2(0f, 18f);
        progressRect.sizeDelta = new Vector2(-36f, 16f);
        progress.minValue = 0f;
        progress.maxValue = 1f;
        progress.value = 0f;
        progress.interactable = false;

        panel.SetActive(false);
        return panel;
    }

    private static GameObject CreateResultPanel(Transform parent, Font font, out DungeonResultUI resultUI)
    {
        GameObject overlay = CreatePanel(ResultPanelName, parent, new Color(0f, 0f, 0f, 0.72f));
        Stretch(overlay.GetComponent<RectTransform>());

        GameObject window = CreatePanel("Window", overlay.transform, new Color(0.08f, 0.09f, 0.11f, 0.96f));
        RectTransform windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(620f, 560f);

        Text title = CreateText("Title", window.transform, font, "탈출 정산", 34, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, 0f, 1f, 1f, 1f, 0f, -72f, 0f, 0f);

        Text elapsed = CreateText("Elapsed Time", window.transform, font, "진행 시간: 00:00:00", 22, TextAnchor.MiddleLeft);
        SetRect(elapsed.rectTransform, 0f, 1f, 1f, 1f, 44f, -126f, -44f, -84f);

        Text escaped = CreateText("Escaped Players", window.transform, font, "탈출 플레이어: 0", 22, TextAnchor.MiddleLeft);
        SetRect(escaped.rectTransform, 0f, 1f, 1f, 1f, 44f, -172f, -44f, -130f);

        Text playerKills = CreateText("Player Kills", window.transform, font, "잡은 플레이어: 0", 22, TextAnchor.MiddleLeft);
        SetRect(playerKills.rectTransform, 0f, 1f, 1f, 1f, 44f, -218f, -44f, -176f);

        Text monsterKills = CreateText("Monster Kills", window.transform, font, "잡은 몬스터: 0", 22, TextAnchor.MiddleLeft);
        SetRect(monsterKills.rectTransform, 0f, 1f, 1f, 1f, 44f, -264f, -44f, -222f);

        Text gold = CreateText("Gold", window.transform, font, "골드: 0", 22, TextAnchor.MiddleLeft);
        SetRect(gold.rectTransform, 0f, 1f, 1f, 1f, 44f, -310f, -44f, -268f);

        Text items = CreateText("Items", window.transform, font, "획득 아이템: 없음", 20, TextAnchor.UpperLeft);
        SetRect(items.rectTransform, 0f, 0f, 1f, 1f, 44f, 104f, -44f, -328f);

        Button lobbyButton = CreateButton("Lobby Button", window.transform, font, "로비로 이동");
        RectTransform buttonRect = lobbyButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 36f);
        buttonRect.sizeDelta = new Vector2(220f, 58f);

        resultUI = overlay.AddComponent<DungeonResultUI>();
        AssignObject(resultUI, "root", overlay);
        AssignObject(resultUI, "elapsedTimeText", elapsed);
        AssignObject(resultUI, "escapedPlayersText", escaped);
        AssignObject(resultUI, "playerKillsText", playerKills);
        AssignObject(resultUI, "monsterKillsText", monsterKills);
        AssignObject(resultUI, "goldText", gold);
        AssignObject(resultUI, "itemsText", items);
        AssignObject(resultUI, "lobbyButton", lobbyButton);

        overlay.SetActive(false);
        return overlay;
    }

    private static Canvas FindOrCreateCanvas()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            return canvas;
        }

        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = CreateUIObject(name, parent);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        return panel;
    }

    private static Text CreateText(string name, Transform parent, Font font, string value, int size, TextAnchor alignment)
    {
        GameObject obj = CreateUIObject(name, parent);
        Text text = obj.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Slider CreateSlider(string name, Transform parent)
    {
        GameObject sliderObject = CreateUIObject(name, parent);
        Slider slider = sliderObject.AddComponent<Slider>();

        GameObject background = CreatePanel("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.22f));
        Stretch(background.GetComponent<RectTransform>());

        GameObject fillArea = CreateUIObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(2f, 2f);
        fillAreaRect.offsetMax = new Vector2(-2f, -2f);

        GameObject fill = CreatePanel("Fill", fillArea.transform, new Color(0.95f, 0.77f, 0.28f, 1f));
        Stretch(fill.GetComponent<RectTransform>());

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = fill.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Button CreateButton(string name, Transform parent, Font font, string label)
    {
        GameObject buttonObject = CreatePanel(name, parent, new Color(0.22f, 0.46f, 0.92f, 1f));
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();

        Text text = CreateText("Text", buttonObject.transform, font, label, 23, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        return button;
    }

    private static void WireDungeonExitManager(GameObject resultPanel, DungeonResultUI resultUI)
    {
        DungeonExitManager manager = Object.FindFirstObjectByType<DungeonExitManager>();
        if (manager == null)
        {
            return;
        }

        AssignObject(manager, "resultUI", resultUI);
        AssignObject(manager, "resultPanelRoot", resultPanel);
        AssignBool(manager, "showResultPanelOnEscape", true);
        AssignBool(manager, "disablePlayerControlOnEscape", true);
    }

    private static void AssignObject(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void AssignBool(Object target, string propertyName, bool value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.boolValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void RemoveExisting(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    private static Font FindDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }
}
