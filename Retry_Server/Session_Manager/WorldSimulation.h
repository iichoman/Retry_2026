#pragma once
#include "MonsterEntity.h"
#include "Dungeon/DungeonGenerator.h"

#include <functional>
#include <memory>
#include <unordered_map>
#include <vector>

class PlayerEntity;
class GameSession;

// ============================================================================
//  WorldSimulation
//
//  몬스터 시뮬레이션 (스폰/위치/AI). 플레이어 입력은 GameSession이 처리.
//
//  설계:
//   - GameSession이 mutex를 들고 있는 컨텍스트에서 호출됨
//   - SpawnMonsters: 던전 생성 직후 1회 호출. 시드 기반 결정적 스폰.
//   - Step: 매 틱 호출. AI 의사결정 + 위치 갱신 + 공격 판정.
//   - 공격 발생 시 GameSession에 콜백 (MONSTER_ATTACK_EVENT 송신용)
// ============================================================================

class WorldSimulation
{
public:
    // 몬스터가 플레이어를 공격한 이벤트 (AI가 판정).
    // GameSession이 이 콜백을 받아 데미지 적용 + 패킷 송신.
    struct AttackEvent {
        int monsterId;
        int victimClientId;
        int damage;
    };

    WorldSimulation();

    void SpawnMonsters(const DungeonGenerator& dungeon, int seed);

    // 매 틱 호출. session.mtx 잠긴 상태에서.
    // 인자로 플레이어 컨테이너를 받아 AI가 가까운 플레이어 감지/추격.
    // 공격 이벤트는 outAttacks에 누적 (호출자가 처리).
    void Step(float dt,
              std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
              std::vector<AttackEvent>& outAttacks);

    const std::unordered_map<int, std::unique_ptr<MonsterEntity>>& GetMonsters() const
    {
        return monsters;
    }

    // 몬스터 데미지 처리. 7단계 CombatResolver가 호출 예정.
    bool ApplyDamageToMonster(int monsterId, int damage);

private:
    std::unordered_map<int, std::unique_ptr<MonsterEntity>>  monsters;
    int  nextMonsterId;

    // AI 본체 (몬스터 1마리 처리)
    void StepMonsterAI(MonsterEntity& m, float dt, long long nowMs,
                        std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
                        std::vector<AttackEvent>& outAttacks);

    // 가장 가까운 살아있는 플레이어 ID 반환 (감지 거리 안). 없으면 0.
    int FindNearestPlayerInRange(const MonsterEntity& m,
        std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players);
};

