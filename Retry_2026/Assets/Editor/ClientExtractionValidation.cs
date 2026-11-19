#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Simulated authoritative packets through the real serializer/dispatcher, with isolated prefab contents.
/// This is not a live socket test or a visual/latency test.</summary>
public static class ClientExtractionValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Passed = new List<string>();
    [MenuItem("Retry/Extraction/Validate Client Packet Flow")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run client packet validation in Edit mode.");
        Passed.Clear();
        bool previousOnline = NetworkRuntime.IsOnline;
        var runtimeProperty = typeof(NetworkRuntime).GetProperty("IsOnline", BindingFlags.Public | BindingFlags.Static);
        GameObject player = null, harness = null;
        string artifacts = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
        Directory.CreateDirectory(artifacts);
        try
        {
            runtimeProperty.SetValue(null, true);
            var catalog = ItemCatalog.Load();
            Require(catalog != null && catalog.ValidateCatalog(), "generated catalog validates");
            player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LocalPlayerPrefab.prefab");
            // Prefab contents are a temporary preview scene. Never modify the user's loaded scene/assets.
            harness = new GameObject("Client validation harness"); harness.SetActive(false);
            SceneManager.MoveGameObjectToScene(harness, player.scene);
            var bootstrap = harness.AddComponent<NetworkBootstrap>();
            var identity = harness.AddComponent<LocalIdentity>(); identity.SetLocalClientId(9876);
            SetProperty(bootstrap, "Identity", identity); SetProperty(bootstrap, "LocalPlayer", player);
            var bridge = harness.AddComponent<ServerInventoryBridge>(); SetProperty(bootstrap, "Inventory", bridge);
            var inventory = player.GetComponent<PlayerInventory>();
            var equipment = player.GetComponent<PlayerEquipment>();
            var attack = player.GetComponent<Player_Attack>();
            Require(inventory != null && equipment != null && attack != null, "local player prefab inventory/equipment/attack references");
            Set(attack, "createOffhandGunVisual", false);
            Invoke(attack, "CollectWeapons");
            Set(equipment, "inventory", inventory); Set(equipment, "playerAttack", attack); Invoke(equipment, "Awake");
            bridge.Initialize(bootstrap, inventory);
            var sword = catalog.Resolve("sword_001") as EquipmentItemData;
            var gun = catalog.Resolve("gun_001") as EquipmentItemData;
            Require(sword != null && gun != null, "weapon ItemData definitions preserve equipment types");
            var initial = new InventorySyncData { entryCount = 1, totalCount = 1, entries = new ItemStack[32] };
            initial.entries[0] = new ItemStack { itemHash = ItemHash.Of("sword_001"), count = 1 };
            Dispatch(bootstrap, PacketType.INVENTORY_SYNC, initial);
            Dispatch(bootstrap, PacketType.EQUIPMENT_SYNC, new EquipmentSyncData { clientId = 9876, itemHash = ItemHash.Of("sword_001"), slot = 0, weaponKind = 0 });
            Require(attack.EquippedWeaponId == sword.WeaponId && equipment.GetEquippedItem(EquipmentSlotType.Weapon) == sword, "initial server inventory/equipment selects sword profile and view");
            Require(!equipment.TryEquip(gun), "unowned local equip request rejected");
            Require(!equipment.TryUnequip(EquipmentSlotType.Weapon), "online unequip cannot duplicate inventory");
            Dispatch(bootstrap, PacketType.EQUIP_ITEM_RESULT, new EquipItemResult { itemHash = ItemHash.Of("gun_001"), slot = 0, success = 0, failReason = 2 });
            Require(attack.EquippedWeaponId == sword.WeaponId && inventory.Count(sword) == 1, "server equip rejection preserves displayed equipment/counts");
            string[] ids = { "gold_coin", "iron_ore", "health_potion", "rare_gem", "boss_core", "sword_001", "gun_001" };
            int[] counts = { 1000, 3, 2, 2, 1, 2, 1 };
            var all = new InventorySyncData { entryCount = 7, totalCount = 1011, entries = new ItemStack[32] };
            for (int i = 0; i < ids.Length; i++) all.entries[i] = new ItemStack { itemHash = ItemHash.Of(ids[i]), count = counts[i] };
            Dispatch(bootstrap, PacketType.INVENTORY_SYNC, all);
            Require(bridge.TotalItemCount == 1011 && inventory.Count(gun) == 1 && inventory.Count(sword) == 2, "received loot inventory includes all seven IDs and splits stacks");
            int gunSlot = -1;
            for (int i = 0; i < inventory.Slots.Count; i++) if (inventory.Slots[i].item == gun) gunSlot = i;
            Require(gunSlot >= 0 && equipment.TryEquipFromInventory(gunSlot), "owned inventory weapon emits an equip intention");
            Require(attack.EquippedWeaponId == sword.WeaponId && equipment.GetEquippedItem(EquipmentSlotType.Weapon) == sword, "local equip intention does not predict approved state");
            var request = PacketIO.BytesToStruct<EquipItemRequest>(PacketIO.StructToBytes(new EquipItemRequest { itemHash = ItemHash.Of(gun.ItemId), slot = 0 }), 0);
            Require(request.itemHash == ItemHash.Of(gun.ItemId) && request.slot == 0, "equip request wire roundtrip");
            Dispatch(bootstrap, PacketType.EQUIP_ITEM_RESULT, new EquipItemResult { itemHash = request.itemHash, slot = 0, success = 1, failReason = 0 });
            Require(attack.EquippedWeaponId == sword.WeaponId, "equip result feedback alone does not override equipment snapshot");
            Dispatch(bootstrap, PacketType.EQUIPMENT_SYNC, new EquipmentSyncData { clientId = 9876, itemHash = request.itemHash, slot = 0, weaponKind = 3 });
            Require(attack.EquippedWeaponId == gun.WeaponId && bootstrap.ServerWeaponKind == 3 && bootstrap.HasServerEquipment, "approved gun selects matching attack profile and server weapon kind");
            Require(inventory.Count(gun) == 1 && inventory.Count(sword) == 2, "equipping retains owned item counts");
            var gunBehaviour = player.GetComponentInChildren<test_gun>(true);
            Require(gunBehaviour != null, "actual gun weapon component exists on connected prefab");
            gunBehaviour.RequestAttack();
            Require(attack.TryConsumeAttackAnimationRequest(out int combo) && combo > 0, "approved gun can request attack animation from real profile");
            attack.CancelCurrentAttack();
            var result = new ExtractionLootResultData { entryCount = 7, totalCount = 1010, bossKillCount = 1, totalValue = 2930, entries = new ExtractionLootEntry[32] };
            for (int i = 0; i < ids.Length; i++) result.entries[i] = new ExtractionLootEntry
            { itemHash = ItemHash.Of(ids[i]), count = ids[i] == "sword_001" ? 1 : counts[i], unitValue = catalog.Resolve(ids[i]).ExtractionValue };
            Dispatch(bootstrap, PacketType.EXTRACTION_LOOT_RESULT, result);
            bool extractionSawPriorSnapshot = false;
            bootstrap.ExtractionResultReceived += message => extractionSawPriorSnapshot = message.success == 1 && bridge.HasExtractionResult;
            Dispatch(bootstrap, PacketType.EXTRACTION_RESULT, new ExtractionResult { success = 1, itemCount = 1010, failReason = 0, heldSec = ExtractionConst.HOLD_SEC });
            Require(extractionSawPriorSnapshot && bridge.ExtractionResult.totalValue == 2930, "extraction success observes preceding immutable server reward snapshot");
            var manager = harness.AddComponent<DungeonExitManager>(); Set(manager, "cachedBootstrap", bootstrap);
            var escaped = (HashSet<Player>)typeof(DungeonExitManager).GetField("escapedPlayers", Private).GetValue(manager);
            var actor = player.GetComponent<Player>(); Require(actor != null, "local prefab Player actor exists"); escaped.Add(actor);
            var summary = (DungeonExitResult)typeof(DungeonExitManager).GetMethod("BuildResult", Private).Invoke(manager, new object[] { actor });
            Require(summary.serverConfirmedInventory && summary.items.Count == 7 && summary.totalValue == 2930 && summary.bossKillCount == 1, "result model uses server values and dungeon boss credit");
            Require(summary.items.Find(item => item.itemId == "sword_001").count == 1, "extraction result excludes starting sword rather than copying inventory");
            var panel = new GameObject("Result validation", typeof(RectTransform)); panel.transform.SetParent(harness.transform, false);
            var resultUI = panel.AddComponent<DungeonResultUI>(); Set(resultUI, "root", panel);
            var goldText = NewText("Gold", panel.transform); var itemsText = NewText("Items", panel.transform);
            Set(resultUI, "goldText", goldText); Set(resultUI, "itemsText", itemsText);
            resultUI.Show(summary, manager);
            Invoke(resultUI, "Awake");
            Require(panel.activeSelf, "first result Show survives delayed inactive-panel Awake");
            Require(goldText.gameObject.activeSelf && itemsText.gameObject.activeSelf && goldText.text.Contains("2,930"), "result UI enables gold/items and displays total value");
            foreach (string id in ids) Require(itemsText.text.Contains(catalog.Resolve(id).DisplayName), "result UI item label: " + id);
            Require(itemsText.text.Contains("1,000") && goldText.text.Contains("1"), "result UI includes per-item values and boss count");
            File.WriteAllText(Path.Combine(artifacts, "client-packet-flow-validation.txt"),
                "PASS: " + Passed.Count + " checks\nSimulated incoming authoritative packets through actual PacketIO and NetworkBootstrap dispatcher in an isolated prefab preview scene. No live socket or latency test.\n" + string.Join("\n", Passed));
            Debug.Log("[ClientExtractionValidation] PASS " + Passed.Count + " simulated authoritative packet/weapon/UI checks.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(artifacts, "client-packet-flow-validation.txt"), "FAIL\n" + string.Join("\n", Passed) + "\n" + error);
            throw;
        }
        finally
        {
            if (harness != null) UnityEngine.Object.DestroyImmediate(harness);
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
            runtimeProperty.SetValue(null, previousOnline);
        }
    }
    private static Text NewText(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); return go.GetComponent<Text>();
    }
    private static void Dispatch<T>(NetworkBootstrap bootstrap, PacketType kind, T body) where T : struct
    {
        byte[] packet = PacketIO.MakePacket(kind, body);
        var header = PacketIO.BytesToStruct<PacketHeader>(packet, 0);
        if (header.type != kind || header.size != packet.Length - 8) throw new InvalidOperationException("Packet envelope roundtrip");
        byte[] payload = new byte[header.size]; Buffer.BlockCopy(packet, 8, payload, 0, payload.Length);
        typeof(NetworkBootstrap).GetMethod("OnSessionPacket", Private).Invoke(bootstrap, new object[] { kind, payload });
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void SetProperty(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[ClientExtractionValidation] " + label);
        Passed.Add("PASS " + label);
    }
}
#endif
