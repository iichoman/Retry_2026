#pragma once
#include "../Common/MathTypes.h"

class DungeonGenerator;

// ============================================================================
//  PositionValidator (서버 권위 충돌 검증)
//
//  클라/몬스터가 제안한 새 위치를 서버가 검증.
//  유효하지 않으면 직전 위치를 반환 (rollback).
//
//  검증 항목:
//   1) 텔레포트 차단:
//      - 한 틱 안 이동 거리가 maxSpeed * dt * margin 초과 → 거부
//      - margin은 네트워크 지터/입력 누락 보상용 (기본 2.0)
//
//   2) 벽 통과 차단:
//      - 새 위치를 격자로 변환 (DungeonGenerator::WorldToTile)
//      - 그 타일이 floorTiles 안에 있는지 확인
//      - 없으면 X/Z만 직전 위치로 rollback (Y는 그대로 - 점프/낙하 허용)
//
//  반환: 통과한 위치. 거부 시 X/Z는 prevPos, Y는 attemptedPos.
//
//  설계 메모:
//   - 정밀 충돌 검사가 아닌 격자 기반 sanity check.
//   - 벽 모서리에서 살짝 잘못된 위치 보정도 가능 (구석에 끼지 않음).
//   - 졸업 데모용으로 충분. 추후 swept capsule collision으로 정밀화 가능.
// ============================================================================

class PositionValidator
{
public:
    // 새 위치 검증.
    // - dungeon: 검증 기준이 되는 던전 데이터
    // - prevPos: 직전 (검증된) 위치
    // - attemptedPos: 클라/AI가 시도한 새 위치
    // - maxSpeedXZ: 이 객체의 최대 이동 속도 (m/s). 텔레포트 cap 기준.
    // - dt: 직전 검증 이후 경과 시간 (sec)
    static Vec3 ValidateMove(const DungeonGenerator& dungeon,
        const Vec3& prevPos,
        const Vec3& attemptedPos,
        float maxSpeedXZ,
        float dt);

    // 위치가 던전의 floor 영역 안인지만 검사 (텔레포트 검사 없이).
    static bool IsFloorPosition(const DungeonGenerator& dungeon, const Vec3& worldPos);

    // 검증 결과 통계 (로깅용)
    static int TeleportRejectCount;
    static int WallRejectCount;
};