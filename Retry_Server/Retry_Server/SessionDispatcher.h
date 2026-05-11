#pragma once
#include <string>
#include <vector>
#include <mutex>

// ============================================================================
//  SessionDispatcher
//
//  로비 서버 → 세션 매니저(127.0.0.1:9002)에 IPC 명령을 보내는 컴포넌트.
//  세션 생성을 요청하고 응답을 동기적으로 기다린다.
//
//  세션 매니저는 응답으로 IPC_CREATE_SESSION을 그대로 다시 보내주거나
//  실패 시 빈 응답으로 처리. 본 단계에선 매번 새 TCP 연결을 만들어 단순화.
//  (빈도가 낮으므로 connection pooling 불필요)
//
//  스레드 안전: 동시 다중 호출 가능 (각각 새 소켓)
// ============================================================================

class SessionDispatcher
{
public:
    SessionDispatcher(const std::string& sessionMgrIp, int sessionMgrPort);

    // 세션 생성 요청. 동기적으로 응답까지 대기. 성공 여부 반환.
    bool RequestSessionCreate(int sessionId,
                              int hostClientId,
                              int mapSeed,
                              const std::vector<int>& playerIds);

private:
    std::string ip;
    int         port;
};
