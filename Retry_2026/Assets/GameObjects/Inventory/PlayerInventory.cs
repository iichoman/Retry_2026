using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 24;
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();
    [SerializeField] private bool applyStartingItemsOnAwake = true;
    [SerializeField] private List<StartingInventoryEntry> startingItems = new List<StartingInventoryEntry>();

    public event Action<PlayerInventory> InventoryChanged;

    public int Capacity => capacity;
    public IReadOnlyList<InventorySlot> Slots => slots;

    private bool startingItemsApplied;

    private void Awake()
    {
        EnsureSlotCount();
        ApplyStartingItems();
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        EnsureSlotCount();
    }

    public void ApplyStartingItems()
    {
        if (!applyStartingItemsOnAwake || startingItemsApplied || startingItems == null)
        {
            return;
        }

        startingItemsApplied = true;

        for (int i = 0; i < startingItems.Count; i++)
        {
            AddStartingItem(startingItems[i]);
        }
    }

    public bool TryAdd(ItemData item, int amount = 1)
    {
        return TryAddDetailed(item, amount).AddedCount > 0;
    }

    public InventoryAddResult TryAddDetailed(ItemData item, int amount = 1)
    {
        if (item == null || amount <= 0)
        {
            return InventoryAddResult.Empty(amount);
        }

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
            return new InventoryAddResult(0, amount);
        }

        InventoryChanged?.Invoke(this);
        return new InventoryAddResult(added, remaining);
    }

    public bool TryRemove(ItemData item, int amount = 1)
    {
        if (item == null || amount <= 0 || Count(item) < amount)
        {
            return false;
        }

        int remaining = amount;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot.item != item)
            {
                continue;
            }

            int removeCount = Mathf.Min(remaining, slot.count);
            slot.count -= removeCount;
            remaining -= removeCount;

            if (slot.count <= 0)
            {
                slot.Clear();
            }
        }

        InventoryChanged?.Invoke(this);
        return true;
    }

    public bool TryRemoveAt(int slotIndex, int amount = 1)
    {
        if (!IsValidSlotIndex(slotIndex) || amount <= 0)
        {
            return false;
        }

        InventorySlot slot = slots[slotIndex];
        if (slot.IsEmpty)
        {
            return false;
        }

        int removeCount = Mathf.Min(amount, slot.count);
        slot.count -= removeCount;
        if (slot.count <= 0)
        {
            slot.Clear();
        }

        InventoryChanged?.Invoke(this);
        return true;
    }

    public bool TryMoveSlot(int fromIndex, int toIndex, int amount = int.MaxValue)
    {
        if (!IsValidSlotIndex(fromIndex) || !IsValidSlotIndex(toIndex) || fromIndex == toIndex || amount <= 0)
        {
            return false;
        }

        InventorySlot source = slots[fromIndex];
        InventorySlot target = slots[toIndex];
        if (source.IsEmpty)
        {
            return false;
        }

        int moveCount = Mathf.Min(amount, source.count);
        if (target.IsEmpty)
        {
            MoveToEmptySlot(source, target, moveCount);
            InventoryChanged?.Invoke(this);
            return true;
        }

        if (target.item == source.item && target.count < target.item.MaxStack)
        {
            int accepted = Mathf.Min(moveCount, target.RemainingStackSpace);
            if (accepted <= 0)
            {
                return false;
            }

            target.count += accepted;
            source.count -= accepted;
            if (source.count <= 0)
            {
                source.Clear();
            }

            InventoryChanged?.Invoke(this);
            return true;
        }

        if (moveCount < source.count)
        {
            return false;
        }

        SwapSlots(source, target);
        InventoryChanged?.Invoke(this);
        return true;
    }

    public bool TrySwapSlots(int firstIndex, int secondIndex)
    {
        if (!IsValidSlotIndex(firstIndex) || !IsValidSlotIndex(secondIndex) || firstIndex == secondIndex)
        {
            return false;
        }

        SwapSlots(slots[firstIndex], slots[secondIndex]);
        InventoryChanged?.Invoke(this);
        return true;
    }

    public bool TryMergeSlots(int fromIndex, int toIndex)
    {
        if (!IsValidSlotIndex(fromIndex) || !IsValidSlotIndex(toIndex) || fromIndex == toIndex)
        {
            return false;
        }

        InventorySlot source = slots[fromIndex];
        InventorySlot target = slots[toIndex];
        if (source.IsEmpty || target.IsEmpty || source.item != target.item || target.count >= target.item.MaxStack)
        {
            return false;
        }

        int moveCount = Mathf.Min(source.count, target.RemainingStackSpace);
        target.count += moveCount;
        source.count -= moveCount;
        if (source.count <= 0)
        {
            source.Clear();
        }

        InventoryChanged?.Invoke(this);
        return true;
    }

    /// <summary>모든 슬롯 비우기. 서버 인벤토리 동기화(ServerInventoryBridge) 전용.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Clear();
        }

        InventoryChanged?.Invoke(this);
    }

    public int Count(ItemData item)
    {
        if (item == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item == item)
            {
                total += slots[i].count;
            }
        }

        return total;
    }

    public bool IsValidSlotIndex(int index)
    {
        return index >= 0 && index < slots.Count;
    }

    public InventorySlot GetSlot(int index)
    {
        return IsValidSlotIndex(index) ? slots[index] : null;
    }

    private void AddStartingItem(StartingInventoryEntry entry)
    {
        if (entry == null || entry.Item == null)
        {
            return;
        }

        int missingCount = Mathf.Max(0, entry.Count - Count(entry.Item));
        if (missingCount > 0)
        {
            TryAdd(entry.Item, missingCount);
        }
    }

    private static void MoveToEmptySlot(InventorySlot source, InventorySlot target, int amount)
    {
        target.item = source.item;
        target.count = amount;
        source.count -= amount;
        if (source.count <= 0)
        {
            source.Clear();
        }
    }

    private static void SwapSlots(InventorySlot first, InventorySlot second)
    {
        ItemData item = first.item;
        int count = first.count;

        first.item = second.item;
        first.count = second.count;
        second.item = item;
        second.count = count;
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

public readonly struct InventoryAddResult
{
    public readonly int AddedCount;
    public readonly int RemainingCount;

    public InventoryAddResult(int addedCount, int remainingCount)
    {
        AddedCount = Mathf.Max(0, addedCount);
        RemainingCount = Mathf.Max(0, remainingCount);
    }

    public bool FullyAdded => RemainingCount <= 0;

    public static InventoryAddResult Empty(int requestedCount)
    {
        return new InventoryAddResult(0, Mathf.Max(0, requestedCount));
    }
}
