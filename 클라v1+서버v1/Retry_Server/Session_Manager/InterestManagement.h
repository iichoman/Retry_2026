#pragma once
#include "../Common/MathTypes.h"

class GameSession;
class DungeonGenerator;

// ============================================================================
//  InterestManagement (포탈 그래프 기반 AOI + 거리 fallback)
//
//  던전은 방(room) + 복도(corridor) 구조다. 각 방과 복도를 하나의 "노드"로 보고,
//  복도가 연결하는 방들을 간선으로 하는 포탈 그래프를 구성한다(DungeonGenerator).
//  가시성은 이 그래프에서 1-hop으로 판정한다 (강의의 Zone 방식을 정교화).
//
//   - 같은 노드        : 보임 (거리 무관, 방/복도 전체)
//   - 인접 노드(1-hop) : 보임 (방↔연결복도, 복도↔연결방)
//   - 그 외            : 안 보임
//       → 방에 있으면: 그 방 + 직접 연결된 복도들
//       → 복도에 있으면: 그 복도 + 양 끝에 연결된 방들
//       → 방→복도→방(2-hop, 복도 건너 다른 방)은 보이지 않음
//
//   - 노드 미분류 위치(방/복도 밖, 예: 경계 틈) : 거리(VIEW_RANGE) + 시야선 fallback
//
//  성능: 매 틱 각 객체의 소속 노드를 1회 계산하여 캐시한 뒤 그래프 조회로 판정.
//  거리 기준은 XZ 평면. (서버는 Y 무시)
// ============================================================================

class InterestManagement
{
public:
    // 노드 미분류 시 fallback 거리
    static constexpr float VIEW_RANGE = 30.0f;
    static constexpr float VIEW_RANGE_SQ = VIEW_RANGE * VIEW_RANGE;

    // fallback에서 시야선(벽 가림) 검사 on/off
    static constexpr bool  USE_LOS = true;

    // 시야선 검사. from->to 직선이 벽(wallTiles)에 막히는지. 0.5m 간격 샘플링.
    static bool HasLineOfSight(const DungeonGenerator& dungeon,
        const Vec3& fromPos, const Vec3& toPos);

    // 매 틱 호출. session.mtx 잠긴 상태에서.
    void UpdateAll(GameSession& session);

    // 새 클라 attach 시. 양방향 시야 동기화 + 즉시 ENTER_VIEW.
    void OnPlayerJoin(GameSession& session, int newClientId);

    // 클라 퇴장 시. 다른 플레이어들의 viewedPlayers에서 제거 + LEAVE_VIEW.
    void OnPlayerLeave(GameSession& session, int leavingClientId);

    // 몬스터 사망 시. 시야 안 플레이어들에게 MONSTER_DIED.
    void OnMonsterDeath(GameSession& session, int monsterId);

private:
    // 포탈 그래프 가시성 판정.
    //  fromNode/toNode: 미리 계산된 소속 노드 id (NodeIdAt 결과, -1=미분류)
    static bool CanSee(const DungeonGenerator& dungeon,
        const Vec3& fromPos, int fromNode,
        const Vec3& toPos, int toNode);
};