#include "LootSystem.h"
#include "GameSession.h"
#include "PlayerEntity.h"
#include "SessionClientConnection.h"
#include "Dungeon/CSharpRandom.h"
#include "../Common/ItemHash.h"
#include "../Common/Logger.h"

#include <cstring>

// ============================================================================
//  드롭 테이블
//
//  itemId 문자열은 클라 ItemData 에셋의 itemId와 정확히 일치해야 한다.
//  (일치하지 않으면 클라가 아이콘을 못 찾아 이름 없는 슬롯으로 표시됨)
//  밸런싱은 여기 한 곳만 고치면 되고, 클라 재빌드가 필요 없다.
// ============================================================================

static const LootTableEntry NORMAL_DROPS[] = {
    { "gold_coin",   5, 20, 50 },
    { "iron_ore",    1,  3, 30 },
    { "health_potion", 1, 1, 20 },
};

static const LootTableEntry ELITE_DROPS[] = {
    { "gold_coin",  20, 60, 40 },
    { "iron_ore",    3,  8, 25 },
    { "health_potion", 1, 2, 20 },
    { "rare_gem",    1,  1, 15 },
};

static const LootTableEntry BOSS_DROPS[] = {
    { "gold_coin",  80, 200, 35 },
    { "rare_gem",    2,   5, 35 },
    { "boss_core",   1,   1, 30 },
};

// 몬스터 종류별 드롭 테이블 + 굴리는 횟수
static void GetDropTable(int monsterKind,
    const LootTableEntry*& outTable, int& outSize, int& outRolls)
{
    switch (monsterKind)
    {
    case MONSTER_BOSS:
        outTable = BOSS_DROPS;
        outSize = (int)(sizeof(BOSS_DROPS) / sizeof(BOSS_DROPS[0]));
        outRolls = 3;
        break;
    case MONSTER_ELITE:
        outTable = ELITE_DROPS;
        outSize = (int)(sizeof(ELITE_DROPS) / sizeof(ELITE_DROPS[0]));
        outRolls = 2;
        break;
    default:
        outTable = NORMAL_DROPS;
        outSize = (int)(sizeof(NORMAL_DROPS) / sizeof(NORMAL_DROPS[0]));
        outRolls = 1;
        break;
    }
}

// ============================================================================
//  LootDrop / ServerInventory
// ============================================================================

bool LootDrop::IsEmpty() const
{
    for (const ItemStack& s : contents)
        if (s.count > 0) return false;
    return true;
}

int ServerInventory::Add(int itemHash, int count)
{
    if (count <= 0) return 0;

    auto it = items.find(itemHash);
    if (it == items.end())
    {
        // 새 종류 → 슬롯 한도 검사
        if ((int)items.size() >= MAX_INVENTORY_ENTRIES) return 0;
        items[itemHash] = count;
        return count;
    }

    it->second += count;
    return count;
}

int ServerInventory::GetCount(int itemHash) const
{
    auto it = items.find(itemHash);
    return (it == items.end()) ? 0 : it->second;
}

int ServerInventory::TotalCount() const
{
    int total = 0;
    for (const auto& kv : items) total += kv.second;
    return total;
}

void ServerInventory::FillSyncPacket(InventorySyncData& out) const
{
    std::memset(&out, 0, sizeof(out));
    int i = 0;
    for (const auto& kv : items)
    {
        if (i >= MAX_INVENTORY_ENTRIES) break;
        out.entries[i].itemHash = kv.first;
        out.entries[i].count = kv.second;
        ++i;
    }
    out.entryCount = i;
    out.totalCount = TotalCount();
}

// ============================================================================
//  LootSystem
// ============================================================================

LootSystem::LootSystem() : nextLootId(1) {}

void LootSystem::FillSpawnPacket(LootSpawnData& out, const LootDrop& drop)
{
    std::memset(&out, 0, sizeof(out));
    out.lootId = drop.lootId;
    out.sourceMonsterId = drop.sourceMonsterId;
    out.posX = drop.position.x;
    out.posY = drop.position.y;
    out.posZ = drop.position.z;

    int i = 0;
    for (const ItemStack& s : drop.contents)
    {
        if (i >= MAX_LOOT_ENTRIES) break;
        if (s.count <= 0) continue;
        out.entries[i++] = s;
    }
    out.entryCount = i;
}

