using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Server-owned containers. Opening F shows contents; every take is a request.</summary>
public class RemoteLootRegistry : MonoBehaviour
{
    public GameObject LootPrefab;
    [SerializeField] private ServerLootUI lootUiPrefab;
    [SerializeField] private Vector3 lootScale = new Vector3(0.6f, 0.4f, 0.6f);
    private NetworkBootstrap bootstrap;
    private ItemCatalog catalog;
    private ServerLootUI ui;
    private Defalult_Input localInput;
    private readonly Dictionary<int, GameObject> active = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, LootVfxController> visuals = new Dictionary<int, LootVfxController>();
    private readonly Dictionary<int, LootSpawnData> contents = new Dictionary<int, LootSpawnData>();
    private readonly Dictionary<long, float> pending = new Dictionary<long, float>();
    private readonly List<long> expired = new List<long>();
    private int openedLoot, nearestLoot;
    private bool wasInRange;
    private int lastSuccessfulLoot;
    private float lastSuccessTime;

    public void Initialize(NetworkBootstrap bs)
    {
        Unsubscribe(); bootstrap = bs; catalog = ItemCatalog.Load();
        if (bootstrap == null) return;
        if (LootPrefab == null)
        {
            var vfxCatalog = CombatVfxCatalog.Load();
            if (vfxCatalog != null) LootPrefab = vfxCatalog.LootPrefab;
        }
        bootstrap.LootSpawnReceived += OnSpawn;
        bootstrap.LootRemovedReceived += OnRemoved;
        bootstrap.PickupResultReceived += OnPickupResult;
        if (lootUiPrefab == null) lootUiPrefab = Resources.Load<ServerLootUI>("ServerLootUI");
        if (ui == null)
        {
            if (lootUiPrefab != null) ui = Instantiate(lootUiPrefab, transform);
            else ui = new GameObject("Server Loot UI", typeof(RectTransform), typeof(ServerLootUI)).GetComponent<ServerLootUI>();
            ui.EnsureBuilt();
        }
    }
    private void Unsubscribe()
    {
        if (bootstrap == null) return;
        bootstrap.LootSpawnReceived -= OnSpawn; bootstrap.LootRemovedReceived -= OnRemoved; bootstrap.PickupResultReceived -= OnPickupResult;
    }
    public void ClearSessionLoot()
    {
        if(ui!=null){ui.Close();ui.SetHint(false,string.Empty);}
        foreach(var go in active.Values)if(go!=null){go.SetActive(false);Destroy(go);}
        active.Clear();visuals.Clear();contents.Clear();pending.Clear();expired.Clear();
        openedLoot=nearestLoot=lastSuccessfulLoot=0;wasInRange=false;
    }
    private void OnDestroy()
    {
        Unsubscribe();
        if (ui != null) { ui.Close(); Destroy(ui.gameObject); }
        foreach (var go in active.Values) if (go != null) Destroy(go);
    }
    public void OnSpawn(LootSpawnData sp)
    {
        if (sp.lootId <= 0 || sp.entries == null || sp.entryCount < 0 || sp.entryCount > LootConst.MAX_LOOT_ENTRIES || sp.entryCount > sp.entries.Length) return;
        contents[sp.lootId] = sp;
        if (!active.TryGetValue(sp.lootId, out var go) || go == null)
        {
            var chest=sp.sourceMonsterId<EnvironmentClient.ChestSource?Resources.Load<GameObject>(sp.sourceMonsterId<EnvironmentClient.RewardChestSource?"EContent/ChestGold":"EContent/Chest"):null;
            if(chest!=null)go=Instantiate(chest,transform);
            else if (LootPrefab != null) go = Instantiate(LootPrefab);
            else { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = lootScale; }
            go.name = (sp.sourceMonsterId<EnvironmentClient.ChestSource?"Chest_":"Loot_")+sp.lootId;
            go.transform.position = new Vector3(sp.posX, sp.posY, sp.posZ);
            if (chest != null)
            {
                var dungeon = FindFirstObjectByType<DungeonGenerator_ChunkMesh>();
                if (dungeon != null)
                {
                    var tile = dungeon.WorldToTile(go.transform.position); tile.y = 0;
                    foreach (var room in dungeon.Rooms) if (room.floorTiles.Contains(tile))
                    { go.transform.rotation = Quaternion.LookRotation((Vector3)RoomBlueprint.Entrance(room)); break; }
                }
            }
            // A network container must never expose the offline direct-pickup path.
            foreach (var local in go.GetComponentsInChildren<LootContainer>(true)) { local.SetAvailable(false); local.enabled = false; }
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = sp.sourceMonsterId<EnvironmentClient.ChestSource;
            if (chest != null) SimplePropCollider.Apply(go, new Vector3(0, .525f, 0), new Vector3(1.5f, 1.05f, 1.1f));
            active[sp.lootId] = go;
            var vfx = go.GetComponent<LootVfxController>();
            if (vfx == null) vfx = go.AddComponent<LootVfxController>();
            vfx.RewardChest=sp.sourceMonsterId<EnvironmentClient.RewardChestSource;
            visuals[sp.lootId] = vfx;
        }
        int rarity = 0; bool weapon = false, boss = false;
        for (int i = 0; i < sp.entryCount; i++)
        {
            if (sp.entries[i].count <= 0) continue;
            var item = catalog != null ? catalog.Resolve(sp.entries[i].itemHash) : null;
            if (item == null) continue;
            rarity = Mathf.Max(rarity, (int)item.Rarity); weapon |= item.ItemType == ItemType.Weapon; boss |= item.BossExclusive;
        }
        if (visuals.TryGetValue(sp.lootId, out var view) && view != null) view.Configure(ItemData.ColorFor((ItemRarity)rarity), rarity, weapon, boss);
        if (openedLoot == sp.lootId) RefreshOpen();
    }
    public void OnRemoved(LootRemovedData rm)
    {
        bool chest=active.TryGetValue(rm.lootId,out var go)&&go!=null&&go.name.StartsWith("Chest_");
        if (visuals.TryGetValue(rm.lootId, out var vfx) && vfx != null){if(chest){vfx.enabled=false;if(vfx.Beam!=null)vfx.Beam.enabled=false;if(vfx.GroundRing!=null)vfx.GroundRing.enabled=false;foreach(var ps in go.GetComponentsInChildren<ParticleSystem>())ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}else vfx.PlayRemoval();}
        if(go!=null){if(chest){go.name="Empty_"+go.name;foreach(var part in go.GetComponentsInChildren<Transform>())if(part.name.EndsWith("_lid"))part.localRotation=Quaternion.Euler(-65,0,0);foreach(var collider in go.GetComponentsInChildren<Collider>())collider.enabled=false;Destroy(go,.7f);}else Destroy(go); }
        active.Remove(rm.lootId); visuals.Remove(rm.lootId); contents.Remove(rm.lootId);
        if (openedLoot == rm.lootId && ui != null)
        {
            if (lastSuccessfulLoot != rm.lootId || Time.unscaledTime - lastSuccessTime > 1f)
                ui.Feedback("빈 전리품", false);
            ui.Close();
        }
    }
    private void Update()
    {
        if (bootstrap == null || bootstrap.LocalPlayer == null) return;
        if (bootstrap.Session == null || !bootstrap.Session.IsConnected)
        { if (ui != null) { ui.Close(); ui.SetHint(false, string.Empty); } return; }
        if (localInput == null) localInput = bootstrap.LocalPlayer.GetComponent<Defalult_Input>();
        if(bootstrap.IsAwaitingDebugTeleport){ui?.Close();ui?.SetHint(false,string.Empty);return;}
        Vector3 position = bootstrap.LocalPlayer.transform.position;
        nearestLoot = FindNearestLootInRange(position);
        foreach (var entry in visuals) if (entry.Value != null) entry.Value.SetInRange(entry.Key == nearestLoot);
        string hint="[F] 전리품";
        if(contents.TryGetValue(nearestLoot,out var corpse) && corpse.sourceMonsterId==0) hint="[F] 사망한 플레이어 소지품";
        if(contents.TryGetValue(nearestLoot,out var nearest))hint=nearest.sourceMonsterId<EnvironmentClient.RewardChestSource?"[F] 보물 상자":nearest.sourceMonsterId<EnvironmentClient.ChestSource?"[F] 상자":hint;
        if (ui != null) ui.SetHint(nearestLoot != 0, hint);
        if (openedLoot != 0)
        {
            bool inRange = InRange(openedLoot);
            if (wasInRange != inRange) { wasInRange = inRange; RefreshOpen(); }
        }
        expired.Clear();
        foreach (var entry in pending) if (Time.unscaledTime >= entry.Value) expired.Add(entry.Key);
        foreach (long key in expired) pending.Remove(key);
        if (expired.Count > 0) { ui?.Feedback("응답 지연", false); RefreshOpen(); }
        if (ChatUI.IsTyping) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (ui != null && ui.IsOpen && (keyboard.escapeKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame)) { ui.Close(); return; }
        if (!keyboard.fKey.wasPressedThisFrame || nearestLoot == 0 || ui == null || (localInput != null && !localInput.IsGameplayInputEnabled)) return;
        openedLoot = nearestLoot; wasInRange = true;
        ui.Open(RequestItem, RequestAll, OnClosed); RefreshOpen();
        if (localInput != null) localInput.SetInputMode(PlayerInputMode.UI);
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
    }
    private void OnClosed()
    {
        openedLoot = 0;
        if (GameUIController.Instance != null) GameUIController.Instance.RefreshCurrentInputMode();
        else if (localInput != null) localInput.SetInputMode(PlayerInputMode.Gameplay);
    }
    private static long Key(int lootId, int itemHash) => ((long)lootId << 32) | (uint)itemHash;
    private bool IsPending(int hash) => pending.ContainsKey(Key(openedLoot, hash));
    private bool InRange(int lootId)
    {
        if (bootstrap == null || bootstrap.LocalPlayer == null || !active.TryGetValue(lootId, out var go) || go == null) return false;
        var d = go.transform.position - bootstrap.LocalPlayer.transform.position; d.y = 0;
        return d.sqrMagnitude <= LootConst.PICKUP_RANGE * LootConst.PICKUP_RANGE;
    }
    private void RefreshOpen()
    {
        if (ui != null && contents.TryGetValue(openedLoot, out var data)) ui.Refresh(data, catalog, InRange(openedLoot), IsPending);
    }
    private void RequestItem(int hash)
    {
        if (openedLoot == 0 || !contents.ContainsKey(openedLoot) || IsPending(hash)) return;
        if (!InRange(openedLoot)) { ui.Feedback("거리 초과", false); return; }
        pending[Key(openedLoot, hash)] = Time.unscaledTime + 4f;
        bootstrap.RequestItemPickup(openedLoot, hash, 0); RefreshOpen();
    }
    private void RequestAll()
    {
        if (!contents.TryGetValue(openedLoot, out var data)) return;
        for (int i = 0; i < data.entryCount; i++) if (data.entries[i].count > 0) RequestItem(data.entries[i].itemHash);
    }
    private void OnPickupResult(ItemPickupResult result)
    {
        pending.Remove(Key(result.lootId, result.itemHash));
        if (result.success == 1 && result.grantedCount > 0)
        {
            lastSuccessfulLoot = result.lootId; lastSuccessTime = Time.unscaledTime;
            var item = catalog != null ? catalog.Resolve(result.itemHash) : null;
            ui?.Feedback($"획득: {(item != null ? item.DisplayName : "아이템")} ×{result.grantedCount}", true);
            if (bootstrap.LocalPlayer != null) LootVfxController.PlayPickupSuccess(bootstrap.LocalPlayer.transform.position);
        }
        else
        {
            string reason = (PickupFailReason)result.failReason switch
            {
                PickupFailReason.NoLoot => "빈 전리품", PickupFailReason.TooFar => "거리 초과",
                PickupFailReason.NoItem => "이미 획득된 아이템",
                PickupFailReason.InvFull => "소지품 가득 참", PickupFailReason.Dead => "사망 중 획득 불가",
                _ => "획득 불가"
            };
            ui?.Feedback(reason, false);
        }
        RefreshOpen();
    }
    private int FindNearestLootInRange(Vector3 playerPos)
    {
        int best = 0; float bestSq = LootConst.PICKUP_RANGE * LootConst.PICKUP_RANGE;
        foreach (var kv in active)
        {
            if (kv.Value == null) continue;
            var d = kv.Value.transform.position - playerPos; d.y = 0;
            if (d.sqrMagnitude > bestSq) continue;
            bestSq = d.sqrMagnitude; best = kv.Key;
        }
        return best;
    }
}
