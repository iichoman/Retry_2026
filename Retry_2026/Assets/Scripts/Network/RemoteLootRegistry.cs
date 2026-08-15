using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
//  RemoteLootRegistry
//
//  서버가 생성한 전리품 컨테이너를 월드에 표시하고 루팅 요청을 중계한다.
//   - OnSpawn   : 컨테이너 생성 또는 내용물 갱신
//   - OnRemoved : 컨테이너 소멸 (내용물 소진)
//
//  컨테이너 내용물은 서버가 소유한다. 클라는 표시만 하고,
//  플레이어가 F키(또는 UI)로 루팅하면 서버에 요청만 보낸다.
//
//  Inspector 설정:
//   - LootPrefab: 비우면 기본 Cube로 대체 표시
// ============================================================================
public class RemoteLootRegistry : MonoBehaviour
{
    [Tooltip("전리품 컨테이너 prefab. 비우면 기본 Cube 생성.")]
    public GameObject LootPrefab;

    [SerializeField] private Vector3 lootScale = new Vector3(0.6f, 0.4f, 0.6f);


    private NetworkBootstrap bootstrap;

    private readonly Dictionary<int, GameObject> active = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, LootSpawnData> contents = new Dictionary<int, LootSpawnData>();

    public void Initialize(NetworkBootstrap bs)
    {
        bootstrap = bs;
        if (bootstrap == null) return;

        bootstrap.LootSpawnReceived += OnSpawn;
        bootstrap.LootRemovedReceived += OnRemoved;
    }

    private void OnDestroy()
    {
        if (bootstrap == null) return;

        bootstrap.LootSpawnReceived -= OnSpawn;
        bootstrap.LootRemovedReceived -= OnRemoved;
    }

    public void OnSpawn(LootSpawnData sp)
    {
        contents[sp.lootId] = sp;   // 갱신 패킷이면 내용물만 덮어씀

        if (active.ContainsKey(sp.lootId)) return;

        GameObject go;
        if (LootPrefab != null)
        {
            go = Instantiate(LootPrefab);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = lootScale;
        }

        go.name = $"Loot_{sp.lootId}";
        go.transform.position = new Vector3(sp.posX, sp.posY, sp.posZ);
        active[sp.lootId] = go;
    }

    public void OnRemoved(LootRemovedData rm)
    {
        if (active.TryGetValue(rm.lootId, out GameObject go) && go != null)
        {
            Destroy(go);
        }
        active.Remove(rm.lootId);
        contents.Remove(rm.lootId);
    }

    private void Update()
    {
        if (bootstrap == null || bootstrap.LocalPlayer == null) return;
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.fKey.wasPressedThisFrame) return;

        int lootId = FindNearestLootInRange(bootstrap.LocalPlayer.transform.position);
        if (lootId == 0) return;

        // 컨테이너 내용물을 전부 요청. 거리/재고/여유 검증은 서버가 한다.
        LootSpawnData data = contents[lootId];
        int n = Mathf.Min(data.entryCount, LootConst.MAX_LOOT_ENTRIES);
        for (int i = 0; i < n; i++)
        {
            bootstrap.RequestItemPickup(lootId, data.entries[i].itemHash, 0);
        }
    }

    // 루팅 사거리 안의 가장 가까운 컨테이너. 없으면 0.
    private int FindNearestLootInRange(Vector3 playerPos)
    {
        int best = 0;
        float bestSq = LootConst.PICKUP_RANGE * LootConst.PICKUP_RANGE;

        foreach (var kv in active)
        {
            if (kv.Value == null) continue;

            Vector3 d = kv.Value.transform.position - playerPos;
            d.y = 0f;                       // 서버 판정과 동일하게 XZ 평면
            float sq = d.sqrMagnitude;
            if (sq > bestSq) continue;

            bestSq = sq;
            best = kv.Key;
        }

        return best;
    }
}
