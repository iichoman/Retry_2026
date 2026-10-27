#pragma once

class GameSession;
struct PlayerAttackRequest;

// ============================================================================
//  CombatResolver (7단계)
//
//  플레이어 공격 의도(PLAYER_ATTACK_REQUEST)를 받아 데미지를 적용.
//
//  처리 흐름:
//   1) 무기 데이터 조회 (데미지, 사거리, 쿨다운)
//   2) 쿨다운 검사 (PlayerEntity.lastAttackTime)
//   3) PLAYER_ATTACK_BROADCAST를 모든 클라에 broadcast (액션 애니용. 빗나가도 송신)
//   4) 사거리 안 가장 가까운 타겟 검색 (몬스터 우선, 없으면 다른 플레이어)
//   5) 명중 시:
//      - 몬스터 → WorldSimulation::ApplyDamageToMonster
//      - 플레이어 → PlayerEntity.hp 차감
//   6) COMBAT_EVENT broadcast (HP 변화 알림)
//   7) 타겟 사망 시 MONSTER_DIED/PLAYER_DIED broadcast
//
//  설계 메모:
//   - 정확한 hit detection (ray cast, capsule overlap) 대신 "사거리 안 가장 가까운 1개"
//     단순화. 졸업 데모용으로 충분. 추후 콜리전 메시 적용 시 정밀화.
//   - GameSession.mtx 잠긴 상태에서만 호출됨.
// ============================================================================

class CombatResolver
{
public:
    struct WeaponData {
        int       damage;
        float     range;            // 근거리: 직육면체 길이(m) / 원거리: 최대 비행거리(m)
        float     width;            // 근거리: 직육면체 폭(m). 원거리는 미사용.
        long long cooldownMs;
        bool      isRanged;         // true=투사체(활/총), false=근접 범위공격(검)
        float     projectileSpeed;  // 원거리 투사체 속력(m/s). 근거리는 0.
    };

    static WeaponData GetWeaponData(int weaponKind);

    // 공격 처리. session.mtx 잠긴 상태에서 호출.
    void HandleAttack(GameSession& session, int attackerId,
        const PlayerAttackRequest& req);
};