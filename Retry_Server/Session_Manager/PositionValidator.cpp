#include "PositionValidator.h"
#include "Dungeon/DungeonGenerator.h"
#include "../Common/Logger.h"

int PositionValidator::TeleportRejectCount = 0;
int PositionValidator::WallRejectCount = 0;

bool PositionValidator::IsFloorPosition(const DungeonGenerator& dungeon, const Vec3& worldPos)
{
    // 월드 좌표 → 격자 좌표 (worldOffset 적용)
    IntVec3 tile = dungeon.WorldToTile(worldPos);
    tile.y = 0;     // 던전 floor 타일은 모두 y=0에 존재
    return dungeon.IsFloorTile(tile);
}

Vec3 PositionValidator::ValidateMove(const DungeonGenerator& dungeon,
    const Vec3& prevPos,
    const Vec3& attemptedPos,
    float maxSpeedXZ,
    float dt)
{
    // ── 1. 텔레포트 차단 ───────────────────────────────────────
    // 거리 비교 (XZ 평면만)
    float dx = attemptedPos.x - prevPos.x;
    float dz = attemptedPos.z - prevPos.z;
    float distSq = dx * dx + dz * dz;

    // 허용 최대 거리 = maxSpeed * dt * margin(2.0). 네트워크 지터 보상.
    // dt가 너무 작으면 (예: 0ms) maxDist도 0. 최소 1m는 허용.
    float maxDist = maxSpeedXZ * dt * 2.0f;
    if (maxDist < 1.0f) maxDist = 1.0f;

    if (distSq > maxDist * maxDist)
    {
        TeleportRejectCount++;
        if (TeleportRejectCount <= 10 || TeleportRejectCount % 100 == 0)
        {
            Log::Warn("[Validator] 텔레포트 차단: dist=%.1fm > max=%.1fm (count=%d)",
                (float)std::sqrt(distSq), maxDist, TeleportRejectCount);
        }
        return prevPos;     // 완전 거부 - 직전 위치 유지
    }

    // ── 2. 벽 통과 차단 ───────────────────────────────────────
    if (!IsFloorPosition(dungeon, attemptedPos))
    {
        WallRejectCount++;

        // 단순 슬라이딩: X 또는 Z 한 축만 적용해보고 floor면 그쪽으로
        Vec3 xOnly(attemptedPos.x, attemptedPos.y, prevPos.z);
        if (IsFloorPosition(dungeon, xOnly))
        {
            return xOnly;       // X 축으로만 슬라이드 (벽을 따라 미끄러짐)
        }

        Vec3 zOnly(prevPos.x, attemptedPos.y, attemptedPos.z);
        if (IsFloorPosition(dungeon, zOnly))
        {
            return zOnly;       // Z 축으로만 슬라이드
        }

        // 양쪽 다 막힘 → XZ 그대로 prev, Y만 attempted (점프/낙하 허용)
        if (WallRejectCount <= 10 || WallRejectCount % 200 == 0)
        {
            Log::Info("[Validator] 벽 통과 차단: attempted=(%.1f,%.1f,%.1f) → prev XZ 유지 (count=%d)",
                attemptedPos.x, attemptedPos.y, attemptedPos.z, WallRejectCount);
        }
        return Vec3(prevPos.x, attemptedPos.y, prevPos.z);
    }

    // 통과
    return attemptedPos;
}