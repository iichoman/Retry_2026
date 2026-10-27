using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class MonsterHealthBarBuilder
{
    private const string HealthBarName = "Monster HP Bar";

    [MenuItem("Tools/Retry/Add Monster HP Bar To Selected")]
    public static void AddHealthBarToSelected()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("Select a monster GameObject or prefab instance first.");
            return;
        }

        Monster_State monsterState = selected.GetComponent<Monster_State>();
        if (monsterState == null)
        {
            monsterState = selected.GetComponentInParent<Monster_State>();
        }

        if (monsterState == null)
        {
            monsterState = selected.GetComponentInChildren<Monster_State>();
        }

        if (monsterState == null)
        {
            Debug.LogWarning("No Monster_State found on the selected object, parent, or children.", selected);
            return;
        }

        CreateOrReplaceHealthBar(monsterState);
    }

    [MenuItem("Tools/Retry/Add Monster HP Bar To Selected", true)]
    public static bool ValidateAddHealthBarToSelected()
    {
        return Selection.activeGameObject != null;
    }

    private static void CreateOrReplaceHealthBar(Monster_State monsterState)
    {
        Transform monsterTransform = monsterState.transform;
        Transform existing = monsterTransform.Find(HealthBarName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject(HealthBarName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(monsterTransform, false);
        root.transform.localPosition = new Vector3(0f, 2.4f, 0f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        CanvasGroup visibilityGroup = root.AddComponent<CanvasGroup>();

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(180f, 44f);

        GameObject background = CreatePanel("Background", root.transform, new Color(0.02f, 0.02f, 0.02f, 0.78f));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        Stretch(backgroundRect);

        Slider slider = CreateSlider("HP Slider", root.transform);
        RectTransform sliderRect = slider.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(0f, -2f);
        sliderRect.sizeDelta = new Vector2(-18f, 16f);
        slider.interactable = false;

        Text hpText = CreateText("HP Text", root.transform, FindDefaultFont(), string.Empty, 13, TextAnchor.MiddleCenter);
        RectTransform hpTextRect = hpText.rectTransform;
        hpTextRect.anchorMin = Vector2.zero;
        hpTextRect.anchorMax = Vector2.one;
        hpTextRect.offsetMin = Vector2.zero;
        hpTextRect.offsetMax = Vector2.zero;

        MonsterHealthBarUI healthBarUI = root.AddComponent<MonsterHealthBarUI>();
        AssignObject(healthBarUI, "monsterState", monsterState);
        AssignObject(healthBarUI, "root", root);
        AssignObject(healthBarUI, "visibilityGroup", visibilityGroup);
        AssignObject(healthBarUI, "hpSlider", slider);
        AssignObject(healthBarUI, "fillImage", slider.fillRect.GetComponent<Image>());
        AssignObject(healthBarUI, "hpText", hpText);
        AssignBool(healthBarUI, "hideWhenFull", true);
        AssignFloat(healthBarUI, "visibleDurationAfterDamage", 3f);
        AssignBool(healthBarUI, "faceCamera", true);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(monsterState);
        EditorSceneManager.MarkSceneDirty(monsterState.gameObject.scene);
        Selection.activeGameObject = root;
    }

    private static Slider CreateSlider(string name, Transform parent)
    {
        GameObject sliderObject = CreateUIObject(name, parent);
        Slider slider = sliderObject.AddComponent<Slider>();

        GameObject background = CreatePanel("Slider Background", sliderObject.transform, new Color(0.24f, 0.24f, 0.24f, 1f));
        Stretch(background.GetComponent<RectTransform>());

        GameObject fillArea = CreateUIObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(2f, 2f);
        fillAreaRect.offsetMax = new Vector2(-2f, -2f);

        GameObject fill = CreatePanel("Fill", fillArea.transform, new Color(0.82f, 0.08f, 0.08f, 1f));
        Stretch(fill.GetComponent<RectTransform>());

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = fill.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        return slider;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    private static void AssignFloat(Object target, string propertyName, float value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.floatValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
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
