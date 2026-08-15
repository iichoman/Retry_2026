#include "WorldSimulation.h"
#include "PlayerEntity.h"
#include "PositionValidator.h"
#include "Dungeon/CSharpRandom.h"
#include "../Common/Logger.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <vector>

using namespace std::chrono;

WorldSimulation::WorldSimulation()
    : nextMonsterId(1)
{
}

// ============================================================================
//  SpawnMonsters - 시드 기반 결정적 스폰
// ============================================================================
void WorldSimulation::SpawnMonsters(const DungeonGenerator& dungeon, int seed)
{
    monsters.clear();

    // "Mons" 마법수로 시드 분리 (던전 RNG와 충돌 방지)
    CSharpRandom random(seed ^ 0x4D6F6E73);

    for (const Room& room : dungeon.rooms)
    {
        if (room.type == ROOM_TYPE_START) continue;

        int spawnCount;
        int monsterKind;

        if (room.type == ROOM_TYPE_BOSS)
        {
            spawnCount = 1;
            monsterKind = MONSTER_BOSS;
        }
        else
        {
            spawnCount = random.Next(1, 4);
            monsterKind = (random.NextDouble() < 0.2) ? MONSTER_ELITE : MONSTER_NORMAL;
        }

        // unordered_set은 비결정적 → 결정성 보장 위해 정렬
        std::vector<IntVec3> floorList(room.floorTiles.begin(), room.floorTiles.end());
        std::sort(floorList.begin(), floorList.end(),
            [](const IntVec3& a, const IntVec3& b) {
                if (a.x != b.x) return a.x < b.x;
                if (a.y != b.y) return a.y < b.y;
                return a.z < b.z;
            });
        if (floorList.empty()) continue;

        for (int i = 0; i < spawnCount; ++i)
        {
            int idx = random.Next(0, (int)floorList.size());
            const IntVec3& tile = floorList[idx];
            // 격자 좌표 → 월드 좌표 (worldOffset 적용된 것)
            Vec3 worldPos = dungeon.TileToWorldCenter(tile);

            int mid = nextMonsterId++;
            auto m = std::make_unique<MonsterEntity>(mid, monsterKind, worldPos);
            monsters[mid] = std::move(m);
        }
    }

    Log::Info("[WorldSim] 몬스터 스폰 완료: %d마리 (방 %d개)",
        (int)monsters.size(), (int)dungeon.rooms.size());
}

// ============================================================================
//  Step - 매 틱
// ============================================================================
void WorldSimulation::Step(float dt,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
    const DungeonGenerator& dungeon,
    std::vector<AttackEvent>& outAttacks)
{
    long long nowMs = duration_cast<milliseconds>(
        system_clock::now().time_since_epoch()).count();

    for (auto& kv : monsters)
    {
        MonsterEntity& m = *kv.second;
        if (m.aiState == AI_DEAD) continue;
        StepMonsterAI(m, dt, nowMs, players, dungeon, outAttacks);
    }
}

