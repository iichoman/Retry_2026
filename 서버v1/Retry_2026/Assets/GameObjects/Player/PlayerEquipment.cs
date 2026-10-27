using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerEquipment : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Player_Attack playerAttack;
    [SerializeField] private bool applyStartingLoadout = true;
    [SerializeField] private EquipmentItemData startingWeaponItem;
    [SerializeField] private List<EquipmentSlot> slots = new List<EquipmentSlot>();

    public event Action<PlayerEquipment> EquipmentChanged;

    public IReadOnlyList<EquipmentSlot> Slots => slots;
    public int TotalMaxHpBonus { get; private set; }
    public int TotalAttackBonus { get; private set; }
    public int TotalDefenseBonus { get; private set; }

    private bool startingLoadoutApplied;

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        if (playerAttack == null)
        {
            playerAttack = GetComponent<Player_Attack>();
        }

        EnsureSlots();
        RecalculateBonuses();
    }

    private void Start()
    {
        ApplyStartingLoadout();
    }

    private void OnValidate()
    {
        EnsureSlots();
        RecalculateBonuses();
    }

    public bool TryEquipFromInventory(int inventorySlotIndex)
    {
        if (inventory == null || !inventory.IsValidSlotIndex(inventorySlotIndex))
        {
            return false;
        }

        InventorySlot inventorySlot = inventory.GetSlot(inventorySlotIndex);
        if (inventorySlot == null || inventorySlot.IsEmpty || inventorySlot.item is not EquipmentItemData equipmentItem)
        {
            return false;
        }

        return TryEquip(equipmentItem, inventorySlotIndex);
    }

    public bool TryEquip(EquipmentItemData equipmentItem, int sourceInventorySlotIndex = -1)
    {
        if (equipmentItem == null)
        {
            return false;
        }

        EnsureSlots();
        EquipmentSlot equipmentSlot = GetOrCreateSlot(equipmentItem.EquipmentSlot);
        EquipmentItemData previousItem = equipmentSlot.item;

        if (equipmentItem.ChangesWeapon && playerAttack != null && !playerAttack.EquipWeapon(equipmentItem.WeaponId))
        {
            return false;
        }

        if (inventory != null && sourceInventorySlotIndex >= 0)
        {
            if (!inventory.TryRemoveAt(sourceInventorySlotIndex, 1))
            {
                return false;
            }
        }

        equipmentSlot.item = equipmentItem;

        if (previousItem != null && inventory != null && (previousItem != equipmentItem || sourceInventorySlotIndex >= 0))
        {
            inventory.TryAdd(previousItem, 1);
        }

        RecalculateBonuses();
        EquipmentChanged?.Invoke(this);
        return true;
    }

    private void ApplyStartingLoadout()
    {
        if (!applyStartingLoadout || startingLoadoutApplied)
        {
            return;
        }

        startingLoadoutApplied = true;

        if (startingWeaponItem != null)
        {
            TryEquip(startingWeaponItem);
        }

    }

    public bool TryUnequip(EquipmentSlotType slotType)
    {
        EquipmentSlot equipmentSlot = GetSlot(slotType);
        if (equipmentSlot == null || equipmentSlot.item == null)
        {
            return false;
        }

        if (inventory != null && !inventory.TryAdd(equipmentSlot.item, 1))
        {
            return false;
        }

        equipmentSlot.item = null;
        RecalculateBonuses();
        EquipmentChanged?.Invoke(this);
        return true;
    }

    public EquipmentItemData GetEquippedItem(EquipmentSlotType slotType)
    {
        EquipmentSlot slot = GetSlot(slotType);
        return slot != null ? slot.item : null;
    }

    private EquipmentSlot GetOrCreateSlot(EquipmentSlotType slotType)
    {
        EquipmentSlot slot = GetSlot(slotType);
        if (slot != null)
        {
            return slot;
        }

        slot = new EquipmentSlot(slotType);
        slots.Add(slot);
        return slot;
    }

    private EquipmentSlot GetSlot(EquipmentSlotType slotType)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].slotType == slotType)
            {
                return slots[i];
            }
        }

        return null;
    }

    private void EnsureSlots()
    {
        if (slots == null)
        {
            slots = new List<EquipmentSlot>();
        }

        Array slotTypes = Enum.GetValues(typeof(EquipmentSlotType));
        for (int i = 0; i < slotTypes.Length; i++)
        {
            EquipmentSlotType slotType = (EquipmentSlotType)slotTypes.GetValue(i);
            GetOrCreateSlot(slotType);
        }
    }

    private void RecalculateBonuses()
    {
        TotalMaxHpBonus = 0;
        TotalAttackBonus = 0;
        TotalDefenseBonus = 0;

        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            EquipmentItemData item = slots[i]?.item;
            if (item == null)
            {
                continue;
            }

            TotalMaxHpBonus += item.MaxHpBonus;
            TotalAttackBonus += item.AttackBonus;
            TotalDefenseBonus += item.DefenseBonus;
        }
    }
}

[Serializable]
public class EquipmentSlot
{
    public EquipmentSlotType slotType;
    public EquipmentItemData item;

    public EquipmentSlot(EquipmentSlotType slotType)
    {
        this.slotType = slotType;
    }
}
