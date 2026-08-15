#pragma once
#include "../Common/MathTypes.h"
#include "../Common/PacketProtocol.h"

// ============================================================================
//  MonsterEntity
//
//  서버 측 몬스터 1마리의 모든 게임 상태.
//  AI/이동 로직은 8단계(MonsterAI)에서 본격 구현.
//  현재: 자료구조만 정의, 위치는 스폰 시 결정 후 정지.
//
//  데이터:
//   - id: 세션 안에서 고유. 부호 반전 트릭으로 패킷에서 플레이어와 구분.
//   - kind: MonsterKind (NORMAL/ELITE/BOSS)
//   - position/rotY: 월드 좌표
//   - hp/maxHp: 권위적 HP
//   - aiState: 클라가 애니메이션 결정에 사용 (Idle/Chase/Attack/Dead)
//   - targetClientId: 추격/공격 대상 (없으면 0)
// ============================================================================

class MonsterEntity
{
public:
    int       id;
    int       kind;          // MonsterKind 캐스팅
    Vec3      position;
    float     rotY;
    int       hp;
    int       maxHp;
    int       aiState;       // MonsterAiState 캐스팅
    int       targetClientId;

    // 8단계 추가: AI 의사결정 상태
    Vec3      patrolOrigin;          // 스폰 위치 (집으로 돌아갈 때 기준)
    Vec3      patrolWaypoint;        // 현재 patrol 목적지
    long long lastDecisionTime;      // ms. 의사결정 주기
    long long lastAttackTime;        // ms. 공격 쿨다운

    // 종류별 파라미터 (생성자에서 설정)
    float     moveSpeed;             // m/s
    float     detectRange;           // 플레이어 감지 거리 (m)
    float     attackRange;           // 공격 사거리 (m)
    float     attackCooldownMs;      // 공격 간격
    int       attackDamage;          // 공격 1회 데미지

    // attack wind-up: reserve on START, apply real damage on LAND after windup
    long long pendingHitTime = 0;    // ms. 0 = none
    int       pendingVictim = 0;     // victim clientId for delayed damage
    int       pendingDamage = 0;     // damage to apply on LAND

    MonsterEntity(int monsterId, int monsterKind, const Vec3& spawnPos);
};
