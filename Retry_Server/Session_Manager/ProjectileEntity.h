#pragma once
#include "../Common/MathTypes.h"

// ============================================================================
//  ProjectileEntity (원거리 투사체)
//
//  활/총으로 발사된 직육면체 투사체. 매 틱 velocity만큼 직선 이동하며,
//  벽/몬스터/플레이어 중 먼저 닿는 것에 맞으면 소멸한다.
//  서버는 XZ 평면 기준으로 충돌을 판정한다 (Y 무시).
// ============================================================================
struct ProjectileEntity
{
    int   id;             // 투사체 고유 id
    int   ownerId;        // 발사한 플레이어 clientId (자기 자신엔 안 맞음)
    int   weaponKind;     // WEAPON_BOW / WEAPON_GUN
    int   damage;         // 명중 시 데미지

    Vec3  position;       // 현재 위치
    Vec3  velocity;       // 진행 속도 (정규화 방향 × speed), m/s
    float speed;          // 속력(m/s) - 클라 전송용
    float maxDistance;    // 최대 비행 거리(m). 초과 시 소멸.
    float traveled;       // 누적 이동 거리(m)

    bool  alive;

    ProjectileEntity()
        : id(0), ownerId(0), weaponKind(0), damage(0),
        speed(0.f), maxDistance(0.f), traveled(0.f), alive(true) {
    }
};