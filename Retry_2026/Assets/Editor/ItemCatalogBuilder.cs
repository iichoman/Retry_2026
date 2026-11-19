#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ItemCatalogBuilder
{
    public const string CatalogPath = "Assets/Resources/ItemCatalog.asset";
    [MenuItem("Retry/Build Item Catalog")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Resources/Items");
        AssetDatabase.Refresh();
        var items = new List<ItemData>
        {
            Configure("Assets/Resources/Items/GoldCoin.asset", "gold_coin", "금화", "탈출 후 가치로 인정되는 던전의 통화.", ItemType.Currency, ItemRarity.Common, 1, 999),
            Configure("Assets/Resources/Items/IronOre.asset", "iron_ore", "철광석", "흔하지만 꾸준한 가치가 있는 제작 재료.", ItemType.Material, ItemRarity.Common, 10, 99),
            Configure("Assets/Resources/Items/HealthPotion.asset", "health_potion", "회복 물약", "추출 가치가 있는 소모품. 현재 세션에서는 보관 및 획득용입니다.", ItemType.Consumable, ItemRarity.Uncommon, 25, 16),
            Configure("Assets/Resources/Items/RareGem.asset", "rare_gem", "희귀 보석", "정예 몬스터가 지키는 고가의 보석.", ItemType.Material, ItemRarity.Rare, 150, 20),
            Configure("Assets/Resources/Items/BossCore.asset", "boss_core", "수호자의 코어", "던전 수호자 처치 시 보장되는 보스 전용 보상.", ItemType.Material, ItemRarity.Boss, 1000, 10, true),
            Configure("Assets/GameObjects/Player/New Equipment Item Data.asset", "sword_001", "수호단의 검", "가까운 적을 베는 검. 인벤토리에서 우클릭하여 장착 요청.", ItemType.Weapon, ItemRarity.Uncommon, 200, 1),
            Configure("Assets/GameObjects/Inventory/TestGunEquipmentItem.asset", "gun_001", "탐사대의 총", "서버 판정 투사체를 발사하는 총. 인벤토리에서 우클릭하여 장착 요청.", ItemType.Weapon, ItemRarity.Rare, 350, 1)
        };
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<ItemCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        catalog.EditorSetItems(items);
        EditorUtility.SetDirty(catalog);
        ItemIconContentBuilder.BindAvailableIcons(catalog);
        if (!catalog.ValidateCatalog()) throw new InvalidOperationException("Invalid ItemCatalog");
        string uiPath = "Assets/Resources/ServerLootUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(uiPath) == null)
        {
            var ui = new GameObject("ServerLootUI", typeof(RectTransform), typeof(ServerLootUI));
            PrefabUtility.SaveAsPrefabAsset(ui, uiPath);
            UnityEngine.Object.DestroyImmediate(ui);
        }
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("[ItemCatalogBuilder] Built seven items, preserved weapon GUIDs, Resources catalog and online loot UI prefab.");
    }
    private static ItemData Configure(string path, string id, string displayName, string description, ItemType type, ItemRarity rarity, int value, int stack, bool boss = false)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null)
        {
            if (type == ItemType.Weapon) throw new InvalidOperationException("Existing weapon asset missing: " + path);
            item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, path);
        }
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = id;
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("description").stringValue = description;
        serialized.FindProperty("itemType").enumValueIndex = (int)type;
        serialized.FindProperty("rarity").enumValueIndex = (int)rarity;
        serialized.FindProperty("extractionValue").intValue = value;
        serialized.FindProperty("maxStack").intValue = stack;
        serialized.FindProperty("bossExclusive").boolValue = boss;
        serialized.FindProperty("questOnly").boolValue = false;
        serialized.FindProperty("fallbackIconColor").colorValue = ItemData.ColorFor(rarity);
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(item);
        return item;
    }
    [MenuItem("Retry/Validate Item Catalog And Inventory")]
    public static void Validate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        Require(catalog != null && catalog.ValidateCatalog(), "catalog validity");
        string[] expected = { "gold_coin", "iron_ore", "health_potion", "rare_gem", "boss_core", "sword_001", "gun_001" };
        foreach (var id in expected) Require(catalog.Resolve(ItemHash.Of(id)) != null, "catalog ID " + id);
        var globalIds = new HashSet<string>(StringComparer.Ordinal); var hashes = new HashSet<int>();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null) continue;
            Require(globalIds.Add(item.ItemId), "duplicate itemId across project: " + item.ItemId);
            Require(hashes.Add(ItemHash.Of(item.ItemId)), "FNV collision across project: " + item.ItemId);
        }
        var probe = new GameObject("Inventory validation probe");
        try
        {
            var inventory = probe.AddComponent<PlayerInventory>(); inventory.SetServerOwned();
            int changed = 0; inventory.InventoryChanged += _ => changed++;
            var entries = new ItemStack[32];
            entries[0] = new ItemStack { itemHash = ItemHash.Of("health_potion"), count = 28 };
            entries[1] = new ItemStack { itemHash = ItemHash.Of("sword_001"), count = 2 };
            var snapshot = new InventorySyncData { entryCount = 2, totalCount = 30, entries = entries };
            Require(inventory.ApplyServerSnapshot(snapshot, catalog), "valid atomic snapshot");
            Require(changed == 1 && inventory.Capacity == 30 && inventory.TotalCount == 30, "one change notification and 30 total items");
            Require(inventory.GetSlot(0).count == 10 && inventory.GetSlot(1).count == 10 && inventory.GetSlot(2).count == 8 && inventory.GetSlot(3).count == 1, "stack split");
            Require(!inventory.TryAdd(catalog.Resolve("gold_coin"), 1) && !inventory.TryRemove(catalog.Resolve("sword_001"), 1), "online mutation blocked");
            entries[0] = new ItemStack { itemHash = ItemHash.Of("sword_001"), count = 33 };
            snapshot.entryCount = 1; snapshot.totalCount = 33;
            Require(!inventory.ApplyServerSnapshot(snapshot, catalog), "capacity overflow rejected");
            Require(inventory.Count(catalog.Resolve("health_potion")) == 28 && changed == 1, "rejected snapshot does not partially apply");
            entries[0] = new ItemStack { itemHash = 123, count = 1 }; snapshot.totalCount = 1;
            Require(!inventory.ApplyServerSnapshot(snapshot, catalog), "unknown item rejected");
            Debug.Log("[InventoryValidation] PASS catalog IDs/hashes, 30 total items, atomic snapshot, overflow, unknown ID, local-write guards.");
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException("[InventoryValidation] " + label); }
}
#endif
