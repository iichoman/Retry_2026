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
    [SerializeField, TextArea(2, 5)] private string description;
    [SerializeField] private ItemRarity rarity;
    [SerializeField, Min(0)] private int extractionValue;
    [SerializeField] private bool bossExclusive;
    [SerializeField] private bool questOnly;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public ItemType ItemType => itemType;
    public Sprite Icon => icon;
    public Sprite SlotBackground => slotBackground;
    public Color FallbackIconColor => fallbackIconColor;
    public int MaxStack => itemType == ItemType.Consumable ? 16 : Mathf.Max(1, maxStack);
    public string Description => itemType == ItemType.Weapon && ABItemDefinitions.Find(ItemHash.Of(itemId)) is var weapon && weapon != null
        ? EquipmentPresentation.WeaponDescription(weapon) : description ?? string.Empty;
    public ItemRarity Rarity => rarity;
    public int ExtractionValue => Mathf.Max(0, extractionValue);
    public bool BossExclusive => bossExclusive;
    public bool QuestOnly => questOnly;
    public Color RarityColor => ColorFor(rarity);
    public string RarityName => rarity switch
    {
        ItemRarity.Common => "일반", ItemRarity.Uncommon => "고급", ItemRarity.Rare => "희귀",
        ItemRarity.Epic => "영웅", ItemRarity.Boss => "보스 전용", _ => "일반"
    };
    public string TypeName => itemType switch
    {
        ItemType.Passive => "패시브", ItemType.Active => "액티브", ItemType.Consumable => "소모품", ItemType.Weapon => "무기", ItemType.Armor => "방어구",
        ItemType.Material => "재료", ItemType.Quest => "퀘스트", ItemType.Currency => "재화", _ => "아이템"
    };
    public static Color ColorFor(ItemRarity value) => value switch
    {
        ItemRarity.Uncommon => new Color(0.3f, 0.9f, 0.45f),
        ItemRarity.Rare => new Color(0.3f, 0.65f, 1f),
        ItemRarity.Epic => new Color(0.8f, 0.4f, 1f),
        ItemRarity.Boss => new Color(1f, 0.65f, 0.15f),
        _ => new Color(0.8f, 0.83f, 0.87f)
    };
    private void OnValidate() { maxStack = Mathf.Max(1, maxStack); extractionValue = Mathf.Max(0, extractionValue); }
}

public enum ItemRarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Boss = 4 }
