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
    private NetworkBootstrap server;
    public bool IsServerOwned => server != null || (inventory != null && inventory.IsServerOwned);
    public string LastEquipmentFeedback { get; private set; } = string.Empty;
    public event Action<string, bool> EquipmentFeedback;

    public void InitializeServer(NetworkBootstrap bootstrap)
    {
        UnsubscribeServer();
        server = bootstrap;
        startingLoadoutApplied = true;
        foreach (var slot in slots) if (slot != null) slot.item = null;
        RecalculateBonuses();
        if (server == null) return;
        server.EquipmentSyncReceived += ApplyServerEquipment;
        server.EquipResultReceived += HandleEquipResult;
    }
    private void OnDestroy() => UnsubscribeServer();
    private void UnsubscribeServer()
    {
        if (server == null) return;
        server.EquipmentSyncReceived -= ApplyServerEquipment;
        server.EquipResultReceived -= HandleEquipResult;
    }
    public void ApplyServerEquipment(EquipmentSyncData data)
    {
        if (server == null || server.Identity == null || data.clientId != server.Identity.LocalClientId || data.slot != 0) return;
        var catalog = ItemCatalog.Load();
        var item = catalog != null ? catalog.Resolve(data.itemHash) as EquipmentItemData : null;
        if (item == null || !item.ChangesWeapon)
        { Debug.LogError($"[Equipment] Unknown server equipped weapon {data.itemHash}.", this); return; }
        if (playerAttack != null && !playerAttack.EquipWeapon(item.WeaponId))
        { Debug.LogError($"[Equipment] Weapon profile missing: {item.WeaponId}", this); return; }
        GetOrCreateSlot(EquipmentSlotType.Weapon).item = item;
        RecalculateBonuses();
        EquipmentChanged?.Invoke(this);
    }
    public void ApplyABEquipment(EquipmentItemData item)
    {
        if (item == null || ABClient.Active == null || !ABClient.Active.Supported) return;
        if (playerAttack != null && !playerAttack.EquipWeapon(item.WeaponId)) return;
        GetOrCreateSlot(EquipmentSlotType.Weapon).item = item;
        RecalculateBonuses();
        LastEquipmentFeedback = "장착: " + item.DisplayName;
        EquipmentChanged?.Invoke(this);
        EquipmentFeedback?.Invoke(LastEquipmentFeedback, true);
        PooledVfxSpawner.Play(CombatVfxKind.Equip, transform.position + Vector3.up, Quaternion.identity, .6f);
    }
    public void ClearABEquipment()
    {
        playerAttack?.UnequipWeapon();GetOrCreateSlot(EquipmentSlotType.Weapon).item=null;
        RecalculateBonuses();EquipmentChanged?.Invoke(this);
    }
    private void HandleEquipResult(EquipItemResult result)
    {
        bool success = result.success == 1;
        LastEquipmentFeedback = success ? "서버 승인: 무기를 장착했습니다." : (EquipFailReason)result.failReason switch
        {
            EquipFailReason.Dead => "사망 상태에서는 장착할 수 없습니다.", EquipFailReason.NoItem => "보유하지 않은 아이템입니다.",
            EquipFailReason.NotWeapon => "장착 가능한 무기가 아닙니다.", EquipFailReason.InvalidSlot => "올바르지 않은 장비 슬롯입니다.", _ => "장착 요청이 거절되었습니다."
        };
        if (success && Application.isPlaying) PooledVfxSpawner.Play(CombatVfxKind.Equip, transform.position + Vector3.up, Quaternion.identity, 0.7f);
        EquipmentFeedback?.Invoke(LastEquipmentFeedback, success);
    }

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

        if (IsServerOwned)
        {
            if (server == null || equipmentItem.EquipmentSlot != EquipmentSlotType.Weapon || inventory == null || inventory.Count(equipmentItem) <= 0)
                return false;
            server.RequestEquipItem(ItemHash.Of(equipmentItem.ItemId), 0);
            LastEquipmentFeedback = "서버 장착 승인 대기 중…";
            EquipmentFeedback?.Invoke(LastEquipmentFeedback, false);
            return true;
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
        if (IsServerOwned || !applyStartingLoadout || startingLoadoutApplied)
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
        // Online weapons remain in inventory; choosing another owned weapon replaces this slot.
        if (IsServerOwned) return false;
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
        if (slotType == EquipmentSlotType.Weapon) playerAttack?.UnequipWeapon();
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
