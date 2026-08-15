using UnityEngine;
using UnityEngine.UI;

public static class GameUIVisualPolish
{
    private static readonly Color PanelTop = new Color(0.12f, 0.105f, 0.085f, 0.96f);
    private static readonly Color PanelBottom = new Color(0.025f, 0.028f, 0.035f, 0.94f);
    private static readonly Color SlotTop = new Color(0.19f, 0.18f, 0.15f, 0.98f);
    private static readonly Color SlotBottom = new Color(0.055f, 0.06f, 0.072f, 0.98f);
    private static readonly Color ButtonTop = new Color(0.44f, 0.32f, 0.13f, 1f);
    private static readonly Color ButtonBottom = new Color(0.16f, 0.105f, 0.055f, 1f);
    private static readonly Color RedFillTop = new Color(1f, 0.2f, 0.17f, 1f);
    private static readonly Color RedFillBottom = new Color(0.45f, 0.02f, 0.015f, 1f);
    private static readonly Color Gold = new Color(1f, 0.76f, 0.32f, 1f);
    private static readonly Color SoftGold = new Color(1f, 0.86f, 0.58f, 1f);
    private static readonly Color TextMain = new Color(0.94f, 0.91f, 0.82f, 1f);
    private static readonly Color MutedText = new Color(0.72f, 0.68f, 0.58f, 1f);

    public static void ApplyTo(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        ConfigureCanvas(canvas);
        ApplyTo(canvas.transform);
    }

    public static void ApplyTo(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            StyleImage(images[i]);
        }

        Slider[] sliders = root.GetComponentsInChildren<Slider>(true);
        for (int i = 0; i < sliders.Length; i++)
        {
            StyleSlider(sliders[i]);
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            StyleButton(buttons[i]);
        }

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            StyleText(texts[i]);
        }
    }

    public static void ApplyToSlot(Transform slotRoot)
    {
        if (slotRoot == null)
        {
            return;
        }

        Image[] images = slotRoot.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            StyleImage(images[i]);
        }

        Text[] texts = slotRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            StyleText(texts[i]);
        }
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        if (canvas.renderMode == RenderMode.WorldSpace)
        {
            return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            return;
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static void StyleImage(Image image)
    {
        if (image == null)
        {
            return;
        }

        string objectName = image.gameObject.name.ToLowerInvariant();
        if (IsIcon(objectName))
        {
            image.raycastTarget = false;
            return;
        }

        if (objectName.Contains("selected"))
        {
            image.color = new Color(1f, 0.78f, 0.24f, 0.62f);
            EnsureOutline(image, new Color(1f, 0.88f, 0.36f, 0.82f), new Vector2(2f, -2f));
            return;
        }

        if (objectName.Contains("fill"))
        {
            image.color = Color.white;
            EnsureGradient(image, RedFillTop, RedFillBottom);
            return;
        }

        if (objectName.Contains("slot"))
        {
            image.color = Color.white;
            EnsureGradient(image, SlotTop, SlotBottom);
            EnsureOutline(image, new Color(0.7f, 0.52f, 0.22f, 0.72f), new Vector2(1f, -1f));
            return;
        }

        if (objectName.Contains("panel") || objectName.Contains("window") || objectName.Contains("background"))
        {
            image.color = Color.white;
            EnsureGradient(image, PanelTop, PanelBottom);
            EnsureOutline(image, new Color(0.78f, 0.58f, 0.24f, 0.52f), new Vector2(2f, -2f));
            EnsureShadow(image, new Color(0f, 0f, 0f, 0.58f), new Vector2(6f, -6f));
        }
    }

    private static void StyleSlider(Slider slider)
    {
        if (slider == null)
        {
            return;
        }

        Image[] images = slider.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null)
            {
                continue;
            }

            bool isFill = slider.fillRect != null && image.transform == slider.fillRect;
            if (isFill || image.gameObject.name.ToLowerInvariant().Contains("fill"))
            {
                image.color = Color.white;
                EnsureGradient(image, RedFillTop, RedFillBottom);
                continue;
            }

            image.color = new Color(0.045f, 0.04f, 0.035f, 0.92f);
            EnsureOutline(image, new Color(0.75f, 0.56f, 0.25f, 0.52f), new Vector2(1f, -1f));
        }
    }

    private static void StyleButton(Button button)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = Color.white;
            EnsureGradient(image, ButtonTop, ButtonBottom);
            EnsureOutline(image, new Color(1f, 0.78f, 0.28f, 0.65f), new Vector2(1.5f, -1.5f));
            EnsureShadow(image, new Color(0f, 0f, 0f, 0.45f), new Vector2(3f, -3f));
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.92f, 0.62f, 1f);
        colors.pressedColor = new Color(0.72f, 0.48f, 0.18f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.25f, 0.22f, 0.18f, 0.6f);
        button.colors = colors;
    }

    private static void StyleText(Text text)
    {
        if (text == null)
        {
            return;
        }

        string objectName = text.gameObject.name.ToLowerInvariant();
        bool isTitle = objectName.Contains("title") || objectName.Contains("header");
        bool isCount = objectName.Contains("count");

        text.color = isTitle ? Gold : isCount ? SoftGold : TextMain;
        text.fontStyle = isTitle ? FontStyle.Bold : text.fontStyle;
        text.supportRichText = true;

        EnsureShadow(text, new Color(0f, 0f, 0f, 0.82f), isTitle ? new Vector2(2f, -2f) : new Vector2(1.5f, -1.5f));

        if (isTitle)
        {
            EnsureOutline(text, new Color(0.12f, 0.07f, 0.025f, 0.9f), new Vector2(1.2f, -1.2f));
        }
        else if (!isCount)
        {
            EnsureOutline(text, new Color(0f, 0f, 0f, 0.55f), new Vector2(0.8f, -0.8f));
        }

        if (objectName.Contains("detail") || objectName.Contains("name"))
        {
            text.color = MutedText;
        }
    }

    private static void EnsureGradient(Image image, Color top, Color bottom)
    {
        GameUIGradient gradient = image.GetComponent<GameUIGradient>();
        if (gradient == null)
        {
            gradient = image.gameObject.AddComponent<GameUIGradient>();
        }

        gradient.SetColors(top, bottom);
    }

    private static void EnsureOutline(Graphic graphic, Color color, Vector2 distance)
    {
        Outline outline = graphic.GetComponent<Outline>();
        if (outline == null)
        {
            outline = graphic.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    private static void EnsureShadow(Graphic graphic, Color color, Vector2 distance)
    {
        Shadow[] shadows = graphic.GetComponents<Shadow>();
        Shadow shadow = null;
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] != null && shadows[i].GetType() == typeof(Shadow))
            {
                shadow = shadows[i];
                break;
            }
        }

        if (shadow == null)
        {
            shadow = graphic.gameObject.AddComponent<Shadow>();
        }

        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static bool IsIcon(string objectName)
    {
        return objectName.Contains("icon") ||
               objectName.Contains("heart") ||
               objectName.Contains("image") && !objectName.Contains("background");
    }
}
