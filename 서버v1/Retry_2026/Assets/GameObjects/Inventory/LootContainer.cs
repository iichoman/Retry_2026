using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LootContainer : MonoBehaviour, IPickupable
{
    [SerializeField, Min(1)] private int capacity = 12;
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();
    [SerializeField] private bool available;
    [SerializeField] private bool deactivateWhenEmpty = true;

    public event Action<LootContainer> ContentsChanged;

    public int Capacity => capacity;
    public bool IsAvailable => available;
    public IReadOnlyList<InventorySlot> Slots => slots;

    private void Awake()
    {
        EnsureSlotCount();
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        EnsureSlotCount();
    }

    public void SetAvailable(bool isAvailable)
    {
        available = isAvailable;
    }

    public void Clear()
    {
        EnsureSlotCount();
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Clear();
        }

        ContentsChanged?.Invoke(this);
    }

    public bool TryAdd(ItemData item, int amount = 1)
    {
        if (item == null || amount <= 0)
        {
            return false;
        }

        EnsureSlotCount();
        int remaining = amount;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.item != item || slot.count >= item.MaxStack)
            {
                continue;
            }

            int moveCount = Mathf.Min(remaining, slot.RemainingStackSpace);
            slot.count += moveCount;
            remaining -= moveCount;
        }

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int moveCount = Mathf.Min(remaining, item.MaxStack);
            slot.item = item;
            slot.count = moveCount;
            remaining -= moveCount;
        }

        int added = amount - remaining;
        if (added <= 0)
        {
            return false;
        }

        ContentsChanged?.Invoke(this);
        return true;
    }

    public bool TryTakeSlot(int slotIndex, PlayerInventory targetInventory, int amount = int.MaxValue)
    {
        if (!available || targetInventory == null || !IsValidSlotIndex(slotIndex) || amount <= 0)
        {
            return false;
        }

        InventorySlot source = slots[slotIndex];
        if (source.IsEmpty)
        {
            return false;
        }

        int requested = Mathf.Min(amount, source.count);
        InventoryAddResult result = targetInventory.TryAddDetailed(source.item, requested);
        if (result.AddedCount <= 0)
        {
            return false;
        }

        source.count -= result.AddedCount;
        if (source.count <= 0)
        {
            source.Clear();
        }

        HandleContentsChanged();
        return true;
    }

    public bool TryTakeAll(PlayerInventory targetInventory)
    {
        if (!available || targetInventory == null)
        {
            return false;
        }

        bool movedAny = false;
        for (int i = 0; i < slots.Count; i++)
        {
            while (!slots[i].IsEmpty && TryTakeSlot(i, targetInventory))
            {
                movedAny = true;
            }
        }

        return movedAny;
    }

    public bool TryPickup(GameObject picker)
    {
        if (!available || picker == null)
        {
            return false;
        }

        PlayerInventory targetInventory = picker.GetComponent<PlayerInventory>();
        if (targetInventory == null)
        {
            targetInventory = picker.GetComponentInParent<PlayerInventory>();
        }

        if (targetInventory == null)
        {
            return false;
        }

        if (GameUIController.Instance != null && GameUIController.Instance.OpenLoot(this, targetInventory))
        {
            return true;
        }

        return TryTakeAll(targetInventory);
    }

    public bool IsEmpty()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty)
            {
                return false;
            }
        }

        return true;
    }

    public InventorySlot GetSlot(int index)
    {
        return IsValidSlotIndex(index) ? slots[index] : null;
    }

    public bool IsValidSlotIndex(int index)
    {
        return index >= 0 && index < slots.Count;
    }

    private void HandleContentsChanged()
    {
        if (deactivateWhenEmpty && IsEmpty())
        {
            available = false;
        }

        ContentsChanged?.Invoke(this);
    }

    private void EnsureSlotCount()
    {
        if (slots == null)
        {
            slots = new List<InventorySlot>();
        }

        while (slots.Count < capacity)
        {
            slots.Add(new InventorySlot());
        }

        while (slots.Count > capacity)
        {
            slots.RemoveAt(slots.Count - 1);
        }
    }
}
