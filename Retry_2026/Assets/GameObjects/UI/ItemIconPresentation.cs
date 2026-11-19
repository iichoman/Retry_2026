using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared icon fallback for inventory, local/server loot and extraction results.</summary>
public static class ItemIconPresentation
{
    public const string FallbackResource = "UI/ItemIcons/item_unknown";
    private static Sprite fallback;

    public static Sprite Fallback
    {
        get
        {
            if (fallback == null) fallback = Resources.Load<Sprite>(FallbackResource);
            return fallback;
        }
    }

    public static void Apply(Image image, ItemData item, UITextureSet textureSet = null)
    {
        if (image == null) return;
        Sprite sprite = item != null ? item.Icon : null;
        if (sprite == null && textureSet != null) sprite = textureSet.missingIconSprite;
        if (sprite == null) sprite = Fallback;
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : item != null ? item.FallbackIconColor : Color.gray;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }
}
