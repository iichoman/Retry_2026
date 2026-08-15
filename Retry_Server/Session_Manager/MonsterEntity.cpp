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
    case MONSTER_BOSS:
        hp = maxHp = 5000;
        moveSpeed = 2.5f;
        detectRange = 25.0f;
        attackRange = 4.0f;
        attackCooldownMs = 6000.0f;
        attackDamage = 5; // 보스 (원래 50)
        break;

    case MONSTER_ELITE:
        hp = maxHp = 800;
        moveSpeed = 3.5f;
        detectRange = 18.0f;
        attackRange = 2.5f;
        attackCooldownMs = 4000.0f;
        attackDamage = 5; // 정예 (원래 25)
        break;

    case MONSTER_NORMAL:
    default:
        hp = maxHp = 200;
        moveSpeed = 3.0f;
        detectRange = 12.0f;
        attackRange = 2.0f;
        attackCooldownMs = 5000.0f;
        attackDamage = 5; // 일반 (원래 10)
        break;
    }
}
