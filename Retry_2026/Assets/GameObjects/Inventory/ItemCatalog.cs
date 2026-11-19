using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Single Resources catalog, independent of dynamically created bridge Inspector fields.</summary>
[CreateAssetMenu(menuName = "Retry/Inventory/Item Catalog")]
public sealed class ItemCatalog : ScriptableObject
{
    public const string ResourcePath = "ItemCatalog";
    public const int InventoryCapacity = 30;
    [SerializeField] private List<ItemData> items = new List<ItemData>();
    private readonly Dictionary<int, ItemData> byHash = new Dictionary<int, ItemData>();
    private readonly Dictionary<string, ItemData> byId = new Dictionary<string, ItemData>(StringComparer.Ordinal);
    private bool ready;
    private static ItemCatalog instance;
    private static bool missingReported;
    public IReadOnlyList<ItemData> Items => items;
    public static ItemCatalog Load()
    {
        if (instance == null) instance = Resources.Load<ItemCatalog>(ResourcePath);
        if (instance == null && !missingReported)
        {
            missingReported = true;
            Debug.LogError("[ItemCatalog] Resources/ItemCatalog.asset is missing. Run Retry > Build Item Catalog.");
        }
        if (instance != null && !instance.ready) instance.ValidateCatalog();
        return instance;
    }
    public ItemData Resolve(int hash) { if (!ready) ValidateCatalog(); return byHash.TryGetValue(hash, out var item) ? item : null; }
    public ItemData Resolve(string id) { if (!ready) ValidateCatalog(); return id != null && byId.TryGetValue(id, out var item) ? item : null; }
    public bool ValidateCatalog()
    {
        ready = true;
        byHash.Clear(); byId.Clear();
        bool valid = true;
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
            { Debug.LogError("[ItemCatalog] Null item or empty itemId.", this); valid = false; continue; }
            bool ascii = true;
            foreach (char c in item.ItemId) if (c > 127 || char.IsWhiteSpace(c)) ascii = false;
            if (!ascii) { Debug.LogError($"[ItemCatalog] ASCII itemId required: {item.ItemId}", item); valid = false; continue; }
            int hash = ItemHash.Of(item.ItemId);
            if (byId.ContainsKey(item.ItemId))
            { Debug.LogError($"[ItemCatalog] Duplicate itemId: {item.ItemId}", item); valid = false; continue; }
            if (byHash.TryGetValue(hash, out var other))
            { Debug.LogError($"[ItemCatalog] FNV-1a collision: {other.ItemId} / {item.ItemId} ({hash})", item); valid = false; continue; }
            byId.Add(item.ItemId, item); byHash.Add(hash, item);
        }
        return valid;
    }
    private void OnEnable() { ready = false; }
#if UNITY_EDITOR
    public void EditorSetItems(List<ItemData> value) { items = value; ready = false; }
#endif
}
