#include "InterestManagement.h"
#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "SessionClientConnection.h"
#include "Dungeon/DungeonGenerator.h"
#include "../Common/PacketProtocol.h"

#include <chrono>
#include <cmath>
#include <cstring>
#include <unordered_map>
#include <unordered_set>
#include <vector>

using namespace std::chrono;

// ============================================================================
//  시야선 검사 (벽 가림)
// ============================================================================
bool InterestManagement::HasLineOfSight(const DungeonGenerator& dungeon,
    const Vec3& fromPos, const Vec3& toPos)
{
    float dx = toPos.x - fromPos.x;
    float dz = toPos.z - fromPos.z;
    float len = std::sqrt(dx * dx + dz * dz);
    if (len < 0.5f) return true;

    int steps = (int)(len / 0.5f);
    if (steps > 90) steps = 90;        // 안전 상한 (45m/0.5)

    for (int i = 1; i < steps; ++i)
    {
        float t = (float)i / (float)steps;
        Vec3 sample(fromPos.x + dx * t, 0.f, fromPos.z + dz * t);
        IntVec3 tile = dungeon.WorldToTile(sample);
        tile.y = 0;
        if (dungeon.IsWallTile(tile))
            return false;
    }
    return true;
}

// ============================================================================
//  포탈 그래프 가시성 판정
// ============================================================================
bool InterestManagement::CanSee(const DungeonGenerator& dungeon,
    const Vec3& fromPos, int fromNode,
    const Vec3& toPos, int toNode)
{
    // 노드 미분류(방/복도 밖) → 거리 + 시야선 fallback
    if (fromNode < 0 || toNode < 0)
    {
        float distSq = fromPos.DistanceSqXZ(toPos);
        if (distSq > VIEW_RANGE_SQ) return false;
        if (USE_LOS)
            return HasLineOfSight(dungeon, fromPos, toPos);
        return true;
    }

    // 같은 노드이거나 1-hop 인접 노드면 보임 (거리 무관, 시야선 생략).
    //  AreNodesAdjacent는 fromNode==toNode인 경우도 true를 반환한다.
    return dungeon.AreNodesAdjacent(fromNode, toNode);
}

// ============================================================================
//  패킷 본문 구성 헬퍼
// ============================================================================
static void FillPlayerEnter(PlayerEnterView& ev, const PlayerEntity& p)
{
    std::memset(&ev, 0, sizeof(ev));
    ev.clientId = p.clientId;
    std::strncpy(ev.playerName, p.playerName, sizeof(ev.playerName) - 1);
    ev.posX = p.position.x;
    ev.posY = p.position.y;
    ev.posZ = p.position.z;
    ev.rotY = p.rotY;
    ev.hp = p.hp;
    ev.maxHp = p.maxHp;
}

static void FillPlayerMove(PlayerMove& pm, const PlayerEntity& p, long long ts)
{
    std::memset(&pm, 0, sizeof(pm));
    pm.clientId = p.clientId;
    pm.posX = p.position.x;
    pm.posY = p.position.y;
    pm.posZ = p.position.z;
    pm.rotY = p.rotY;
    pm.speed = p.speed;
    pm.animState = p.animState;
    pm.timestamp = ts;
}

static void FillMonsterEnter(MonsterEnterView& ev, const MonsterEntity& m)
{
    std::memset(&ev, 0, sizeof(ev));
    ev.monsterId = m.id;
    ev.monsterKind = m.kind;
    ev.posX = m.position.x;
    ev.posY = m.position.y;
    ev.posZ = m.position.z;
    ev.rotY = m.rotY;
    ev.hp = m.hp;
    ev.maxHp = m.maxHp;
}

static void FillMonsterMove(MonsterMove& mm, const MonsterEntity& m, long long ts)
{
    std::memset(&mm, 0, sizeof(mm));
    mm.monsterId = m.id;
    mm.posX = m.position.x;
    mm.posY = m.position.y;
    mm.posZ = m.position.z;
    mm.rotY = m.rotY;
    mm.aiState = m.aiState;
    mm.targetClientId = m.targetClientId;
    mm.timestamp = ts;
}

