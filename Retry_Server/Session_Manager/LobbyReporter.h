#pragma once
#include <string>

// ============================================================================
//  LobbyReporter
//
//  세션 매니저 → 로비 서버(Retry_Server) 역방향 IPC 송신.
//  SessionDispatcher(로비 → 세션)의 반대 방향이며 구조도 대칭이다.
//
//  용도: 세션이 끝났음을 로비에 보고 → 로비가 방 정리 + 클라 상태 복귀 처리.
//
//  설계 메모:
//   - 송신 빈도가 매우 낮으므로(세션당 1회) 매번 새 TCP 연결. 풀링 불필요.
//   - 로비가 죽어 있어도 세션 종료 자체는 진행되어야 하므로 실패해도 무시.
//   - 스레드 안전: 상태를 갖지 않아 동시 호출 가능.
// ============================================================================

class LobbyReporter
{
public:
    LobbyReporter(const std::string& lobbyIp, int lobbyEventPort);

    // 세션 종료 보고. 응답을 기다리지 않는 fire-and-forget.
    // 반환: 송신 성공 여부 (실패해도 호출자는 종료를 계속 진행할 것).
    bool ReportSessionEnded(int sessionId, int reason,
        int totalPlayers, int survivors);

private:
    std::string ip;
    int         port;
};
