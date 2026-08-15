using UnityEngine;

[CreateAssetMenu(menuName = "Retry/Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField] private string itemId;
    [SerializeField] private string displayName;
    [SerializeField] private ItemType itemType = ItemType.Material;
    [SerializeField] private Sprite icon;
    [SerializeField] private Sprite slotBackground;
    [SerializeField] private Color fallbackIconColor = Color.white;
    [SerializeField, Min(1)] private int maxStack = 1;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public ItemType ItemType => itemType;
    public Sprite Icon => icon;
    public Sprite SlotBackground => slotBackground;
    public Color FallbackIconColor => fallbackIconColor;
    public int MaxStack => maxStack;
}
