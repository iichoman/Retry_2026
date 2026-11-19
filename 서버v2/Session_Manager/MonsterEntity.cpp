#include "MonsterEntity.h"

MonsterEntity::MonsterEntity(int monsterId, int monsterKind, const Vec3& spawnPos)
    : id(monsterId)
    , kind(monsterKind)
    , position(spawnPos)
    , rotY(0.f)
    , aiState(AI_IDLE)
    , targetClientId(0)
    , patrolOrigin(spawnPos)
    , patrolWaypoint(spawnPos)
    , lastDecisionTime(0)
    , lastAttackTime(0)
{
    // 종류별 기본 파라미터. 추후 데이터 테이블화 가능.
    switch (monsterKind)
    {
    case MONSTER_MUMMY:
        hp = maxHp = 100;
        moveSpeed = 1.5f; detectRange = 14.f; attackRange = 6.f;
        attackCooldownMs = 4200.f; attackDamage = 8; attackElement = 3;
        breath.monsterId = monsterId;
        break;
    // 기준: 한손검 1타 20, 스태미나 100(1타당 2) → 한 바퀴에 50타.
    case MONSTER_BOSS:
        hp = maxHp = 2500;
        moveSpeed = 2.5f;
        detectRange = 25.0f;
        attackRange = 4.0f;
        attackCooldownMs = 4000.0f;
        attackDamage = 22;            // 5대 맞으면 사망(100HP) → 반드시 빠졌다 붙어야 함
        break;

    case MONSTER_ELITE:
        hp = maxHp = 300;             // 15타
        moveSpeed = 1.0f; // Deliberate knight walk; matches the client stride cadence.
        detectRange = 18.0f;
        attackRange = 2.5f;
        attackCooldownMs = 3500.0f;
        attackDamage = 16;            // 7대
        break;

    case MONSTER_NORMAL:
    default:
        hp = maxHp = 80;              // 4타
        moveSpeed = 3.0f;
        detectRange = 12.0f;
        attackRange = 2.0f;
        attackCooldownMs = 3000.0f;
        attackDamage = 8;             // 13대
        break;
    }
}
