using UnityEngine;

[CreateAssetMenu(menuName = "Retry/UI/UI Texture Set")]
public class UITextureSet : ScriptableObject
{
    [Header("Panel")]
    public Sprite panelSprite;
    public Color panelFallbackColor = new Color(0.08f, 0.08f, 0.09f, 0.9f);

    [Header("Inventory Slot")]
    public Sprite slotSprite;
    public Sprite selectedSlotSprite;
    public Color slotFallbackColor = new Color(0.18f, 0.18f, 0.2f, 1f);
    public Color selectedSlotFallbackColor = new Color(0.9f, 0.72f, 0.28f, 1f);

    [Header("HUD")]
    public Sprite hpBarBackgroundSprite;
    public Sprite hpBarFillSprite;
    public Color hpBarBackgroundFallbackColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    public Color hpBarFillFallbackColor = new Color(0.75f, 0.08f, 0.08f, 1f);

    [Header("Icon")]
    public Sprite missingIconSprite;
    public Color missingIconFallbackColor = new Color(0.7f, 0.7f, 0.7f, 1f);
}
