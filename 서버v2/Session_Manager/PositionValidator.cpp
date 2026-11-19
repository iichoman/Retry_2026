#include "PositionValidator.h"
#include "Dungeon/DungeonGenerator.h"
#include "EnvironmentSystem.h"
#include "../Common/Logger.h"
#include <cmath>

std::atomic<int> PositionValidator::TeleportRejectCount{ 0 };
std::atomic<int> PositionValidator::WallRejectCount{ 0 };

bool PositionValidator::IsFloorPosition(const DungeonGenerator& dungeon, const Vec3& worldPos)
{
    // 월드 좌표 → 격자 좌표 (worldOffset 적용)
    IntVec3 tile = dungeon.WorldToTile(worldPos);
    tile.y = 0;     // 던전 floor 타일은 모두 y=0에 존재
    for(const auto& chest:dungeon.environmentChests)
        {auto size=Env::ChestHalfSize(dungeon,chest);if(std::abs(worldPos.x-chest.x)<size.x && std::abs(worldPos.z-chest.z)<size.z)return false;}
    for(const auto& box:dungeon.environmentFurniture)
        if(box.round ? worldPos.DistanceSqXZ(box.center)<box.halfX*box.halfX : (std::abs(worldPos.x-box.center.x)<box.halfX&&std::abs(worldPos.z-box.center.z)<box.halfZ))return false;
    return dungeon.IsFloorTile(tile) && !dungeon.IsWallTile(tile);
}

Vec3 PositionValidator::ValidateMove(const DungeonGenerator& dungeon,
    const Vec3& prevPos,
    const Vec3& attemptedPos,
    float maxSpeedXZ,
    float dt)
{
    Vec3 target = attemptedPos;   // const 파라미터 복사 (clamp 가능하도록)

    // ── 1. 텔레포트 cap ───────────────────────────────────────
    // 한 틱 허용 이동 거리 = maxSpeed * dt * margin(2.0). 최소 1m.
    float dx = target.x - prevPos.x;
    float dz = target.z - prevPos.z;
    float distSq = dx * dx + dz * dz;

    float maxDist = maxSpeedXZ * dt * 2.0f;
    if (maxDist < 1.0f) maxDist = 1.0f;

    if (distSq > maxDist * maxDist)
    {
        const int rejects = ++TeleportRejectCount;
        if (rejects <= 10 || rejects % 100 == 0)
        {
            Log::Warn("[Validator] 텔레포트 cap: dist=%.1fm > max=%.1fm → %.1fm로 제한 (count=%d)",
                (float)std::sqrt(distSq), maxDist, maxDist, rejects);
        }

        // ★ 핵심 수정: 거부(freeze)하지 않고 "허용 거리만큼만" attempted 방향으로 이동.
        //   - 직전 위치 유지(rollback)는 클라가 계속 전진할 때 서버가 멈춰
        //     영구 desync를 유발했음(회전만 동기화되는 증상).
        //   - 이렇게 하면 서버 위치가 매 틱 maxDist만큼 클라 쪽으로 수렴 → 끊김/끼임 자동 복구.
        //   - 진짜 텔레포트 치트는 여전히 틱당 maxDist로 제한되어 막힘.
        float dist = std::sqrt(distSq);
        float t = (dist > 0.0001f) ? (maxDist / dist) : 0.0f;
        target.x = prevPos.x + dx * t;
        target.z = prevPos.z + dz * t;
        // target.y는 그대로 (점프/낙하 허용)
    }

    // Sweep the complete accepted step against chests; a dash may end beyond a chest.
    auto sweep=[&](const Vec3& chest,float halfX,float halfZ) {
        if (std::abs(prevPos.x-chest.x)<halfX && std::abs(prevPos.z-chest.z)<halfZ) return;
        float enter=0,leave=1;bool hitX=true;
        auto axis=[&](float start,float delta,float center,float extent,bool xAxis) {
            if(std::abs(delta)<.00001f)return std::abs(start-center)<extent;
            float a=(center-extent-start)/delta,b=(center+extent-start)/delta;
            if(a>b)std::swap(a,b);
            if(a>enter){enter=a;hitX=xAxis;}leave=(std::min)(leave,b);return enter<=leave;
        };
        if(axis(prevPos.x,target.x-prevPos.x,chest.x,halfX,true) && axis(prevPos.z,target.z-prevPos.z,chest.z,halfZ,false)) {
            float fraction=(std::max)(0.f,enter-.001f);
            // Preserve tangential movement so brushing a flat prop face does not stop both axes.
            if(hitX)target.x=prevPos.x+(target.x-prevPos.x)*fraction;
            else target.z=prevPos.z+(target.z-prevPos.z)*fraction;
        }
    };
    for(int pass=0;pass<2;pass++){
    for(auto& chest:dungeon.environmentChests){auto size=Env::ChestHalfSize(dungeon,chest);sweep(chest,size.x,size.z);}
    for(auto& box:dungeon.environmentFurniture){
        if(!box.round){sweep(box.center,box.halfX,box.halfZ);continue;}
        float x=prevPos.x-box.center.x,z=prevPos.z-box.center.z,dx=target.x-prevPos.x,dz=target.z-prevPos.z;
        float a=dx*dx+dz*dz,b=2*(x*dx+z*dz),c=x*x+z*z-box.halfX*box.halfX,disc=b*b-4*a*c;
        if(c>0&&a>.000001f&&disc>=0){float t=(-b-std::sqrt(disc))/(2*a);if(t>=0&&t<=1){t=std::max(0.f,t-.001f);target.x=prevPos.x+dx*t;target.z=prevPos.z+dz*t;}}
    }
    }

    // ── 2. 벽 통과 차단 ───────────────────────────────────────
    if (!IsFloorPosition(dungeon, target))
    {
        const int rejects = ++WallRejectCount;

        // 단순 슬라이딩: X 또는 Z 한 축만 적용해보고 floor면 그쪽으로
        Vec3 xOnly(target.x, target.y, prevPos.z);
        if (IsFloorPosition(dungeon, xOnly))
        {
            return xOnly;       // X 축으로만 슬라이드 (벽을 따라 미끄러짐)
        }

        Vec3 zOnly(prevPos.x, target.y, target.z);
        if (IsFloorPosition(dungeon, zOnly))
        {
            return zOnly;       // Z 축으로만 슬라이드
        }

        // 양쪽 다 막힘 → XZ 그대로 prev, Y만 target (점프/낙하 허용)
        if (rejects <= 10 || rejects % 200 == 0)
        {
            Log::Info("[Validator] 벽 통과 차단: attempted=(%.1f,%.1f,%.1f) → prev XZ 유지 (count=%d)",
                target.x, target.y, target.z, rejects);
        }
        return Vec3(prevPos.x, target.y, prevPos.z);
    }

    // 통과
    return target;
}
