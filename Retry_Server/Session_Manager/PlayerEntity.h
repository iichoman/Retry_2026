#pragma once
#include "../Common/MathTypes.h"
#include <atomic>
#include <unordered_set>

class SessionClientConnection;

// ============================================================================
//  PlayerEntity
//
//  세션 안에서 플레이어 1명의 게임 상태.
//  네트워크 연결과 별개의 객체로 관리:
//   - 클라가 끊어졌다 재접속해도 같은 PlayerEntity가 유지될 수 있음
//   - SessionClientConnection은 약한 참조로만 보유 (소유권 없음)
//
//  Phase 1 (현재): 클라가 보낸 위치를 그대로 신뢰.
//  Phase 4 이후: sanity check (속도 초과/벽 통과 검증) 추가 예정.
// ============================================================================

class PlayerEntity
{
public:
    int             clientId;
    char            playerName[32];

    Vec3            position;       // 월드 좌표 (서버는 XZ만 시야에 사용)
    float           rotY;           // 캐릭터 몸통 Y축 회전 (degrees)
    float           speed;          // 애니메이션 블렌드용 (m/s)
    int             animState;      // 0=Idle,1=Walk,2=Run,3=Attack,...

    int             hp;
    int             maxHp;

    long long       lastInputTimestamp;
    long long       lastAttackTime;     // ms. 공격 쿨다운 검사용 (7단계)

    int startPosResendTicks = 0;   // 본인 시작 위치 재송신 카운터

    // 약한 참조: 클라 연결 객체. 소유권 없음.
    // 클라가 끊기면 nullptr로 설정됨. 재접속 시 다시 채워짐.
    SessionClientConnection* conn;

    // ── 시야 처리 (6단계) ─────────────────────────────────────
    // 이 플레이어의 클라 화면에 현재 보이고 있는 다른 객체들의 ID 집합.
    // 매 틱 새 시야와 비교하여 ENTER/LEAVE/MOVE 결정.
    // GameSession::mtx 잠긴 상태에서만 접근.
    std::unordered_set<int>  viewedPlayers;
    std::unordered_set<int>  viewedMonsters;

    explicit PlayerEntity(int cid);

    // PLAYER_INPUT 패킷 본문을 적용.
    // 서버는 클라가 보고한 위치를 그대로 신뢰 (Phase 1).
    void ApplyInput(float posX, float posY, float posZ,
        float yaw,
        float moveX, float moveY,
        int sprint,
        long long timestamp,
        float dt);
};