// ============================================================================
//  StepMonsterAI - 단일 몬스터 1프레임 AI
//
//  단순 FSM:
//    IDLE   - 가만히. 일정 주기마다 주변 검색 → 감지 시 CHASE
//    CHASE  - 타겟 추격. 사거리 안 → ATTACK. 타겟 사망/이탈 → IDLE
//    ATTACK - 공격 쿨다운 후 데미지 발생. 사거리 벗어나면 CHASE.
//    DEAD   - 처리 안 함
//
//  벽 충돌은 단순화 (직선 이동). 추후 CollisionMesh 적용 시 보강.
// ============================================================================
void WorldSimulation::StepMonsterAI(MonsterEntity& m, float dt, long long nowMs,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
    const DungeonGenerator& dungeon,
    std::vector<AttackEvent>& outAttacks)
{
    // 공격 wind-up: 예약된 타격 시간 도달 시 실제 데미지 발생(LAND)
    if (m.pendingHitTime > 0 && nowMs >= m.pendingHitTime)
    {
        AttackEvent land{};
        land.monsterId = m.id;
        land.victimClientId = m.pendingVictim;
        land.damage = m.pendingDamage;   // damage>0 → 클라는 데미지만 반영(애니 X)
        outAttacks.push_back(land);
        m.pendingHitTime = 0;
        m.pendingVictim = 0;
        m.pendingDamage = 0;
    }

    // 타겟 유효성 검증
    PlayerEntity* target = nullptr;
    if (m.targetClientId != 0)
    {
        auto it = players.find(m.targetClientId);
        if (it != players.end() && it->second->hp > 0)
        {
            target = it->second.get();
        }
        else
        {
            m.targetClientId = 0;
            m.aiState = AI_IDLE;
        }
    }

    switch (m.aiState)
    {
    case AI_IDLE:
    {
        // 일정 주기마다 주변 플레이어 감지 (0.5초 주기)
        if (nowMs - m.lastDecisionTime > 500)
        {
            m.lastDecisionTime = nowMs;
            int found = FindNearestPlayerInRange(m, players);
            if (found != 0)
            {
                m.targetClientId = found;
                m.aiState = AI_CHASE;
            }
        }
        break;
    }

    case AI_CHASE:
    {
        if (!target) { m.aiState = AI_IDLE; m.targetClientId = 0; break; }

        float dist = m.position.DistanceXZ(target->position);

        // 추격 한계
        if (dist > m.detectRange * 1.5f)
        {
            m.targetClientId = 0;
            m.aiState = AI_IDLE;
            break;
        }

        if (dist <= m.attackRange)
        {
            m.aiState = AI_ATTACK;
            break;
        }

        // 타겟 방향으로 직선 이동 (XZ 평면)
        float dx = target->position.x - m.position.x;
        float dz = target->position.z - m.position.z;
        float invLen = 1.f / std::max(0.001f, std::sqrt(dx * dx + dz * dz));
        float vx = dx * invLen * m.moveSpeed;
        float vz = dz * invLen * m.moveSpeed;

        // 새 위치 후보
        Vec3 attempted(m.position.x + vx * dt, m.position.y, m.position.z + vz * dt);

        // 위치 검증: 벽 통과 시 슬라이딩, 텔레포트 시 거부
        m.position = PositionValidator::ValidateMove(
            dungeon, m.position, attempted, m.moveSpeed, dt);

        // 타겟 방향으로 회전 (degrees, Unity Y축)
        m.rotY = std::atan2(dx, dz) * 180.f / 3.14159265358979f;
        break;
    }

    case AI_ATTACK:
    {
        if (!target) { m.aiState = AI_IDLE; m.targetClientId = 0; break; }

        float dist = m.position.DistanceXZ(target->position);
        if (dist > m.attackRange)
        {
            m.aiState = AI_CHASE;
            break;
        }

        // 타겟 향해 회전
        float dx = target->position.x - m.position.x;
        float dz = target->position.z - m.position.z;
        m.rotY = std::atan2(dx, dz) * 180.f / 3.14159265358979f;

        // 쿨다운 후 공격: 준비동작(START, 애니만) → windup 뒤 실제 타격(LAND)
        if (nowMs - m.lastAttackTime >= (long long)m.attackCooldownMs && m.pendingHitTime == 0)
        {
            m.lastAttackTime = nowMs;

            // START: 공격 애니메이션 트리거 (damage=0 → HP 변화 없음)
            AttackEvent start{};
            start.monsterId = m.id;
            start.victimClientId = target->clientId;
            start.damage = 0;
            outAttacks.push_back(start);

            // windup 후 실제 데미지 예약 (애니메이션 타격 프레임과 맞춤)
            const long long kAttackWindupMs = 600;
            m.pendingHitTime = nowMs + kAttackWindupMs;
            m.pendingVictim = target->clientId;
            m.pendingDamage = m.attackDamage;
        }
        break;
    }

    default:
        break;
    }
}

// ============================================================================
//  FindNearestPlayerInRange
// ============================================================================
int WorldSimulation::FindNearestPlayerInRange(const MonsterEntity& m,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players)
{
    int   nearestId = 0;
    float bestSq = m.detectRange * m.detectRange;

    for (auto& kv : players)
    {
        const PlayerEntity& p = *kv.second;
        if (!p.IsActiveInWorld()) continue;
        if (!p.conn) continue;       // 끊긴 클라는 인지 안 함 (단순화)

        float d2 = m.position.DistanceSqXZ(p.position);
        if (d2 < bestSq)
        {
            bestSq = d2;
            nearestId = p.clientId;
        }
    }
    return nearestId;
}

// ============================================================================
//  ApplyDamageToMonster
// ============================================================================
bool WorldSimulation::ApplyDamageToMonster(int monsterId, int damage)
{
    auto it = monsters.find(monsterId);
    if (it == monsters.end()) return false;

    MonsterEntity& m = *it->second;
    if (m.aiState == AI_DEAD) return false;

    m.hp -= damage;
    if (m.hp <= 0)
    {
        m.hp = 0;
        m.aiState = AI_DEAD;
        m.targetClientId = 0;
        return true;
    }
    return false;
}