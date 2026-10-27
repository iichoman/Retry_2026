#pragma once
#include "ProjectileEntity.h"
#include "../Common/MathTypes.h"
#include <unordered_map>

class GameSession;

// ============================================================================
//  ProjectileSystem (원거리 투사체 관리)
//
//  - Spawn(): 활/총 공격 시 투사체 생성 + PROJECTILE_SPAWN broadcast.
//  - Update(): 매 틱 모든 투사체를 이동시키고 충돌을 판정.
//      · 이동 경로를 0.5m 간격으로 샘플링하여 빠른 투사체의 벽 관통(tunneling) 방지.
//      · 벽 명중      → 소멸 (데미지 없음)
//      · 몬스터 명중  → 데미지 + COMBAT_EVENT(+MONSTER_DIED) + 소멸
//      · 플레이어 명중→ 데미지 + COMBAT_EVENT(+PLAYER_DIED) + 소멸
//      · 사거리 초과  → 소멸
//      소멸 시 PROJECTILE_DESPAWN broadcast (클라는 직육면체 제거 후 손에 초기화).
//      살아있으면 PROJECTILE_MOVE broadcast.
//
//  GameSession.mtx 잠긴 상태에서 호출된다 (Spawn은 CombatResolver 경유,
//  Update는 TickStep 경유).
// ============================================================================

class ProjectileSystem
{
public:
    // 캐릭터 피격 반경(m). 투사체 중심이 이 안에 들어오면 명중.
    static constexpr float HIT_RADIUS = 0.7f;

    // 투사체 생성. origin에서 dir 방향으로 speed로 발사.
    void Spawn(GameSession& session, int ownerId, int weaponKind,
        const Vec3& origin, const Vec3& dir,
        int damage, float maxDistance, float speed);

    // 매 틱 호출 (dtSec 초). 이동 + 충돌 + 소멸 처리.
    void Update(GameSession& session, float dtSec);

    const std::unordered_map<int, ProjectileEntity>& Get() const { return projectiles; }

private:
    std::unordered_map<int, ProjectileEntity> projectiles;
    int nextId = 1;
};