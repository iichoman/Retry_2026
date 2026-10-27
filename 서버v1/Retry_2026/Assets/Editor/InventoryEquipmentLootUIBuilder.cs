using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class InventoryEquipmentLootUIBuilder
{
    private const string EquipmentPanelName = "Equipment Panel";
    private const string LootPanelName = "Loot Panel";
    private const string InventoryPanelName = "Inventory Panel";

    [MenuItem("Tools/Retry/Create Inventory Equipment Loot UI")]
    public static void CreateInventoryEquipmentLootUI()
    {
        Canvas canvas = FindOrCreateCanvas();
        Font font = FindDefaultFont();

        InventoryUI inventoryUI = FindOrCreateInventoryPanel(canvas.transform, font);
        EquipmentUI equipmentUI = CreateEquipmentPanel(canvas.transform, font);
        LootUI lootUI = CreateLootPanel(canvas.transform, font, inventoryUI);
        GameUIController gameUIController = FindOrCreateGameUIController(canvas.gameObject);

        WireGameUIController(gameUIController, inventoryUI, equipmentUI, lootUI);
        EnsureEventSystem();

        Selection.activeGameObject = canvas.gameObject;
        EditorUtility.SetDirty(canvas.gameObject);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
    }

    private static InventoryUI FindOrCreateInventoryPanel(Transform canvasTransform, Font font)
    {
        Transform existing = canvasTransform.Find(InventoryPanelName);
        if (existing != null && existing.TryGetComponent(out InventoryUI existingInventoryUI))
        {
            AssignObject(existingInventoryUI, "slotPrefab", FindSlotPrefab());
            EnsureInventoryDetailText(existingInventoryUI, existing, font);
            return existingInventoryUI;
        }

        GameObject panel = CreatePanel(InventoryPanelName, canvasTransform, new Color(0.07f, 0.08f, 0.09f, 0.94f));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(24f, 0f);
        rect.sizeDelta = new Vector2(470f, -96f);

        Text title = CreateText("Title", panel.transform, font, "인벤토리", 26, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, 0f, 1f, 1f, 1f, 20f, -54f, -20f, -12f);

        Transform slotRoot = CreateGridRoot("Slot Grid", panel.transform, 4, new Vector2(92f, 92f), new Vector2(10f, 10f));
        SetRect(slotRoot.GetComponent<RectTransform>(), 0f, 0.28f, 1f, 1f, 20f, 0f, -20f, -70f);

        Text detail = CreateText("Item Detail", panel.transform, font, string.Empty, 18, TextAnchor.UpperLeft);
        SetRect(detail.rectTransform, 0f, 0f, 1f, 0.28f, 20f, 18f, -20f, -12f);

        InventoryUI inventoryUI = panel.AddComponent<InventoryUI>();
        AssignObject(inventoryUI, "panelRoot", panel);
        AssignObject(inventoryUI, "panelBackground", panel.GetComponent<Image>());
        AssignObject(inventoryUI, "itemDetailText", detail);
        AssignObject(inventoryUI, "slotRoot", slotRoot);
        AssignObject(inventoryUI, "slotPrefab", FindSlotPrefab());
        AssignBool(inventoryUI, "visibleOnStart", false);

        panel.SetActive(false);
        return inventoryUI;
    }

    private static EquipmentUI CreateEquipmentPanel(Transform canvasTransform, Font font)
    {
        RemoveExisting(canvasTransform, EquipmentPanelName);

        GameObject panel = CreatePanel(EquipmentPanelName, canvasTransform, new Color(0.06f, 0.07f, 0.08f, 0.94f));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-24f, 0f);
        rect.sizeDelta = new Vector2(360f, -96f);

        Text title = CreateText("Title", panel.transform, font, "장비", 26, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, 0f, 1f, 1f, 1f, 20f, -54f, -20f, -12f);

        var slotViews = new List<EquipmentSlotUI>();
        EquipmentSlotType[] slotTypes =
        {
            EquipmentSlotType.Weapon,
            EquipmentSlotType.Helmet,
            EquipmentSlotType.Chest,
            EquipmentSlotType.Gloves,
            EquipmentSlotType.Boots,
            EquipmentSlotType.Accessory
        };

        for (int i = 0; i < slotTypes.Length; i++)
        {
            EquipmentSlotUI slotView = CreateEquipmentSlot(slotTypes[i], panel.transform, font);
            RectTransform slotRect = slotView.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(1f, 1f);
            slotRect.pivot = new Vector2(0.5f, 1f);
            slotRect.anchoredPosition = new Vector2(0f, -76f - i * 76f);
            slotRect.sizeDelta = new Vector2(-40f, 64f);
            slotViews.Add(slotView);
        }

        EquipmentUI equipmentUI = panel.AddComponent<EquipmentUI>();
        AssignObject(equipmentUI, "root", panel);
        AssignObjectList(equipmentUI, "slotViews", slotViews);
        AssignBool(equipmentUI, "visibleOnStart", false);

        panel.SetActive(false);
        return equipmentUI;
    }

    private static LootUI CreateLootPanel(Transform canvasTransform, Font font, InventoryUI inventoryUI)
    {
        RemoveExisting(canvasTransform, LootPanelName);

        GameObject panel = CreatePanel(LootPanelName, canvasTransform, new Color(0f, 0f, 0f, 0.68f));
        Stretch(panel.GetComponent<RectTransform>());

        GameObject window = CreatePanel("Window", panel.transform, new Color(0.08f, 0.09f, 0.11f, 0.97f));
        RectTransform windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(560f, 520f);

        Text title = CreateText("Title", window.transform, font, "전리품", 28, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, 0f, 1f, 1f, 1f, 24f, -58f, -24f, -12f);

        Transform slotRoot = CreateGridRoot("Loot Slot Grid", window.transform, 4, new Vector2(92f, 92f), new Vector2(10f, 10f));
        SetRect(slotRoot.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f, 24f, 92f, -24f, -80f);

        Button takeAllButton = CreateButton("Take All Button", window.transform, font, "모두 획득");
        RectTransform takeAllRect = takeAllButton.GetComponent<RectTransform>();
        takeAllRect.anchorMin = new Vector2(0f, 0f);
        takeAllRect.anchorMax = new Vector2(0f, 0f);
        takeAllRect.pivot = new Vector2(0f, 0f);
        takeAllRect.anchoredPosition = new Vector2(24f, 24f);
        takeAllRect.sizeDelta = new Vector2(180f, 48f);

        Button closeButton = CreateButton("Close Button", window.transform, font, "닫기");
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 0f);
        closeRect.anchorMax = new Vector2(1f, 0f);
        closeRect.pivot = new Vector2(1f, 0f);
        closeRect.anchoredPosition = new Vector2(-24f, 24f);
        closeRect.sizeDelta = new Vector2(180f, 48f);

        LootUI lootUI = panel.AddComponent<LootUI>();
        AssignObject(lootUI, "root", panel);
        AssignObject(lootUI, "titleText", title);
        AssignObject(lootUI, "lootSlotRoot", slotRoot);
        AssignObject(lootUI, "slotPrefab", FindSlotPrefab());
        AssignObject(lootUI, "playerInventoryUI", inventoryUI);
        AssignObject(lootUI, "takeAllButton", takeAllButton);
        AssignObject(lootUI, "closeButton", closeButton);

        panel.SetActive(false);
        return lootUI;
    }

    private static EquipmentSlotUI CreateEquipmentSlot(EquipmentSlotType slotType, Transform parent, Font font)
    {
        GameObject slot = CreatePanel(slotType.ToString(), parent, new Color(0.16f, 0.17f, 0.19f, 1f));
        EquipmentSlotUI slotUI = slot.AddComponent<EquipmentSlotUI>();
        AssignEnum(slotUI, "slotType", (int)slotType);

        GameObject iconObject = CreatePanel("Icon", slot.transform, new Color(0.05f, 0.05f, 0.06f, 1f));
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(8f, 0f);
        iconRect.sizeDelta = new Vector2(48f, 48f);

        Text slotName = CreateText("Slot Name", slot.transform, font, FormatEquipmentSlot(slotType), 17, TextAnchor.MiddleLeft);
        SetRect(slotName.rectTransform, 0f, 0.5f, 0.45f, 1f, 66f, -28f, 0f, 0f);

        Text itemName = CreateText("Item Name", slot.transform, font, string.Empty, 16, TextAnchor.MiddleLeft);
        SetRect(itemName.rectTransform, 0.45f, 0f, 1f, 1f, 0f, 0f, -10f, 0f);

        AssignObject(slotUI, "iconImage", iconObject.GetComponent<Image>());
        AssignObject(slotUI, "slotNameText", slotName);
        AssignObject(slotUI, "itemNameText", itemName);
        return slotUI;
    }

    private static void EnsureInventoryDetailText(InventoryUI inventoryUI, Transform inventoryPanel, Font font)
    {
        SerializedObject serializedObject = new SerializedObject(inventoryUI);
        SerializedProperty detailProperty = serializedObject.FindProperty("itemDetailText");
        if (detailProperty == null || detailProperty.objectReferenceValue != null)
        {
            return;
        }

        Text detail = CreateText("Item Detail", inventoryPanel, font, string.Empty, 18, TextAnchor.UpperLeft);
        SetRect(detail.rectTransform, 0f, 0f, 1f, 0.28f, 20f, 18f, -20f, -12f);
        detailProperty.objectReferenceValue = detail;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(inventoryUI);
    }

    private static GameUIController FindOrCreateGameUIController(GameObject canvasObject)
    {
        GameUIController controller = canvasObject.GetComponent<GameUIController>();
        if (controller == null)
        {
            controller = canvasObject.AddComponent<GameUIController>();
        }

        return controller;
    }

    private static void WireGameUIController(GameUIController controller, InventoryUI inventoryUI, EquipmentUI equipmentUI, LootUI lootUI)
    {
        AssignObject(controller, "playerInput", Object.FindFirstObjectByType<Defalult_Input>());
        AssignObject(controller, "inventoryUI", inventoryUI);
        AssignObject(controller, "equipmentUI", equipmentUI);
        AssignObject(controller, "lootUI", lootUI);

        if (inventoryUI != null)
        {
            AssignObject(inventoryUI, "inventory", Object.FindFirstObjectByType<PlayerInventory>());
            AssignObject(inventoryUI, "playerEquipment", Object.FindFirstObjectByType<PlayerEquipment>());
        }

        if (equipmentUI != null)
        {
            AssignObject(equipmentUI, "playerEquipment", Object.FindFirstObjectByType<PlayerEquipment>());
        }
    }

    private static Canvas FindOrCreateCanvas()
    {
        Canvas selectedCanvas = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<Canvas>()
            : null;

        if (selectedCanvas != null)
        {
            return selectedCanvas;
        }

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

    private static string FormatEquipmentSlot(EquipmentSlotType slotType)
    {
        return slotType switch
        {
            EquipmentSlotType.Weapon => "무기",
            EquipmentSlotType.Helmet => "투구",
            EquipmentSlotType.Chest => "갑옷",
            EquipmentSlotType.Gloves => "장갑",
            EquipmentSlotType.Boots => "신발",
            EquipmentSlotType.Accessory => "장신구",
            _ => slotType.ToString()
        };
    }

    private static InventorySlotUI FindSlotPrefab()
    {
        string[] guids = AssetDatabase.FindAssets("Slot t:Prefab", new[] { "Assets/GameObjects/UI/Slot" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            InventorySlotUI prefab = AssetDatabase.LoadAssetAtPath<InventorySlotUI>(path);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
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

    private static Button CreateButton(string name, Transform parent, Font font, string label)
    {
        GameObject buttonObject = CreatePanel(name, parent, new Color(0.22f, 0.46f, 0.92f, 1f));
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();

        Text text = CreateText("Text", buttonObject.transform, font, label, 20, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        return button;
    }

    private static Transform CreateGridRoot(string name, Transform parent, int columns, Vector2 cellSize, Vector2 spacing)
    {
        GameObject root = CreateUIObject(name, parent);
        GridLayoutGroup grid = root.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.cellSize = cellSize;
        grid.spacing = spacing;
        grid.childAlignment = TextAnchor.UpperLeft;
        return root.transform;
    }

    private static void AssignObject(Object target, string propertyName, Object value)
    {
        if (target == null)
        {
            return;
        }

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
        if (target == null)
        {
            return;
        }

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

    private static void AssignEnum(Object target, string propertyName, int value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.enumValueIndex = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void AssignObjectList<T>(Object target, string propertyName, List<T> values) where T : Object
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray)
        {
            return;
        }

        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

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
