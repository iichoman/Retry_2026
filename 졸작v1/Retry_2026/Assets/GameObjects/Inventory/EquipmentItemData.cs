using UnityEngine;

[CreateAssetMenu(menuName = "Retry/Inventory/Equipment Item Data")]
public class EquipmentItemData : ItemData
{
    [SerializeField] private EquipmentSlotType equipmentSlot = EquipmentSlotType.Weapon;
    [SerializeField] private string weaponId;
    [SerializeField] private int maxHpBonus;
    [SerializeField] private int attackBonus;
    [SerializeField] private int defenseBonus;

    public EquipmentSlotType EquipmentSlot => equipmentSlot;
    public string WeaponId => weaponId;
    public int MaxHpBonus => maxHpBonus;
    public int AttackBonus => attackBonus;
    public int DefenseBonus => defenseBonus;
    public bool ChangesWeapon => equipmentSlot == EquipmentSlotType.Weapon && !string.IsNullOrWhiteSpace(weaponId);
}