void LootSystem::SpawnMonsterLoot(GameSession& session, int monsterId,
    const Vec3& position, int monsterKind, int mapSeed)
{
    // 같은 시드 + 같은 몬스터면 항상 같은 드롭 (재현 가능한 버그 추적용).
    // 클라가 예측할 필요는 없다. 결과는 LOOT_SPAWN으로 통보된다.
    CSharpRandom random(mapSeed ^ (monsterId * 2654435761u));

    const LootTableEntry* table = nullptr;
    int tableSize = 0, rolls = 0;
    GetDropTable(monsterKind, table, tableSize, rolls);

    int totalWeight = 0;
    for (int i = 0; i < tableSize; ++i) totalWeight += table[i].weight;
    if (totalWeight <= 0) return;

    LootDrop drop;
    drop.lootId = nextLootId++;
    drop.sourceMonsterId = monsterId;
    drop.position = position;

    for (int r = 0; r < rolls; ++r)
    {
        int pick = random.Next(0, totalWeight);
        int acc = 0, chosen = 0;
        for (int i = 0; i < tableSize; ++i)
        {
            acc += table[i].weight;
            if (pick < acc) { chosen = i; break; }
        }

        const LootTableEntry& e = table[chosen];
        int count = random.Next(e.minCount, e.maxCount + 1);
        if (count <= 0) continue;

        int hash = ItemHash::Of(e.itemId);

        // 같은 아이템이 또 나오면 합친다 (슬롯 낭비 방지)
        bool merged = false;
        for (ItemStack& s : drop.contents)
        {
            if (s.itemHash == hash) { s.count += count; merged = true; break; }
        }
        if (!merged && (int)drop.contents.size() < MAX_LOOT_ENTRIES)
            drop.contents.push_back({ hash, count });
    }

    if (drop.contents.empty()) return;

    int lootId = drop.lootId;
    loots[lootId] = std::move(drop);

    LootSpawnData pkt;
    FillSpawnPacket(pkt, loots[lootId]);
    session.Broadcast((int)PacketType::LOOT_SPAWN, &pkt, sizeof(pkt), 0);

    Log::Info("[Loot] 드롭 생성 lootId=%d (몬스터 %d, 종류 %d) 아이템 %d종",
        lootId, monsterId, monsterKind, pkt.entryCount);
}

void LootSystem::HandlePickupRequest(GameSession& session, int clientId,
    const ItemPickupRequest& req)
{
    ItemPickupResult res{};
    res.lootId = req.lootId;
    res.itemHash = req.itemHash;

    PlayerEntity* p = session.GetPlayerForLoot(clientId);
    if (!p) return;

    auto sendResult = [&](int reason, int granted)
        {
            res.failReason = reason;
            res.grantedCount = granted;
            res.success = (reason == PICKUP_OK) ? 1 : 0;
            if (p->conn && p->conn->active)
                p->conn->SendPacket((int)PacketType::ITEM_PICKUP_RESULT, &res, sizeof(res));
        };

    // 1) 상태 검사
    if (!p->IsActiveInWorld()) { sendResult(PICKUP_FAIL_DEAD, 0); return; }

    // 2) 컨테이너 존재 검사
    auto lit = loots.find(req.lootId);
    if (lit == loots.end()) { sendResult(PICKUP_FAIL_NO_LOOT, 0); return; }
    LootDrop& drop = lit->second;

    // 3) 거리 검사 (서버 권위 위치 기준)
    if (p->position.DistanceSqXZ(drop.position) > LOOT_PICKUP_RANGE_SQ)
    {
        sendResult(PICKUP_FAIL_TOO_FAR, 0);
        return;
    }

    // 4) 재고 검사
    ItemStack* stack = nullptr;
    for (ItemStack& s : drop.contents)
        if (s.itemHash == req.itemHash && s.count > 0) { stack = &s; break; }

    if (!stack) { sendResult(PICKUP_FAIL_NO_ITEM, 0); return; }

    // count<=0 이면 전량 요청. 재고보다 많이 요청하면 재고만큼만.
    int want = (req.count <= 0) ? stack->count : req.count;
    if (want > stack->count) want = stack->count;

    // 5) 인벤토리 여유 검사 + 적립
    ServerInventory& inv = inventories[clientId];
    int granted = inv.Add(req.itemHash, want);
    if (granted <= 0) { sendResult(PICKUP_FAIL_INV_FULL, 0); return; }

    stack->count -= granted;
    sendResult(PICKUP_OK, granted);
    SendInventorySync(session, clientId);

    Log::Info("[Loot] cid=%d 획득 lootId=%d hash=%d x%d (보유 총 %d)",
        clientId, req.lootId, req.itemHash, granted, inv.TotalCount());

    // 6) 비었으면 컨테이너 제거 통보
    if (drop.IsEmpty())
    {
        LootRemovedData rm{};
        rm.lootId = req.lootId;
        loots.erase(lit);
        session.Broadcast((int)PacketType::LOOT_REMOVED, &rm, sizeof(rm), 0);
    }
    else
    {
        // 남은 수량을 모두에게 다시 알림 (다른 클라의 UI 갱신용)
        LootSpawnData pkt;
        FillSpawnPacket(pkt, drop);
        session.Broadcast((int)PacketType::LOOT_SPAWN, &pkt, sizeof(pkt), 0);
    }
}

void LootSystem::SendAllLootTo(GameSession& session, int clientId)
{
    PlayerEntity* p = session.GetPlayerForLoot(clientId);
    if (!p || !p->conn || !p->conn->active) return;

    for (const auto& kv : loots)
    {
        LootSpawnData pkt;
        FillSpawnPacket(pkt, kv.second);
        p->conn->SendPacket((int)PacketType::LOOT_SPAWN, &pkt, sizeof(pkt));
    }
    SendInventorySync(session, clientId);
}

void LootSystem::SendInventorySync(GameSession& session, int clientId)
{
    PlayerEntity* p = session.GetPlayerForLoot(clientId);
    if (!p || !p->conn || !p->conn->active) return;

    InventorySyncData sync;
    inventories[clientId].FillSyncPacket(sync);
    p->conn->SendPacket((int)PacketType::INVENTORY_SYNC, &sync, sizeof(sync));
}

int LootSystem::GetTotalItemCount(int clientId)
{
    auto it = inventories.find(clientId);
    return (it == inventories.end()) ? 0 : it->second.TotalCount();
}
