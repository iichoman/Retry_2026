#include "PlayerEntity.h"

#include <cmath>
#include <cstring>

PlayerEntity::PlayerEntity(int cid)
    : clientId(cid)
    , position(0.f, 0.f, 0.f)
    , rotY(0.f)
    , speed(0.f)
    , animState(0)
    , hp(100)
    , maxHp(100)
    , lastInputTimestamp(0)
    , lastAttackTime(0)
    , conn(nullptr)
{
    playerName[0] = '\0';
}

void PlayerEntity::ApplyInput(float posX, float posY, float posZ,
    float yaw,
    float moveX, float moveY,
    int sprint,
    long long timestamp,
    float dt)
{
    // 직전 위치 저장 (속도 계산용)
    float prevX = position.x;
    float prevZ = position.z;

    // 클라 위치를 그대로 신뢰
    position.x = posX;
    position.y = posY;
    position.z = posZ;
    rotY = yaw;

    // 애니메이션 동기화에 쓸 평면 속도 (m/s)
    if (dt > 1e-4f)
    {
        float dx = position.x - prevX;
        float dz = position.z - prevZ;
        speed = std::sqrt(dx * dx + dz * dz) / dt;
    }
    else
    {
        speed = 0.f;
    }

    // 애니메이션 상태 결정 (단순 휴리스틱)
    if (speed < 0.1f)        animState = 0;          // Idle
    else if (sprint)         animState = 2;          // Run
    else                     animState = 1;          // Walk

    lastInputTimestamp = timestamp;
}