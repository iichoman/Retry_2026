using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  ServerInventoryBridge
//
//  서버 권위 인벤토리를 클라에 반영하는 브리지.
//
//  역할:
//   1) itemHash → ItemData 해석 (inspector에 등록한 목록 기준)
//   2) INVENTORY_SYNC를 받아 로컬 PlayerInventory를 서버 상태로 덮어쓰기
//   3) 루팅 요청 송신 헬퍼
//
//  중요: 로컬 PlayerInventory는 이제 "표시용 사본"이다.
//        직접 TryAdd/TryRemove를 호출하면 서버와 어긋나고, 다음 SYNC에서
//        되돌려진다. 아이템 획득은 반드시 RequestPickup을 거칠 것.
//
//  Inspector 설정:
//   - knownItems: 이 게임에 등장하는 모든 ItemData. 서버 드롭 테이블의
//     itemId 문자열과 ItemData.ItemId가 일치해야 아이콘이 붙는다.
// ============================================================================
[DisallowMultipleComponent]
public class ServerInventoryBridge : MonoBehaviour
{
    [Tooltip("등장하는 모든 ItemData. 서버 드롭 테이블의 itemId와 일치해야 함.")]
    [SerializeField] private List<ItemData> knownItems = new List<ItemData>();

    private readonly Dictionary<int, ItemData> byHash = new Dictionary<int, ItemData>();
    private PlayerInventory localInventory;
    private NetworkBootstrap bootstrap;

    /// <summary>서버가 통보한 총 보유 개수. 탈출 결과의 itemCount와 같은 기준.</summary>
    public int TotalItemCount { get; private set; }

    private void Awake()
    {
        BuildLookup();
    }

    public void Initialize(NetworkBootstrap bs, PlayerInventory inventory)
    {
        bootstrap = bs;
        localInventory = inventory;

        if (bootstrap != null)
        {
            bootstrap.InventorySyncReceived += OnInventorySync;
            bootstrap.PickupResultReceived += OnPickupResult;
        }
    }

    private void OnDestroy()
    {
        if (bootstrap != null)
        {
            bootstrap.InventorySyncReceived -= OnInventorySync;
            bootstrap.PickupResultReceived -= OnPickupResult;
        }
    }

    private void BuildLookup()
    {
        byHash.Clear();
        for (int i = 0; i < knownItems.Count; i++)
        {
            ItemData item = knownItems[i];
            if (item == null || string.IsNullOrEmpty(item.ItemId)) continue;

            int hash = ItemHash.Of(item.ItemId);
            if (byHash.ContainsKey(hash))
            {
                Debug.LogError($"[Inventory] 아이템 해시 충돌: {item.ItemId} — itemId를 바꿀 것", item);
                continue;
            }
            byHash[hash] = item;
        }
    }

    /// <summary>해시로 ItemData 찾기. 등록 안 된 아이템이면 null.</summary>
    public ItemData Resolve(int itemHash)
    {
        return byHash.TryGetValue(itemHash, out ItemData item) ? item : null;
    }

    /// <summary>루팅 요청. 성립 여부는 서버가 판정한다. count 0 이하 = 전량.</summary>
    public void RequestPickup(int lootId, int itemHash, int count = 0)
    {
        if (bootstrap == null) return;
        bootstrap.RequestItemPickup(lootId, itemHash, count);
    }

    /// <summary>ItemData로 루팅 요청 (UI에서 쓰기 편한 형태).</summary>
    public void RequestPickup(int lootId, ItemData item, int count = 0)
    {
        if (item == null) return;
        RequestPickup(lootId, ItemHash.Of(item.ItemId), count);
    }

    // 서버 인벤토리를 로컬에 그대로 반영. 로컬 상태는 신뢰하지 않는다.
    private void OnInventorySync(InventorySyncData sync)
    {
        TotalItemCount = sync.totalCount;

        if (localInventory == null) return;

        localInventory.ClearAll();

        int n = Mathf.Min(sync.entryCount, LootConst.MAX_INVENTORY_ENTRIES);
        for (int i = 0; i < n; i++)
        {
            ItemStack stack = sync.entries[i];
            ItemData item = Resolve(stack.itemHash);
            if (item == null)
            {
                Debug.LogWarning($"[Inventory] 모르는 아이템 해시 {stack.itemHash} — knownItems에 등록 필요");
                continue;
            }
            localInventory.TryAdd(item, stack.count);
        }
    }

    private void OnPickupResult(ItemPickupResult result)
    {
        if (result.success == 1) return;   // 성공은 INVENTORY_SYNC가 처리
        Debug.LogWarning($"[Inventory] 루팅 거부: {(PickupFailReason)result.failReason}");
    }
}
