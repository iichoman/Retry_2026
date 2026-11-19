using System.Collections.Generic;
using UnityEngine;

/// <summary>Server snapshots are the sole writer of online inventory and extraction rewards.</summary>
[DisallowMultipleComponent]
public class ServerInventoryBridge : MonoBehaviour
{
    [SerializeField] private ItemCatalog catalog;
    private PlayerInventory localInventory;
    private NetworkBootstrap bootstrap;
    private InventorySyncData? pendingInventory;
    public int TotalItemCount { get; private set; }
    public bool HasExtractionResult { get; private set; }
    public ExtractionLootResultData ExtractionResult { get; private set; }
    public ItemCatalog Catalog => catalog != null ? catalog : catalog = ItemCatalog.Load();
    public void Initialize(NetworkBootstrap bs, PlayerInventory inventory)
    {
        if (bootstrap != bs)
        {
            Unsubscribe();
            bootstrap = bs;
            if (bootstrap != null)
            {
                bootstrap.InventorySyncReceived += OnInventorySync;
                bootstrap.ExtractionLootResultReceived += OnExtractionLootResult;
            }
        }
        if (inventory != null && inventory != localInventory)
        {
            localInventory = inventory;
            localInventory.SetServerOwned();
            var equipment = inventory.GetComponent<PlayerEquipment>();
            if (equipment != null) equipment.InitializeServer(bs);
        }
        if (pendingInventory.HasValue && localInventory != null) OnInventorySync(pendingInventory.Value);
    }
    private void OnDestroy() => Unsubscribe();
    private void Unsubscribe()
    {
        if (bootstrap == null) return;
        bootstrap.InventorySyncReceived -= OnInventorySync;
        bootstrap.ExtractionLootResultReceived -= OnExtractionLootResult;
    }
    public ItemData Resolve(int itemHash) => Catalog != null ? Catalog.Resolve(itemHash) : null;
    public void RequestPickup(int lootId, int itemHash, int count = 0)
    {
        if (bootstrap != null) bootstrap.RequestItemPickup(lootId, itemHash, count);
    }
    public void RequestPickup(int lootId, ItemData item, int count = 0)
    {
        if (item != null) RequestPickup(lootId, ItemHash.Of(item.ItemId), count);
    }
    private void OnInventorySync(InventorySyncData sync)
    {
        pendingInventory = sync;
        if (localInventory == null) return;
        if (!localInventory.ApplyServerSnapshot(sync, Catalog))
        {
            Debug.LogError("[Inventory] Rejected invalid server snapshot: unknown item, count mismatch, duplicate ID or capacity overflow.");
            return;
        }
        TotalItemCount = sync.totalCount;
    }
    private void OnExtractionLootResult(ExtractionLootResultData result)
    {
        HasExtractionResult = false;
        if (result.entries == null || result.entryCount < 0 || result.entryCount > LootConst.MAX_INVENTORY_ENTRIES ||
            result.entryCount > result.entries.Length || result.bossKillCount < 0) return;
        long totalValue = 0, totalCount = 0;
        var seen = new HashSet<int>();
        for (int i = 0; i < result.entryCount; i++)
        {
            var entry = result.entries[i];
            if (Resolve(entry.itemHash) == null || entry.count <= 0 || entry.unitValue < 0 || !seen.Add(entry.itemHash)) return;
            totalValue += (long)entry.unitValue * entry.count;
            totalCount += entry.count;
        }
        if (totalValue != result.totalValue || totalCount != result.totalCount) return;
        ExtractionResult = result;
        HasExtractionResult = true;
    }
    public void ClearExtractionResult() { HasExtractionResult = false; ExtractionResult = default; }
}
