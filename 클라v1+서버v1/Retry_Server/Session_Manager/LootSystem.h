#pragma once
#include "../Common/MathTypes.h"
#include "../Common/PacketProtocol.h"

#include <string>
#include <unordered_map>
#include <vector>

class GameSession;
class PlayerEntity;
class CSharpRandom;

// ============================================================================
//  LootSystem (서버 권위 전리품 + 인벤토리)
//
//  왜 서버로 옮겼나:
//   탈출 시 "가지고 나간 아이템 수"를 판정하려면 인벤토리가 서버에 있어야 한다.
//   클라가 보고하는 값은 위조 가능하므로 전리품 생성부터 획득까지 서버가 소유한다.
//
//  흐름:
//   1) 몬스터 사망 → SpawnMonsterLoot: 시드 기반 결정적 드롭 결정
//      → LootDrop 생성 + LOOT_SPAWN broadcast
//   2) 클라가 컨테이너에 접근해 ITEM_PICKUP_REQUEST 송신
//   3) HandlePickupRequest: 거리/재고/인벤토리 여유 검증
//      → 성공 시 서버 인벤토리에 적립 + ITEM_PICKUP_RESULT + INVENTORY_SYNC
//      → 컨테이너가 비면 LOOT_REMOVED broadcast
//   4) 탈출 시 GameSession이 GetTotalItemCount로 결과를 채움
//
//  범위 메모:
//   현재 전리품 출처는 몬스터 드롭뿐이다. 맵에 미리 배치된 상자/아이템
//   (RoomObjectPlacer가 클라에서 생성)은 아직 서버가 모른다. 그쪽까지
//   권위로 옮기려면 배치 알고리즘을 던전처럼 시드 동기 포팅해야 한다.
//
//  스레드: 모든 메서드는 GameSession::mtx 잠긴 상태에서 호출된다.
// ============================================================================

// 서버가 아는 아이템 1종의 드롭 정의
struct LootTableEntry
{
    const char* itemId;        // 클라 ItemData.itemId와 동일 문자열
    int         minCount;
    int         maxCount;
    int         weight;        // 추첨 가중치
};

// 월드에 놓인 전리품 컨테이너 1개
struct LootDrop
{
    int                    lootId;
    int                    sourceMonsterId;
    Vec3                   position;
    std::vector<ItemStack> contents;

    bool IsEmpty() const;
};

// 플레이어 1명의 서버 권위 인벤토리
class ServerInventory
{
public:
    // 아이템 적립. 슬롯(아이템 종류) 한도를 넘으면 실패.
    // 반환: 실제로 넣은 수량 (0이면 실패).
    int Add(int itemHash, int count);

    int  GetCount(int itemHash) const;
    int  TotalCount() const;
    int  DistinctCount() const { return (int)items.size(); }

    // INVENTORY_SYNC 본문 구성.
    void FillSyncPacket(InventorySyncData& out) const;

private:
    std::unordered_map<int, int> items;   // itemHash → count
};

class LootSystem
{
public:
    LootSystem();

    // 몬스터 사망 시 호출. 드롭이 있으면 컨테이너 생성 + LOOT_SPAWN broadcast.
    // 같은 (mapSeed, monsterId)면 항상 같은 결과 (재현 가능).
    void SpawnMonsterLoot(GameSession& session, int monsterId,
        const Vec3& position, int monsterKind, int mapSeed);

    // ITEM_PICKUP_REQUEST 처리. 검증 후 결과를 요청자에게 송신.
    void HandlePickupRequest(GameSession& session, int clientId,
        const ItemPickupRequest& req);

    // 새 클라 입장 시: 이미 월드에 있는 컨테이너들을 알려준다.
    void SendAllLootTo(GameSession& session, int clientId);

    ServerInventory& GetInventory(int clientId) { return inventories[clientId]; }
    int GetTotalItemCount(int clientId);

private:
    std::unordered_map<int, LootDrop>        loots;         // lootId → 컨테이너
    std::unordered_map<int, ServerInventory> inventories;   // clientId → 인벤토리
    int nextLootId;

    void SendInventorySync(GameSession& session, int clientId);
    static void FillSpawnPacket(LootSpawnData& out, const LootDrop& drop);
};