static inline void SafeSend(PlayerEntity& target, int packetType,
    const void* body, int size)
{
    if (target.conn && target.conn->active)
        target.conn->SendPacket(packetType, body, size);
}

// ============================================================================
//  UpdateAll - 매 틱 호출되는 핵심
// ============================================================================
void InterestManagement::UpdateAll(GameSession& session)
{
    auto& players = session.players;
    auto& monsters = session.worldSim.GetMonsters();
    const DungeonGenerator& dungeon = session.dungeon;

    long long ts = duration_cast<milliseconds>(
        system_clock::now().time_since_epoch()).count();

    // --- 0단계: 모든 객체의 소속 방을 1회 계산하여 캐시 ---
    std::unordered_map<int, int> playerNode;    // clientId -> roomId
    std::unordered_map<int, int> monsterNode;   // monsterId -> roomId
    for (auto& kv : players)
        playerNode[kv.first] = dungeon.NodeIdAt(kv.second->position);
    for (auto& kv : monsters)
    {
        if (kv.second->aiState == AI_DEAD) continue;
        monsterNode[kv.second->id] = dungeon.NodeIdAt(kv.second->position);
    }

    // --- 각 플레이어 시야 갱신 ---
    for (auto& kv : players)
    {
        PlayerEntity& p = *kv.second;
        if (!p.conn || !p.conn->active) continue;

        int pNode = playerNode[p.clientId];

        // 1) 새 시야 계산 (방 기반 + 시야선)
        std::unordered_set<int> nearPlayers;
        std::unordered_set<int> nearMonsters;

        for (auto& kv2 : players)
        {
            if (kv2.first == p.clientId) continue;
            const PlayerEntity& op = *kv2.second;
            int oNode = playerNode[op.clientId];
            if (CanSee(dungeon, p.position, pNode, op.position, oNode))
                nearPlayers.insert(op.clientId);
        }
        for (auto& kv2 : monsters)
        {
            const MonsterEntity& m = *kv2.second;
            if (m.aiState == AI_DEAD) continue;
            int mNode = monsterNode[m.id];
            if (CanSee(dungeon, p.position, pNode, m.position, mNode))
                nearMonsters.insert(m.id);
        }

        // 2) 플레이어 ENTER/MOVE/LEAVE 처리
        for (int id : nearPlayers)
        {
            auto pit = players.find(id);
            if (pit == players.end()) continue;
            const PlayerEntity& op = *pit->second;
            if (p.viewedPlayers.count(id))
            {
                PlayerMove pm; FillPlayerMove(pm, op, ts);
                SafeSend(p, (int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
            }
            else
            {
                PlayerEnterView ev; FillPlayerEnter(ev, op);
                SafeSend(p, (int)PacketType::PLAYER_ENTER_VIEW, &ev, sizeof(ev));
            }
        }
        for (int id : p.viewedPlayers)
        {
            if (!nearPlayers.count(id))
            {
                PlayerLeaveView lv; std::memset(&lv, 0, sizeof(lv));
                lv.clientId = id;
                SafeSend(p, (int)PacketType::PLAYER_LEAVE_VIEW, &lv, sizeof(lv));
            }
        }
        p.viewedPlayers = nearPlayers;

        // 3) 몬스터 ENTER/MOVE/LEAVE 처리
        for (int id : nearMonsters)
        {
            auto mit = monsters.find(id);
            if (mit == monsters.end()) continue;
            const MonsterEntity& m = *mit->second;
            if (p.viewedMonsters.count(id))
            {
                MonsterMove mm; FillMonsterMove(mm, m, ts);
                SafeSend(p, (int)PacketType::MONSTER_MOVE, &mm, sizeof(mm));
            }
            else
            {
                MonsterEnterView ev; FillMonsterEnter(ev, m);
                SafeSend(p, (int)PacketType::MONSTER_ENTER_VIEW, &ev, sizeof(ev));
            }
        }
        for (int id : p.viewedMonsters)
        {
            if (!nearMonsters.count(id))
            {
                MonsterLeaveView lv; std::memset(&lv, 0, sizeof(lv));
                lv.monsterId = id;
                SafeSend(p, (int)PacketType::MONSTER_LEAVE_VIEW, &lv, sizeof(lv));
            }
        }
        p.viewedMonsters = nearMonsters;
    }
}

// ============================================================================
//  OnPlayerJoin - 새 클라 입장 시 즉시 양방향 시야 동기화
// ============================================================================
void InterestManagement::OnPlayerJoin(GameSession& session, int newClientId)
{
    auto& players = session.players;
    auto& monsters = session.worldSim.GetMonsters();
    const DungeonGenerator& dungeon = session.dungeon;

    auto it = players.find(newClientId);
    if (it == players.end()) return;
    PlayerEntity& self = *it->second;
    int selfNode = dungeon.NodeIdAt(self.position);

    // 1) 본인 시야: 다른 플레이어 + 몬스터 ENTER 송신
    for (auto& kv : players)
    {
        if (kv.first == newClientId) continue;
        PlayerEntity& op = *kv.second;
        int oNode = dungeon.NodeIdAt(op.position);
        if (CanSee(dungeon, self.position, selfNode, op.position, oNode))
        {
            PlayerEnterView ev; FillPlayerEnter(ev, op);
            SafeSend(self, (int)PacketType::PLAYER_ENTER_VIEW, &ev, sizeof(ev));
            self.viewedPlayers.insert(op.clientId);
        }
    }
    for (auto& kv : monsters)
    {
        const MonsterEntity& m = *kv.second;
        if (m.aiState == AI_DEAD) continue;
        int mNode = dungeon.NodeIdAt(m.position);
        if (CanSee(dungeon, self.position, selfNode, m.position, mNode))
        {
            MonsterEnterView ev; FillMonsterEnter(ev, m);
            SafeSend(self, (int)PacketType::MONSTER_ENTER_VIEW, &ev, sizeof(ev));
            self.viewedMonsters.insert(m.id);
        }
    }

    // 2) 본인이 다른 플레이어 시야에 들어가는 경우: 그들에게 ENTER
    for (auto& kv : players)
    {
        if (kv.first == newClientId) continue;
        PlayerEntity& op = *kv.second;
        int oNode = dungeon.NodeIdAt(op.position);
        if (CanSee(dungeon, op.position, oNode, self.position, selfNode))
        {
            PlayerEnterView ev; FillPlayerEnter(ev, self);
            SafeSend(op, (int)PacketType::PLAYER_ENTER_VIEW, &ev, sizeof(ev));
            op.viewedPlayers.insert(self.clientId);
        }
    }
}

// ============================================================================
//  OnPlayerLeave - 퇴장 시 다른 플레이어 시야에서 제거
// ============================================================================
void InterestManagement::OnPlayerLeave(GameSession& session, int leavingClientId)
{
    auto& players = session.players;
    for (auto& kv : players)
    {
        if (kv.first == leavingClientId) continue;
        PlayerEntity& op = *kv.second;
        if (op.viewedPlayers.erase(leavingClientId) > 0)
        {
            PlayerLeaveView lv; std::memset(&lv, 0, sizeof(lv));
            lv.clientId = leavingClientId;
            SafeSend(op, (int)PacketType::PLAYER_LEAVE_VIEW, &lv, sizeof(lv));
        }
    }
}

// ============================================================================
//  OnMonsterDeath - 몬스터 사망 시 시야 안 플레이어에게 MONSTER_DIED
// ============================================================================
void InterestManagement::OnMonsterDeath(GameSession& session, int monsterId)
{
    auto& players = session.players;
    for (auto& kv : players)
    {
        PlayerEntity& p = *kv.second;
        if (p.viewedMonsters.erase(monsterId) > 0)
        {
            MonsterDied md; std::memset(&md, 0, sizeof(md));
            md.monsterId = monsterId;
            md.killerId = 0;
            SafeSend(p, (int)PacketType::MONSTER_DIED, &md, sizeof(md));
        }
    }
}